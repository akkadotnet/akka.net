using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using NBench;

namespace Akka.Persistence.Performance.TestKit
{
    public abstract class ManyActorsWriteSpec
    {
        public const int Timeout = 30000;
        public const int ActorCount = 1000;
        public const int EventsPerActor = 100;

        protected abstract Config Configuration { get; }

        protected ActorSystem System;

        #region init
        private IActorRef[] _testRefs;

        [PerfSetup]
        public virtual void Setup()
        {
            System = ActorSystem.Create("ManyActorsWriteSpec", Configuration);

            var tasks = new Task[ActorCount];
            _testRefs = new IActorRef[ActorCount];
            for (int i = 0; i < ActorCount; i++)
            {
                var pid = "p-" + i;
                var tref = System.ActorOf(PerfTestActor.Props(pid), pid);
                tasks[i] = tref.Ask<Initialized>(Init.Instance, TimeSpan.FromSeconds(1));
                _testRefs[i] = tref;
            }

            Task.WaitAll(tasks);
        }

        [PerfCleanup]
        public virtual void Cleanup()
        {
            System.Dispose();
        }

        #endregion

        [PerfBenchmark]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = Timeout)]
        public void End2EndManyActorsPersistThroughtput()
        {
            for (int i = 1; i <= EventsPerActor; i++)
            {
                for (int j = 0; j < ActorCount; j++)
                {
                    _testRefs[j].Tell(new Store(1));
                }
            }

            var tasks = new Task[ActorCount];
            for (int i = 0; i < ActorCount; i++)
            {
                tasks[i] = _testRefs[i].Ask<Finished>(Finish.Instance, TimeSpan.FromMilliseconds(Timeout));
            }

            Task.WaitAll(tasks);
        }

        [PerfBenchmark]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = Timeout)]
        public void End2EndManyActorsPersistAsyncThroughtput()
        {
            for (int i = 1; i <= EventsPerActor; i++)
            {
                for (int j = 0; j < ActorCount; j++)
                {
                    _testRefs[j].Tell(new StoreAsync(1));
                }
            }

            var tasks = new Task[ActorCount];
            for (int i = 0; i < ActorCount; i++)
            {
                tasks[i] = _testRefs[i].Ask<Finished>(Finish.Instance, TimeSpan.FromMilliseconds(Timeout));
            }

            Task.WaitAll(tasks);
        }
    }
}