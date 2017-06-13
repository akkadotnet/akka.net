//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Serialization
{
    public abstract class SnapshotStoreSerializationSpec : PluginSpec
    {
        protected SnapshotStoreSerializationSpec(Config config, string actorSystem, ITestOutputHelper output) : base(config, actorSystem, output)
        {
        }

        protected IActorRef SnapshotStore => Extension.SnapshotStoreFor(null);

        [Fact]
        public virtual void SnapshotStore_should_serialize_AtLeastOnceDeliverySnapshot()
        {
            var probe = CreateTestProbe();

            var deliveries = new UnconfirmedDelivery[]
            {
                new UnconfirmedDelivery(1, ActorPath.Parse("akka://my-sys/user/service-a/worker1"), "test1"),
                new UnconfirmedDelivery(2, ActorPath.Parse("akka://my-sys/user/service-a/worker2"), "test2"),
            };
            var atLeastOnceDeliverySnapshot = new AtLeastOnceDeliverySnapshot(2L, deliveries);

            var metadata = new SnapshotMetadata(Pid, 2);
            SnapshotStore.Tell(new SaveSnapshot(metadata, atLeastOnceDeliverySnapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot.Equals(atLeastOnceDeliverySnapshot));
        }

        [Fact(Skip = "Not implemented yet")]
        public virtual void SnapshotStore_should_serialize_PersistentFSMSnapshot()
        {
            //var probe = CreateTestProbe();

            //var persistentFSMSnapshot = new PersistentFSMSnapshot<string>("mystate", "mydata", TimeSpan.FromDays(4));

            //var metadata = new SnapshotMetadata(Pid, 2);
            //SnapshotStore.Tell(new SaveSnapshot(metadata, persistentFSMSnapshot), probe.Ref);
            //probe.ExpectMsg<SaveSnapshotSuccess>();

            //SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            //probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot.Equals(persistentFSMSnapshot));
        }
    }
}
