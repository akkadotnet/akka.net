//-----------------------------------------------------------------------
// <copyright file="SnapshotActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using Actor;

    public class SnapshotActor : UntypedPersistentActor
    {
        public SnapshotActor(IActorRef probe)
        {
            _probe = probe;
        }

        private readonly IActorRef _probe;

        public override string PersistenceId  => "bar";

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case "save":
                    SaveSnapshot(message);
                    return;

                case DeleteOne del:
                    DeleteSnapshot(del.SequenceNr);
                    return;

                case DeleteMany del:
                    DeleteSnapshots(del.Criteria);
                    return;

                case SaveSnapshotSuccess _:
                case SaveSnapshotFailure _:
                case DeleteSnapshotSuccess _:
                case DeleteSnapshotFailure _:
                case DeleteSnapshotsSuccess _:
                case DeleteSnapshotsFailure _:
                    _probe.Tell(message);
                    return;

                default:
                    return;
            }
        }

        protected override void OnRecover(object message)
        {
            if (message is SnapshotOffer snapshot)
            {
                _probe.Tell(message);
            }
        }

        protected override void OnRecoveryFailure(Exception reason, object message = null)
        {
            _probe.Tell(new RecoveryFailure(reason, message));
            base.OnRecoveryFailure(reason, message);
        }

        public class DeleteOne
        {
            public DeleteOne(long sequenceNr)
            {
                SequenceNr = sequenceNr;
            }

            public long SequenceNr { get; }
        }

        public class DeleteMany
        {
            public DeleteMany(SnapshotSelectionCriteria criteria)
            {
                Criteria = criteria;
            }

            public SnapshotSelectionCriteria Criteria { get; }
        }

        public class RecoveryFailure
        {
            public RecoveryFailure(Exception reason, object message)
            {
                Reason = reason;
                Message = message;
            }

            public Exception Reason { get; }
            public object Message { get; }
        }
    }
}
