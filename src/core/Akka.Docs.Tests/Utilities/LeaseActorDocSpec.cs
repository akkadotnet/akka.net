//-----------------------------------------------------------------------
// <copyright file="LeaseActorDocSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Coordination;
using Akka.Coordination.Tests;
using Akka.Event;
using Akka.TestKit.Xunit2;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;

#nullable enable
namespace DocsExamples.Utilities.Leases;

public class LeaseActorDocSpec: TestKit
{
    private class LeaseFailed : Exception
    {
        public LeaseFailed(string message) : base(message)
        {
        }

        public LeaseFailed(string message, Exception innerEx)
            : base(message, innerEx)
        {
        }
    }

    private const string ResourceId = "protected-resource";

    private LeaseUsageSettings LeaseSettings => new("test-lease", TimeSpan.FromSeconds(1));
    private string LeaseOwner { get; } = $"leased-actor-{Guid.NewGuid():N}";
    
    public LeaseActorDocSpec(ITestOutputHelper output)
        : base(TestLease.Configuration, nameof(LeaseActorDocSpec), output)
    {
        // start test lease extension
        TestLeaseExt.Get(Sys);
    }
    
    private TestLease GetLease()
    {
        TestLease? testLease = null;
        
        // wait until TestLease is created.
        AwaitAssert(() =>
        {
            testLease = TestLeaseExt.Get(Sys).GetTestLease(ResourceId);
        }, 3.Seconds(), 100.Milliseconds());
        
        return testLease!;
    } 
    
    [Fact]
    public void Actor_with_lease_should_not_be_active_until_lease_is_acquired()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LeaseActor(LeaseSettings, ResourceId, LeaseOwner)));
        testActor.Tell("Hi", TestActor);
        ExpectNoMsg(200.Milliseconds());

        var lease = GetLease();
        lease.InitialPromise.SetResult(true);
        ExpectMsg("Hi");
    }
    
    [Fact]
    public void Actor_with_lease_should_retry_if_initial_acquire_is_false()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LeaseActor(LeaseSettings, ResourceId, LeaseOwner)));
        testActor.Tell("Hi", TestActor);
        ExpectNoMsg(200.Milliseconds());
        
        var lease = GetLease();
        lease.InitialPromise.SetResult(false);
        ExpectNoMsg(200.Milliseconds());
        lease.SetNextAcquireResult(Task.FromResult(true));
        ExpectMsg("Hi");
    }
    
    [Fact]
    public void Actor_with_lease_should_retry_if_initial_acquire_fails()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LeaseActor(LeaseSettings, ResourceId, LeaseOwner)));
        testActor.Tell("Hi", TestActor);
        ExpectNoMsg(200.Milliseconds());
        
        var lease = GetLease();
        lease.InitialPromise.SetException(new LeaseFailed("oh no"));
        ExpectNoMsg(200.Milliseconds());
        lease.SetNextAcquireResult(Task.FromResult(true));
        ExpectMsg("Hi");
    }

    [Fact]
    public void Actor_with_lease_should_terminate_if_lease_lost()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LeaseActor(LeaseSettings, ResourceId, LeaseOwner)));
        testActor.Tell("Hi", TestActor);
        ExpectNoMsg(200.Milliseconds());
        
        var lease = GetLease();
        lease.InitialPromise.SetResult(true);
        ExpectMsg("Hi");
        
        Watch(testActor);
        lease.GetCurrentCallback()(new LeaseFailed("oh dear"));
        ExpectTerminated(testActor);
    }

    [Fact]
    public void Actor_with_lease_should_release_lease_when_stopped()
    {
        var testActor = Sys.ActorOf(Props.Create(() => new LeaseActor(LeaseSettings, ResourceId, LeaseOwner)));
        testActor.Tell("Hi", TestActor);
        ExpectNoMsg(200.Milliseconds());
        
        var lease = GetLease();
        lease.InitialPromise.SetResult(true);
        lease.Probe.ExpectMsg(new TestLease.AcquireReq(LeaseOwner));
        ExpectMsg("Hi");

        Watch(testActor);
        lease.GetCurrentCallback()(new LeaseFailed("oh dear"));
        ExpectTerminated(testActor);
        
        lease.Probe.ExpectMsg(new TestLease.ReleaseReq(LeaseOwner));
    }
    
}

public class LeaseActor: ReceiveActor, IWithStash, IWithTimers
{
    #region messages
    private sealed record LeaseAcquireResult(bool Acquired, Exception? Reason);
    private sealed record LeaseLost(Exception Reason);
    private sealed class LeaseRetryTick
    {
        public static readonly LeaseRetryTick Instance = new();
        private LeaseRetryTick() { }
    }
    #endregion
    
    private const string LeaseRetryTimer = "lease-retry";
    
    private readonly string _resourceId;
    private readonly Lease _lease;
    private readonly TimeSpan _leaseRetryInterval;
    private readonly ILoggingAdapter _log;
    private readonly string _uniqueId;

    #region constructor
    public LeaseActor(LeaseUsageSettings leaseSettings, string resourceId, string actorUniqueId)
    {
        _resourceId = resourceId;
        _uniqueId = actorUniqueId;
        
        _lease = LeaseProvider.Get(Context.System).GetLease(
            leaseName: _resourceId,
            configPath: leaseSettings.LeaseImplementation,
            ownerName: _uniqueId);
        _leaseRetryInterval = leaseSettings.LeaseRetryInterval;
        
        _log = Context.GetLogger();
    }
    #endregion

    public IStash Stash { get; set; } = null!;

    public ITimerScheduler Timers { get; set; } = null!;

    #region actor-states
    private void AcquiringLease()
    {
        Receive<LeaseAcquireResult>(lar =>
        {
            if (lar.Acquired)
            {
                _log.Debug("{0}: Lease acquired", _resourceId);
                Stash.UnstashAll();
                Become(Active);
            }
            else
            {
                _log.Error(lar.Reason, "{0}: Failed to get lease for unique Id [{1}]. Retry in {2}",
                    _resourceId, _uniqueId, _leaseRetryInterval);
                Timers.StartSingleTimer(LeaseRetryTimer, LeaseRetryTick.Instance, _leaseRetryInterval);
            }
        });
        
        Receive<LeaseRetryTick>(_ => AcquireLease());
        
        Receive<LeaseLost>(HandleLeaseLost);
        
        ReceiveAny(msg =>
        {
            _log.Debug("{0}: Got msg of type [{1}] from [{2}] while waiting for lease, stashing",
                _resourceId, msg.GetType().Name, Sender);
            Stash.Stash();
        });
    }

    private void Active()
    {
        Receive<LeaseLost>(HandleLeaseLost);
        
        // TODO: Insert your actor message handlers here
        ReceiveAny(msg => Sender.Tell(msg, Self));
    }
    
    private void HandleLeaseLost(LeaseLost msg)
    {
        _log.Error(msg.Reason, "{0}: unique id [{1}] lease lost", _resourceId, _uniqueId);
        Context.Stop(Self);
    }
    #endregion

    #region lease-acquisition
    private void AcquireLease()
    {
        _log.Info("{0}: Acquiring lease {1}", _resourceId, _lease.Settings);
        var self = Self;
        Acquire().PipeTo(self);
        Become(AcquiringLease);
        return;
        
        async Task<LeaseAcquireResult> Acquire()
        {
            try
            {
                var result = await _lease.Acquire(reason => { self.Tell(new LeaseLost(reason)); });
                return new LeaseAcquireResult(result, null);
            }
            catch (Exception ex)
            {
                return new LeaseAcquireResult(false, ex);
            }
        }
    }
    #endregion

    #region lease-lifecycle
    protected override void PreStart()
    {
        base.PreStart();
        // Acquire a lease when actor starts
        AcquireLease();
    }

    protected override void PostStop()
    {
        base.PostStop();
        // Release the lease when actor stops
        _lease.Release().GetAwaiter().GetResult();
    }
    #endregion

}
