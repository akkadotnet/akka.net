using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using static Akka.Cluster.Benchmarks.Persistence.PersistenceInfrastructure;

namespace Akka.Cluster.Benchmarks.Persistence
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class RecoveryBenchmark
    {
        private static readonly Store Message = new Store(1);

        [Params(1, 10, 100)] public int PersistentActors;

        [Params(100, 1000)] public int WriteMsgCount;

        private ActorSystem _sys1;

        private IActorRef _doneActor;
        private HashSet<IActorRef> _persistentActors;

        /*
        * Don't need to worry about cleaning up in-memory SQLite databases: https://www.sqlite.org/inmemorydb.html
        * Database is automatically deleted once the last connection to it is closed.
        */

        [GlobalSetup]
        public async Task GlobalSetup()
        {
            var config = GenerateJournalConfig();
            _sys1 = ActorSystem.Create("MySys", config);
            _doneActor = _sys1.ActorOf(Props.Create(() => new BenchmarkDoneActor(PersistentActors)), "done");
            _persistentActors = new HashSet<IActorRef>();

            var tasks = new List<Task<Done>>();
            var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            foreach (var i in Enumerable.Range(0, PersistentActors))
            {
                var myRef = _sys1.ActorOf(Props.Create(() => new PerformanceTestActor(i.ToString(), _doneActor, WriteMsgCount)), i.ToString());
                _persistentActors.Add(myRef);
                tasks.Add(myRef.Ask<Done>(Init.Instance, startupCts.Token));
            }

            // all persistence actors have started and successfully communicated with journal
            await Task.WhenAll(tasks);

            var completionTask = _doneActor.Ask<Done>(IsFinished.Instance, startupCts.Token);

            foreach (var _ in Enumerable.Range(0, WriteMsgCount))
                foreach (var a in _persistentActors)
                {
                    a.Tell(Message);
                }

            await completionTask;

            Cleanup();
        }

        [GlobalCleanup]
        public async Task GlobalCleanup()
        {
            await _sys1.Terminate();
        }

        [IterationCleanup]
        public void Cleanup()
        {
            foreach (var a in _persistentActors)
            {
                _sys1.Stop(a);
            }
            _persistentActors.Clear();
        }


        [Benchmark]
        public async Task RecoverPersistedActors()
        {
            var startupCts = new CancellationTokenSource(TimeSpan.FromMinutes(1));

            var completionTask = _doneActor.Ask<RecoveryFinished>(IsFinished.Instance, startupCts.Token);

            foreach (var i in Enumerable.Range(0, PersistentActors))
            {
                var myRef = _sys1.ActorOf(Props.Create(() => new PerformanceTestActor(i.ToString(), _doneActor, WriteMsgCount)), i.ToString());
                _persistentActors.Add(myRef);
            }

            await completionTask;
        }
    }
}
