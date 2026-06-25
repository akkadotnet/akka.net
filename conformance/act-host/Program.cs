using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Conformance;

namespace Akka.Cluster.Conformance.Host
{
    /// <summary>
    /// Runs an instrumented ACT reference seed and drives a verdict against an external
    /// (out-of-process) node-under-test such as the Go worker.
    ///
    /// Usage: act-host [--port=5110] [--seconds=40] [--worker=akka.tcp://ConformanceCluster@127.0.0.1:6000]
    ///
    /// It prints SEED_URI (so the worker knows where to connect) and ACT_HOST_READY, runs for the
    /// configured window (exiting early once a worker has joined and then been removed), and finally
    /// prints the ACT verdict and the full captured trace between clear markers.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var port = ArgInt(args, "--port", 5110);
            var seconds = ArgInt(args, "--seconds", 40);
            var workerArg = ArgStr(args, "--worker", null);
            const string clusterName = "ConformanceCluster";

            await using var seed = await ReferenceSeed.StartAsync(clusterName, "127.0.0.1", port);

            Console.WriteLine($"SEED_URI={seed.SeedNodeUri}");
            Console.WriteLine("ACT_HOST_READY");
            Console.Out.Flush();

            // Run the observation window. Exit early once a worker has been Up and then Removed.
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            var sawWorkerUp = false;
            while (DateTime.UtcNow < deadline)
            {
                var members = seed.Cluster.State.Members;
                var others = members.Where(m => !m.Address.Equals(seed.Address)).ToList();
                if (others.Any(m => m.Status == MemberStatus.Up))
                    sawWorkerUp = true;

                // lifecycle complete: a worker joined (Up) and is now gone
                if (sawWorkerUp && others.Count == 0 && seed.Trace.Has("MemberRemoved"))
                    break;

                await Task.Delay(500);
            }

            Address? workerAddr = workerArg is not null
                ? Address.Parse(workerArg)
                : DetectWorker(seed);

            Console.WriteLine("===ACT===");
            if (workerAddr is null)
            {
                Console.WriteLine("No node-under-test was observed contacting the reference seed.");
            }
            else
            {
                Console.WriteLine($"node-under-test: {workerAddr}");
                var result = Act.Check(seed.Trace, workerAddr);
                Console.WriteLine(result);
            }

            Console.WriteLine("===TRACE===");
            Console.WriteLine(seed.Trace.Render());
            Console.WriteLine("===END===");
            Console.Out.Flush();
            return 0;
        }

        // The node-under-test is the first peer the reference seed exchanged anything with
        // that is not the seed itself.
        private static Address? DetectWorker(ReferenceSeed seed)
        {
            foreach (var e in seed.Trace.Snapshot())
            {
                if (e.Peer is { } p && !p.Equals(seed.Address) && !string.IsNullOrEmpty(p.Host))
                    return p;
            }
            return null;
        }

        private static int ArgInt(string[] args, string name, int def)
        {
            var s = ArgStr(args, name, null);
            return s is not null && int.TryParse(s, out var v) ? v : def;
        }

        private static string? ArgStr(string[] args, string name, string? def)
        {
            foreach (var a in args)
            {
                if (a.StartsWith(name + "=", StringComparison.Ordinal))
                    return a.Substring(name.Length + 1);
            }
            return def;
        }
    }
}
