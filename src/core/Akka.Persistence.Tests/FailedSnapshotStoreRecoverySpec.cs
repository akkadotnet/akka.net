//-----------------------------------------------------------------------
// <copyright file="FailedSnapshotStoreRecoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests;

/// <summary>
/// Scenario: actor that uses ONLY the SnapshotStore fails to recover - what happens?
/// </summary>
public class FailedSnapshotStoreRecoverySpec : PersistenceTestKit
{
    public enum FailureMode
    {
        Explicit,
        Timeout
    }

    public static readonly Config Config = ConfigurationFactory.ParseString(@"
        # need to set recovery timeout to 1s
        akka.persistence.journal-plugin-fallback.recovery-event-timeout = 1s
    ");
    
    public FailedSnapshotStoreRecoverySpec(ITestOutputHelper output) : base(Config, output:output){}
    
    private record Save(string Data);

    private record Fetch();

    private record SnapshotSaved();
    private record RecoveryCompleted();
    
    public sealed class PersistentActor : UntypedPersistentActor
    {
        public PersistentActor(string persistenceId, IActorRef targetActor)
        {
            PersistenceId = persistenceId;
            _targetActor = targetActor;
        }

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly IActorRef _targetActor;

        public override string PersistenceId { get; }
        public string CurrentData { get; set; } = "none";
        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Save s:
                {
                    CurrentData = s.Data;
                    SaveSnapshot(CurrentData);
                    Sender.Tell("ack");
                    break;
                }
                case Fetch:
                {
                    Sender.Tell(CurrentData);
                    break;
                }
                case SaveSnapshotSuccess success:
                {
                    _log.Info("Snapshot saved");
                    _targetActor.Tell(new SnapshotSaved());
                    break;
                }
                case SaveSnapshotFailure failure:
                {
                    _log.Error(failure.Cause, "Snapshot failed");
                    break;
                }
            }
        }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case SnapshotOffer { Snapshot: string str }:
                {
                    CurrentData = str;
                    break;
                }
            }
        }

        protected override void OnReplaySuccess()
        {
            _targetActor.Tell(new RecoveryCompleted());
        }

        protected override void OnRecoveryFailure(Exception reason, object message = null)
        {
            _log.Error(reason, "Recovery failed");
            base.OnRecoveryFailure(reason, message);
        }
    }

    [Theory(DisplayName = "PersistentActor using Snapshots only must fail if snapshots are irrecoverable")]
    [InlineData(FailureMode.Explicit)]
    [InlineData(FailureMode.Timeout)]
    public async Task PersistentActor_using_Snapshots_only_must_fail_if_snapshots_irrecoverable(FailureMode mode)
    {
        // arrange
        var probe = CreateTestProbe();
        var actor = Sys.ActorOf(Props.Create(() => new PersistentActor("p1", probe.Ref)));
        await probe.ExpectMsgAsync<RecoveryCompleted>();
        actor.Tell(new Save("a"), probe);
        await probe.ExpectMsgAsync("ack");
        await probe.ExpectMsgAsync<SnapshotSaved>();
        await actor.GracefulStop(RemainingOrDefault);

        Task SelectBehavior(SnapshotStoreLoadBehavior behavior)
        {
            switch (mode)
            {
                case FailureMode.Timeout:
                    return behavior.FailWithDelay(TimeSpan.FromMinutes(1));
                case FailureMode.Explicit:
                default:
                    return behavior.Fail();
            }
        }

        // act
        await WithSnapshotLoad(SelectBehavior, async () =>
        {
            var actor2 = Sys.ActorOf(Props.Create(() => new PersistentActor("p1", probe.Ref)));
            Watch(actor2);
            await probe.ExpectNoMsgAsync();
            await ExpectTerminatedAsync(actor2);
        });

    }
}