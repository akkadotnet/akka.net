using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class PersistAsync
    {
        public class MyPersistentActor : UntypedPersistentActor
        {
            public override string PersistenceId => "my-stable-persistence-id";

            protected override void OnRecover(object message)
            {
                // handle recovery here
            }

            protected override void OnCommand(object message)
            {
                if (message is string c)
                {
                    Sender.Tell(c);
                    Persist($"evt-{c}-1", e => Sender.Tell(e));
                    Persist($"evt-{c}-2", e => Sender.Tell(e));
                    DeferAsync($"evt-{c}-3", e => Sender.Tell(e));
                }
            }
        }

        public static void MainApp()
        {
            var system = ActorSystem.Create("PersistAsync");
            var persistentActor = system.ActorOf<MyPersistentActor>();

            // usage
            persistentActor.Tell("a");
            persistentActor.Tell("b");

            // possible order of received messages:
            // a
            // b
            // evt-a-1
            // evt-a-2
            // evt-b-1
            // evt-b-2
        }
    }
}
