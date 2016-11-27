using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using NBench;

namespace Akka.Persistence.Tests.Performance
{
    public abstract class SingleActorWriteSpec
    {
        public const int Timeout = 10000;
        public const int EventsPerActor = 1000;

        protected abstract Config Configuration { get; }

        protected ActorSystem System;

        #region init
        private IActorRef _testRef;

        [PerfSetup]
        public void Setup()
        {
            System = ActorSystem.Create("SingleActorWriteSpec", Configuration);
            _testRef = System.ActorOf(PerfTestActor.Props("p-1"), "p-1");
            _testRef.Ask<Initialized>(Init.Instance, TimeSpan.FromSeconds(1)).Wait();
        }

        [PerfCleanup]
        public void Cleanup()
        {
            System.Dispose();
        }

        #endregion

        [PerfBenchmark]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = Timeout)]
        public void End2EndSingleActorPersistThroughtput()
        {
            for (int i = 1; i <= EventsPerActor; i++)
            {
                _testRef.Tell(new Store(1));
            }

            _testRef.Ask<Finished>(Finish.Instance, TimeSpan.FromMilliseconds(Timeout)).Wait();
        }

        [PerfBenchmark]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = Timeout)]
        public void End2EndSingleActorPersistAsyncThroughtput()
        {
            for (int i = 1; i <= EventsPerActor; i++)
            {
                _testRef.Tell(new StoreAsync(1));
            }

            _testRef.Ask<Finished>(Finish.Instance, TimeSpan.FromMilliseconds(Timeout)).Wait();
        }
    }
}