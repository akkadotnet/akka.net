//-----------------------------------------------------------------------
// <copyright file="SnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Fsm;
using Akka.Persistence.TCK.Serialization;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Snapshot
{
    /// <summary>
    /// This spec aims to verify custom <see cref="SnapshotStore"/> implementations. 
    /// Every custom authors snapshot store spec should have it's spec suite included.
    /// </summary>
    public abstract class SnapshotStoreSpec : PluginSpec
    {
        private static readonly string _specConfigTemplate = @"
            akka.persistence.publish-plugin-commands = on
            akka.persistence.snapshot-store {
                plugin = ""akka.persistence.snapshot-store.my""
                my {
                    class = ""TestPersistencePlugin.MySnapshotStore, TestPersistencePlugin""
                    plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                }
            }
            akka.actor{
                serializers{
                    persistence-tck-test=""Akka.Persistence.TCK.Serialization.TestSerializer,Akka.Persistence.TCK""
                }
                serialization-bindings {
                    ""Akka.Persistence.TCK.Serialization.TestPayload,Akka.Persistence.TCK"" = persistence-tck-test
                }
            }";

        protected static readonly Config Config = 
            ConfigurationFactory.ParseString(_specConfigTemplate);

        protected override bool SupportsSerialization => true;

        private readonly TestProbe _senderProbe;
        protected List<SnapshotMetadata> Metadata;
        
        protected SnapshotStoreSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null) 
            : base(FromConfig(config).WithFallback(Config), actorSystemName ?? "SnapshotStoreSpec", output)
        {
            _senderProbe = CreateTestProbe();
        }

        protected SnapshotStoreSpec(Type snapshotStoreType, string actorSystemName = null)
            : base(ConfigFromTemplate(snapshotStoreType), actorSystemName)
        {
        }

        protected IActorRef SnapshotStore => Extension.SnapshotStoreFor(null);

        /// <summary>
        /// The limit defines a number of bytes persistence plugin can support to store the snapshot.
        /// If plugin does not support persistence of the snapshots of 10000 bytes or may support more than default size,
        /// the value can be overriden by the SnapshotStoreSpec implementation with a note in a plugin documentation.
        /// </summary>
        protected virtual int SnapshotByteSizeLimit { get; } = 10000;

        /// <summary>
        /// Initializes a snapshot store with set of predefined snapshots.
        /// </summary>
        protected IEnumerable<SnapshotMetadata> Initialize()
        {
            return Metadata = WriteSnapshots().ToList();
        }

        private static Config ConfigFromTemplate(Type snapshotStoreType)
        {
            return ConfigurationFactory.ParseString(string.Format(_specConfigTemplate, snapshotStoreType.FullName));
        }

        private IEnumerable<SnapshotMetadata> WriteSnapshots()
        {
            for (int i = 1; i <= 5; i++)
            {
                var metadata = new SnapshotMetadata(Pid, i + 10);
                SnapshotStore.Tell(new SaveSnapshot(metadata, $"s-{i}"), _senderProbe.Ref);
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
        public void SnapshotStore_should_load_the_most_recent_snapshot()
        {
            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => 
                result.ToSequenceNr == long.MaxValue 
                && result.Snapshot != null 
                && result.Snapshot.Metadata.Equals(Metadata[4])
                && result.Snapshot.Snapshot.ToString() == "s-5");
        }

        [Fact]
        public void SnapshotStore_should_load_the_most_recent_snapshot_matching_an_upper_sequence_number_bound()
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
        public void SnapshotStore_should_load_the_most_recent_snapshot_matching_an_upper_sequence_number_and_timestamp_bound()
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
        public void SnapshotStore_should_delete_a_single_snapshot_identified_by_SequenceNr_in_snapshot_metadata()
        {
            var md = Metadata[2];
            md = new SnapshotMetadata(md.PersistenceId, md.SequenceNr); // don't care about timestamp for delete of a single snap
            var command = new DeleteSnapshot(md);
            var sub = CreateTestProbe();

            Subscribe<DeleteSnapshot>(sub.Ref);
            SnapshotStore.Tell(command, _senderProbe.Ref);
            sub.ExpectMsg(command);
            _senderProbe.ExpectMsg<DeleteSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(md.SequenceNr), long.MaxValue), _senderProbe.Ref);
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
            SnapshotStore.Tell(command, _senderProbe.Ref);
            sub.ExpectMsg(command);
            _senderProbe.ExpectMsg<DeleteSnapshotsSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(md.SequenceNr, md.Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result => result.Snapshot == null && result.ToSequenceNr == long.MaxValue);


            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(Metadata[3].SequenceNr, Metadata[3].Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[3])
                && result.Snapshot.Snapshot.ToString() == "s-4");
        }

        [Fact]
        public void SnapshotStore_should_not_delete_snapshots_with_non_matching_upper_timestamp_bounds()
        {
            var md = Metadata[3];
            var criteria = new SnapshotSelectionCriteria(md.SequenceNr, md.Timestamp.Subtract(TimeSpan.FromTicks(1)));
            var command = new DeleteSnapshots(Pid, criteria);
            var sub = CreateTestProbe();

            Subscribe<DeleteSnapshots>(sub.Ref);
            SnapshotStore.Tell(command, _senderProbe.Ref);
            sub.ExpectMsg(command);
            _senderProbe.ExpectMsg<DeleteSnapshotsSuccess>(m => m.Criteria.Equals(criteria));

            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(md.SequenceNr, md.Timestamp), long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(result =>
                result.ToSequenceNr == long.MaxValue
                && result.Snapshot != null
                && result.Snapshot.Metadata.Equals(Metadata[3])
                && result.Snapshot.Snapshot.ToString() == "s-4");
        }

        [Fact]
        public void SnapshotStore_should_save_and_overwrite_snapshot_with_same_sequence_number()
        {
            var md = Metadata[4];
            SnapshotStore.Tell(new SaveSnapshot(md, "s-5-modified"), _senderProbe.Ref);
            var md2 = _senderProbe.ExpectMsg<SaveSnapshotSuccess>().Metadata;
            Assert.Equal(md.SequenceNr, md2.SequenceNr);
            SnapshotStore.Tell(new LoadSnapshot(Pid, new SnapshotSelectionCriteria(md.SequenceNr), long.MaxValue), _senderProbe.Ref);
            var result = _senderProbe.ExpectMsg<LoadSnapshotResult>();
            Assert.Equal("s-5-modified", result.Snapshot.Snapshot.ToString());
            Assert.Equal(md.SequenceNr, result.Snapshot.Metadata.SequenceNr);
            // metadata timestamp may have been changed
        }

        [Fact]
        public void SnapshotStore_should_save_bigger_size_snapshot()
        {
            var metadata = new SnapshotMetadata(Pid, 100);
            var bigSnapshot = new byte[SnapshotByteSizeLimit];
            new Random().NextBytes(bigSnapshot);
            SnapshotStore.Tell(new SaveSnapshot(metadata, bigSnapshot), _senderProbe.Ref);
            _senderProbe.ExpectMsg<SaveSnapshotSuccess>();
        }


        [Fact]
        public void ShouldSerializeSnapshots()
        {
            if (!SupportsSerialization) return;

            var probe = CreateTestProbe();
            var metadata = new SnapshotMetadata(Pid, 100L);
            var snap = new TestPayload(probe.Ref);

            SnapshotStore.Tell(new SaveSnapshot(metadata, snap), _senderProbe.Ref);
            _senderProbe.ExpectMsg<SaveSnapshotSuccess>(o =>
            {
                Assertions.AssertEqual(metadata.PersistenceId, o.Metadata.PersistenceId);
                Assertions.AssertEqual(metadata.SequenceNr, o.Metadata.SequenceNr);
            });

            var pid = Pid;
            SnapshotStore.Tell(new LoadSnapshot(pid, SnapshotSelectionCriteria.Latest, long.MaxValue), _senderProbe.Ref);
            _senderProbe.ExpectMsg<LoadSnapshotResult>(l =>
            {
                Assertions.AssertEqual(pid, l.Snapshot.Metadata.PersistenceId);
                Assertions.AssertEqual(100L, l.Snapshot.Metadata.SequenceNr);
                Assertions.AssertEqual(l.Snapshot.Snapshot, snap);
            });
        }
    }
}
