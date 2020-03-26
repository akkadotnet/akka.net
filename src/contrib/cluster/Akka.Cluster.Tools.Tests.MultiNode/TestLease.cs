using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Coordination;
using Akka.Event;
using Akka.Util;

namespace Akka.Cluster.Tools.Tests.MultiNode
{
    internal class TestLeaseActor : ActorBase
    {
        internal interface ILeaseRequest
        {
        }

        internal sealed class Acquire : ILeaseRequest, IEquatable<Acquire>
        {
            public string Owner { get; }

            public Acquire(string owner)
            {
                Owner = owner;
            }

            public bool Equals(Acquire other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Owner, other.Owner);
            }

            public override bool Equals(object obj) => obj is Acquire a && Equals(a);

            public override int GetHashCode() => Owner.GetHashCode();

            public override string ToString() => $"Acquire({Owner})";
        }

        internal sealed class Release : ILeaseRequest, IEquatable<Release>
        {
            public string Owner { get; }

            public Release(string owner)
            {
                Owner = owner;
            }

            public bool Equals(Release other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(Owner, other.Owner);
            }

            public override bool Equals(object obj) => obj is Release r && Equals(r);

            public override int GetHashCode() => Owner.GetHashCode();

            public override string ToString() => $"Release({Owner})";
        }

        internal sealed class Create : ILeaseRequest, IEquatable<Create>
        {
            public string LeaseName { get; }
            public string OwnerName { get; }

            public Create(string leaseName, string ownerName)
            {
                LeaseName = leaseName;
                OwnerName = ownerName;
            }

            public bool Equals(Create other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(this, other)) return true;

                return Equals(LeaseName, other.LeaseName) && Equals(OwnerName, other.OwnerName);
            }

            public override bool Equals(object obj) => obj is Create c && Equals(c);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = LeaseName.GetHashCode();
                    hashCode = (hashCode * 397) ^ OwnerName.GetHashCode();
                    return hashCode;
                }
            }

            public override string ToString() => $"Create({LeaseName}, {OwnerName})";
        }

        internal sealed class GetRequests
        {
            public static readonly GetRequests Instance = new GetRequests();
            private GetRequests()
            {
            }
        }

        internal sealed class LeaseRequests
        {
            public List<ILeaseRequest> Requests { get; }

            public LeaseRequests(List<ILeaseRequest> requests)
            {
                Requests = requests;
            }

            public override string ToString() => $"LeaseRequests({string.Join(", ", Requests.Select(i => i.ToString()))})";
        }


        internal sealed class ActionRequest // boolean of Failure
        {
            public ILeaseRequest Request { get; }
            public bool Result { get; }

            public ActionRequest(ILeaseRequest request, bool result)
            {
                Request = request;
                Result = result;
            }

            public override string ToString() => $"ActionRequest({Request}, {Result})";
        }

        public static Props Props => Props.Create(() => new TestLeaseActor());

        private ILoggingAdapter _log = Context.GetLogger();
        private readonly List<(IActorRef, ILeaseRequest)> requests = new List<(IActorRef, ILeaseRequest)>();

        public TestLeaseActor()
        {
        }

        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Create c:
                    _log.Info("Lease created with name {0} ownerName {1}", c.LeaseName, c.OwnerName);
                    return true;

                case ILeaseRequest request:
                    _log.Info("Lease request {0} from {1}", request, Sender);
                    requests.Insert(0, (Sender, request));
                    return true;

                case GetRequests _:
                    Sender.Tell(new LeaseRequests(requests.Select(i => i.Item2).ToList()));
                    return true;

                case ActionRequest ar:
                    var r = requests.Where(i => i.Item2.Equals(ar.Request)).FirstOrDefault();
                    if (r.Item1 != null)
                    {
                        _log.Info("Actioning request {0} to {1}", r.Item2, ar.Result);
                        r.Item1.Tell(ar.Result);
                        requests.RemoveAll(i => i.Item2.Equals(ar.Request));
                    }
                    else
                        throw new InvalidOperationException($"unknown request to action: {ar.Request}. Requests: { string.Join(", ", requests.Select(i => $"([{i.Item1}],[{i.Item2}])"))}");
                    return true;
            }
            return false;
        }
    }



    public class TestLeaseActorClientExtExtensionProvider : ExtensionIdProvider<TestLeaseActorClientExt>
    {
        public override TestLeaseActorClientExt CreateExtension(ExtendedActorSystem system)
        {
            var extension = new TestLeaseActorClientExt(system);
            return extension;
        }
    }

    public class TestLeaseActorClientExt : IExtension
    {
        public static TestLeaseActorClientExt Get(ActorSystem system)
        {
            return system.WithExtension<TestLeaseActorClientExt, TestLeaseActorClientExtExtensionProvider>();
        }

        private readonly ExtendedActorSystem _system;
        private AtomicReference<IActorRef> leaseActor = new AtomicReference<IActorRef>();

        public TestLeaseActorClientExt(ExtendedActorSystem system)
        {
            _system = system;
        }

        internal IActorRef GetLeaseActor()
        {
            var lease = leaseActor.Value;
            if (lease == null)
                throw new InvalidOperationException("LeaseActorRef must be set first");
            return lease;
        }

        internal void SetActorLease(IActorRef client)
        {
            leaseActor.GetAndSet(client);
        }
    }

    internal class TestLeaseActorClient : Lease
    {
        private ILoggingAdapter _log;

        private IActorRef leaseActor;

        public TestLeaseActorClient(LeaseSettings settings, ExtendedActorSystem system)
            : base(settings)
        {
            _log = Logging.GetLogger(system, "TestLeaseActorClient");

            leaseActor = TestLeaseActorClientExt.Get(system).GetLeaseActor();
            _log.Info("lease created {0}", settings);
            leaseActor.Tell(new TestLeaseActor.Create(settings.LeaseName, settings.OwnerName));
        }

        public override Task<bool> Acquire()
        {
            return leaseActor.Ask(new TestLeaseActor.Acquire(Settings.OwnerName)).ContinueWith(r => (bool)r.Result);
        }

        public override Task<bool> Release()
        {
            return leaseActor.Ask(new TestLeaseActor.Release(Settings.OwnerName)).ContinueWith(r => (bool)r.Result);
        }

        public override bool CheckLease() => false;

        public override Task<bool> Acquire(Action<Exception> leaseLostCallback)
        {
            return leaseActor.Ask(new TestLeaseActor.Acquire(Settings.OwnerName)).ContinueWith(r => (bool)r.Result);
        }
    }
}
