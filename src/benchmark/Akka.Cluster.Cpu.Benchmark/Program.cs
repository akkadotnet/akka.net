using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NDesk.Options;
using Tmds.Utils;
using Universe.CpuUsage;

namespace Akka.Cluster.Cpu.Benchmark
{
    public static class Program
    {
        private const int DefaultSampleDuration = 5; // in seconds
        private const int DefaultDelay = 5; // in seconds
        private const int DefaultRepeat = 60;
        private const int DefaultClusterSize = 9;

        private const int DefaultWarmUpRepeat = 5;
        
        private static readonly List<CpuUsage> Usages = new List<CpuUsage>();
        private static readonly List<Process> Processes = new List<Process>();
        
        public static async Task<int> Main(string[] args)
        {
            // ExecFunction hook
            if (ExecFunction.IsExecFunctionCommand(args))
                return ExecFunction.Program.Main(args);

            // Argument parsing
            var sampleDuration = DefaultSampleDuration;
            var delay = DefaultDelay;
            var repeat = DefaultRepeat;
            var clusterSize = DefaultClusterSize;
            var warmUp = DefaultWarmUpRepeat;
            var showHelp = false;

            var optionSet = new OptionSet()
                .Add(
                    "d|sample-duration=", 
                    "Sets the sample point duration in seconds. Default: 5 seconds", 
                    s => { sampleDuration = int.Parse(s); })
                .Add(
                    "delay=",
                    "Sets the initial delay to wait for cluster to stabilize before benchmark starts in seconds. Default: 5 seconds",
                    s => { delay = int.Parse(s); })
                .Add(
                    "s|samples=",
                    "Sets how many samples are taken during the benchmark. Default: 60 samples",
                    s => { repeat = int.Parse(s); })
                .Add(
                    "c|cluster-size=",
                    "Sets how many nodes to be added to the cluster in addition to the tested node. Default: 9 nodes",
                    s => { clusterSize = int.Parse(s); })
                .Add(
                    "w|warm-up-count=",
                    "Sets how many blank samples to be performed as benchmark warm-up before benchmark starts. Default: 5 samples",
                    s => { warmUp = int.Parse(s); })
                .Add(
                    "h|?|help",
                    "Shows help",
                    s => { showHelp = s != null; });

            optionSet.Parse(args);
            
            if (showHelp)
            {
                Console.WriteLine("Usage: dotnet run -c Release");
                Console.WriteLine("Usage: dotnet run -c Release -- [OPTIONS]");
                Console.WriteLine("Usage: Akka.Cluster.Cpu.Benchmark [OPTIONS]");
                Console.WriteLine("Options:");
                optionSet.WriteOptionDescriptions(Console.Out);
                return 0;
            }
            
            // Start the benchmark node
            var node = new BenchmarkNode(0);
            node.Start();
            
            var executor = new FunctionExecutor(o =>
            {
                o.StartInfo.RedirectStandardError = true;
                o.OnExit = p =>
                {
                    if (p.ExitCode != 0)
                    {
                        var message =
                            "Function execution failed with exit code: " +
                            $"{p.ExitCode}{Environment.NewLine}{p.StandardError.ReadToEnd()}";
                        throw new Exception(message);
                    }
                };
            });

            // Spin up cluster nodes
            foreach (var portOffset in Enumerable.Range(1, clusterSize))
            {
                Processes.Add(executor.Start(BenchmarkNode.EntryPoint, new []
                {
                    portOffset.ToString()
                }));
            }

            // Wait until things settles down 
            await Task.Delay(TimeSpan.FromSeconds(delay));

            // Warm up
            foreach (var i in Enumerable.Range(1, warmUp))
            {
                var start = CpuUsage.GetByProcess();
                await Task.Delay(TimeSpan.FromSeconds(sampleDuration));
                var end = CpuUsage.GetByProcess();
                var final = end - start;
                
                Console.WriteLine($"{i}. [Warmup] {final}");
            }
            Console.WriteLine();

            // Start benchmark
            foreach (var i in Enumerable.Range(1, repeat))
            {
                var start = CpuUsage.GetByProcess();
                await Task.Delay(TimeSpan.FromSeconds(sampleDuration));
                var end = CpuUsage.GetByProcess();
                var final = end - start;
                
                Console.WriteLine($"{i}. Cpu Usage: {final}");
                Usages.Add(final.Value);
            }

            // Kill cluster node processes
            foreach (var process in Processes)
            {
                process.Kill();
                process.Dispose();
            }

            // Stop benchmark node
            await node.StopAsync();

            // Generate csv report
            var now = DateTime.Now;
            var sb = new StringBuilder();
            sb.AppendLine($"CPU Benchmark {now}");
            sb.AppendLine($"Sample Time,{sampleDuration},second(s)");
            sb.AppendLine($"Sample points,{repeat}");
            sb.AppendLine($"Cluster size,{clusterSize},node(s)");
            sb.AppendLine("Sample time,User usage,User percent,Kernel usage,Kernel percent,Total usage,Total percent");
            foreach (var iter in Enumerable.Range(1, repeat))
            {
                var usage = Usages[iter - 1];
                var user = usage.UserUsage.TotalSeconds;
                var kernel = usage.KernelUsage.TotalSeconds;
                var total = usage.TotalMicroSeconds / 1000000.0;
                sb.AppendLine($"{iter * sampleDuration},{user},{(user/sampleDuration)*100},{kernel},{(kernel/sampleDuration)*100},{total},{(total/sampleDuration)*100}");
            }
            
            await File.WriteAllTextAsync($"CpuBenchmark_{now.ToFileTime()}.csv", sb.ToString());
            
            // Generate console report
            sb.Clear();
            sb.AppendLine("CPU Benchmark complete.");
            sb.AppendLine();

            sb.AppendLine()
                .AppendLine(" CPU Usage Mode | Mean | StdErr | StdDev | Median | Maximum |")
                .AppendLine("--------------- |----- |------- |------- |------- |-------- |")
                .AppendLine(CalculateResult(Usages.Select(u => u.UserUsage.TotalMicroSeconds), "User"))
                .AppendLine(CalculateResult(Usages.Select(u => u.KernelUsage.TotalMicroSeconds), "Kernel"))
                .AppendLine(CalculateResult(Usages.Select(u => u.TotalMicroSeconds), "Total"));
            
            Console.WriteLine(sb.ToString());
            
            return 0;
        }
        
        private static string CalculateResult(IEnumerable<long> values, string name)
        {
            var times = values.OrderBy(i => i).ToArray();
            var medianIndex = times.Length / 2;
            
            var mean = times.Average();
            var stdDev = Math.Sqrt(times.Average(v => Math.Pow(v - mean, 2)));
            var stdErr = stdDev / Math.Sqrt(times.Length);
            double median;
            if (times.Length % 2 == 0)
                median = (times[medianIndex - 1] + times[medianIndex]) / 2.0;
            else
                median = times[medianIndex];

            return $" {name} | {(mean / 1000.0):N3} ms | {(stdErr / 1000.0):N3} ms | {(stdDev / 1000.0):N3} ms | {(median / 1000.0):N3} ms | {(times.Last() / 1000.0):N3} ms |";
        }
    }
}

