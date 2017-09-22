using Akka.Actor;
using Akka.Persistence;
using System;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class AvoidPoisonPill
    {
        public class Shutdown {}

        public class SafePersistentActor : UntypedPersistentActor
        {
            public override string PersistenceId => "safe-actor";

            protected override void OnRecover(object message)
            {
                // handle recovery here
            }

            protected override void OnCommand(object message)
            {
                if (message is string c)
                {
                    Console.WriteLine(c);
                    Persist($"handle-{c}", param =>
                    {
                        Console.WriteLine(param);
                    });
                }
                else if (message is Shutdown)
                {
                    Context.Stop(Self);
                }
            }
        }

        public static void MainApp()
        {
            var system = ActorSystem.Create("AvoidPoisonPill");
            var persistentActor = system.ActorOf<SafePersistentActor>();

            // UN-SAFE, due to PersistentActor's command stashing:
            persistentActor.Tell("a");
            persistentActor.Tell("b");
            persistentActor.Tell(PoisonPill.Instance);
            // order of received messages:
            // a
            //   # b arrives at mailbox, stashing;        internal-stash = [b]
            //   # PoisonPill arrives at mailbox, stashing; internal-stash = [b, Shutdown]
            // PoisonPill is an AutoReceivedMessage, is handled automatically
            // !! stop !!
            // Actor is stopped without handling `b` nor the `a` handler!

            // SAFE:
            persistentActor.Tell("a");
            persistentActor.Tell("b");
            persistentActor.Tell(new Shutdown());
            // order of received messages:
            // a
            //   # b arrives at mailbox, stashing;        internal-stash = [b]
            //   # Shutdown arrives at mailbox, stashing; internal-stash = [b, Shutdown]
            // handle-a
            //   # unstashing;                            internal-stash = [Shutdown]
            // b
            // handle-b
            //   # unstashing;                            internal-stash = []
            // Shutdown
            // -- stop --
        }
    }
}
