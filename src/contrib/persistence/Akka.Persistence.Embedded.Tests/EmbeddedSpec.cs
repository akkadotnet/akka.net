//-----------------------------------------------------------------------
// <copyright file="EmbeddedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Embedded.Tests
{
    public sealed record WriteOutcome(
        bool Succeeded,
        Exception? Failure,
        IReadOnlyList<IPersistentRepresentation> Written,
        IReadOnlyList<WriteMessageRejected> Rejected);

    public sealed record ReplayOutcome(
        IReadOnlyList<IPersistentRepresentation> Replayed,
        long HighestSequenceNr,
        Exception? Failure);

    /// <summary>Base for plugin-specific specs: one temp database and one actor system per test class.</summary>
    public abstract class EmbeddedSpec : Akka.TestKit.Xunit.TestKit
    {
        protected static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        protected EmbeddedSpec(SqliteTestDb db, Config config, string systemName, ITestOutputHelper output)
            : base(config.WithFallback(ConfigurationFactory.ParseString(TestSerializerConfig.Hocon)), systemName, output)
        {
            Db = db;
            SqliteSpecConfig.EnsureThreadPoolWarmed();
        }

        protected SqliteTestDb Db { get; }

        protected IActorRef Journal => Persistence.Instance.Apply((ExtendedActorSystem)Sys).JournalFor(null);

        protected IActorRef SnapshotStore => Persistence.Instance.Apply((ExtendedActorSystem)Sys).SnapshotStoreFor(null);

        /// <summary>Waits until the journal created and verified its tables.</summary>
        protected async Task InitializeJournalAsync()
            => await Journal.Ask<Initialized>(EnsureInitialized.Instance, Timeout);

        protected async Task InitializeSnapshotStoreAsync()
            => await SnapshotStore.Ask<Initialized>(EnsureInitialized.Instance, Timeout);

        protected static IPersistentRepresentation Evt(string pid, long seq, object payload, long timestamp = 0L)
            => new Persistent(payload, seq, pid, string.Empty, false, ActorRefs.NoSender, "test-writer", timestamp);

        protected static AtomicWrite Write(params IPersistentRepresentation[] events)
            => new(events.ToImmutableList());

        /// <summary>Sends a write and collects what the journal answers.</summary>
        protected async Task<WriteOutcome> WriteAsync(params AtomicWrite[] writes)
        {
            var probe = CreateTestProbe();
            SendWrite(probe, writes);
            return await CollectWriteAsync(probe, writes);
        }

        protected void SendWrite(TestProbe probe, params AtomicWrite[] writes)
            => Journal.Tell(new WriteMessages(writes, probe.Ref, 1));

        protected async Task<WriteOutcome> CollectWriteAsync(TestProbe probe, AtomicWrite[] writes)
        {
            var written = new List<IPersistentRepresentation>();
            var rejected = new List<WriteMessageRejected>();
            var total = writes.Sum(w => w.Size);
            var successful = false;
            while (true)
            {
                if (successful && written.Count + rejected.Count == total)
                    return new WriteOutcome(true, null, written, rejected);

                var message = await probe.ExpectMsgAsync<object>(Timeout);
                switch (message)
                {
                    case WriteMessageSuccess success:
                        written.Add(success.Persistent);
                        break;
                    case WriteMessageRejected reject:
                        rejected.Add(reject);
                        break;
                    case WriteMessagesSuccessful:
                        successful = true;
                        break;
                    case WriteMessagesFailed failed:
                        for (var i = 0; i < total; i++)
                            await probe.ExpectMsgAsync<WriteMessageFailure>(Timeout);
                        return new WriteOutcome(false, failed.Cause, written, rejected);
                }
            }
        }

        protected async Task<ReplayOutcome> ReplayAsync(string pid, long from = 0, long to = long.MaxValue, long max = long.MaxValue)
        {
            var probe = CreateTestProbe();
            Journal.Tell(new ReplayMessages(from, to, max, pid, probe.Ref));
            var replayed = new List<IPersistentRepresentation>();
            while (true)
            {
                var message = await probe.ExpectMsgAsync<object>(Timeout);
                switch (message)
                {
                    case ReplayedMessage replay:
                        replayed.Add(replay.Persistent);
                        break;
                    case RecoverySuccess success:
                        return new ReplayOutcome(replayed, success.HighestSequenceNr, null);
                    case ReplayMessagesFailure failure:
                        return new ReplayOutcome(replayed, 0, failure.Cause);
                }
            }
        }

        protected async Task DeleteToAsync(string pid, long toSequenceNr)
        {
            var probe = CreateTestProbe();
            Journal.Tell(new DeleteMessagesTo(pid, toSequenceNr, probe.Ref));
            await probe.ExpectMsgAsync<DeleteMessagesSuccess>(Timeout);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Db.Dispose();
        }
    }
}
