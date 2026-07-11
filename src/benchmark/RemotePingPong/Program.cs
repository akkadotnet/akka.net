//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Serialization;
using Akka.Util.Internal;

namespace RemotePingPong
{
    public static class Messages
    {
        public class Msg { public override string ToString() { return "msg"; } }
        public class Run { public override string ToString() { return "run"; } }
        public class Started { public override string ToString() { return "started"; } }
    }

    internal class Program
    {
        public static uint CpuSpeed()
        {
#if THREADS
            var mo = new System.Management.ManagementObject("Win32_Processor.DeviceID='CPU0'");
            var sp = (uint)(mo["CurrentClockSpeed"]);
            mo.Dispose();
            return sp;
#else
            return 0;
            
#endif
        }

        // Selected once at startup via the "--artery" option; controls which remote transport
        // both ActorSystems bind. Default is the classic DotNetty TCP transport (the historical
        // baseline); "--artery" points the exact same benchmark at Artery.Tcp so the two produce
        // directly comparable msgs/sec numbers over an otherwise-identical workload.
        private static bool _useArtery;

        // Selected once at startup via the "--oneway" option. Default mode is ping-pong
        // (client sends, destination echoes every message back - see EchoActor/BenchmarkActor).
        // "--oneway" switches to a Pekko-MaxThroughput-style one-directional firehose: the sender
        // fires a credit-based stream of messages at the receiver with no reply loop, so the
        // benchmark measures pure one-way transport throughput. See OneWaySenderActor /
        // OneWayReceiverActor.
        private static bool _onewayMode;

        // Number of messages used to "prime the pump" for each client before awaiting completion -
        // i.e. the in-flight window size. Defaults to 50 (the historical hard-coded value) but can
        // be overridden via the "--window N" option.
        private static int _windowSize = 50;

        // When set (via the "--clients N" option), pins the benchmark to a single client
        // count instead of sweeping the full GetClientSettings() series.
        private static int? _pinnedClients;

        // When set (via the "--iobuf SIZE" option), overrides Akka.IO's
        // akka.io.tcp.receive-buffer-size / send-buffer-size (default 8k) for both ActorSystems.
        // The raw string is passed through verbatim as a HOCON size value (e.g. "128k", "1m").
        private static string? _ioBufSize;

        // When set (via the "--msgs N" option), overrides the per-client message count
        // (default: the "repeat" constant, 100000L). One-way mode's default run length is often
        // sub-second, which isn't long enough to observe steady-state throughput - this lets callers
        // push the run into multi-second territory without touching the constant.
        private static long? _msgsOverride;

        // Selected once at startup via the "--payload" option: "toy" (default) is today's historical
        // payload - the actors exchange a bare "hit" string, serialized by the default NewtonsoftJson
        // fallback, byte-for-byte unchanged. "real" swaps in RealPayload.RealPayloadFactory's
        // canonical message (several primitives, a nested type, a collection), serialized by whichever
        // arm "--serializer" selects - see ResolvePayloadInstance()/RealPayloadSerializationConfig().
        private static string _payloadMode = "toy";

        // Selected once at startup via the "--serializer" option: "v2" (Akka.Serialization.V2
        // source-generated MessagePack) or "protobuf" (hand-written Google.Protobuf, string manifest).
        // Only meaningful when _payloadMode is "real" - see BuildRootCommand()'s cross-option
        // validators, which require --serializer whenever --payload real is given and reject it
        // otherwise.
        private static string? _serializerArm;

        // The actual object every "hit"/priming Tell sends, resolved once per process by
        // ResolvePayloadInstance() from _payloadMode/_serializerArm. Defaults to the literal "hit"
        // string so a --payload-less invocation is byte-for-byte identical to pre-real-payload
        // behavior.
        private static object _payloadInstance = "hit";

        // Guards EnsureRealPayloadWiring() so the startup serializer-resolution/round-trip proof
        // (and the one-time bytes-on-wire report folded into PrintSysInfo/PrintServerInfo) runs
        // exactly once per process, regardless of how many client-count/times-to-run reps follow.
        private static bool _realPayloadWiringVerified;

        // Set by EnsureRealPayloadWiring() the one time it runs in a --payload real invocation: the
        // exact serialized byte length of RealPayloadFactory's canonical message under the selected
        // arm. Reported in PrintSysInfo/PrintServerInfo - see the S.4 bytes-on-wire requirement.
        private static int _realPayloadWireBytes;

        // Default akka.remote.artery.advanced.outbound-message-queue-size (see Remote.conf /
        // AssociationRegistry.DefaultOutboundQueueCapacity) - the per-association capacity every
        // benchmark client's outbound traffic funnels through when running in Artery mode.
        private const int DefaultOutboundQueueCapacity = 3072;

        // When set (via the "--qsize N" option), overrides
        // akka.remote.artery.advanced.outbound-message-queue-size (default 3072, see
        // DefaultOutboundQueueCapacity) for both ActorSystems. Only meaningful in Artery mode.
        // Plain integer - the underlying HOCON key is read with GetInt, not GetByteSize, so
        // (unlike --iobuf) there's no k/m size-suffix parsing to mirror here.
        private static int? _qSizeOverride;

        // Resolved once (in RunLoopbackAsync/RunServerModeAsync/RunClientModeAsync, from
        // _qSizeOverride/_windowSize/_pinnedClients) before the run starts and applied uniformly
        // to every ActorSystem created for the whole invocation - see ResolveQueueSize(). Null
        // means "leave akka.remote.artery.advanced.outbound-message-queue-size at its HOCON
        // default": no new HOCON key is set at all, so default-config runs (e.g. --window 50
        // --clients 25) are byte-for-byte identical to pre-qsize behavior and stay historically
        // comparable.
        private static int? _effectiveQueueSize;

        // Set alongside _effectiveQueueSize when the harness auto-raised the queue (as opposed to
        // the caller passing an explicit --qsize); printed verbatim in the run header so an
        // auto-raised run is never mistaken for a default-config one.
        private static string? _queueSizeAutoRaiseReason;

        // Split two-process mode (via the "server" / "client" subcommands): runs the two
        // sides of the benchmark in separate processes so they can sit on two physical machines and
        // exercise a real network. When neither is used ("run", the default), the benchmark
        // behaves exactly as before - both ActorSystems in this one process over loopback,
        // byte-for-byte identical config. Split mode supports either transport, selected by the
        // exact same "--artery" option as single-process mode (see _useArtery) - neither
        // subcommand forces a transport on its own. (Dispatch between run/server/client is now
        // handled directly by which subcommand's action runs - see BuildRootCommand() - so unlike
        // the old positional parser there is no standalone "_serverMode" flag to check.)
        private static bool _clientMode;

        // "--host" - in server mode, the address this process advertises to clients (required).
        // NOTE: neither transport's config sets a separate bind hostname here - ArterySettings'
        // canonical.hostname and DotNetty's public-hostname both fall back to the bind hostname
        // when left unset (see CreateActorSystemConfig) - so the listener binds directly to this
        // address as well (bind == advertise) regardless of transport - use the machine's LAN IP,
        // not 0.0.0.0. In client mode, the server's advertised address to benchmark against
        // (required) - it must match the server's --host EXACTLY, since the string is part of the
        // association key both sides agree on.
        private static string? _host;

        // "--port" - in server mode, the port the transport's listener binds/advertises; in client
        // mode, the server's port. Defaults to DefaultSplitPort on both sides, so omitting it
        // everywhere Just Works. (The client's OWN system always binds an ephemeral port - see
        // Benchmark().)
        private static int _splitPort = DefaultSplitPort;

        // Matches ArterySettings' canonical.port default (which is also Pekko Artery's default).
        // Used uniformly for split mode regardless of transport - DotNetty's own historical
        // default is 2552 - so split-mode command lines are identical across transports; only
        // the "--artery" option changes.
        private const int DefaultSplitPort = 25520;

        // "--myhost" (client mode only) - the address the CLIENT's own remote system advertises.
        // Server->client replies matter in both modes (echo replies in ping-pong, Ack/Complete
        // credit grants in one-way), so this must be reachable FROM the server - never localhost
        // when the server is a remote machine. Defaults to auto-detecting the local outbound IP
        // toward the server via the connected-UDP-socket trick (see DetectLocalOutboundIp).
        private static string? _myHost;

        // True when _myHost came from DetectLocalOutboundIp rather than an explicit "--myhost" -
        // disclosed in the client header so a mis-detected address is easy to spot and override.
        private static bool _myHostAutoDetected;

        // The wire scheme for whichever transport this run selected: "akka" for Artery
        // (RemoteSettings.AkkaScheme, unwrapped) or "akka.tcp" for classic DotNetty remoting
        // (RemoteSettings.AkkaScheme + TcpTransport's SchemeIdentifier, joined by
        // SchemeAugmenter). Split-client mode has to build the server's Address by hand (see
        // Benchmark()) since there's no local Provider.DefaultAddress to read it from, so this
        // has to track _useArtery exactly, or RemoteScope deployment can't resolve the
        // association at all. Single-process mode never needs this - it always reads the scheme
        // straight off the real Provider.DefaultAddress.
        private static string SplitServerScheme => _useArtery ? "akka" : "akka.tcp";

        /// <summary>
        /// Auto-detects the local IP the OS would use to reach <paramref name="serverHost"/>: the
        /// connected-UDP-socket trick. UDP "connect" sends no packets - it only asks the kernel's
        /// routing table which local interface/address the route to the server resolves to - so
        /// this works without the server being up yet and picks the right interface on multi-homed
        /// boxes. (Toward a loopback server it legitimately yields 127.0.0.1, which IS reachable
        /// from a loopback server - the thing this avoids is advertising localhost to a REMOTE
        /// server, where replies would then dead-end.)
        /// </summary>
        private static string DetectLocalOutboundIp(string serverHost, int serverPort)
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(serverHost, serverPort);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }

        /// <summary>
        /// Resolves the effective akka.remote.artery.advanced.outbound-message-queue-size for this
        /// entire invocation. An explicit "--qsize" always wins. Otherwise, if windowSize times the
        /// largest client count this run will exercise (the pinned "--clients" value, or the max of
        /// GetClientSettings() when sweeping) would exceed 80% of the default 3072 capacity, the
        /// queue is auto-raised to 2x that product - covers both the one-way credit-window case and
        /// ping-pong's echo traffic, which both funnel through the same per-association outbound
        /// queue. Left null (HOCON untouched) otherwise, so default-config runs never change.
        /// </summary>
        private static void ResolveQueueSize()
        {
            if (!_useArtery)
            {
                // --qsize/auto-raise tune akka.remote.artery.advanced.outbound-message-queue-size,
                // which DotNetty's transport never reads - CreateActorSystemConfig() only writes
                // that HOCON key when _useArtery is true. Leave _effectiveQueueSize/
                // _queueSizeAutoRaiseReason both null (never even computed) for a DotNetty run, so
                // there's nothing to auto-raise; an explicit --qsize override is instead surfaced as
                // a one-line "ignored" notice in the run header - see PrintSysInfo/PrintServerInfo.
                return;
            }

            if (_qSizeOverride.HasValue)
            {
                _effectiveQueueSize = _qSizeOverride.Value;
                return;
            }

            var effectiveClientCount = _pinnedClients ?? GetClientSettings().Max();
            var product = (long)_windowSize * effectiveClientCount;
            if (product > DefaultOutboundQueueCapacity * 0.8)
            {
                var raised = (int)Math.Min(int.MaxValue, product * 2);
                _effectiveQueueSize = raised;
                _queueSizeAutoRaiseReason =
                    $"outbound-message-queue-size auto-raised to {raised} (window x clients = {product} exceeds default {DefaultOutboundQueueCapacity})";
            }
        }

        public static Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            var commonConfig = ConfigurationFactory.ParseString(@"
            akka {
              actor.provider = remote
              loglevel = ERROR
              suppress-json-serializer-warning = on
              log-dead-letters = off
              remote.log-remote-lifecycle-events = off
            }");

            var transportConfig = _useArtery
                ? ConfigurationFactory.ParseString($@"
                akka.remote.artery {{
                  enabled = on
                  canonical.hostname = ""{ipOrHostname}""
                  canonical.port = {port}
                }}")
                : ConfigurationFactory.ParseString($@"
                akka.remote.dot-netty.tcp {{
                  hostname = ""{ipOrHostname}""
                  port = {port}
                }}");

            var config = transportConfig.WithFallback(commonConfig);

            if (!string.IsNullOrEmpty(_ioBufSize))
            {
                var ioBufConfig = ConfigurationFactory.ParseString($@"
                akka.io.tcp {{
                  receive-buffer-size = {_ioBufSize}
                  send-buffer-size = {_ioBufSize}
                }}");
                config = ioBufConfig.WithFallback(config);
            }

            // Only set this key when it's actually going to be read (Artery mode) AND the caller/
            // auto-raise logic actually resolved an override - see ResolveQueueSize(). Leaving
            // _effectiveQueueSize null means no HOCON key is added here at all, so default-config
            // runs keep an identical config to pre-qsize behavior.
            if (_useArtery && _effectiveQueueSize.HasValue)
            {
                var queueSizeConfig = ConfigurationFactory.ParseString($@"
                akka.remote.artery.advanced {{
                  outbound-message-queue-size = {_effectiveQueueSize.Value}
                }}");
                config = queueSizeConfig.WithFallback(config);
            }

            // Only set when "--payload real" was given - a --payload-less (or "--payload toy")
            // invocation never touches akka.actor.serializers/serialization-bindings, so the toy
            // default's config (and therefore its NewtonsoftJson-fallback wire behavior) stays
            // byte-for-byte identical to pre-real-payload behavior.
            var realPayloadConfig = RealPayloadSerializationConfig();
            if (realPayloadConfig != null)
                config = realPayloadConfig.WithFallback(config);

            return config;
        }

        /// <summary>
        /// Builds the akka.actor.serializers/serialization-bindings HOCON for whichever real-payload
        /// arm "--serializer" selected, or null when "--payload" is "toy" (the default). Applied to
        /// EVERY ActorSystem this invocation creates - both loopback systems in "run" mode, and each
        /// side of split "server"/"client" mode - since CreateActorSystemConfig() is the single choke
        /// point all three subcommands funnel through. Split mode still relies on the operator passing
        /// the SAME "--payload"/"--serializer" flags to both the "server" and "client" processes (the
        /// same convention "--artery"/"--oneway"/"--qsize" already use) - there is no over-the-wire
        /// negotiation of serializer bindings.
        /// </summary>
        private static Config? RealPayloadSerializationConfig()
        {
            if (_payloadMode != "real")
                return null;

            return _serializerArm switch
            {
                "v2" => ConfigurationFactory.ParseString(@"
                akka.actor {
                  serializers {
                    real-benchmark-v2 = ""RemotePingPong.RealPayload.V2.RealBenchmarkSerializer, RemotePingPong""
                  }
                  serialization-bindings {
                    ""RemotePingPong.RealPayload.V2.RealBenchmarkMessage, RemotePingPong"" = real-benchmark-v2
                  }
                }"),
                "protobuf" => ConfigurationFactory.ParseString(@"
                akka.actor {
                  serializers {
                    real-benchmark-protobuf = ""RemotePingPong.RealPayload.Protobuf.RealBenchmarkProtobufSerializer, RemotePingPong""
                  }
                  serialization-bindings {
                    ""RemotePingPong.RealPayload.Protobuf.RealBenchmarkMessage, RemotePingPong"" = real-benchmark-protobuf
                  }
                }"),
                _ => throw new InvalidOperationException(
                    "\"--payload real\" requires --serializer to have been resolved (v2|protobuf) before " +
                    "an ActorSystem config can be built - this should have been rejected by the CLI's " +
                    "cross-option validators before reaching here.")
            };
        }

        /// <summary>
        /// Resolves the object every "hit"/priming Tell actually sends for this invocation, from
        /// _payloadMode/_serializerArm. "toy" (default) returns the literal "hit" string - identical to
        /// pre-real-payload behavior. "real" builds RealPayloadFactory's ONE canonical message and
        /// converts it to whichever arm's wire type "--serializer" selected, so both arms carry
        /// identical logical content (see RealPayloadFactory's remarks).
        /// </summary>
        private static object ResolvePayloadInstance()
        {
            if (_payloadMode != "real")
                return "hit";

            var canonical = RealPayload.RealPayloadFactory.CreateCanonical();
            return _serializerArm switch
            {
                "v2" => canonical,
                "protobuf" => RealPayload.RealPayloadFactory.ToProtobuf(canonical),
                _ => throw new InvalidOperationException(
                    "\"--payload real\" requires --serializer to have been resolved (v2|protobuf) before " +
                    "the payload instance can be built - this should have been rejected by the CLI's " +
                    "cross-option validators before reaching here.")
            };
        }

        /// <summary>
        /// Proof of correct wiring for a "--payload real" run (see the harness's S.3/4 requirements):
        /// resolves the serializer Akka actually picked for the canonical payload via
        /// <see cref="Akka.Serialization.Serialization.FindSerializerFor"/>, logs its id/type (so a
        /// silent NewtonsoftJson fallback - e.g. from a serialization-bindings typo - is impossible to
        /// miss), then round-trips the canonical message through it and FAILS FAST (throws) if the
        /// resolved serializer isn't the expected arm's type, or if the round-tripped value isn't
        /// logically equal to the original. Runs exactly once per process (guarded by
        /// _realPayloadWiringVerified), on whichever ActorSystem calls it first - loopback/split-client
        /// mode call this from Benchmark() right after creating system1; split-server mode calls it
        /// from RunServer() right after creating its system. No-op when _payloadMode is "toy".
        /// </summary>
        private static void EnsureRealPayloadWiring(ActorSystem system)
        {
            if (_payloadMode != "real" || _realPayloadWiringVerified)
                return;

            _realPayloadWiringVerified = true;

            var extendedSystem = (ExtendedActorSystem)system;
            var canonical = _payloadInstance;

            var expectedType = _serializerArm switch
            {
                "v2" => typeof(RealPayload.V2.RealBenchmarkSerializer),
                "protobuf" => typeof(RealPayload.Protobuf.RealBenchmarkProtobufSerializer),
                _ => throw new InvalidOperationException($"Unknown --serializer value [{_serializerArm}].")
            };

            var serializer = extendedSystem.Serialization.FindSerializerFor(canonical);

            Console.WriteLine();
            Console.WriteLine("Real-payload serializer wiring (system \"{0}\"):", system.Name);
            Console.WriteLine("  --serializer arm:                  {0}", _serializerArm);
            Console.WriteLine("  Resolved serializer:               {0} (Id={1})", serializer.GetType().FullName, serializer.Identifier);

            if (serializer.GetType() != expectedType)
            {
                throw new InvalidOperationException(
                    $"Real-payload wiring check FAILED: expected serializer [{expectedType.FullName}] for " +
                    $"--serializer {_serializerArm}, but Serialization.FindSerializerFor resolved " +
                    $"[{serializer.GetType().FullName}] instead (Id={serializer.Identifier}). This usually means " +
                    "the serialization-bindings HOCON entry for the message type is missing or mistyped, and " +
                    "Akka silently fell back to a different serializer (e.g. NewtonsoftJson).");
            }

            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, canonical);
            var bytes = serializer.ToBinary(canonical);
            var roundTripped = extendedSystem.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

            if (!canonical.Equals(roundTripped))
            {
                throw new InvalidOperationException(
                    $"Real-payload wiring check FAILED: round-tripping the canonical message through " +
                    $"[{serializer.GetType().FullName}] (serialize -> deserialize) did not preserve logical " +
                    "equality. Original and deserialized values differ.");
            }

            _realPayloadWireBytes = bytes.Length;
            Console.WriteLine("  Round-trip check:                  PASSED ({0} bytes)", bytes.Length);
            Console.WriteLine();
        }

        private static async Task<int> Main(string[] args)
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Attempted to elevate process priority, but failed due to {ex.Message} - carrying on at normal process priority.");
            }

            var rootCommand = BuildRootCommand();
            var parseResult = rootCommand.Parse(args);
            return await parseResult.InvokeAsync();
        }

        /// <summary>
        /// Builds the harness's command-line interface (System.CommandLine). Three subcommands:
        /// "run" (the default single-process loopback benchmark - both ActorSystems in this
        /// process over loopback), "server" and "client" (the split two-node mode - see the field
        /// docs on _clientMode/_host/_splitPort/_myHost above). Every knob that used to
        /// be a bare "token" or "key=value" pair in the old positional parser is now a named,
        /// self-documenting `--option` with its own description and default, shared across
        /// whichever subcommands actually read it (the same Option&lt;T&gt; instance can be added to
        /// multiple Command objects - System.CommandLine tracks parsed values per invocation, not
        /// per Option instance). Invoking the harness with NO subcommand (bare `RemotePingPong`)
        /// preserves the historical default behavior exactly: it runs the same single-process
        /// loopback ping-pong sweep "run" performs with every option left at its default.
        /// e.g. `RemotePingPong run --artery --oneway --window 1000 --clients 10 --iobuf 128k --msgs 500000 --qsize 10000`
        /// or `RemotePingPong server --host 10.0.0.5 --oneway --window 200 --clients 25` (machine A, DotNetty)
        ///  + `RemotePingPong client --times 3 --host 10.0.0.5 --oneway --window 200 --clients 25` (machine B, DotNetty)
        /// or `RemotePingPong server --artery --host 10.0.0.5 --oneway --window 200 --clients 25` (machine A, Artery)
        ///  + `RemotePingPong client --artery --times 3 --host 10.0.0.5 --oneway --window 200 --clients 25` (machine B, Artery).
        /// </summary>
        private static RootCommand BuildRootCommand()
        {
            var arteryOption = new Option<bool>("--artery")
            {
                Description = "Use the Artery.Tcp transport instead of the classic DotNetty TCP transport (the default)."
            };

            var onewayOption = new Option<bool>("--oneway")
            {
                Description = "Switch from ping-pong mode to one-directional firehose mode: the sender fires a " +
                              "credit-based stream of messages at the receiver with no reply loop, measuring pure " +
                              "one-way transport throughput (see OneWaySenderActor/OneWayReceiverActor)."
            };

            var windowOption = new Option<int>("--window")
            {
                Description = "In-flight priming window size per client (i.e. how many messages are sent before " +
                              "awaiting a reply/ack).",
                DefaultValueFactory = _ => 50
            };
            windowOption.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<int>();
                if (value <= 0)
                    result.AddError($"--window must be greater than zero (got {value}).");
            });

            var clientsOption = new Option<int?>("--clients")
            {
                Description = "Pin the benchmark to a single client count instead of sweeping the full series " +
                              "(1, 5, 10, 15, 20, 25, 30)."
            };

            var iobufOption = new Option<string?>("--iobuf")
            {
                Description = "Override akka.io.tcp.receive-buffer-size / send-buffer-size for both ActorSystems " +
                              "(HOCON size value, e.g. \"128k\", \"1m\"). Default (8k) is unchanged when omitted."
            };

            var msgsOption = new Option<long?>("--msgs")
            {
                Description = "Override the per-client message count (default 100000). Useful for one-way mode, " +
                              "whose default run length is often sub-second."
            };

            var qsizeOption = new Option<int?>("--qsize")
            {
                Description = "Override akka.remote.artery.advanced.outbound-message-queue-size (default " +
                              $"{DefaultOutboundQueueCapacity}, Artery mode only). When omitted, the harness " +
                              "auto-raises it whenever window x clients would exceed 80% of the default capacity."
            };

            var timesOption = new Option<uint>("--times")
            {
                Description = "Number of times to repeat the full benchmark sweep/run.",
                DefaultValueFactory = _ => 1u
            };

            var payloadOption = new Option<string>("--payload")
            {
                Description = "Message payload to exchange: \"toy\" (default) is today's historical payload - a " +
                              "bare \"hit\" string, serialized by the default NewtonsoftJson fallback, byte-for-byte " +
                              "unchanged. \"real\" sends a realistic message (int/long/double/bool primitives, a " +
                              "string, a nested complex type, and a collection) built from one canonical source and " +
                              "serialized by the arm --serializer selects; requires --serializer.",
                DefaultValueFactory = _ => "toy"
            };
            payloadOption.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string>();
                if (value != "toy" && value != "real")
                    result.AddError($"--payload must be \"toy\" or \"real\" (got \"{value}\").");
            });

            var serializerOption = new Option<string?>("--serializer")
            {
                Description = "Real-payload serializer arm: \"v2\" (Akka.Serialization.V2 source-generated " +
                              "MessagePack) or \"protobuf\" (hand-written Google.Protobuf, string manifest). " +
                              "Required when --payload real is given; rejected otherwise (--payload toy has no " +
                              "custom serializer to select)."
            };
            serializerOption.Validators.Add(result =>
            {
                var value = result.GetValueOrDefault<string?>();
                if (value is not null && value != "v2" && value != "protobuf")
                    result.AddError($"--serializer must be \"v2\" or \"protobuf\" (got \"{value}\").");
            });

            void AddPayloadSerializerValidation(Command command)
            {
                command.Validators.Add(result =>
                {
                    var payload = result.GetValue(payloadOption);
                    var serializer = result.GetValue(serializerOption);
                    if (payload == "real" && serializer is null)
                        result.AddError("--serializer <v2|protobuf> is required when --payload real is specified.");
                    else if (payload != "real" && serializer is not null)
                        result.AddError("--serializer is only meaningful with --payload real (omit --serializer, " +
                                         "or add --payload real).");
                });
            }

            var runCommand = new Command("run",
                "Run the default in-process loopback benchmark: both ActorSystems live in this process, " +
                "communicating over loopback. This is what runs when no subcommand is given.");
            runCommand.Options.Add(arteryOption);
            runCommand.Options.Add(onewayOption);
            runCommand.Options.Add(windowOption);
            runCommand.Options.Add(clientsOption);
            runCommand.Options.Add(iobufOption);
            runCommand.Options.Add(msgsOption);
            runCommand.Options.Add(qsizeOption);
            runCommand.Options.Add(timesOption);
            runCommand.Options.Add(payloadOption);
            runCommand.Options.Add(serializerOption);
            AddPayloadSerializerValidation(runCommand);
            runCommand.SetAction(parseResult => RunLoopbackAsync(
                parseResult.GetValue(arteryOption),
                parseResult.GetValue(onewayOption),
                parseResult.GetValue(windowOption),
                parseResult.GetValue(clientsOption),
                parseResult.GetValue(iobufOption),
                parseResult.GetValue(msgsOption),
                parseResult.GetValue(qsizeOption),
                parseResult.GetValue(timesOption),
                parseResult.GetValue(payloadOption)!,
                parseResult.GetValue(serializerOption)));

            var serverHostOption = new Option<string>("--host")
            {
                Description = "Address this process advertises/binds to remote clients (use this machine's LAN " +
                              "IP, not 0.0.0.0).",
                Required = true
            };

            var serverPortOption = new Option<int>("--port")
            {
                Description = "Port this server's transport listener binds/advertises.",
                DefaultValueFactory = _ => DefaultSplitPort
            };

            var serverCommand = new Command("server",
                "Run the long-lived split-mode server side: a dumb remote host with no benchmark actors of its " +
                "own - every echo/receiver actor is RemoteScope-deployed onto it by a 'client' process. Serves " +
                "sequential benchmark runs until killed (Ctrl+C/SIGTERM).");
            serverCommand.Options.Add(arteryOption);
            serverCommand.Options.Add(onewayOption);
            serverCommand.Options.Add(windowOption);
            serverCommand.Options.Add(clientsOption);
            serverCommand.Options.Add(iobufOption);
            serverCommand.Options.Add(qsizeOption);
            serverCommand.Options.Add(serverHostOption);
            serverCommand.Options.Add(serverPortOption);
            serverCommand.Options.Add(payloadOption);
            serverCommand.Options.Add(serializerOption);
            AddPayloadSerializerValidation(serverCommand);
            serverCommand.SetAction(parseResult => RunServerModeAsync(
                parseResult.GetValue(arteryOption),
                parseResult.GetValue(onewayOption),
                parseResult.GetValue(windowOption),
                parseResult.GetValue(clientsOption),
                parseResult.GetValue(iobufOption),
                parseResult.GetValue(qsizeOption),
                parseResult.GetValue(serverHostOption),
                parseResult.GetValue(serverPortOption),
                parseResult.GetValue(payloadOption)!,
                parseResult.GetValue(serializerOption)));

            var clientHostOption = new Option<string>("--host")
            {
                Description = "The split-mode server's advertised address to benchmark against (must match the " +
                              "server's --host exactly).",
                Required = true
            };

            var clientPortOption = new Option<int>("--port")
            {
                Description = "The split-mode server's port.",
                DefaultValueFactory = _ => DefaultSplitPort
            };

            var myHostOption = new Option<string?>("--myhost")
            {
                Description = "Address this client's own remote system advertises for server -> client replies " +
                              "(echoes in ping-pong, Ack/Complete credit grants in one-way). Must be reachable " +
                              "from the server - never localhost when the server is a remote machine. Defaults " +
                              "to auto-detecting the local outbound IP toward the server."
            };

            var clientCommand = new Command("client",
                "Run the split-mode client side: points the full benchmark protocol at a remote 'server' " +
                "process instead of an in-process second ActorSystem.");
            clientCommand.Options.Add(arteryOption);
            clientCommand.Options.Add(onewayOption);
            clientCommand.Options.Add(windowOption);
            clientCommand.Options.Add(clientsOption);
            clientCommand.Options.Add(iobufOption);
            clientCommand.Options.Add(msgsOption);
            clientCommand.Options.Add(qsizeOption);
            clientCommand.Options.Add(timesOption);
            clientCommand.Options.Add(clientHostOption);
            clientCommand.Options.Add(clientPortOption);
            clientCommand.Options.Add(myHostOption);
            clientCommand.Options.Add(payloadOption);
            clientCommand.Options.Add(serializerOption);
            AddPayloadSerializerValidation(clientCommand);
            clientCommand.SetAction(parseResult => RunClientModeAsync(
                parseResult.GetValue(arteryOption),
                parseResult.GetValue(onewayOption),
                parseResult.GetValue(windowOption),
                parseResult.GetValue(clientsOption),
                parseResult.GetValue(iobufOption),
                parseResult.GetValue(msgsOption),
                parseResult.GetValue(qsizeOption),
                parseResult.GetValue(timesOption),
                parseResult.GetValue(clientHostOption),
                parseResult.GetValue(clientPortOption),
                parseResult.GetValue(myHostOption),
                parseResult.GetValue(payloadOption)!,
                parseResult.GetValue(serializerOption)));

            var rootCommand = new RootCommand(
                "RemotePingPong - Akka.Remote cross-platform performance benchmark harness. With no subcommand, " +
                "behaves exactly like 'run' with every option at its default (single-process loopback ping-pong " +
                "sweep).");
            rootCommand.Subcommands.Add(runCommand);
            rootCommand.Subcommands.Add(serverCommand);
            rootCommand.Subcommands.Add(clientCommand);
            // Bare invocation (no subcommand) preserves the historical default behavior byte-for-byte: the
            // same single-process loopback benchmark "run" performs with every option left at its default.
            rootCommand.SetAction(_ => RunLoopbackAsync(
                artery: false, oneway: false, window: 50, clients: null, iobuf: null, msgs: null, qsize: null,
                times: 1u, payload: "toy", serializer: null));

            return rootCommand;
        }

        private static async Task<int> RunLoopbackAsync(bool artery, bool oneway, int window, int? clients,
            string? iobuf, long? msgs, int? qsize, uint times, string payload, string? serializer)
        {
            _useArtery = artery;
            _onewayMode = oneway;
            _windowSize = window;
            _pinnedClients = clients;
            _ioBufSize = iobuf;
            _msgsOverride = msgs;
            _qSizeOverride = qsize;
            _payloadMode = payload;
            _serializerArm = serializer;
            _payloadInstance = ResolvePayloadInstance();

            ResolveQueueSize();
            await Start(times);
            return 0;
        }

        private static async Task<int> RunServerModeAsync(bool artery, bool oneway, int window, int? clients,
            string? iobuf, int? qsize, string host, int port, string payload, string? serializer)
        {
            _useArtery = artery;
            _onewayMode = oneway;
            _windowSize = window;
            _pinnedClients = clients;
            _ioBufSize = iobuf;
            _qSizeOverride = qsize;
            _host = host;
            _splitPort = port;
            _payloadMode = payload;
            _serializerArm = serializer;
            _payloadInstance = ResolvePayloadInstance();

            ResolveQueueSize();
            await RunServer();
            return 0;
        }

        private static async Task<int> RunClientModeAsync(bool artery, bool oneway, int window, int? clients,
            string? iobuf, long? msgs, int? qsize, uint times, string host, int port, string? myHost,
            string payload, string? serializer)
        {
            _useArtery = artery;
            _onewayMode = oneway;
            _windowSize = window;
            _pinnedClients = clients;
            _ioBufSize = iobuf;
            _msgsOverride = msgs;
            _qSizeOverride = qsize;
            _clientMode = true;
            _host = host;
            _splitPort = port;
            _payloadMode = payload;
            _serializerArm = serializer;
            _payloadInstance = ResolvePayloadInstance();

            if (!string.IsNullOrWhiteSpace(myHost))
            {
                _myHost = myHost;
                _myHostAutoDetected = false;
            }
            else
            {
                _myHost = DetectLocalOutboundIp(host, port);
                _myHostAutoDetected = true;
            }

            ResolveQueueSize();
            await Start(times);
            return 0;
        }

        private static bool _firstRun = true;

        private static void PrintSysInfo(long effectiveRepeat){
            var processorCount = Environment.ProcessorCount;
            if (processorCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read processor count..");
                return;
            }

            Console.WriteLine("Transport:                         {0}", _useArtery ? "Artery.Tcp" : "DotNetty");
            Console.WriteLine("Mode:                              {0}", _onewayMode ? "one-way" : "ping-pong");
            Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:                    {0}", processorCount);
            Console.WriteLine("ClockSpeed:                        {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:                       {0}", processorCount * 2);
            // One-way mode has no reply loop, so "sent" and "received" per client are both
            // effectiveRepeat (not effectiveRepeat*2 as in ping-pong, where every message
            // travels out and back) - mirrors the per-client half of GetTotalMessagesReceived().
            Console.WriteLine("Messages sent/received per client: {0}  ({0:0e0})", _onewayMode ? effectiveRepeat : effectiveRepeat*2);
            Console.WriteLine("Is Server GC:                      {0}", GCSettings.IsServerGC);
            Console.WriteLine("Thread count:                      {0}", Process.GetCurrentProcess().Threads.Count);
            Console.WriteLine("Window size (in-flight):           {0}", _windowSize);
            if (_pinnedClients.HasValue)
            {
                Console.WriteLine("Pinned client count:               {0}", _pinnedClients.Value);
            }
            if (!string.IsNullOrEmpty(_ioBufSize))
            {
                Console.WriteLine("IO buffer size (akka.io.tcp):      {0}", _ioBufSize);
            }
            if (_useArtery)
            {
                // Always print the effective value - default (untouched), explicit --qsize
                // override, or auto-raised - so a run's queue capacity is never ambiguous from
                // the header alone.
                Console.WriteLine("Outbound queue size (artery):      {0}{1}", _effectiveQueueSize ?? DefaultOutboundQueueCapacity,
                    _qSizeOverride.HasValue ? " (explicit --qsize)" : _effectiveQueueSize.HasValue ? " (auto-raised)" : " (default)");
                if (_queueSizeAutoRaiseReason != null)
                {
                    var prevColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(_queueSizeAutoRaiseReason);
                    Console.ForegroundColor = prevColor;
                }
            }
            else if (_qSizeOverride.HasValue)
            {
                // --qsize only means something in Artery mode (outbound-message-queue-size);
                // called out explicitly rather than silently doing nothing, so a stray --qsize on a
                // DotNetty run is never mistaken for having taken effect.
                Console.WriteLine("--qsize ignored (DotNetty)");
            }
            if (_clientMode)
            {
                Console.WriteLine("Split mode:                        client -> {0}://SystemB@{1}:{2}", SplitServerScheme, _host, _splitPort);
                Console.WriteLine("Client advertised address:         {0}{1}", _myHost, _myHostAutoDetected ? " (auto-detected)" : " (explicit --myhost)");
                // Deliberate divergence from single-process mode, disclosed up front: the split
                // server persists across all timesToRun reps (its JIT/caches stay warm) while this
                // client recreates its system per rep - single-process mode restarts BOTH systems
                // every rep, so split-mode rep-1 "cold" numbers warm up faster than historical ones.
                Console.WriteLine("NOTE: server system persists across reps (server-side JIT stays warm); client recreates its system per rep.");
            }
            if (_payloadMode == "real")
            {
                PrintRealPayloadInfo();
            }
            Console.WriteLine();

            //Print tables
            Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads");

            _firstRun = false;
        }

        /// <summary>
        /// S.4 bytes-on-wire reporting for "--payload real" runs: the exact serialized payload size
        /// (measured once by EnsureRealPayloadWiring's round-trip check, before any actor is created)
        /// alongside the msgs/sec table, since msgs/sec alone conflates CPU cost with frame size and
        /// the two arms produce very different frame sizes. Only ever called when _payloadMode is
        /// "real" (see the two call sites in PrintSysInfo/PrintServerInfo), so a "toy" run's output is
        /// completely unaffected - not even an extra blank line.
        /// </summary>
        /// <remarks>
        /// This reports the exact serialized MESSAGE payload only (what the chosen serializer's
        /// ToBinary/Serialize actually produced) - not the full on-wire frame. Estimating the full
        /// Artery/DotNetty frame (length-prefix, RemoteEnvelope protobuf wrapper or Artery's compact
        /// binary header, actor path strings, etc.) was investigated but isn't cheaply reachable from
        /// this benchmark project: those types (e.g. Akka.Remote.Serialization.Proto.Msg.RemoteEnvelope,
        /// Akka.Remote.Artery's HeaderBuilder/EnvelopeBufferPool) are internal to Akka.Remote (see
        /// Akka.Remote.csproj's `Protobuf Access="internal"` items) and there is no supported public API
        /// to size them without constructing a live association. Rather than fabricate an unverified
        /// frame-size estimate, this reports only the number this benchmark can prove exactly, and
        /// documents the gap: DotNetty classic remoting adds a 4-byte length-prefix
        /// (Akka.IO's LengthFieldPrepender) plus a RemoteEnvelope protobuf wrapper (recipient/sender
        /// actor path strings + serializer id + manifest) on top of the figure below; Artery adds its
        /// own compact binary header (association uid, serializer id, manifest/path compression table
        /// refs, flags) instead.
        /// </remarks>
        private static void PrintRealPayloadInfo()
        {
            Console.WriteLine("Payload:                           real (--serializer {0})", _serializerArm);
            Console.WriteLine("Serialized payload size:           {0} bytes (canonical message, {1} readings) " +
                               "[message bytes only - see PrintRealPayloadInfo's remarks for wire-frame overhead]",
                _realPayloadWireBytes, RealPayload.RealPayloadFactory.ReadingCount);
        }

        const long repeat = 100000L;

        private static async Task Start(uint timesToRun)
        {
            var effectiveRepeat = _msgsOverride ?? repeat;
            for (var i = 0; i < timesToRun; i++)
            {
                var redCount = 0;
                var bestThroughput = 0L;
                var clientSettings = _pinnedClients.HasValue
                    ? new[] { _pinnedClients.Value }
                    : GetClientSettings();
                foreach (var throughput in clientSettings)
                {
                    var result1 = await Benchmark(throughput, effectiveRepeat, _windowSize, bestThroughput, redCount);
                    bestThroughput = result1.Item2;
                    redCount = result1.Item3;
                }
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Done..");
        }

        /// <summary>
        /// Split-mode server: a deliberately dumb, long-lived remote host - Artery or DotNetty,
        /// whichever _useArtery selected (same "--artery" option rule as single-process mode; see
        /// CreateActorSystemConfig()). It creates NO benchmark actors of its own - every
        /// echo/receiver actor is RemoteScope-deployed onto it by the client, via the exact same
        /// deployments single-process mode makes against its in-process system2 (see Benchmark()).
        /// Those land under /remote/[scheme]/SystemA@[myhost]:[ephemeral-port]/... paths on this
        /// system ([scheme] being SplitServerScheme's "akka" or "akka.tcp"), and the client binds
        /// a fresh ephemeral port every rep, so sequential runs can never collide on actor names -
        /// Akka's remote-deployment daemon already IS a "server-side factory keyed by client
        /// identity", which is why no run-id negotiation or receptionist protocol is needed here.
        /// --window/--clients/--qsize/--oneway are accepted so the orchestrator can pass the
        /// SAME values to both sides; functionally only the queue sizing matters on this side (it
        /// sizes THIS system's outbound queue for the reply traffic - echoes in ping-pong,
        /// Ack/Complete credit grants in one-way, and only in Artery mode - see
        /// ResolveQueueSize()), while oneway/window/clients are disclosed in the header so
        /// operators can eyeball that both sides were launched consistently. Serves an arbitrary
        /// number of sequential benchmark runs until killed (Ctrl+C/SIGTERM).
        /// </summary>
        private static async Task RunServer()
        {
            var system = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", _host!, _splitPort));
            EnsureRealPayloadWiring(system);
            // DefaultAddress reflects the ACTUALLY-bound endpoint (ActorSystem.Create doesn't
            // return until the transport's listener is up), so printing READY from it is truthful.
            var boundAddress = ((ExtendedActorSystem)system).Provider.DefaultAddress;

            // Same drop visibility as Benchmark(), but long-lived: this process has no knowledge of
            // client rep boundaries, so instead of one per-rep total it prints a SUSPECT delta line
            // whenever new drops appeared since the last poll (below). Zero drops -> zero noise, so
            // any SUSPECT line in a server log taints the client-side numbers from the same window.
            var dropCounter = new DropCounter();
            var dropWatcher = system.ActorOf(Props.Create(() => new DropCounterActor(dropCounter)), "dropWatcher");
            system.EventStream.Subscribe(dropWatcher, typeof(Dropped));

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true; // shut the ActorSystem down instead of hard-killing the process
                system.Terminate();
            };

            PrintServerInfo();
            Console.WriteLine("SERVER READY on {0}:{1}", boundAddress.Host, boundAddress.Port);

            var lastSeen = 0L;
            while (!system.WhenTerminated.IsCompleted)
            {
                await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(5)), system.WhenTerminated);
                var nowSeen = dropCounter.Count;
                if (nowSeen > lastSeen)
                {
                    var prevColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    Console.WriteLine("  *** SUSPECT: dropped {0} (outbound queue overflow, server side) ***", nowSeen - lastSeen);
                    Console.ForegroundColor = prevColor;
                }
                lastSeen = nowSeen;
            }
        }

        /// <summary>
        /// Server-mode analogue of <see cref="PrintSysInfo"/>: discloses the options this side was
        /// launched with, most importantly the effective outbound queue size. The values only match
        /// the client's if the orchestrator passed the same --window/--clients/--qsize values to
        /// both sides - keeping that the operator's job (dumb and explicit) is deliberate; there is
        /// no negotiation protocol to go wrong.
        /// </summary>
        private static void PrintServerInfo()
        {
            Console.WriteLine("Transport:                         {0}", _useArtery ? "Artery.Tcp" : "DotNetty");
            Console.WriteLine("Mode:                              {0} (split: server side)", _onewayMode ? "one-way" : "ping-pong");
            Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:                    {0}", Environment.ProcessorCount);
            Console.WriteLine("Is Server GC:                      {0}", GCSettings.IsServerGC);
            Console.WriteLine("Window size (in-flight):           {0}", _windowSize);
            if (_pinnedClients.HasValue)
            {
                Console.WriteLine("Pinned client count:               {0}", _pinnedClients.Value);
            }
            if (_useArtery)
            {
                Console.WriteLine("Outbound queue size (artery):      {0}{1}", _effectiveQueueSize ?? DefaultOutboundQueueCapacity,
                    _qSizeOverride.HasValue ? " (explicit --qsize)" : _effectiveQueueSize.HasValue ? " (auto-raised)" : " (default)");
                if (_queueSizeAutoRaiseReason != null)
                {
                    var prevColor = Console.ForegroundColor;
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(_queueSizeAutoRaiseReason);
                    Console.ForegroundColor = prevColor;
                }
            }
            else if (_qSizeOverride.HasValue)
            {
                Console.WriteLine("--qsize ignored (DotNetty)");
            }
            if (_payloadMode == "real")
            {
                PrintRealPayloadInfo();
            }
        }

        public static IEnumerable<int> GetClientSettings()
        {
            yield return 1;
            yield return 5;
            yield return 10;
            yield return 15;
            yield return 20;
            yield return 25;
            yield return 30;
        }

        private static long GetTotalMessagesReceived(int numberOfClients, long numberOfRepeats)
        {
            // Ping-pong mode counts both directions of travel (client send + destination echo) per
            // repeat. One-way mode has no reply loop - throughput is measured receiver-side only,
            // so a single direction of travel is counted per repeat.
            return _onewayMode
                ? numberOfClients * numberOfRepeats
                : numberOfClients * numberOfRepeats * 2;
        }

        private static async Task<(bool, long, int)> Benchmark(int numberOfClients, long numberOfRepeats, int windowSize, long bestThroughput, int redCount)
        {
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfClients, numberOfRepeats);
            // In split-client mode the local system advertises _myHost (which must be reachable
            // FROM the server, since replies flow server->client in both modes); single-process
            // mode keeps the historical loopback binding. Port 0 either way: a fresh OS-assigned
            // port per rep, which split mode additionally relies on for server-side actor-path
            // uniqueness (see the system2 comment below).
            var system1 = ActorSystem.Create("SystemA", CreateActorSystemConfig("SystemA", _clientMode ? _myHost! : "127.0.0.1", 0));
            EnsureRealPayloadWiring(system1);

            // system2 - the serving side (echo actors in ping-pong, receivers in one-way) - only
            // exists in this process in single-process mode. In split-client mode the serving side
            // is a long-lived RunServer() process and system2Address simply points at it: all
            // server-side actors are still created through the exact same RemoteScope deployments
            // below, they just genuinely land on the other machine, under
            // /remote/[scheme]/SystemA@[myhost]:[ephemeral-port]/... paths that are unique per rep
            // (fresh client port each rep), so sequential runs never collide on the shared server.
            // Deliberate divergence from single-process mode: that server persists across all
            // timesToRun reps (its JIT stays warm) while system1 here is recreated per rep -
            // single-process mode restarts BOTH systems every rep.
            ActorSystem? system2 = null;
            Address system2Address;
            if (_clientMode)
            {
                // The split server has no local Provider.DefaultAddress to read here (it's a
                // separate process) - its Address has to be built by hand, and the scheme MUST
                // match the transport it was actually bound with (see SplitServerScheme) or
                // RemoteScope deployment below can't resolve the association at all: classic
                // DotNetty remoting addresses are "akka.tcp://...", Artery's are "akka://...".
                system2Address = new Address(SplitServerScheme, "SystemB", _host, _splitPort);
            }
            else
            {
                system2 = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", "127.0.0.1", 0));
                system2Address = ((ExtendedActorSystem)system2).Provider.DefaultAddress;
            }

            // Drop visibility: subscribe to the local systems' EventStream for Akka.Event.Dropped -
            // ArteryRemoting.EnqueueOutbound publishes one directly to the EventStream (bypassing
            // System.DeadLetters.Tell) every time an association's bounded outbound queue is full
            // and a message is silently discarded (at-most-once delivery, by design). Those drops
            // are otherwise invisible here: DeadLetterListener only logs its summary at INFO, and
            // this harness hardcodes akka.loglevel=ERROR. This works regardless of loglevel because
            // it's a direct EventStream subscription, not a log listener. Fresh counter/subscriber
            // per rep (both are scoped to this Benchmark() call and torn down with the systems
            // below), so drop counts never leak across reps or client-count sweeps. In split-client
            // mode only system1 is watched from here - the server process runs its own watcher and
            // prints its own SUSPECT lines to its own stdout, so BOTH sides' drops stay visible.
            var dropCounter = new DropCounter();
            var dropWatcher = system1.ActorOf(Props.Create(() => new DropCounterActor(dropCounter)), "dropWatcher");
            system1.EventStream.Subscribe(dropWatcher, typeof(Dropped));
            system2?.EventStream.Subscribe(dropWatcher, typeof(Dropped));

            List<Task<long>> tasks = new List<Task<long>>();
            // Holds the system1-side actor that needs the initial "go" nudge for each pair: the
            // BenchmarkActor client in ping-pong mode, or the OneWaySenderActor in one-way mode.
            List<IActorRef> primeTargets = new List<IActorRef>();
            // The server-side (RemoteScope-deployed) actor of each pair: the echo in ping-pong,
            // the receiver in one-way. Tracked so split-client mode can PoisonPill them after the
            // rep - the split server is long-lived, so without this every rep would strand its
            // idle actors there. (Single-process mode doesn't need it: system2.Terminate() below
            // reaps everything, and skipping the extra wire traffic keeps default runs untouched.)
            List<IActorRef> serverSideActors = new List<IActorRef>();

            var canStart = system1.ActorOf(Props.Create(() => new AllStartedActor()), "canStart");

            if (_onewayMode)
            {
                // Credit-based flow control: unbounded fire-and-forget would overflow Artery's
                // bounded outbound queue (default capacity 3072/association) and silently starve,
                // since there's no reply loop to naturally pace the sender. Capping in-flight credit
                // at windowSize per pair bounds total unacked messages to clients*windowSize across
                // the whole benchmark. Callers no longer need to manually keep that product under
                // ~2500-3000: ResolveQueueSize() auto-raises outbound-message-queue-size to 2x the
                // product whenever it would exceed 80% of the default capacity (or callers can pin
                // an explicit value via "--qsize") - see the field doc on _effectiveQueueSize. Drops
                // (if the queue is ever undersized regardless) are surfaced via the dropWatcher
                // subscription above rather than silently deadlocking the credit gate.
                var ackEvery = Math.Max(1, windowSize / 2);

                for (var i = 0; i < numberOfClients; i++)
                {
                    var ts = new TaskCompletionSource<long>();
                    tasks.Add(ts.Task);
                    // The completion latch (TaskCompletionSource) is held by the sender on system1,
                    // never by the RemoteScope-deployed receiver: a Deploy/RemoteScope actor's Props
                    // (including constructor args) are genuinely serialized across the wire to
                    // instantiate the actor on the target system - even though system2 happens to be
                    // in-process here, it still goes through the real remote-deployment protocol - and
                    // a TaskCompletionSource can't survive that trip. Completion *authority* still
                    // lives with the receiver's count (it decides when "repeat" has been observed);
                    // it just reports that decision back to the sender via a Complete message so the
                    // sender can fulfil the locally-held latch.
                    var receiver =
                        system1.ActorOf(
                            Props.Create(() => new OneWayReceiverActor(numberOfRepeats, ackEvery))
                                .WithDeploy(new Deploy(new RemoteScope(system2Address))),
                            "receiver" + i);
                    var sender =
                        system1.ActorOf(
                            Props.Create(() => new OneWaySenderActor(numberOfRepeats, windowSize, ackEvery, receiver, ts, _payloadInstance)),
                            "sender" + i);

                    primeTargets.Add(sender);
                    serverSideActors.Add(receiver);

                    canStart.Tell(receiver);
                    canStart.Tell(sender);
                }
            }
            else
            {
                var echoProps = Props.Create(() => new EchoActor()).WithDeploy(new Deploy(new RemoteScope(system2Address)));

                for (var i = 0; i < numberOfClients; i++)
                {
                    var echo = system1.ActorOf(echoProps, "echo" + i);
                    var ts = new TaskCompletionSource<long>();
                    tasks.Add(ts.Task);
                    var receiver =
                        system1.ActorOf(
                            Props.Create(() => new BenchmarkActor(numberOfRepeats, ts, echo)),
                            "benchmark" + i);

                    primeTargets.Add(receiver);
                    serverSideActors.Add(echo);

                    canStart.Tell(echo);
                    canStart.Tell(receiver);
                }
            }

            var rsp = await canStart.Ask(new AllStartedActor.AllStarted(), TimeSpan.FromSeconds(10));
            var testReady = (bool)rsp;
            if (!testReady)
            {
                throw new Exception("Received report that 1 or more remote actor is unable to begin the test. Aborting run.");
            }

            // now that the dispatchers in both ActorSystems are started, we want to measure thread count and other system
            // metrics here - but only the very first benchmark
            if(_firstRun){
                PrintSysInfo(numberOfRepeats);
            }

            var startThreads = Process.GetCurrentProcess().Threads.Count;

            var sw = Stopwatch.StartNew();
            if (_onewayMode)
            {
                // One trigger per sender: OneWaySenderActor sends its whole windowSize credit
                // up-front as soon as it sees Messages.Run, then tops back up on each Ack.
                var run = new Messages.Run();
                primeTargets.ForEach(c => c.Tell(run));
            }
            else
            {
                primeTargets.ForEach(c =>
                {
                    for (var i = 0; i < windowSize; i++) // prime the pump so EndpointWriters can take advantage of their batching model
                        c.Tell(_payloadInstance);
                });
            }
            var waiting = Task.WhenAll(tasks);
            await Task.WhenAll(waiting);
            sw.Stop();
            
            var endThreads = Process.GetCurrentProcess().Threads.Count;

            if (_clientMode)
            {
                // Tidy the long-lived split server between reps (see serverSideActors above).
                // Post-measurement, so the extra wire traffic never lands inside the timed window.
                serverSideActors.ForEach(a => a.Tell(PoisonPill.Instance));
            }

            // Reset the drop subscription before tearing down the systems it's attached to - keeps
            // this rep's count final/stable and avoids relying on subscriber cleanup racing Terminate().
            system1.EventStream.Unsubscribe(dropWatcher);
            system2?.EventStream.Unsubscribe(dropWatcher);
            var dropped = dropCounter.Count;

            // force clean termination (split-client mode: only the local system - the server
            // process owns system2's lifecycle and keeps serving subsequent reps/runs)
            await Task.WhenAll(system2 == null
                ? new[] { system1.Terminate() }
                : new[] { system1.Terminate(), system2.Terminate() });

            var elapsedMilliseconds = sw.ElapsedMilliseconds;
            long throughput = elapsedMilliseconds == 0 ? -1 : (long)Math.Ceiling((double)totalMessagesReceived / elapsedMilliseconds * 1000);
            var foregroundColor = Console.ForegroundColor;
            if (throughput >= bestThroughput)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                bestThroughput = throughput;
                redCount = 0;
            }
            else
            {
                redCount++;
                Console.ForegroundColor = ConsoleColor.Red;
            }

            Console.ForegroundColor = foregroundColor;
            Console.WriteLine("{0,10},{1,8},{2,10},{3,11}, {4,13}, {5,15}", numberOfClients, totalMessagesReceived, throughput, sw.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture), startThreads, endThreads);

            if (dropped > 0)
            {
                // Prominently marked (own line, own color) so a rep with drops is never mistaken
                // for a clean run when skimming the table above - a nonzero count here means the
                // outbound queue overflowed and the throughput/completion result for this rep is
                // SUSPECT (see the drop-visibility comment where dropWatcher is subscribed).
                var prevColor = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine("  *** SUSPECT: dropped {0} (outbound queue overflow) ***", dropped);
                Console.ForegroundColor = prevColor;
            }

            return (redCount <= 3, bestThroughput, redCount);
        }

        /// <summary>
        /// Thread-safe drop tally shared between a <see cref="DropCounterActor"/> and the harness
        /// loop that reads it. Kept as a plain object (rather than actor state read via Ask) so the
        /// count can be read synchronously right after unsubscribing, with no extra message round-trip.
        /// </summary>
        private sealed class DropCounter
        {
            private long _drops;

            public void Increment() => Interlocked.Increment(ref _drops);

            public long Count => Interlocked.Read(ref _drops);
        }

        /// <summary>
        /// Minimal EventStream subscriber that tallies every <see cref="Dropped"/> event it sees into
        /// a <see cref="DropCounter"/>. In single-process mode one instance (living on system1) is
        /// subscribed to BOTH system1's and system2's EventStream for each rep - IActorRef.Tell
        /// doesn't care which ActorSystem published the event, only that the target mailbox is
        /// valid - so a single actor covers drops from either association direction (oneway sender
        /// traffic system1->system2, or ping-pong echo/ack traffic in either direction). In split
        /// mode each process runs its own instance against its own local system(s) and prints its
        /// own SUSPECT lines: the client per rep (see Benchmark()), the server as a long-lived
        /// periodic delta (see RunServer()).
        /// </summary>
        private class DropCounterActor : UntypedActor
        {
            private readonly DropCounter _counter;

            public DropCounterActor(DropCounter counter)
            {
                _counter = counter;
            }

            protected override void OnReceive(object message)
            {
                if (message is Dropped)
                    _counter.Increment();
            }
        }

        private class AllStartedActor : UntypedActor
        {
            public class AllStarted { }

            private readonly HashSet<IActorRef> _actors = new();
            private int _correlationId = 0;

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case IActorRef a:
                        _actors.Add(a);
                        break;
                    case AllStarted a:
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        var s = Sender;
                        var count = _actors.Count;
                        var c = _correlationId++;
                        var t = Task.WhenAll(_actors.Select(
                            x => x.Ask<ActorIdentity>(new Identify(c), cts.Token)));
                        t.ContinueWith(tr =>
                        {
                            return tr.Result.Length == count && tr.Result.All(x => x.MessageId.Equals(c));
                        }, TaskContinuationOptions.OnlyOnRanToCompletion).PipeTo(s);
                        break;
                }
            }
        }

        private class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        private class BenchmarkActor : UntypedActor
        {
            private readonly long _maxExpectedMessages;
            private readonly IActorRef _echo;
            private long _currentMessages = 0;
            private readonly TaskCompletionSource<long> _completion;

            public BenchmarkActor(long maxExpectedMessages, TaskCompletionSource<long> completion, IActorRef echo)
            {
                _maxExpectedMessages = maxExpectedMessages;
                _completion = completion;
                _echo = echo;
            }
            protected override void OnReceive(object message)
            {
                if (_currentMessages < _maxExpectedMessages)
                {
                    _currentMessages++;
                    _echo.Tell(message);
                }
                else
                {
                    _completion.TrySetResult(_maxExpectedMessages);
                }
            }
        }

        /// <summary>
        /// Small dedicated flow-control message used by <see cref="OneWayReceiverActor"/> to grant
        /// the <see cref="OneWaySenderActor"/> more send credit. Kept as a trivial, field-less
        /// marker type (rather than a distinct string constant, e.g. "ack") so it can't collide with
        /// the "hit" payload messages on the wire; it round-trips through Akka's default
        /// NewtonSoftJsonSerializer fallback the same way the "hit" strings do - no custom
        /// serializer/binding is required for this benchmark.
        /// </summary>
        private sealed class Ack
        {
            public static readonly Ack Instance = new();

            private Ack() { }
        }

        /// <summary>
        /// Small dedicated "done" message: <see cref="OneWayReceiverActor"/> sends this back to the
        /// <see cref="OneWaySenderActor"/> once its own count reaches the expected total, since the
        /// receiver - not the sender - is the authority on when the run is actually complete (it's the
        /// side that observed every message actually arrive over the wire).
        /// </summary>
        private sealed class Complete
        {
            public long TotalReceived { get; }

            public Complete(long totalReceived)
            {
                TotalReceived = totalReceived;
            }
        }

        /// <summary>
        /// One-way firehose sender (lives on system1, paired 1:1 with a <see cref="OneWayReceiverActor"/>
        /// on system2). Implements credit-based flow control: unbounded fire-and-forget sending would
        /// overflow Artery's bounded outbound queue (default capacity 3072/association, auto-raised
        /// by ResolveQueueSize() - or overridden via "--qsize" - when window x clients would otherwise
        /// exceed it) and silently stall, so this actor never has more than `windowSize` messages in
        /// flight per pair. It sends its whole window up-front as credit, then tops back up by
        /// `ackEvery` messages every time the receiver grants an <see cref="Ack"/>, until it has sent
        /// `maxMessages` total.
        ///
        /// This actor also owns the latch (<see cref="TaskCompletionSource{TResult}"/>) that the
        /// benchmark harness awaits. It is fulfilled when the receiver reports <see cref="Complete"/> -
        /// the receiver's count is authoritative for "done", but the latch itself has to live here
        /// because it's on system1, never RemoteScope-deployed (see the comment on OneWayReceiverActor
        /// construction in Benchmark() for why a TaskCompletionSource can't live on the receiver).
        /// </summary>
        private class OneWaySenderActor : UntypedActor
        {
            private readonly long _maxMessages;
            private readonly int _windowSize;
            private readonly int _ackEvery;
            private readonly IActorRef _receiver;
            private readonly TaskCompletionSource<long> _completion;
            // The payload every message actually carries - "hit" (default/toy) or the resolved
            // real-payload instance (see Program._payloadInstance/ResolvePayloadInstance()). Unlike
            // ping-pong mode (where one priming send is enough - the same object bounces between
            // BenchmarkActor/EchoActor for the rest of the repeats), one-way mode has no reply loop:
            // every individual message sent here must carry the payload itself.
            private readonly object _payload;
            private long _sent;

            public OneWaySenderActor(long maxMessages, int windowSize, int ackEvery, IActorRef receiver, TaskCompletionSource<long> completion, object payload)
            {
                _maxMessages = maxMessages;
                _windowSize = windowSize;
                _ackEvery = ackEvery;
                _receiver = receiver;
                _completion = completion;
                _payload = payload;
            }

            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Messages.Run:
                        SendBatch(_windowSize);
                        break;
                    case Ack:
                        SendBatch(_ackEvery);
                        break;
                    case Complete c:
                        _completion.TrySetResult(c.TotalReceived);
                        break;
                }
            }

            private void SendBatch(int count)
            {
                for (var i = 0; i < count && _sent < _maxMessages; i++)
                {
                    _receiver.Tell(_payload);
                    _sent++;
                }
            }
        }

        /// <summary>
        /// One-way firehose receiver (deployed remotely onto system2, paired 1:1 with a
        /// <see cref="OneWaySenderActor"/> on system1). Counts every message it receives; every
        /// `ackEvery` messages it grants the sender more credit via a single small <see cref="Ack"/>
        /// reply. When its count reaches `maxExpectedMessages` it reports <see cref="Complete"/> back
        /// to the sender - the receiver's count is authoritative for "done" in one-way mode, even
        /// though (for serialization reasons - see Benchmark()) the actual completion latch lives on
        /// the sender.
        /// </summary>
        private class OneWayReceiverActor : UntypedActor
        {
            private readonly long _maxExpectedMessages;
            private readonly int _ackEvery;
            private long _received;

            public OneWayReceiverActor(long maxExpectedMessages, int ackEvery)
            {
                _maxExpectedMessages = maxExpectedMessages;
                _ackEvery = ackEvery;
            }

            protected override void OnReceive(object message)
            {
                _received++;

                if (_received % _ackEvery == 0)
                    Sender.Tell(Ack.Instance);

                if (_received >= _maxExpectedMessages)
                    Sender.Tell(new Complete(_received));
            }
        }
    }
}
