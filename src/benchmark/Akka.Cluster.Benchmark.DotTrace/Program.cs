using System;
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
                MeasureProfiler.StartCollectingData();
                for (int i = 0; i < 10; i++)
                {
                    Console.WriteLine($"Try {i}");
                    await runIters(10000, container);
                    
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