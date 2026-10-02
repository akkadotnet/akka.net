//-----------------------------------------------------------------------
// <copyright file="TestLease.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Hosting.SBR;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Event;
using Akka.Hosting;
using Akka.Hosting.Coordination;
using Akka.Util;

namespace Akka.Cluster.Hosting.Tests.Lease
{
    public class TestLeaseExtExtensionProvider : ExtensionIdProvider<TestLeaseExt>
    {
        public override TestLeaseExt CreateExtension(ExtendedActorSystem system)
        {
            var extension = new TestLeaseExt(system);
            return extension;
        }
    }

    public class TestLeaseExt : IExtension
    {
        public static TestLeaseExt Get(ActorSystem system)
        {
            return system.WithExtension<TestLeaseExt, TestLeaseExtExtensionProvider>();
        }

        private readonly ExtendedActorSystem _system;
        private readonly ConcurrentDictionary<string, TestLease> _testLeases = new();

        public TestLeaseExt(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(LeaseProvider.DefaultConfig());
        }

        public TestLease GetTestLease(string name)
        {
            if (!_testLeases.TryGetValue(name, out var lease))
            {
                throw new InvalidOperationException($"Test lease {name} has not been set yet. Current leases {string.Join(",", _testLeases.Keys)}");
            }
            return lease;
        }

        public void SetTestLease(string name, TestLease lease)
        {
            _testLeases[name] = lease;
        }
    }

    public sealed class TestLeaseOption : LeaseOptionBase
    {
        public override string ConfigPath => "test-lease";
        public override Type Class => typeof(TestLease);
        public override void Apply(AkkaConfigurationBuilder builder, Setup? setup = null)
        {
            // no-op
        }
    }
    
    public class TestLease : Coordination.Lease
    {
        public sealed class AcquireReq : IEquatable<AcquireReq>
        {
            public string Owner { get; }

            public AcquireReq(string owner)
            {
                Owner = owner;
            }

            public bool Equals(AcquireReq? other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Owner, other.Owner);
            }

            public override bool Equals(object? obj) => obj is AcquireReq a && Equals(a);

            public override int GetHashCode() => Owner.GetHashCode();

            public override string ToString() => $"AcquireReq({Owner})";
        }

        public sealed class ReleaseReq : IEquatable<ReleaseReq>
        {
            public string Owner { get; }

            public ReleaseReq(string owner)
            {
                Owner = owner;
            }

            public bool Equals(ReleaseReq? other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Owner, other.Owner);
            }

            public override bool Equals(object? obj) => obj is ReleaseReq r && Equals(r);

            public override int GetHashCode() => Owner.GetHashCode();

            public override string ToString() => $"ReleaseReq({Owner})";
        }

        public static Config Configuration => ConfigurationFactory.ParseString(
            $"test-lease.lease-class = \"{typeof(TestLease).AssemblyQualifiedName}\"");

        private readonly AtomicReference<Task<bool>> _nextAcquireResult;
        private readonly AtomicBoolean _nextCheckLeaseResult = new();
        private readonly AtomicReference<Action<Exception>> _currentCallBack = new(_ => { });
        private readonly ILoggingAdapter _log;
        private TaskCompletionSource<bool> InitialPromise { get; } = new();

        public TestLease(LeaseSettings settings, ExtendedActorSystem system)
            : base(settings)
        {
            _log = Logging.GetLogger(system, "TestLease");
            _log.Info("Creating lease {0}", settings);

            _nextAcquireResult = new AtomicReference<Task<bool>>(InitialPromise.Task);

            TestLeaseExt.Get(system).SetTestLease(settings.LeaseName, this);
        }

        public void SetNextAcquireResult(Task<bool> next) => _nextAcquireResult.GetAndSet(next);

        public void SetNextCheckLeaseResult(bool value) => _nextCheckLeaseResult.GetAndSet(value);

        public Action<Exception> GetCurrentCallback() => _currentCallBack.Value;


        public override Task<bool> Acquire()
        {
            _log.Info("acquire, current response " + _nextAcquireResult);
            return _nextAcquireResult.Value;
        }

        public override Task<bool> Release()
        {
            return Task.FromResult(true);
        }

        public override bool CheckLease() => _nextCheckLeaseResult.Value;

        public override Task<bool> Acquire(Action<Exception> leaseLostCallback)
        {
            _currentCallBack.GetAndSet(leaseLostCallback);
            return Acquire();
        }
    }
}
