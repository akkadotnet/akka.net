using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Coordination;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit2;
using Akka.Util;

namespace Akka.Cluster.Tools.Tests
{
    public class TestLease : Lease
    {
        public sealed class AcquireReq : IEquatable<AcquireReq>
        {
            public string Owner { get; }

            public AcquireReq(string owner)
            {
                Owner = owner;
            }

            public bool Equals(AcquireReq other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Owner, other.Owner);
            }

            public override bool Equals(object obj) => obj is AcquireReq a && Equals(a);

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

            public bool Equals(ReleaseReq other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Owner, other.Owner);
            }

            public override bool Equals(object obj) => obj is ReleaseReq r && Equals(r);

            public override int GetHashCode() => Owner.GetHashCode();

            public override string ToString() => $"ReleaseReq({Owner})";
        }

        public static Config Configuration
        {
            get { return ConfigurationFactory.ParseString(@"
                test-lease {
                    lease-class = ""Akka.Cluster.Tools.Tests.TestLease, Akka.Cluster.Tools.Tests""
                }
                "); }
        }

        public TestProbe Probe { get; }
        private AtomicReference<Task<bool>> nextAcquireResult;
        private AtomicBoolean nextCheckLeaseResult = new AtomicBoolean(false);
        private AtomicReference<Action<Exception>> currentCallBack = new AtomicReference<Action<Exception>>(_ => { });
        private ILoggingAdapter _log;
        public TaskCompletionSource<bool> InitialPromise { get; } = new TaskCompletionSource<bool>();


        public TestLease(LeaseSettings settings, ExtendedActorSystem system)
            : base(settings)
        {
            _log = Logging.GetLogger(system, "TestLease");
            Probe = new TestProbe(system, new XunitAssertions());
            _log.Info("Creating lease {0}", settings);

            nextAcquireResult = new AtomicReference<Task<bool>>(InitialPromise.Task);

            TestLeaseExt.Get(system).SetTestLease(settings.LeaseName, this);
        }

        public void SetNextAcquireResult(Task<bool> next) => nextAcquireResult.GetAndSet(next);

        public void SetNextCheckLeaseResult(bool value) => nextCheckLeaseResult.GetAndSet(value);

        public Action<Exception> GetCurrentCallback() => currentCallBack.Value;


        public override Task<bool> Acquire()
        {
            _log.Info("acquire, current response " + nextAcquireResult);
            Probe.Ref.Tell(new AcquireReq(Settings.OwnerName));
            return nextAcquireResult.Value;
        }

        public override Task<bool> Release()
        {
            Probe.Ref.Tell(new ReleaseReq(Settings.OwnerName));
            return Task.FromResult(true);
        }

        public override bool CheckLease() => nextCheckLeaseResult.Value;

        public override Task<bool> Acquire(Action<Exception> leaseLostCallback)
        {
            currentCallBack.GetAndSet(leaseLostCallback);
            return Acquire();
        }
    }

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
        private readonly ConcurrentDictionary<string, TestLease> testLeases = new ConcurrentDictionary<string, TestLease>();

        public TestLeaseExt(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(LeaseProvider.DefaultConfig());
        }

        public TestLease GetTestLease(string name)
        {
            if (!testLeases.TryGetValue(name, out var lease))
            {
                throw new InvalidOperationException($"Test lease {name} has not been set yet. Current leases {string.Join(",", testLeases.Keys)}");
            }
            return lease;
        }

        public void SetTestLease(string name, TestLease lease)
        {
            testLeases[name] = lease;
        }
    }
}
