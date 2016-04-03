//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence.Query.Sql.Tests
{
    public class TestActor: PersistentActor
    {
        [Serializable]
        public sealed class DeleteCommand
        {
            public readonly long ToSequenceNr;

            public DeleteCommand(long toSequenceNr)
            {
                ToSequenceNr = toSequenceNr;
            }
        }

        public static Props Props(string persistenceId) => Actor.Props.Create(() => new TestActor(persistenceId));

        public TestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public override string PersistenceId { get; }
        protected override bool ReceiveRecover(object message) => true;

        protected override bool ReceiveCommand(object message) => message.Match()
            .With<DeleteCommand>(delete =>
            {
                DeleteMessages(delete.ToSequenceNr);
                Sender.Tell(delete.ToSequenceNr.ToString() + "-deleted");
            })
            .With<string>(cmd =>
            {
                var sender = Sender;
                Persist(cmd, e => sender.Tell(e + "-done"));
            })
            .WasHandled;
    }
}