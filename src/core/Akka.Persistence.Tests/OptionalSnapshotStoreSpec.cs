//-----------------------------------------------------------------------
// <copyright file="OptionalSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class OptionalSnapshotStoreSpec : PersistenceSpec
    {
        public sealed class GetConfig
        {
            public static readonly GetConfig Instance = new GetConfig();
            private GetConfig() { }
        }

        public class AnyPersistentActor : PersistentActor
        {
            private readonly string _name;
            private IActorRef _lastSender = Context.System.DeadLetters;

            public AnyPersistentActor(string name)
            {
                _name = name;
            }

            protected override bool ReceiveCommand(object message)
            {
                if (message is string)
                {
                    _lastSender = Sender;
                    SaveSnapshot(message);
                }
                else if (message is SaveSnapshotFailure || message is SaveSnapshotSuccess)
                    _lastSender.Tell(message);
                else return false;
                return true;
            }

            protected override bool ReceiveRecover(object message)
            {
                return false;
            }

            public override string PersistenceId { get { return _name; } }
        }

        public class PickedSnapshotStorePersistentActor : AnyPersistentActor
        {
            public PickedSnapshotStorePersistentActor(string name) : base(name)
            {
                SnapshotPluginId = "akka.persistence.snapshot-store.local";
            }
        }

        public OptionalSnapshotStoreSpec() : base(ConfigurationFactory.ParseString(
  @"akka.persistence.publish-plugin-commands = on
    akka.persistence.journal.plugin = ""akka.persistence.journal.inmem""

    # snapshot store plugin is NOT defined, things should still work
    akka.persistence.snapshot-store.plugin = ""akka.persistence.no-snapshot-store""
    akka.persistence.snapshot-store.local.dir = ""target/snapshots-" + typeof(OptionalSnapshotStoreSpec).FullName + @"/"""))
        {
        }
        
        [Fact]
        public void Persistence_extension_should_fail_if_PersistentActor_tries_to_SaveSnapshot_without_snapshot_store_available()
        {
            var pref = Sys.ActorOf(Props.Create(() => new AnyPersistentActor(Name)));
            pref.Tell("snap");
            var message = ExpectMsg<SaveSnapshotFailure>().Cause.Message;
            message.ShouldStartWith("No snapshot store configured");
        }

        [Fact]
        public void Persistence_extension_should_successfully_save_a_snapshot_when_no_default_snapshot_store_configured_yet_PersistentActor_picked_one_explicitly()
        {
            var pref = Sys.ActorOf(Props.Create(() => new PickedSnapshotStorePersistentActor(Name)));
            pref.Tell("snap");
            ExpectMsg<SaveSnapshotSuccess>();
        }
    }
}
