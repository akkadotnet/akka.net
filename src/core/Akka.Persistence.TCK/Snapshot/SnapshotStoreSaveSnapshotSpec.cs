// -----------------------------------------------------------------------
//  <copyright file="SnapshotStoreSaveSnapshotSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using Akka.Persistence.TCK.Serialization;
using Akka.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.TCK.Snapshot;

/// <summary>
/// This spec aims to verify custom <see cref="SnapshotStore"/> <c>SaveSnapshot</c> implementations. 
/// This is an optional spec that checks if <c>SaveSnapshot</c> supports:
/// 1. Saving snapshots with the same sequence number (upsert operation)
/// 2. Saving snapshots concurrently and be able to save them in correct order
/// </summary>
public class SnapshotStoreSaveSnapshotSpec : PluginSpec
{
    private const int RepeatCount = 200;

    private const string SpecConfigTemplate = """
akka.persistence.publish-plugin-commands = on
akka.persistence.snapshot-store {
    plugin = "akka.persistence.snapshot-store.my"
    my {
        class = "TestPersistencePlugin.MySnapshotStore, TestPersistencePlugin"
        plugin-dispatcher = "akka.persistence.dispatchers.default-plugin-dispatcher"
    }
}
akka.actor {
    serializers {
        persistence-tck-test="Akka.Persistence.TCK.Serialization.TestSerializer, Akka.Persistence.TCK"
    }
    serialization-bindings {
        "Akka.Persistence.TCK.Serialization.TestPayload, Akka.Persistence.TCK" = persistence-tck-test
    }
}
""";

    private static readonly Config Config = 
        ConfigurationFactory.ParseString(SpecConfigTemplate);

    private string _guid = string.Empty;
    
    protected override bool SupportsSerialization => true;
    protected virtual bool SupportsConcurrentSaves => false;
    protected string PersistenceId => $"ac-{_guid}";
    protected readonly TestProbe SenderProbe;

    protected SnapshotStoreSaveSnapshotSpec(Config config = null, string actorSystemName = null, ITestOutputHelper output = null) 
        : base(FromConfig(config).WithFallback(Config), actorSystemName ?? "SnapshotStoreSpec", output)
    {
        SenderProbe = CreateTestProbe();
    }

    [Fact(DisplayName = "Rapid multiple SaveSnapshot invocation with no journal persist should only save the latest snapshot")]
    public virtual async Task MultipleSnapshotsWithNoPersistTest()
    {
        if(!SupportsConcurrentSaves)
            return;

        foreach (var iteration in Enumerable.Range(1, RepeatCount))
        {
            NewPersistenceId();
            
            var persistenceActor = CreatePersistenceActor(Sys);
            
            // No persist call before SaveSnapshot burst
            persistenceActor.Tell(new TakeSnapshotsWithValues(new []{ new[]{0}, new[]{1}, new[]{2}, new []{3} }), SenderProbe);
            await SenderProbe.ExpectMsgAsync<SnapshotAck>();
            
            await StopActorAsync(persistenceActor);
            persistenceActor = CreatePersistenceActor(Sys);
            
            persistenceActor.Tell(GetAll.Instance, SenderProbe);
            var result = await SenderProbe.ExpectMsgAsync<int[]>();
            await StopActorAsync(persistenceActor);
            
            result.Length.Should().Be(1, $"expecting an array with length 1 (on iteration {iteration}/{RepeatCount})");
            result[0].Should().Be(3, $"recovered snapshot should be the last snapshot (on iteration {iteration}/{RepeatCount})");
            
            Output.WriteLine($"Iteration: {iteration}");
        }
    }

    [Fact(DisplayName = "Rapid multiple SaveSnapshot invocation with journal persist should only save the latest snapshot")]
    public virtual async Task MultipleSnapshotsWithPersistTest()
    {
        if(!SupportsConcurrentSaves)
            return;

        foreach (var iteration in Enumerable.Range(1, RepeatCount))
        {
            NewPersistenceId();
        
            var persistenceActor = CreatePersistenceActor(Sys);
            
            // persist 2 events
            persistenceActor.Tell(1, SenderProbe);
            SenderProbe.ExpectMsg<Ack>();
            persistenceActor.Tell(2, SenderProbe);
            SenderProbe.ExpectMsg<Ack>();
            
            persistenceActor.Tell(new TakeSnapshotsWithValues(new []{ new[]{0}, new[]{1}, new[]{2}, new []{3} }), SenderProbe);
            await SenderProbe.ExpectMsgAsync<SnapshotAck>();
            
            await StopActorAsync(persistenceActor);
            persistenceActor = CreatePersistenceActor(Sys);
        
            persistenceActor.Tell(GetAll.Instance, SenderProbe);
            var result = await SenderProbe.ExpectMsgAsync<int[]>();
            await StopActorAsync(persistenceActor);
            
            result.Length.Should().Be(1, $"expecting an array with length 1 (on iteration {iteration}/{RepeatCount})");
            result[0].Should().Be(3, $"recovered snapshot should be the last snapshot (on iteration {iteration}/{RepeatCount})");
            
            Output.WriteLine($"Iteration: {iteration}");
        }
    }
    
    [Fact(DisplayName = "Multiple SaveSnapshot invocation with the same sequence number should not throw")]
    public virtual async Task MultipleSnapshotsWithSameSeqNo()
    {
        var persistence = Persistence.Instance.Apply(Sys);
        var snapshotStore = persistence.SnapshotStoreFor(null);
        var snap = new TestPayload(SenderProbe.Ref);
        
        var metadata = new SnapshotMetadata(PersistenceId, 3, DateTime.UtcNow);
        snapshotStore.Tell(new SaveSnapshot(metadata, snap), SenderProbe);
        var success = await SenderProbe.ExpectMsgAsync<SaveSnapshotSuccess>(10.Minutes());
        success.Metadata.PersistenceId.Should().Be(metadata.PersistenceId);
        success.Metadata.Timestamp.Should().Be(metadata.Timestamp);
        success.Metadata.SequenceNr.Should().Be(metadata.SequenceNr);
        
        metadata = new SnapshotMetadata(PersistenceId, 3, DateTime.UtcNow);
        snapshotStore.Tell(new SaveSnapshot(metadata, 3), SenderProbe);
        success = await SenderProbe.ExpectMsgAsync<SaveSnapshotSuccess>();
        success.Metadata.PersistenceId.Should().Be(metadata.PersistenceId);
        success.Metadata.Timestamp.Should().Be(metadata.Timestamp);
        success.Metadata.SequenceNr.Should().Be(metadata.SequenceNr);
    }
    
    #region Utility

    private void NewPersistenceId()
    {
        _guid = Guid.NewGuid().ToString("N")[^8..];
    }
    
    private IActorRef CreatePersistenceActor(ActorSystem sys)
        => sys.ActorOf(Props.Create(() => new MyPersistenceActor(PersistenceId)), "persistence-actor-1");

    private async Task StopActorAsync(IActorRef actor)
    {
        await WatchAsync(actor);
        actor.Tell(PoisonPill.Instance);
        await ExpectTerminatedAsync(actor);
        await UnwatchAsync(actor);
    }
    #endregion

    #region Classes
    private sealed class MyPersistenceActor : ReceivePersistentActor
    {
        private List<int> _values = new();
        private IActorRef? _sender;
        private int _snapshotCount;
        private int _savedSnapshotCount;

        public MyPersistenceActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            Recover<SnapshotOffer>(
                offer =>
                {
                    if (offer.Snapshot is IEnumerable<int> ints)
                        _values = ints.ToList();
                });
            
            Recover<int>(_values.Add);
            
            Command<int>( i =>
            {
                _sender = Sender;
                Persist(i, _ =>
                    {
                        _values.Add(i);
                        _sender.Tell(Ack.Instance);
                    });
            });
            
            Command<TakeSnapshot>(_ => SaveSnapshot(_values));
            
            Command<TakeSnapshotWithValue>(msg => SaveSnapshots(new []{ msg.Values }));
            
            Command<TakeSnapshotsWithValues>(msg => SaveSnapshots(msg.Values));
            
            Command<GetAll>(_ => Sender.Tell(_values.ToArray()));
            
            Command<SaveSnapshotSuccess>(
                _ =>
                {
                    _savedSnapshotCount++;
                    if(_savedSnapshotCount == _snapshotCount)
                        _sender.Tell(SnapshotAck.Instance);
                });
        }

        public override string PersistenceId { get; }

        private void SaveSnapshots(int[][] snapshots)
        {
            _sender = Sender;
            _snapshotCount = snapshots.Length;
            _savedSnapshotCount = 0;
            foreach (var snapshot in snapshots)
            {
                _values = snapshot.ToList();
                SaveSnapshot(_values);
            }
        }
    }
    
    private sealed class Ack
    {
        public static readonly Ack Instance = new Ack();
        private Ack() { }
    }
    
    private sealed class SnapshotAck
    {
        public static readonly SnapshotAck Instance = new();
        private SnapshotAck() { }
    }

    private sealed class GetAll
    {
        public static readonly GetAll Instance = new();
        private GetAll() { }
    }
    
    private sealed class TakeSnapshot
    {
        public static readonly TakeSnapshot Instance = new();
        private TakeSnapshot() { }
    }

    private sealed record TakeSnapshotWithValue(int[] Values);
    
    private sealed record TakeSnapshotsWithValues(int[][] Values);
    
    #endregion
}