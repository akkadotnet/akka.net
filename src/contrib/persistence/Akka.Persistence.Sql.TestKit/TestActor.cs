// -----------------------------------------------------------------------
//  <copyright file="TestActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence.Sql.TestKit;

public class TestActor : PersistentActor
{
    private IActorRef _parentTestActor;

    public TestActor(string persistenceId)
    {
        PersistenceId = persistenceId;
    }

    public override string PersistenceId { get; }

    public static Props Props(string persistenceId)
    {
        return Actor.Props.Create(() => new TestActor(persistenceId));
    }

    protected override bool ReceiveRecover(object message)
    {
        return true;
    }

    protected override bool ReceiveCommand(object message)
    {
        switch (message)
        {
            case DeleteCommand delete:
                _parentTestActor = Sender;
                DeleteMessages(delete.ToSequenceNr);
                return true;
            case DeleteMessagesSuccess deleteSuccess:
                _parentTestActor.Tell(deleteSuccess.ToSequenceNr + "-deleted");
                return true;
            case string cmd:
                var sender = Sender;
                Persist(cmd, e => sender.Tell(e + "-done"));
                return true;
            default:
                return false;
        }
    }

    [Serializable]
    public sealed class DeleteCommand
    {
        public readonly long ToSequenceNr;

        public DeleteCommand(long toSequenceNr)
        {
            ToSequenceNr = toSequenceNr;
        }
    }
}