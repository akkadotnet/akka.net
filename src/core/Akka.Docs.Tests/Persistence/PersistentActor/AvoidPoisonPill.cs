//-----------------------------------------------------------------------
// <copyright file="AvoidPoisonPill.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Persistence;
using System;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class AvoidPoisonPill
    {
        #region AvoidPoisonPill1
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
        #endregion

        public static void MainApp()
        {
            var system = ActorSystem.Create("AvoidPoisonPill");
            var persistentActor = system.ActorOf<SafePersistentActor>();

            #region AvoidPoisonPill2
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
            #endregion
        }
    }
}
