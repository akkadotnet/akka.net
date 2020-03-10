//-----------------------------------------------------------------------
// <copyright file="SnapshotDecodeFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using Xunit;
using static Akka.Persistence.Tests.SnapshotSpec;

namespace Akka.Persistence.Tests
{
    public class SnapshotDecodeFailureSpec : PersistenceSpec
    {
        #region Internal test classes

        internal class SaveSnapshotTestPersistentActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public SaveSnapshotTestPersistentActor(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                switch (message)
                {
                    case Cmd c:
                        Persist(c.Data, _ =>
                        {
                            SaveSnapshot(c.Data);
                        });
                        return true;
                    case SaveSnapshotSuccess msq:
                        _probe.Tell(msq.Metadata.SequenceNr);
                        return true;
                }
                return false;
            }
        }

        internal class LoadSnapshotTestPersistentActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public LoadSnapshotTestPersistentActor(string name, IActorRef probe) : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is SnapshotOffer offer)
                    throw new Exception("kanbudong");

                _probe.Tell(message);
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                return false;
            }
        }

        #endregion

        public SnapshotDecodeFailureSpec()
            : base(Configuration("SnapshotDecodeFailureSpec"))
        {
            var persistentActor = ActorOf(() => new SaveSnapshotTestPersistentActor(Name, TestActor));
            persistentActor.Tell(new Cmd("payload"));

            ExpectMsg(1L);
        }

        [Fact]
        public void PersistentActor_with_a_failing_snapshot_loading_must_fail_recovery_and_stop_actor_when_no_snapshot_could_be_loaded()
        {
            var filters = new EventFilterBase[] { new ErrorFilter(typeof(Exception), new ContainsString("kanbudong")) };

            Sys.EventStream.Subscribe(TestActor, typeof(Error));
            Sys.EventStream.Publish(new Mute(filters));
            try
            {
                var lPersistentActor = Sys.ActorOf(Props.Create(() => new LoadSnapshotTestPersistentActor(Name, TestActor)));
                ExpectMsg<Error>(msg => msg.Message.ToString().StartsWith("Persistence failure when replaying events for persistenceId"));

                Watch(lPersistentActor);
                ExpectTerminated(lPersistentActor);
            }
            finally
            {
                Sys.EventStream.Unsubscribe(TestActor, typeof(Error));
                Sys.EventStream.Publish(new Unmute(filters));
            }
        }
    }
}
