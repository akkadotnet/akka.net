using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using NBench;

namespace Akka.Persistence.Performance.TestKit
{
    public abstract class ManyActorsRecoverySpec
    {
        public const int ActorsCount = 1000;
        public const int EventsPerActor = 100;
        public const int Timeout = 30000;

        protected abstract Config Configuration { get; }
        protected ActorSystem System;

        #region init

        class CompletionActor : ReceiveActor
        {
            public CompletionActor(int state, TaskCompletionSource<int> completion)
            {
                Receive<WriteMessageSuccess>(success =>
                {
                    state--;
                    if (state == 0) completion.SetResult(0);
                });
                Receive<WriteMessagesSuccessful>(_ => { });
                Receive<WriteMessagesFailed>(failed => completion.SetException(failed.Cause));
                Receive<WriteMessageFailure>(failure => completion.SetException(failure.Cause));
            }
        }

        private IActorRef _journalRef;

        [PerfSetup]
        public virtual void Setup()
        {
            System = ActorSystem.Create("ManyActorsRecoverySpec", Configuration);
            _journalRef = Persistence.Instance.Apply(System).JournalFor(null);

            var completion = new TaskCompletionSource<int>();
            var tref = System.ActorOf(Props.Create(() => new CompletionActor(ActorsCount * EventsPerActor, completion)));

            for (int i = 0; i < ActorsCount; i++)
            {
                for (int j = 0; j < EventsPerActor; j++)
                {
                    var e = new IPersistentEnvelope[] { new AtomicWrite(new Persistent(new Stored(1), i, "p-" + i, sender: tref)) };
                    var msg = new WriteMessages(e, tref, i);
                    _journalRef.Tell(msg, tref);
                }
            }

            completion.Task.Wait(Timeout);
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
        public void End2EndManyActorsRecoveryThroughtput()
        {
            var tasks = new Task[ActorsCount];
            for (int i = 0; i < ActorsCount; i++)
            {
                var aref = System.ActorOf(PerfTestActor.Props("p-" + i));
                tasks[i] = aref.Ask<Initialized>(Init.Instance, TimeSpan.FromMilliseconds(Timeout));
            }

            Task.WaitAll(tasks);
        }
    }
}
