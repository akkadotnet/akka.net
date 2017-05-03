using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class NestedPersistsAsync
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

                    PersistAsync($"{message}-1-outer", outer1 =>
                    {
                        Sender.Tell(outer1, Self);
                        PersistAsync($"{c}-1-inner", inner1 => Sender.Tell(inner1));
                    });

                    PersistAsync($"{message}-2-outer", outer2 =>
                    {
                        Sender.Tell(outer2, Self);
                        PersistAsync($"{c}-2-inner", inner2 => Sender.Tell(inner2));
                    });
                }
            }
        }

        public static void MainApp()
        {
            var system = ActorSystem.Create("NestedPersistsAsync");
            var persistentActor = system.ActorOf<MyPersistentActor>();

            persistentActor.Tell("a");
            persistentActor.Tell("b");

            // order of received messages:
            // a
            // b
            // a-outer-1
            // a-outer-2
            // b-outer-1
            // b-outer-2
            // a-inner-1
            // a-inner-2
            // b-inner-1
            // b-inner-2

            // which can be seen as the following causal relationship:
            // a -> a-outer-1 -> a-outer-2 -> a-inner-1 -> a-inner-2
            // b -> b-outer-1 -> b-outer-2 -> b-inner-1 -> b-inner-2
        }
    }
}
