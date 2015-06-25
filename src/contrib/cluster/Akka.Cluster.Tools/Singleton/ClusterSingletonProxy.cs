using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Cluster.Tools.Singleton
{

    internal sealed class ClusterSingletonProxyActor : UntypedActor, IWithUnboundedStash
    {
        internal sealed class TryToIdentifySingleton
        {
            private TryToIdentifySingleton() { }
            static TryToIdentifySingleton() { }

            private static readonly TryToIdentifySingleton _instance = new TryToIdentifySingleton();
            public static TryToIdentifySingleton Instance { get { return _instance; } }
        }

        private ImmutableSortedSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(MemberAgeOrdering.Descending);

        private readonly string[] _singletonPath;
        private readonly Akka.Cluster.Cluster _cluster = Akka.Cluster.Cluster.Get(Context.System);
        private int _identityCounter = 0;
        private string _identityId;
        private IActorRef _singleton;
        private string _role;
        private CancellationTokenSource _identityTimer;
        private TimeSpan _singletonIdentificationInterval;

        public ClusterSingletonProxyActor(string singletonPathString, string role, TimeSpan singletonIdentificationInterval)
        {
            _singletonPath = singletonPathString.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            _identityId = CreateIdentifyId(_identityCounter);
            _role = role;
        }

        public ClusterSingletonProxyActor(string singletonManagerPath, ClusterSingletonProxySettings settings)
        {
            throw new NotImplementedException();
        }

        private bool MatchingRole(Member member)
        {
            if (String.IsNullOrEmpty(_role)) return true;

            return member.HasRole(_role);
        }

        private void IdentifySingleton()
        {
            // log.debug("Creating singleton identification timer...")
            _identityCounter += 1;
            _identityId = CreateIdentifyId(_identityCounter);
            _singleton = null;
            CancelTimer();
            _identityTimer = new CancellationTokenSource();
            Context.System.Scheduler.Schedule(
                TimeSpan.Zero,
                _singletonIdentificationInterval,
                Self,
                TryToIdentifySingleton.Instance,
                _identityTimer.Token);
        }

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();

            // todo: should execute if after null?
            if (after != null && !after.Equals(before)) IdentifySingleton();
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            TrackChanges(
                () =>
                {
                    _membersByAge =
                        state.Members.Where(m => m.Status == MemberStatus.Up && MatchingRole(m))
                            .ToImmutableSortedSet(MemberAgeOrdering.Descending);
                });
        }


        private void Add(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Add(member));
        }

        private void Remove(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Remove(member));
        }


        private void CancelTimer()
        {
            if (_identityTimer != null)
            {
                _identityTimer.Cancel();
                _identityTimer.Dispose();
                _identityTimer = null;
            }
        }
        private string CreateIdentifyId(int i)
        {
            return "identify-singleton-" + String.Join("/", _singletonPath) + i;
        }

        public IStash Stash { get; set; }

        protected override void PreStart()
        {
            CancelTimer();
            _cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent) });
        }

        protected override void PostStop()
        {
            CancelTimer();
            _cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            message.Match()
                .With<ClusterEvent.CurrentClusterState>(state => HandleInitial(state))
                .With<ClusterEvent.MemberUp>(m => Add(m.Member))
                .With<ClusterEvent.MemberExited>(m => Remove(m.Member))
                .With<ClusterEvent.MemberRemoved>(m => Remove(m.Member))
                .With<ActorIdentity>(identity =>
                {
                    if (identity.Subject != null)
                    {
                        // if the new singleton is defined, unstash all messages
                        // log.info("Singleton identified: {}", s.path)
                        _singleton = identity.Subject;
                        CancelTimer();
                        Stash.UnstashAll();
                    }
                })
                .With<TryToIdentifySingleton>(() =>
                {
                    var oldest = _membersByAge.FirstOrDefault();
                    if (oldest != null && _identityTimer != null)
                    {
                        var singletonAddress = new RootActorPath(oldest.Address) / _singletonPath;
                        Context.ActorSelection(singletonAddress).Tell(new Identify(_identityId));
                    }
                })
                 .Default(msg =>
                  {
                      if (_singleton != null)
                      {
                          //   log.debug("Forwarding message to current singleton instance {}", msg)
                          _singleton.Forward(msg);
                      }
                      else
                      {
                          //    log.debug("No singleton available, stashing message {}", msg)
                          Stash.Stash();
                      }
                  });
        }
    }

    public static class ClusterSingletonProxy
    {
        // todo: implement overloads
        public static Props Props(string singletonPath, string role, TimeSpan singletonIdentificationInterval)
        {
            return Actor.Props.Create<ClusterSingletonProxyActor>(singletonPath, role, singletonIdentificationInterval);
        }

        public static Props Props(string singletonManagerPath, ClusterSingletonProxySettings settings)
        {
            return Actor.Props.Create(() => new ClusterSingletonProxyActor(singletonManagerPath, settings)).WithDeploy(Deploy.Local);
        }
    }

    /**
     * @param singletonName The actor name of the singleton actor that is started by the [[ClusterSingletonManager]].
     * @param role The role of the cluster nodes where the singleton can be deployed. If None, then any node will do.
     * @param singletonIdentificationInterval Interval at which the proxy will try to resolve the singleton instance.
     * @param bufferSize If the location of the singleton is unknown the proxy will buffer this number of messages
     *   and deliver them when the singleton is identified. When the buffer is full old messages will be dropped
     *   when new messages are sent viea the proxy. Use 0 to disable buffering, i.e. messages will be dropped
     *   immediately if the location of the singleton is unknown.
     */
    public sealed class ClusterSingletonProxySettings
    {
        public static ClusterSingletonProxySettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.singleton-proxy");
            if(config == null)
                throw new ConfigurationException(string.Format("Cannot create {0}: akka.cluster.singleton-proxy configuration node not found", typeof(ClusterSingletonProxySettings)));

            return Create(config);
        }

        public static ClusterSingletonProxySettings Create(Config config)
        {
            var role = config.GetString("role");
            if (role == string.Empty) role = null;

            return new ClusterSingletonProxySettings(
                singletonName: config.GetString("singleton-name"),
                role: role, 
                singletonIdentificationInterval: config.GetTimeSpan("singleton-identification-interval"),
                bufferSize: config.GetInt("buffer-size"));
        }

        public readonly string SingletonName;
        public readonly string Role;
        public readonly TimeSpan SingletonIdentificationInterval;
        public readonly int BufferSize;

        public ClusterSingletonProxySettings(string singletonName, string role, TimeSpan singletonIdentificationInterval, int bufferSize)
        {
            if (string.IsNullOrEmpty(singletonName))
                throw new ArgumentNullException("singletonName");
            if (singletonIdentificationInterval == TimeSpan.Zero)
                throw new ArgumentException("ClusterSingletonProxySettings.SingletonIdentificationInterval must be positive", "singletonIdentificationInterval");
            if (bufferSize <= 0)
                throw new ArgumentException("ClusterSingletonProxySettings.BufferSize must be positive", "bufferSize");

            SingletonName = singletonName;
            Role = role;
            SingletonIdentificationInterval = singletonIdentificationInterval;
            BufferSize = bufferSize;
        }

        public ClusterSingletonProxySettings WithSingletonName(string singletonName)
        {
            return Copy(singletonName: singletonName);
        }

        public ClusterSingletonProxySettings WithRole(string role)
        {
            return new ClusterSingletonProxySettings(
                singletonName: SingletonName,
                role: role,
                singletonIdentificationInterval: SingletonIdentificationInterval,
                bufferSize: BufferSize);
        }

        public ClusterSingletonProxySettings WithSingletonIdentificationInterval(string singletonIdentificationInterval)
        {
            return Copy(singletonIdentificationInterval: SingletonIdentificationInterval);
        }

        public ClusterSingletonProxySettings WithBufferSize(int bufferSize)
        {
            return Copy(bufferSize: bufferSize);
        }

        public ClusterSingletonProxySettings Copy(string singletonName = null, string role = null,
            TimeSpan? singletonIdentificationInterval = null, int? bufferSize = null)
        {
            return new ClusterSingletonProxySettings(
                singletonName: singletonName ?? SingletonName,
                role: role ?? Role,
                singletonIdentificationInterval: singletonIdentificationInterval ?? SingletonIdentificationInterval,
                bufferSize: bufferSize ?? BufferSize);
        }
    }
}
