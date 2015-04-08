//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.TestKit.Snapshot
{
    /// <summary>
    /// This spec aims to verify custom <see cref="SnapshotStore"/> implementations. 
    /// Every custom authors snapshot store spec should have it's spec suite included.
    /// </summary>
    public abstract class SnapshotStoreSpec : PluginSpec
    {
        protected static readonly Config Config =
            ConfigurationFactory.ParseString("akka.persistence.publish-plugin-commands = on");

        private static readonly string _specConfigTemplate = @"
            akka.persistence.snapshot-store {
                plugin = ""akka.persistence.snapshot-store.my""
                my {
                    class = ""TestPersistencePlugin.MySnapshotStore, TestPersistencePlugin""
                    plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                }
            }";

        private readonly TestProbe _senderProbe;
        protected List<SnapshotMetadata> Metadata;
        
        protected SnapshotStoreSpec(Config config = null, string actorSystemName = null, string testActorName = null) 
            : base(FromConfig(config).WithFallback(Config), actorSystemName ?? "SnapshotStoreSpec", testActorName)
        {
            _senderProbe = CreateTestProbe();
        }

        protected SnapshotStoreSpec(Type snapshotStoreType, string actorSystemName = null)
            : base(ConfigFromTemplate(snapshotStoreType), actorSystemName)
        {
        }

        protected IActorRef SnapshotStore { get { return Extension.SnapshotStoreFor(null); } }

        private static Config ConfigFromTemplate(Type snapshotStoreType)
        {
            return ConfigurationFactory.ParseString(string.Format(_specConfigTemplate, snapshotStoreType.FullName));
        }

        protected IEnumerable<SnapshotMetadata> WriteSnapshots()
        {
            for (int i = 1; i <= 5; i++)
            {
                var metadata = new SnapshotMetadata(Pid, i + 10);
                SnapshotStore.Tell(new SaveSnapshot(metadata, "s-" + i), _senderProbe.Ref);
                yield return _senderProbe.ExpectMsg<SaveSnapshotSuccess>().Metadata;
            }
        }

        [Fact]
        public void SnapshotStore_should_not_load_a_snapshot_given_an_invalid_persistence_id()
        {
            SnapshotStore.Tell(new LoadSnapshot("invalid", SnapshotSelectionCriteria.Latest, long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue);
        }

        [Fact]
        public void SnapshotStore_should_not_load_a_snapshot_given_non_matching_timestamp_criteria()
        {
            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(long.MaxValue, new DateTime(100000)), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue);
        }

        [Fact]
        public void SnapshotStore_should_not_load_a_snapshot_given_non_matching_sequence_number_criteria()
        {
            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(7), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue);

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, 7), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => result.Snapshot == null && result.ToSequenceNr == 7);
        }

        [Fact]
        public void SnapshotStore_should_load_a_most_recent_snapshot()
        {
            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => 
                result.ToSequenceNr == long.MaxValue 
                && result.Snapshot != null 
                && result.Snapshot.Metadata.Equals(Metadata[4])
                && result.Snapshot.Snapshot.ToString() == "s-5");
        }

        [Fact]
        public void SnapshotStore_should_load_a_most_recent_snapshot_matching_an_upper_sequence_number_bound()
        {
            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(13), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[2])
                && result.Snapshot.Snapshot.ToString() == "s-3");

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, 13), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == 13
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[2])
                && result.Snapshot.Snapshot.ToString() == "s-3");
        }

        [Fact]
        public void SnapshotStore_should_load_a_most_recent_snapshot_matching_an_upper_sequence_number_and_timestamp_bound()
        {
            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(13, Metadata[2].Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[2])
                && result.Snapshot.Snapshot.ToString() == "s-3");

            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(long.MaxValue, Metadata[2].Timestamp), 13), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == 13
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[2])
                && result.Snapshot.Snapshot.ToString() == "s-3");
        }

        [Fact]
        public void SnapshotStore_should_delete_a_single_snapshot_identified_by_snapshot_metadata()
        {
            var md = Metadata[2];
            var command = new DeleteSnapshot(md);
            var sub = CreateTestProbe();

            Subscribe<DeleteSnapshot>(sub.Ref);
            SnapshotStore.Tell(command);
            sub.ExpectMsg(command);

            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(md.SequenceNr, md.Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[1])
                && result.Snapshot.Snapshot.ToString() == "s-2");
        }

        [Fact]
        public void SnapshotStore_should_delete_all_snapshots_matching_upper_sequence_number_and_timestamp_bounds()
        {
            var md = Metadata[2];
            var command = new DeleteSnapshots(Pid, new SnapshotSelectionCriteria(md.SequenceNr, md.Timestamp));
            var sub = CreateTestProbe();

            Subscribe<DeleteSnapshots>(sub.Ref);
            SnapshotStore.Tell(command);
            sub.ExpectMsg(command);

            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(md.SequenceNr, md.Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue);


            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(Metadata[3].SequenceNr, Metadata[3].Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[3])
                && result.Snapshot.Snapshot.ToString() == "s-4");
        }
    }
}
