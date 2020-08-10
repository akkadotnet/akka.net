//-----------------------------------------------------------------------
// <copyright file="Defer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Persistence;

namespace DocsExamples.Persistence.PersistentActor
{
    public static class Defer
    {
        #region Defer
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
                    PersistAsync($"evt-{c}-1", e => Sender.Tell(e));
                    PersistAsync($"evt-{c}-2", e => Sender.Tell(e));
                    DeferAsync($"evt-{c}-3", e => Sender.Tell(e));
                }
            }
        }
        #endregion
    }
}
