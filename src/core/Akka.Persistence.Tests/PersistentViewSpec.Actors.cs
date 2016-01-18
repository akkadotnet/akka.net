//-----------------------------------------------------------------------
// <copyright file="PersistentViewSpec.Actors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Persistence.Tests
{
    public partial class PersistentViewSpec
    {
        #region Internal test classes

        internal class TestPersistentActor : NamedPersistentActor
        {
            private readonly IActorRef _probe;

            public TestPersistentActor(string name, IActorRef probe)
                : base(name)
            {
                _probe = probe;
            }

            protected override bool ReceiveRecover(object message)
            {
                // do nothing
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                Persist(message, o => _probe.Tell(o.ToString() + "-" + LastSequenceNr));
                return true;
            }
        }

        internal class TestPersistentView : PersistentView
        {
            private readonly string _name;
            private readonly IActorRef _probe;
            private readonly TimeSpan _interval;

            private string _failAt;
            private string _last;

            public TestPersistentView(string name, IActorRef probe, TimeSpan interval, string failAt = null)
            {
                _name = name;
                _probe = probe;
                _interval = interval;
                _failAt = failAt;
            }

            public TestPersistentView(string name, IActorRef probe)
                : this(name, probe, TimeSpan.FromMilliseconds(100))
            {
            }

            public override string ViewId { get { return _name + "-view"; } }
            public override string PersistenceId { get { return _name; } }
            public override TimeSpan AutoUpdateInterval { get { return _interval; } }

            protected override bool Receive(object message)
            {
                if (message.ToString() == "get") _probe.Tell(_last);
                else if (message.ToString() == "boom") throw new TestException("boom");
                else if (message is RecoveryFailure)
                {
                    var failure = message as RecoveryFailure;
                    throw failure.Cause;
                }
                else if (IsPersistent && ShouldFail(message)) throw new TestException("boom");
                else if (IsPersistent)
                {
                    _last = string.Format("replicated-{0}-{1}", message, LastSequenceNr);
                    _probe.Tell(_last);
                }
                else return false;
                return true;
            }

            protected override void PostRestart(Exception reason)
            {
                base.PostRestart(reason);
                _failAt = null;
            }

            private bool ShouldFail(object msg)
            {
                return _failAt == msg.ToString();
            }
        }

        internal class PassiveTestPersistentView : PersistentView
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            private string _failAt;
            private string _last;

            public PassiveTestPersistentView(string name, IActorRef probe, string failAt = null)
            {
                _name = name;
                _probe = probe;
                _failAt = failAt;
                _last = null;
            }

            protected override bool Receive(object message)
            {
                if (message.ToString() == "get") _probe.Tell(_last);
                else if (IsPersistent && ShouldFail(message)) throw new TestException("boom");
                else
                {
                    _last = string.Format("replicated-{0}-{1}", message, LastSequenceNr);
                }
                return true;
            }

            public override string ViewId { get { return _name + "-view"; } }
            public override string PersistenceId { get { return _name; } }

            public override bool IsAutoUpdate
            {
                get { return false; }
            }

            public override long AutoUpdateReplayMax
            {
                get { return 0L; }
            }

            protected override void PostRestart(Exception reason)
            {
                base.PostRestart(reason);
                _failAt = null;
            }

            private bool ShouldFail(object msg)
            {
                return _failAt != null && _failAt.Equals(msg.ToString());
            }
        }

        internal class ActiveTestPersistentView : PersistentView
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            public ActiveTestPersistentView(string name, IActorRef probe)
            {
                _name = name;
                _probe = probe;
            }

            protected override bool Receive(object message)
            {
                _probe.Tell(string.Format("replicated-{0}-{1}", message, LastSequenceNr));
                return true;
            }

            public override string ViewId { get { return _name + "-view"; } }
            public override string PersistenceId { get { return _name; } }

            public override TimeSpan AutoUpdateInterval { get { return TimeSpan.FromMilliseconds(50); } }
            public override long AutoUpdateReplayMax { get { return 2L; } }
        }

        internal class BecomingPersistentView : PersistentView
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            public BecomingPersistentView(string name, IActorRef probe)
            {
                _name = name;
                _probe = probe;
                Context.Become(message =>
                {
                    _probe.Tell(string.Format("replicated-{0}-{1}", message, LastSequenceNr));
                    return true;
                });
            }

            protected override bool Receive(object message)
            {
                return true;
            }

            public override string ViewId { get { return _name + "-view"; } }
            public override string PersistenceId { get { return _name; } }
        }

        internal class PersistentOrNotTestPersistentView : PersistentView
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            public override string ViewId { get { return _name + "-view"; } }
            public override string PersistenceId { get { return _name; } }

            public PersistentOrNotTestPersistentView(string name, IActorRef probe)
            {
                _name = name;
                _probe = probe;
            }

            protected override bool Receive(object message)
            {
                _probe.Tell(IsPersistent
                    ? string.Format("replicated-{0}-{1}", message, LastSequenceNr)
                    : string.Format("normal-{0}-{1}", message, LastSequenceNr));
                return true;
            }
        }

        internal class SnapshottingPersistentView : PersistentView
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            private string _last;

            public override string ViewId { get { return _name + "-view"; } }
            public override string PersistenceId { get { return _name; } }

            public override TimeSpan AutoUpdateInterval { get { return TimeSpan.FromMilliseconds(100); } }

            public SnapshottingPersistentView(string name, IActorRef probe)
            {
                _name = name;
                _probe = probe;
                _last = null;
            }

            protected override bool Receive(object message)
            {
                var msgStr = message.ToString();
                if (msgStr == "get")
                    _probe.Tell(_last);
                else if (msgStr == "snap")
                    SaveSnapshot(_last);
                else if (msgStr == "restart")
                    throw new TestException("restart requested");
                else if (message is SaveSnapshotSuccess)
                    _probe.Tell("snapped");
                else if (message is SnapshotOffer)
                {
                    var offer = message as SnapshotOffer;
                    _last = offer.Snapshot.ToString();
                    _probe.Tell(_last);
                }
                else
                {
                    _last = string.Format("replicated-{0}-{1}", message, LastSequenceNr);
                    _probe.Tell(_last);
                }

                return true;
            }
        }

        #endregion
    }
}

