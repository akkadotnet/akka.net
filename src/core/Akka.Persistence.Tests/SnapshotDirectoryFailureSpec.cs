//-----------------------------------------------------------------------
// <copyright file="SnapshotDirectoryFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using Akka.Actor;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class SnapshotDirectoryFailureSpec : PersistenceSpec
    {
        private const string InUseSnapshotPath = "target/inUseSnapshotPath";

        internal class TestPersistentActor : PersistentActor
        {
            private readonly string _name;
            private readonly IActorRef _probe;

            public TestPersistentActor(string name, IActorRef probe)
            {
                _name = name;
                _probe = probe;
            }

            public override string PersistenceId { get { return _name; } }

            protected override bool ReceiveRecover(object message)
            {
                if (message is SnapshotOffer)
                    _probe.Tell(message);
                else return false;
                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                    SaveSnapshot(message);
                else if (message is SaveSnapshotSuccess)
                    _probe.Tell(((SaveSnapshotSuccess)message).Metadata.SequenceNr);
                else
                    _probe.Tell(message);
                return true;
            }
        }

        FileInfo file = new FileInfo(InUseSnapshotPath);

        public SnapshotDirectoryFailureSpec() : base(Configuration("SnapshotDirectoryFailureSpec",
            extraConfig: "akka.persistence.snapshot-store.local.dir = \"" + InUseSnapshotPath + "\""))
        {
        }

        protected override void AtStartup()
        {
            base.AtStartup();
            using (file.Create()) {}
        }

        protected override void AfterTermination()
        {
            file.Delete();
            base.AfterTermination();
        }

        [Fact]
        public void LocalSnapshotStore_configured_with_a_failing_directory_name_should_throw_an_exception_at_startup()
        {
            EventFilter.Exception(typeof (ActorInitializationException)).ExpectOne(() =>
            {
                var pref = Sys.ActorOf(Props.Create(() => new TestPersistentActor("SnapshotDirectoryFailureSpec-1", TestActor)));
                pref.Tell("blahonga");
            });
        }
    }
}