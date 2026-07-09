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

        // Selected once at startup via a command-line "artery" flag; controls which remote transport
        // both ActorSystems bind. Default is the classic DotNetty TCP transport (the historical
        // baseline); "artery" points the exact same benchmark at Artery.Tcp so the two produce
        // directly comparable msgs/sec numbers over an otherwise-identical workload.
        private static bool _useArtery;

        // Selected once at startup via a command-line "oneway" flag. Default mode is ping-pong
        // (client sends, destination echoes every message back - see EchoActor/BenchmarkActor).
        // "oneway" switches to a Pekko-MaxThroughput-style one-directional firehose: the sender
        // fires a credit-based stream of messages at the receiver with no reply loop, so the
        // benchmark measures pure one-way transport throughput. See OneWaySenderActor /
        // OneWayReceiverActor.
        private static bool _onewayMode;

        // Number of messages used to "prime the pump" for each client before awaiting completion -
        // i.e. the in-flight window size. Defaults to 50 (the historical hard-coded value) but can
        // be overridden via the "window=N" command-line token.
        private static int _windowSize = 50;

        // When set (via the "clients=N" command-line token), pins the benchmark to a single client
        // count instead of sweeping the full GetClientSettings() series.
        private static int? _pinnedClients;

        // When set (via the "iobuf=SIZE" command-line token), overrides Akka.IO's
        // akka.io.tcp.receive-buffer-size / send-buffer-size (default 8k) for both ActorSystems.
        // The raw string is passed through verbatim as a HOCON size value (e.g. "128k", "1m").
        private static string? _ioBufSize;

        // When set (via the "msgs=N" command-line token), overrides the per-client message count
        // (default: the "repeat" constant, 100000L). One-way mode's default run length is often
        // sub-second, which isn't long enough to observe steady-state throughput - this lets callers
        // push the run into multi-second territory without touching the constant.
        private static long? _msgsOverride;

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

            return config;
        }

        private static async Task Main(params string[] args)
        {
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Attempted to elevate process priority, but failed due to {ex.Message} - carrying on at normal process priority.");
            }
            // Args (order-independent): the first numeric arg is timesToRun; the literal "artery"
            // (case-insensitive) selects the Artery.Tcp transport instead of the DotNetty default.
            // "oneway" (case-insensitive) switches from ping-pong to the one-directional firehose
            // mode (see OneWaySenderActor/OneWayReceiverActor). "window=N" overrides the in-flight
            // priming window (default 50); "clients=N" pins the benchmark to a single client count
            // instead of sweeping GetClientSettings(). "iobuf=SIZE" overrides
            // akka.io.tcp.receive-buffer-size / send-buffer-size (e.g. "128k", "1m") for both
            // ActorSystems - default behavior (8k) is unchanged when omitted. "msgs=N" overrides the
            // per-client message count (default 100000) - default behavior is unchanged when omitted.
            // e.g. `RemotePingPong 3 artery` or `RemotePingPong artery oneway window=1000 clients=10 iobuf=128k msgs=500000`.
            _useArtery = args.Any(a => a.Equals("artery", StringComparison.OrdinalIgnoreCase));
            _onewayMode = args.Any(a => a.Equals("oneway", StringComparison.OrdinalIgnoreCase));

            var timesToRun = 1u;
            var timesToRunSet = false;
            foreach (var a in args)
            {
                if (!timesToRunSet && uint.TryParse(a, out var parsed))
                {
                    timesToRun = parsed;
                    timesToRunSet = true;
                    continue;
                }

                if (a.StartsWith("window=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(a.Substring("window=".Length), out var window))
                        _windowSize = window;
                    continue;
                }

                if (a.StartsWith("clients=", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(a.Substring("clients=".Length), out var clients))
                        _pinnedClients = clients;
                    continue;
                }

                if (a.StartsWith("iobuf=", StringComparison.OrdinalIgnoreCase))
                {
                    var iobuf = a.Substring("iobuf=".Length);
                    if (!string.IsNullOrWhiteSpace(iobuf))
                        _ioBufSize = iobuf;
                    continue;
                }

                if (a.StartsWith("msgs=", StringComparison.OrdinalIgnoreCase))
                {
                    if (long.TryParse(a.Substring("msgs=".Length), out var msgs))
                        _msgsOverride = msgs;
                    continue;
                }
            }

            await Start(timesToRun);
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
            Console.WriteLine();

            //Print tables
            Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads");

            _firstRun = false;
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
            var system1 = ActorSystem.Create("SystemA", CreateActorSystemConfig("SystemA", "127.0.0.1", 0));

            var system2 = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", "127.0.0.1", 0));

            List<Task<long>> tasks = new List<Task<long>>();
            // Holds the system1-side actor that needs the initial "go" nudge for each pair: the
            // BenchmarkActor client in ping-pong mode, or the OneWaySenderActor in one-way mode.
            List<IActorRef> primeTargets = new List<IActorRef>();

            var canStart = system1.ActorOf(Props.Create(() => new AllStartedActor()), "canStart");

            var system1Address = ((ExtendedActorSystem)system1).Provider.DefaultAddress;
            var system2Address = ((ExtendedActorSystem)system2).Provider.DefaultAddress;

            if (_onewayMode)
            {
                // Credit-based flow control: unbounded fire-and-forget would overflow Artery's
                // bounded outbound queue (capacity 3072/association) and silently starve, since
                // there's no reply loop to naturally pace the sender. Capping in-flight credit at
                // windowSize per pair bounds total unacked messages to clients*windowSize across the
                // whole benchmark - callers are responsible for keeping that product under ~2500-3000.
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
                            Props.Create(() => new OneWaySenderActor(numberOfRepeats, windowSize, ackEvery, receiver, ts)),
                            "sender" + i);

                    primeTargets.Add(sender);

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
                        c.Tell("hit");
                });
            }
            var waiting = Task.WhenAll(tasks);
            await Task.WhenAll(waiting);
            sw.Stop();
            
            var endThreads = Process.GetCurrentProcess().Threads.Count;

            // force clean termination
            await Task.WhenAll(new[] { system1.Terminate(), system2.Terminate() });

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
        /// overflow Artery's bounded outbound queue (capacity 3072/association) and silently stall, so
        /// this actor never has more than `windowSize` messages in flight per pair. It sends its whole
        /// window up-front as credit, then tops back up by `ackEvery` messages every time the receiver
        /// grants an <see cref="Ack"/>, until it has sent `maxMessages` total.
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
            private long _sent;

            public OneWaySenderActor(long maxMessages, int windowSize, int ackEvery, IActorRef receiver, TaskCompletionSource<long> completion)
            {
                _maxMessages = maxMessages;
                _windowSize = windowSize;
                _ackEvery = ackEvery;
                _receiver = receiver;
                _completion = completion;
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
                    _receiver.Tell("hit");
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
