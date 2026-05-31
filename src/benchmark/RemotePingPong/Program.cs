//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
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

        /// <summary>
        /// Selects which message-body serializer is used for user messages during the benchmark.
        /// This is orthogonal to the transport-envelope codec (protobuf vs messagepack).
        /// </summary>
        internal enum SerializerMode
        {
            /// <summary>
            /// Akka.NET built-in NewtonSoft JSON serializer (the historic default).
            /// Good for correctness checks; not the fastest~
            /// </summary>
            Default,

            /// <summary>
            /// Hyperion binary serializer — fast, schema-tolerant.
            /// Requires <c>Akka.Serialization.Hyperion</c> package. UwU
            /// </summary>
            Hyperion,

            /// <summary>
            /// MessagePack typeless serializer — very compact + fast.
            /// Requires <c>Akka.Serialization.MessagePack</c> package. Nyaa~!
            /// </summary>
            MsgPack,
        }

        /// <summary>
        /// Controls what message body type is sent in the ping-pong loop.
        /// </summary>
        internal enum PayloadMode
        {
            /// <summary>
            /// Sends a primitive <see cref="long"/> payload (fast path / primitive serializer path).
            /// </summary>
            Primitive,

            /// <summary>
            /// Sends a custom object payload so user serializers (Hyperion/MessagePack/JSON)
            /// are actually exercised end-to-end.
            /// </summary>
            SerializedObject,
        }

        private static PayloadMode ParsePayloadMode(string? arg)
        {
            return (arg ?? "").ToLowerInvariant() switch
            {
                "object" or "serialized" or "serializer" => PayloadMode.SerializedObject,
                _ => PayloadMode.Primitive,
            };
        }

        private static string PayloadLabel(PayloadMode mode) => mode switch
        {
            PayloadMode.SerializedObject => "Custom object (serializer path)",
            _ => "Primitive long",
        };

        /// <summary>
        /// Parses a serializer mode string from the command line.
        /// Valid values (case-insensitive): "default", "hyperion", "msgpack", "messagepack".
        /// Defaults to <see cref="SerializerMode.Default"/> when unrecognised.
        /// </summary>
        private static SerializerMode ParseSerializerMode(string? arg)
        {
            return (arg ?? "").ToLowerInvariant() switch
            {
                "hyperion"                    => SerializerMode.Hyperion,
                "msgpack" or "messagepack"    => SerializerMode.MsgPack,
                _                             => SerializerMode.Default,
            };
        }

        private static string SerializerLabel(SerializerMode mode) => mode switch
        {
            SerializerMode.Hyperion => "Hyperion",
            SerializerMode.MsgPack  => "MessagePack (typeless)",
            _                       => "JSON (default)",
        };

        /// <summary>
        /// Selects which Akka.Remote transport driver + PDU codec to use for the benchmark run.
        /// </summary>
        internal enum TransportMode
        {
            /// <summary>Legacy DotNetty TCP transport with protobuf codec (the historical baseline).</summary>
            DotNetty,

            /// <summary>System.IO.Pipelines TCP transport with protobuf codec (wire-compatible).</summary>
            PipeProtobuf,

            /// <summary>System.IO.Pipelines TCP transport with MessagePack codec (fastest, cluster-wide opt-in).</summary>
            PipeMsgPack,
        }

        /// <summary>
        /// Parses a transport mode string from the command line.
        /// Valid values (case-insensitive): "dotnetty", "pipe", "pipe-protobuf", "pipe-msgpack", "messagepack".
        /// Defaults to <see cref="TransportMode.DotNetty"/> when the string is empty / unrecognised.
        /// </summary>
        private static TransportMode ParseTransportMode(string? arg)
        {
            return (arg ?? "").ToLowerInvariant() switch
            {
                "pipe" or "pipe-protobuf" or "pipelines" => TransportMode.PipeProtobuf,
                "pipe-msgpack" or "messagepack" or "msgpack" => TransportMode.PipeMsgPack,
                _ => TransportMode.DotNetty,
            };
        }

        private static string TransportLabel(TransportMode mode) => mode switch
        {
            TransportMode.PipeProtobuf => "Pipe/TCP + Protobuf",
            TransportMode.PipeMsgPack  => "Pipe/TCP + MessagePack",
            _                          => "DotNetty/TCP + Protobuf",
        };

        [Serializable]
        private sealed class BenchmarkEnvelope
        {
            public long SequenceNr { get; set; }

            public string Marker { get; set; } = "hit";

            public DateTime TimestampUtc { get; set; }
        }

        private static object CreatePingPayload(PayloadMode mode) => mode switch
        {
            // CopilotNotes: Primitive mode intentionally keeps serializer overhead minimal.
            PayloadMode.Primitive => 1L,
            _ => new BenchmarkEnvelope
            {
                SequenceNr = 1L,
                Marker = "hit",
                TimestampUtc = DateTime.UtcNow,
            },
        };

        public static Config CreateActorSystemConfig(
            string actorSystemName,
            string ipOrHostname,
            int port,
            TransportMode mode = TransportMode.DotNetty,
            SerializerMode serializerMode = SerializerMode.Default)
        {
            // ── Base config shared by all modes ──────────────────────────────
            var baseConfig = ConfigurationFactory.ParseString(@"
            akka {
              actor.provider = remote
              loglevel = ERROR
              suppress-json-serializer-warning = on
              log-dead-letters = off
              remote {
                log-remote-lifecycle-events = off
              }
            }");

            // ── Serializer-specific overrides ─────────────────────────────────
            // CopilotNotes: Both Hyperion and MessagePack bind System.Object so they
            // handle every user message. Default leaves the out-of-box JSON serializer.
            Config serializerConfig = serializerMode switch
            {
                SerializerMode.Hyperion => ConfigurationFactory.ParseString(@"
                    akka.actor {
                        serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
                        serialization-bindings {
                            ""System.Object"" = hyperion
                        }
                    }"),

                SerializerMode.MsgPack => ConfigurationFactory.ParseString(@"
                    akka.actor {
                        serializers.messagepack = ""Akka.Serialization.MessagePack.MsgPackSerializer, Akka.Serialization.MessagePack""
                        serialization-bindings {
                            ""System.Object"" = messagepack
                        }
                    }"),

                // CopilotNotes: Explicitly enable pooled StringBuilder for JSON so we get
                // memory-friendly serialization even on the default path~ uwu
                _ => ConfigurationFactory.ParseString(@"
                    akka.actor.serialization-settings.json {
                        use-pooled-string-builder = true
                        pooled-string-builder-minsize = 2048
                        pooled-string-builder-maxsize = 32768
                    }"),
            };

            // ── Transport-specific overrides ─────────────────────────────────
            Config transportConfig = mode switch
            {
                TransportMode.PipeProtobuf => ConfigurationFactory.ParseString($@"
                    akka.remote {{
                        enabled-transports = [""akka.remote.pipe.tcp""]
                        pipe.tcp {{
                            hostname = ""{ipOrHostname}""
                            port     = {port}
                            envelope = protobuf
                        }}
                    }}"),

                TransportMode.PipeMsgPack => ConfigurationFactory.ParseString($@"
                    akka.remote {{
                        enabled-transports = [""akka.remote.pipe.tcp""]
                        pipe.tcp {{
                            hostname = ""{ipOrHostname}""
                            port     = {port}
                            envelope = messagepack
                        }}
                    }}"),

                // Default: DotNetty
                _ => ConfigurationFactory.ParseString($@"
                    akka.remote {{
                        enabled-transports = [""akka.remote.dot-netty.tcp""]
                        dot-netty.tcp {{
                            hostname = ""{ipOrHostname}""
                            port     = {port}
                        }}
                    }}"),
            };

            // CopilotNotes: Merge order is: transport > serializer > base.
            // WithFallback means "use this if not already set", so highest priority goes first.
            return transportConfig
                .WithFallback(serializerConfig)
                .WithFallback(baseConfig);
        }

        private static async Task Main(params string[] args)
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(
                    $"Attempted to elevate process priority, but failed due to {ex.Message} - carrying on at normal process priority.");
            }

            // ── Parse args ────────────────────────────────────────────────────
            // Usage: RemotePingPong [timesToRun] [transport] [serializer] [payload]
            // transport:  dotnetty | pipe | pipe-msgpack
            // serializer: default  | hyperion | msgpack
            // payload:    primitive | object
            uint timesToRun = 1;
            var  transportMode   = TransportMode.DotNetty;
            var  serializerMode  = SerializerMode.Default;
            var  payloadMode     = PayloadMode.Primitive;

            if (args.Length >= 1 && !uint.TryParse(args[0], out timesToRun))
                timesToRun = 1;
            if (args.Length >= 2)
                transportMode = ParseTransportMode(args[1]);
            if (args.Length >= 3)
                serializerMode = ParseSerializerMode(args[2]);
            if (args.Length >= 4)
                payloadMode = ParsePayloadMode(args[3]);

            // timesToRun = 1;
            // transportMode = TransportMode.PipeMsgPack;
            // serializerMode = SerializerMode.MsgPack;
            // payloadMode = PayloadMode.SerializedObject;
            
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"Transport mode: {TransportLabel(transportMode)}");
            Console.WriteLine($"Serializer:     {SerializerLabel(serializerMode)}");
            Console.WriteLine($"Payload:        {PayloadLabel(payloadMode)}");
            Console.ResetColor();

            await Start(timesToRun, transportMode, serializerMode, payloadMode);
        }

        private static bool _firstRun = true;

        private static void PrintSysInfo(TransportMode mode, SerializerMode serializerMode, PayloadMode payloadMode)
        {
            var processorCount = Environment.ProcessorCount;
            if (processorCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read processor count..");
                return;
            }

            Console.WriteLine("Transport:                         {0}", TransportLabel(mode));
            Console.WriteLine("Serializer:                        {0}", SerializerLabel(serializerMode));
            Console.WriteLine("Payload:                           {0}", PayloadLabel(payloadMode));
            Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:                    {0}", processorCount);
            Console.WriteLine("ClockSpeed:                        {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:                       {0}", processorCount * 2);
            Console.WriteLine("Messages sent/received per client: {0}  ({0:0e0})", repeat * 2);
            Console.WriteLine("Is Server GC:                      {0}", GCSettings.IsServerGC);
            Console.WriteLine("Thread count:                      {0}", Process.GetCurrentProcess().Threads.Count);
            Console.WriteLine();
            Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads");

            _firstRun = false;
        }

        const long repeat = 100000L;

        private static async Task Start(
            uint timesToRun,
            TransportMode mode,
            SerializerMode serializerMode,
            PayloadMode payloadMode)
        {
            for (var i = 0; i < timesToRun; i++)
            {
                var redCount = 0;
                var bestThroughput = 0L;
                foreach (var throughput in GetClientSettings())
                {
                    var result1 = await Benchmark(
                        throughput,
                        repeat,
                        bestThroughput,
                        redCount,
                        mode,
                        serializerMode,
                        payloadMode);
                    bestThroughput = result1.Item2;
                    redCount = result1.Item3;
                }
            }

            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("Done..");
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
            return numberOfClients * numberOfRepeats * 2;
        }

        private static async Task<(bool, long, int)> Benchmark(
            int numberOfClients,
            long numberOfRepeats,
            long bestThroughput,
            int redCount,
            TransportMode mode,
            SerializerMode serializerMode,
            PayloadMode payloadMode)
        {
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfClients, numberOfRepeats);
            var system1 = ActorSystem.Create("SystemA", CreateActorSystemConfig("SystemA", "127.0.0.1", 0, mode, serializerMode));
            var system2 = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", "127.0.0.1", 0, mode, serializerMode));

            List<Task<long>> tasks = new List<Task<long>>();
            List<IActorRef> receivers = new List<IActorRef>();

            var canStart = system1.ActorOf(Props.Create(() => new AllStartedActor()), "canStart");

            var system1Address = ((ExtendedActorSystem)system1).Provider.DefaultAddress;
            var system2Address = ((ExtendedActorSystem)system2).Provider.DefaultAddress;

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

                receivers.Add(receiver);

                canStart.Tell(echo);
                canStart.Tell(receiver);
            }

            var rsp = await canStart.Ask(new AllStartedActor.AllStarted(), TimeSpan.FromSeconds(10));
            var testReady = (bool)rsp;
            if (!testReady)
            {
                throw new Exception(
                    "Received report that 1 or more remote actor is unable to begin the test. Aborting run.");
            }

            if (_firstRun)
            {
                PrintSysInfo(mode, serializerMode, payloadMode);
            }

            var startThreads = Process.GetCurrentProcess().Threads.Count;
            var pingPayload = CreatePingPayload(payloadMode);

            var sw = Stopwatch.StartNew();
            receivers.ForEach(c =>
            {
                for (var i = 0; i < 50; i++) // prime the pump
                    c.Tell(pingPayload);
            });
            await Task.WhenAll(tasks);
            sw.Stop();

            var endThreads = Process.GetCurrentProcess().Threads.Count;

            await Task.WhenAll(new[] { system1.Terminate(), system2.Terminate() });

            var elapsedMilliseconds = sw.ElapsedMilliseconds;
            long throughput = elapsedMilliseconds == 0
                ? -1
                : (long)Math.Ceiling((double)totalMessagesReceived / elapsedMilliseconds * 1000);

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
            Console.WriteLine(
                "{0,10},{1,8},{2,10},{3,11}, {4,13}, {5,15}",
                numberOfClients,
                totalMessagesReceived,
                throughput,
                sw.Elapsed.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture),
                startThreads,
                endThreads);

            return (redCount <= 3, bestThroughput, redCount);
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
    }
}
