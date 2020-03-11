//-----------------------------------------------------------------------
// <copyright file="TestSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.TestKit;
    using Xunit;

    public sealed class TestSnapshotStoreSpec : PersistenceTestKit
    {
        public TestSnapshotStoreSpec()
        {
            _probe = CreateTestProbe();
        }

        private readonly TestProbe _probe;

        [Fact]
        public void send_ack_after_load_interceptor_is_set()
        {
            SnapshotsActorRef.Tell(new TestSnapshotStore.UseLoadInterceptor(null), TestActor);
            ExpectMsg<TestSnapshotStore.Ack>();
        }

        [Fact]
        public void send_ack_after_save_interceptor_is_set()
        {
            SnapshotsActorRef.Tell(new TestSnapshotStore.UseSaveInterceptor(null), TestActor);
            ExpectMsg<TestSnapshotStore.Ack>();
        }

        [Fact]
        public void send_ack_after_delete_interceptor_is_set()
        {
            SnapshotsActorRef.Tell(new TestSnapshotStore.UseDeleteInterceptor(null), TestActor);
            ExpectMsg<TestSnapshotStore.Ack>();
        }

        [Fact]
        public async Task after_load_behavior_was_executed_store_is_back_to_pass_mode()
        {
            // create snapshot
            var actor = ActorOf(() => new SnapshotActor(_probe));
            actor.Tell("save");
            _probe.ExpectMsg<SaveSnapshotSuccess>();
            await actor.GracefulStop(TimeSpan.FromSeconds(3));

            await WithSnapshotLoad(load => load.Fail(), () =>
            {
                ActorOf(() => new SnapshotActor(_probe));
                _probe.ExpectMsg<SnapshotActor.RecoveryFailure>();
            });

            ActorOf(() => new SnapshotActor(_probe));
            _probe.ExpectMsg<SnapshotOffer>();
        }

        [Fact]
        public async Task after_save_behavior_was_executed_store_is_back_to_pass_mode()
        {
            // create snapshot
            var actor = ActorOf(() => new SnapshotActor(_probe));

            await WithSnapshotSave(save => save.Fail(), () =>
            {
                actor.Tell("save");
                _probe.ExpectMsg<SaveSnapshotFailure>();
            });

            actor.Tell("save");
            _probe.ExpectMsg<SaveSnapshotSuccess>();
        }

        [Fact]
        public async Task after_delete_behavior_was_executed_store_is_back_to_pass_mode()
        {
            // create snapshot
            var actor = ActorOf(() => new SnapshotActor(_probe));
            actor.Tell("save");

            var success = _probe.ExpectMsg<SaveSnapshotSuccess>();
            var nr = success.Metadata.SequenceNr;

            await WithSnapshotDelete(del => del.Fail(), () =>
            {
                actor.Tell(new SnapshotActor.DeleteOne(nr), TestActor);
                _probe.ExpectMsg<DeleteSnapshotFailure>();
            });

            actor.Tell(new SnapshotActor.DeleteOne(nr), TestActor);
            _probe.ExpectMsg<DeleteSnapshotSuccess>();
        }
    }
}
