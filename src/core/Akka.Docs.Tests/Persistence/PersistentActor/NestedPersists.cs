using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class NestedPersists
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

                    Persist($"{message}-1-outer", outer1 =>
                    {
                        Sender.Tell(outer1, Self);
                        Persist($"{c}-1-inner", inner1 => Sender.Tell(inner1));
                    });

                    Persist($"{message}-2-outer", outer2 =>
                    {
                        Sender.Tell(outer2, Self);
                        Persist($"{c}-2-inner", inner2 => Sender.Tell(inner2));
                    });
                }
            }
        }

        public static void MainApp()
        {
            var system = ActorSystem.Create("NestedPersists");
            var persistentActor = system.ActorOf<MyPersistentActor>();

            persistentActor.Tell("a");
            persistentActor.Tell("b");

            // order of received messages:
            // a
            // a-outer-1
            // a-outer-2
            // a-inner-1
            // a-inner-2
            // and only then process "b"
            // b
            // b-outer-1
            // b-outer-2
            // b-inner-1
            // b-inner-2
        }
    }
}
