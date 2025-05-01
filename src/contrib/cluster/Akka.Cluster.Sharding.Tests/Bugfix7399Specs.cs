//-----------------------------------------------------------------------
// <copyright file="Bugfix7399Specs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence;
using Akka.Persistence.Journal;
using Akka.Persistence.Snapshot;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Sharding.Tests;

/// <summary>
/// Reproduction for https://github.com/akkadotnet/akka.net/issues/7399 
/// </summary>
public class Bugfix7399Specs : AkkaSpec
{
    public static readonly Config SpecConfig = @$"
akka.loglevel = DEBUG
akka.remote.dot-netty.tcp.port = 0
akka.persistence.journal.plugin = ""akka.persistence.journal.failure""
akka.persistence.journal.failure.class = ""{typeof(FailingJournal).AssemblyQualifiedName}""
                            
akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.failure""
akka.persistence.snapshot-store.failure.class = ""{typeof(FailingSnapshot).AssemblyQualifiedName}""

akka.cluster.sharding {{
    journal-plugin-id = akka.persistence.journal.failure
    snapshot-plugin-id = akka.persistence.snapshot-store.failure

    remember-entities = on
    state-store-mode = ddata
    remember-entities-store = eventsourced
    distributed-data.durable.keys = []
}}

 # quick backoffs
akka.cluster.sharding.entity-restart-backoff = 1s
akka.cluster.sharding.shard-failure-backoff = 1s
akka.cluster.sharding.coordinator-failure-backoff = 1s
akka.cluster.sharding.updating-state-timeout = 1s
akka.cluster.sharding.verbose-debug-logging = on
akka.cluster.sharding.fail-on-invalid-entity-state-transition = on

akka.actor.provider = cluster";
    
    public Bugfix7399Specs(ITestOutputHelper helper) : base(SpecConfig, helper)
    {
    }
    
    protected override void AtStartup()
    {
        // Form a one node cluster
        var cluster = Cluster.Get(Sys);
        cluster.Join(cluster.SelfAddress);
        AwaitAssert(() =>
        {
            cluster.ReadView.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1);
        });
    }

    [Fact]
    public async Task RememberEntitiesShouldRecoverFromInitialFailure()
    {
        // set both journal and snapshot store in failing state
        FailingJournal.Working = false;
        FailingSnapshot.Working = false;
        
        // start the shard: an error message is expected, since it tries to read which entity needs to be remembered
        var shard = await ClusterSharding.Get(Sys).StartAsync(
            typeName: "shard-test",
            entityProps: Props.Create(() => new ShardingActor()),
            messageExtractor: new MessageExtractor(100),
            settings: ClusterShardingSettings.Create(Sys));
        
        // ping a message to the shard
        var probe = CreateTestProbe();
        shard.Tell(new ShardingActor.TestMessage("1", "hello"), probe.Ref);
        
        // shouldn't get anything back due to R-E failures
        await probe.ExpectNoMsgAsync(500.Milliseconds());
        
        // set both journal and snapshot store in working state
        FailingJournal.Working = true;
        FailingSnapshot.Working = true;
        
        // ping a message to the shard
        await WithinAsync(TimeSpan.FromSeconds(10), async () =>
        {
            await AwaitAssertAsync(async () =>
            {
                shard.Tell(new ShardingActor.TestMessage("1", "hello"), probe.Ref);
                await probe.ExpectMsgAsync("hello");
            });
        });
    }

    class MessageExtractor : HashCodeMessageExtractor
    {
        public MessageExtractor(int maxNumberOfShards) : base(maxNumberOfShards)
        {
        }

        public override string EntityId(object message)
        {
            return message switch
            {
                ShardingActor.TestMessage x => x.EntityId,
                _ => null
            };
        }
    }

    class ShardingActor : ReceiveActor
    {
        public record TestMessage(string EntityId, string Content);

        public ShardingActor()
        {
            Receive<TestMessage>(x => { Sender.Tell(x.Content); });
        }
    }

    public class FailingJournal : SharedMemoryJournal
    {
        public static bool Working = false;

        public override Task ReplayMessagesAsync(IActorContext context, string persistenceId, long fromSequenceNr,
            long toSequenceNr, long max, Action<IPersistentRepresentation> recoveryCallback)
        {
            if (!Working)
            {
                throw new ApplicationException("Failed");
            }

            return base.ReplayMessagesAsync(context, persistenceId, fromSequenceNr, toSequenceNr, max,
                recoveryCallback);
        }

        protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            if (!Working)
            {
                throw new ApplicationException("Failed");
            }

            return base.WriteMessagesAsync(messages);
        }
    }

    public class FailingSnapshot : SnapshotStore
    {
        public static bool Working = false;

        protected override Task DeleteAsync(SnapshotMetadata metadata)
        {
            if (!Working)
            {
                throw new ApplicationException("Failed");
            }

            return Task.CompletedTask;
        }

        protected override Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            if (!Working)
            {
                throw new ApplicationException("Failed");
            }

            return Task.CompletedTask;
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId,
            SnapshotSelectionCriteria criteria)
        {
            if (!Working)
            {
                throw new ApplicationException("Failed");
            }

            return null;
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            if (!Working)
            {
                throw new ApplicationException("Failed");
            }

            return Task.CompletedTask;
        }
    }
}
