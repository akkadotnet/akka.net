//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence.Sql.TestKit
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
        private IActorRef _parentTestActor;

        protected override bool ReceiveCommand(object message) => message.Match()
            .With<DeleteCommand>(delete =>
            {
                _parentTestActor = Sender;
                DeleteMessages(delete.ToSequenceNr);
            })
            .With<DeleteMessagesSuccess>(deleteSuccess =>
            {
                _parentTestActor.Tell(deleteSuccess.ToSequenceNr.ToString() + "-deleted");
            })
            .With<string>(cmd =>
            {
                var sender = Sender;
                Persist(cmd, e => sender.Tell(e + "-done"));
            })
            .WasHandled;
    }
}
