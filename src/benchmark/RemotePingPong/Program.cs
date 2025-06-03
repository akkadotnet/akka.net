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
using Akka.Event;

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

        public static Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"
            akka {
              actor.provider = remote
              loglevel = ERROR
              suppress-json-serializer-warning = on
              log-dead-letters = off

              remote {
                log-remote-lifecycle-events = off

                dot-netty.tcp {
                    port = 0
                    hostname = ""localhost""
                }
                
              }
            }");

            var bindingConfig =
                ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.hostname = """ + ipOrHostname + @"""")
                    .WithFallback(ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port = " + port));

            return bindingConfig.WithFallback(baseConfig);
        }

        private static async Task Main(params string[] args)
        {
            // NEW: Parse command line arguments for recycler testing
            var config = ParseArgs(args);
            PrintRecyclerConfig(config);
            
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Attempted to elevate process priority, but failed due to {ex.Message} - carrying on at normal process priority.");
            }

            // NEW: Run comparison test if requested
            if (config.CompareMode)
            {
                await RunComparison(config.TimesToRun);
            }
            else
            {
                await Start(config.TimesToRun);
            }
        }

        // NEW: Configuration class for recycler testing
        private class BenchmarkConfig
        {
            public uint TimesToRun { get; set; } = 1;
            public bool CompareMode { get; set; } = false;
            public bool DisableRecycler { get; set; } = false;
            public bool MonitorExceptions { get; set; } = false;
        }

        // NEW: Parse command line arguments
        private static BenchmarkConfig ParseArgs(string[] args)
        {
            var config = new BenchmarkConfig();
            
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--times":
                    case "-t":
                        if (i + 1 < args.Length && uint.TryParse(args[i + 1], out var times))
                        {
                            config.TimesToRun = times;
                            i++; // Skip next argument
                        }
                        break;
                    case "--compare":
                    case "-c":
                        config.CompareMode = true;
                        break;
                    case "--disable-recycler":
                    case "-d":
                        config.DisableRecycler = true;
                        break;
                    case "--monitor-exceptions":
                    case "-m":
                        config.MonitorExceptions = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        Environment.Exit(0);
                        break;
                    default:
                        // Legacy: first argument as times to run
                        if (i == 0 && uint.TryParse(args[0], out var legacyTimes))
                        {
                            config.TimesToRun = legacyTimes;
                        }
                        break;
                }
            }

            // Set recycler environment variable if requested
            if (config.DisableRecycler)
            {
                Environment.SetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread", "0");
            }

            return config;
        }

        // NEW: Print usage information
        private static void PrintUsage()
        {
            Console.WriteLine("RemotePingPong ThreadLocalPool Recycler Test");
            Console.WriteLine();
            Console.WriteLine("Usage: RemotePingPong [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  -t, --times <n>           Number of benchmark runs (default: 1)");
            Console.WriteLine("  -c, --compare             Run comparison: recycler enabled vs disabled");
            Console.WriteLine("  -d, --disable-recycler    Disable ThreadLocalPool recycler");
            Console.WriteLine("  -m, --monitor-exceptions  Monitor for ThreadLocalPool exceptions");
            Console.WriteLine("  -h, --help               Show this help");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  RemotePingPong --compare                    # Compare both modes");
            Console.WriteLine("  RemotePingPong --disable-recycler -t 3      # Run 3 times with recycler disabled");
            Console.WriteLine("  RemotePingPong --monitor-exceptions         # Monitor for exceptions");
        }

        // NEW: Print recycler configuration
        private static void PrintRecyclerConfig(BenchmarkConfig config)
        {
            Console.WriteLine("=== ThreadLocalPool Recycler Configuration ===");
            
            var recyclerSetting = Environment.GetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread");
            var isDisabled = recyclerSetting == "0";
            
            Console.WriteLine($"Environment Variable: {recyclerSetting ?? "NOT SET"}");
            Console.WriteLine($"Recycler Status: {(isDisabled ? "DISABLED" : "ENABLED")}");
            Console.WriteLine($"Compare Mode: {config.CompareMode}");
            Console.WriteLine($"Monitor Exceptions: {config.MonitorExceptions}");
            Console.WriteLine($"Server GC: {GCSettings.IsServerGC}");
            Console.WriteLine();
        }

        // NEW: Run comparison between enabled and disabled recycler
        private static async Task RunComparison(uint timesToRun)
        {
            Console.WriteLine("=== COMPARISON MODE: Testing with and without ThreadLocalPool recycler ===");
            Console.WriteLine();

            // Test with recycler ENABLED
            Console.WriteLine(">>> PHASE 1: ThreadLocalPool Recycler ENABLED <<<");
            Environment.SetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread", null);
            PrintRecyclerStatus();
            var enabledResults = await RunBenchmarkWithMetrics(timesToRun, "ENABLED");

            Console.WriteLine();
            Console.WriteLine(">>> PHASE 2: ThreadLocalPool Recycler DISABLED <<<");
            Environment.SetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread", "0");
            PrintRecyclerStatus();
            var disabledResults = await RunBenchmarkWithMetrics(timesToRun, "DISABLED");

            // Print comparison
            PrintComparison(enabledResults, disabledResults);
        }

        // NEW: Print current recycler status
        private static void PrintRecyclerStatus()
        {
            var setting = Environment.GetEnvironmentVariable("io.netty.recycler.maxCapacityPerThread");
            Console.WriteLine($"io.netty.recycler.maxCapacityPerThread = {setting ?? "NOT SET"}");
            Console.WriteLine($"Recycler Status: {(setting == "0" ? "DISABLED" : "ENABLED")}");
            Console.WriteLine();
        }

        // NEW: Benchmark results class
        private class BenchmarkResults
        {
            public string Mode { get; set; }
            public List<long> Throughputs { get; set; } = new();
            public long TotalMemoryBefore { get; set; }
            public long TotalMemoryAfter { get; set; }
            public int Gen0CollectionsBefore { get; set; }
            public int Gen0CollectionsAfter { get; set; }
            public int Gen1CollectionsBefore { get; set; }
            public int Gen1CollectionsAfter { get; set; }
            public int Gen2CollectionsBefore { get; set; }
            public int Gen2CollectionsAfter { get; set; }
            public int ExceptionCount { get; set; }
            public TimeSpan TotalTime { get; set; }
        }

        // NEW: Run benchmark with detailed metrics collection
        private static async Task<BenchmarkResults> RunBenchmarkWithMetrics(uint timesToRun, string mode)
        {
            var results = new BenchmarkResults { Mode = mode };
            var exceptionMonitor = new ThreadLocalPoolExceptionMonitor();
            
            // Collect initial metrics
            var totalMemoryBefore = GC.GetTotalMemory(true);
            var gen0Before = GC.CollectionCount(0);
            var gen1Before = GC.CollectionCount(1);
            var gen2Before = GC.CollectionCount(2);
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                for (var i = 0; i < timesToRun; i++)
                {
                    var redCount = 0;
                    var bestThroughput = 0L;
                    foreach (var throughput in GetClientSettings())
                    {
                        var result = await Benchmark(throughput, repeat, bestThroughput, redCount, exceptionMonitor);
                        bestThroughput = result.Item2;
                        redCount = result.Item3;
                        results.Throughputs.Add(bestThroughput);
                    }
                }
            }
            finally
            {
                stopwatch.Stop();
                
                // Collect final metrics
                var totalMemoryAfter = GC.GetTotalMemory(true);
                var gen0After = GC.CollectionCount(0);
                var gen1After = GC.CollectionCount(1);
                var gen2After = GC.CollectionCount(2);
                
                results.TotalMemoryBefore = totalMemoryBefore;
                results.TotalMemoryAfter = totalMemoryAfter;
                results.Gen0CollectionsBefore = gen0Before;
                results.Gen0CollectionsAfter = gen0After;
                results.Gen1CollectionsBefore = gen1Before;
                results.Gen1CollectionsAfter = gen1After;
                results.Gen2CollectionsBefore = gen2Before;
                results.Gen2CollectionsAfter = gen2After;
                results.ExceptionCount = exceptionMonitor.ThreadLocalPoolExceptionCount;
                results.TotalTime = stopwatch.Elapsed;
            }
            
            return results;
        }

        // NEW: ThreadLocalPool exception monitor
        private class ThreadLocalPoolExceptionMonitor
        {
            public int ThreadLocalPoolExceptionCount { get; private set; }
            
            public ThreadLocalPoolExceptionMonitor()
            {
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            }
            
            private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
            {
                if (e.ExceptionObject is Exception ex && 
                    IsThreadLocalPoolException(ex))
                {
                    Interlocked.Increment(ref ThreadLocalPoolExceptionCount);
                    Console.WriteLine($"[EXCEPTION] ThreadLocalPool NullReferenceException detected: {ex.Message}");
                    Console.WriteLine($"[EXCEPTION] Stack trace: {ex.StackTrace}");
                }
            }
            
            private static bool IsThreadLocalPoolException(Exception ex)
            {
                return ex is NullReferenceException && 
                       (ex.StackTrace?.Contains("ThreadLocalPool") == true ||
                        ex.StackTrace?.Contains("WeakOrderQueue") == true);
            }
        }

        // NEW: Print comparison results
        private static void PrintComparison(BenchmarkResults enabled, BenchmarkResults disabled)
        {
            Console.WriteLine();
            Console.WriteLine("=== RECYCLER COMPARISON RESULTS ===");
            Console.WriteLine();
            
            var avgThroughputEnabled = enabled.Throughputs.Count > 0 ? enabled.Throughputs.Average() : 0;
            var avgThroughputDisabled = disabled.Throughputs.Count > 0 ? disabled.Throughputs.Average() : 0;
            var throughputDelta = avgThroughputDisabled - avgThroughputEnabled;
            var throughputPercentChange = avgThroughputEnabled > 0 ? (throughputDelta / avgThroughputEnabled) * 100 : 0;
            
            Console.WriteLine($"{"Metric",-30} {"Enabled",-15} {"Disabled",-15} {"Delta",-15} {"% Change",-10}");
            Console.WriteLine(new string('=', 85));
            
            Console.WriteLine($"{"Avg Throughput (msg/sec)",-30} {avgThroughputEnabled,-15:N0} {avgThroughputDisabled,-15:N0} {throughputDelta,-15:N0} {throughputPercentChange,-10:F1}%");
            
            var memoryDeltaEnabled = enabled.TotalMemoryAfter - enabled.TotalMemoryBefore;
            var memoryDeltaDisabled = disabled.TotalMemoryAfter - disabled.TotalMemoryBefore;
            var memoryDiff = memoryDeltaDisabled - memoryDeltaEnabled;
            var memoryPercentChange = memoryDeltaEnabled != 0 ? ((double)memoryDiff / memoryDeltaEnabled) * 100 : 0;
            
            Console.WriteLine($"{"Memory Delta (bytes)",-30} {memoryDeltaEnabled,-15:N0} {memoryDeltaDisabled,-15:N0} {memoryDiff,-15:N0} {memoryPercentChange,-10:F1}%");
            
            var gen0DeltaEnabled = enabled.Gen0CollectionsAfter - enabled.Gen0CollectionsBefore;
            var gen0DeltaDisabled = disabled.Gen0CollectionsAfter - disabled.Gen0CollectionsBefore;
            var gen0Diff = gen0DeltaDisabled - gen0DeltaEnabled;
            var gen0PercentChange = gen0DeltaEnabled > 0 ? ((double)gen0Diff / gen0DeltaEnabled) * 100 : 0;
            
            Console.WriteLine($"{"Gen 0 Collections",-30} {gen0DeltaEnabled,-15} {gen0DeltaDisabled,-15} {gen0Diff,-15} {gen0PercentChange,-10:F1}%");
            
            var gen1DeltaEnabled = enabled.Gen1CollectionsAfter - enabled.Gen1CollectionsBefore;
            var gen1DeltaDisabled = disabled.Gen1CollectionsAfter - disabled.Gen1CollectionsBefore;
            var gen1Diff = gen1DeltaDisabled - gen1DeltaEnabled;
            var gen1PercentChange = gen1DeltaEnabled > 0 ? ((double)gen1Diff / gen1DeltaEnabled) * 100 : 0;
            
            Console.WriteLine($"{"Gen 1 Collections",-30} {gen1DeltaEnabled,-15} {gen1DeltaDisabled,-15} {gen1Diff,-15} {gen1PercentChange,-10:F1}%");
            
            Console.WriteLine($"{"ThreadLocalPool Exceptions",-30} {enabled.ExceptionCount,-15} {disabled.ExceptionCount,-15} {disabled.ExceptionCount - enabled.ExceptionCount,-15} {"N/A",-10}");
            Console.WriteLine($"{"Total Time",-30} {enabled.TotalTime.TotalSeconds,-15:F2}s {disabled.TotalTime.TotalSeconds,-15:F2}s {(disabled.TotalTime - enabled.TotalTime).TotalSeconds,-15:F2}s {"N/A",-10}");
            
            Console.WriteLine();
            Console.WriteLine("=== SUMMARY ===");
            if (disabled.ExceptionCount == 0 && enabled.ExceptionCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("✓ SUCCESS: Disabling recycler eliminated ThreadLocalPool exceptions!");
                Console.ResetColor();
            }
            else if (disabled.ExceptionCount > 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("✗ WARNING: ThreadLocalPool exceptions still occurring with recycler disabled");
                Console.ResetColor();
            }
            
            if (Math.Abs(throughputPercentChange) < 10)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✓ Performance impact acceptable: {throughputPercentChange:F1}% change in throughput");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Significant performance impact: {throughputPercentChange:F1}% change in throughput");
                Console.ResetColor();
            }
        }

        private static bool _firstRun = true;

        private static void PrintSysInfo(){
            var processorCount = Environment.ProcessorCount;
            if (processorCount == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Failed to read processor count..");
                return;
            }

            Console.WriteLine("OSVersion:                         {0}", Environment.OSVersion);
            Console.WriteLine("ProcessorCount:                    {0}", processorCount);
            Console.WriteLine("ClockSpeed:                        {0} MHZ", CpuSpeed());
            Console.WriteLine("Actor Count:                       {0}", processorCount * 2);
            Console.WriteLine("Messages sent/received per client: {0}  ({0:0e0})", repeat*2);
            Console.WriteLine("Is Server GC:                      {0}", GCSettings.IsServerGC);
            Console.WriteLine("Thread count:                      {0}", Process.GetCurrentProcess().Threads.Count);
            Console.WriteLine();

            //Print tables
            Console.WriteLine("Num clients, Total [msg], Msgs/sec, Total [ms], Start Threads, End Threads");

            _firstRun = false;
        }

        const long repeat = 100000L;

        private static async Task Start(uint timesToRun)
        {         
            for (var i = 0; i < timesToRun; i++)
            {
                var redCount = 0;
                var bestThroughput = 0L;
                foreach (var throughput in GetClientSettings())
                {
                    var result1 = await Benchmark(throughput, repeat, bestThroughput, redCount);
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

        private static async Task<(bool, long, int)> Benchmark(int numberOfClients, long numberOfRepeats, long bestThroughput, int redCount, ThreadLocalPoolExceptionMonitor exceptionMonitor = null)
        {
            var totalMessagesReceived = GetTotalMessagesReceived(numberOfClients, numberOfRepeats);
            var system1 = ActorSystem.Create("SystemA", CreateActorSystemConfig("SystemA", "127.0.0.1", 0));

            var system2 = ActorSystem.Create("SystemB", CreateActorSystemConfig("SystemB", "127.0.0.1", 0));

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
                throw new Exception("Received report that 1 or more remote actor is unable to begin the test. Aborting run.");
            }

            // now that the dispatchers in both ActorSystems are started, we want to measure thread count and other system
            // metrics here - but only the very first benchmark
            if(_firstRun){
                PrintSysInfo();
            }

            var startThreads = Process.GetCurrentProcess().Threads.Count;

            var sw = Stopwatch.StartNew();
            receivers.ForEach(c =>
            {
                for (var i = 0; i < 50; i++) // prime the pump so EndpointWriters can take advantage of their batching model
                    c.Tell("hit");
            });
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
    }
}
