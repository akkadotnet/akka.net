//-----------------------------------------------------------------------
// <copyright file="TestSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        public async Task send_ack_after_load_interceptor_is_set()
        {
            SnapshotsActorRef.Tell(new TestSnapshotStore.UseLoadInterceptor(null), TestActor);
            await ExpectMsgAsync<TestSnapshotStore.Ack>();
        }

        [Fact]
        public async Task send_ack_after_save_interceptor_is_set()
        {
            SnapshotsActorRef.Tell(new TestSnapshotStore.UseSaveInterceptor(null), TestActor);
            await ExpectMsgAsync<TestSnapshotStore.Ack>();
        }

        [Fact]
        public async Task send_ack_after_delete_interceptor_is_set()
        {
            SnapshotsActorRef.Tell(new TestSnapshotStore.UseDeleteInterceptor(null), TestActor);
            await ExpectMsgAsync<TestSnapshotStore.Ack>();
        }

        [Fact]
        public async Task after_load_behavior_was_executed_store_is_back_to_pass_mode()
        {
            // create snapshot
            var actor = ActorOf(() => new SnapshotActor(_probe));
            actor.Tell("save");
            await _probe.ExpectMsgAsync<SaveSnapshotSuccess>();
            await actor.GracefulStop(TimeSpan.FromSeconds(3));

            await WithSnapshotLoad(load => load.Fail(), async () =>
            {
                ActorOf(() => new SnapshotActor(_probe));
                await _probe.ExpectMsgAsync<SnapshotActor.RecoveryFailure>();
            });

            ActorOf(() => new SnapshotActor(_probe));
            await _probe.ExpectMsgAsync<SnapshotOffer>();
        }

        [Fact]
        public async Task after_save_behavior_was_executed_store_is_back_to_pass_mode()
        {
            // create snapshot
            var actor = ActorOf(() => new SnapshotActor(_probe));

            await WithSnapshotSave(save => save.Fail(), async () =>
            {
                actor.Tell("save");
                await _probe.ExpectMsgAsync<SaveSnapshotFailure>();
            });

            actor.Tell("save");
            await _probe.ExpectMsgAsync<SaveSnapshotSuccess>();
        }

        [Fact]
        public async Task after_delete_behavior_was_executed_store_is_back_to_pass_mode()
        {
            // create snapshot
            var actor = ActorOf(() => new SnapshotActor(_probe));
            actor.Tell("save");

            var success = await _probe.ExpectMsgAsync<SaveSnapshotSuccess>();
            var nr = success.Metadata.SequenceNr;

            await WithSnapshotDelete(del => del.Fail(), async () =>
            {
                actor.Tell(new SnapshotActor.DeleteOne(nr), TestActor);
                await _probe.ExpectMsgAsync<DeleteSnapshotFailure>();
            });

            actor.Tell(new SnapshotActor.DeleteOne(nr), TestActor);
            await _probe.ExpectMsgAsync<DeleteSnapshotSuccess>();
        }
    }
}
