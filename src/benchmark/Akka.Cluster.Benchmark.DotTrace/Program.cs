using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Cluster.Benchmarks.Sharding;
using Akka.Cluster.Sharding;
using JetBrains.Profiler.Api;

namespace Akka.Cluster.Benchmark.DotTrace
{
    class Program
    {
        static async Task Main(string[] args)
        {

            await Task.Run(async () =>
            {


                var container = new ShardMessageRoutingBenchmarks();
                container.StateMode = StateStoreMode.DData;
                container.MsgCount = 1;
                await container.Setup();
                await runIters(20, container);
                await container.SingleRequestResponseToRemoteEntity();
                var sw = new Stopwatch();
                MeasureProfiler.StartCollectingData();
                for (int i = 0; i < 20; i++)
                {
                    sw.Restart();
                    Console.WriteLine($"Try {i+1}");
                    await runIters(10000, container);
                    Console.WriteLine($"Completed {i+1} in {sw.Elapsed.TotalSeconds:F2} seconds");
                }

                MeasureProfiler.SaveData();
                GC.KeepAlive(container);
                return 1;
            });
            Console.ReadLine();
        }

        private static async Task runIters(int iters,
            ShardMessageRoutingBenchmarks container)
        {
            for (int i = 0; i < iters; i++)
            {
                await container.SingleRequestResponseToRemoteEntity();
            }
        }
    }
}