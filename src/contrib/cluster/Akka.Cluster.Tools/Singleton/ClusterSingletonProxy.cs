//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonProxy.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;

namespace Akka.Cluster.Tools.Singleton
{
    /// <summary>
    /// <para>
    /// The <see cref="ClusterSingletonProxy"/> works together with the <see cref="ClusterSingletonManager"/> to provide a
    /// distributed proxy to the singleton actor.
    /// </para>
    /// <para>
    /// The proxy can be started on every node where the singleton needs to be reached and used as if it were the singleton
    /// itself. It will then act as a router to the currently running singleton instance. If the singleton is not currently
    /// available, e.g., during hand off or startup, the proxy will buffer the messages sent to the singleton and then deliver
    /// them when the singleton is finally available. The size of the buffer is configurable and it can be disabled by using
    /// a buffer size of 0. When the buffer is full old messages will be dropped when new messages are sent via the proxy.
    /// </para>
    /// <para>
    /// The proxy works by keeping track of the oldest cluster member. When a new oldest member is identified, e.g. because
    /// the older one left the cluster, or at startup, the proxy will try to identify the singleton on the oldest member by
    /// periodically sending an <see cref="Identify"/> message until the singleton responds with its <see cref="ActorIdentity"/>.
    /// </para>
    /// <remarks>
    /// Note that this is a best effort implementation: messages can always be lost due to the distributed nature of the actors involved.
    /// </remarks>
    /// </summary>
    public sealed class ClusterSingletonProxy : ReceiveActor
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class TryToIdentifySingleton : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static TryToIdentifySingleton Instance { get; } = new TryToIdentifySingleton();
            private TryToIdentifySingleton() { }
        }

        /// <summary>
        /// Returns default HOCON configuration for the cluster singleton.
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterSingletonManager>("Akka.Cluster.Tools.Singleton.reference.conf");
        }

        /// <summary>
        /// Factory method for <see cref="ClusterSingletonProxy"/> <see cref="Actor.Props"/>.
        /// </summary>
        /// <param name="singletonManagerPath">
        /// The logical path of the singleton manager, e.g. `/user/singletonManager`,
        /// which ends with the name you defined in `actorOf` when creating the <see cref="ClusterSingletonManager"/>.
        /// </param>
        /// <param name="settings">Cluster singleton proxy settings.</param>
        /// <returns>TBD</returns>
        public static Props Props(string singletonManagerPath, ClusterSingletonProxySettings settings)
        {
            return Actor.Props.Create(() => new ClusterSingletonProxy(singletonManagerPath, settings)).WithDeploy(Deploy.Local);
        }

        private readonly ClusterSingletonProxySettings _settings;
        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly Queue<KeyValuePair<object, IActorRef>> _buffer = new Queue<KeyValuePair<object, IActorRef>>(); // queue seems to fit better
        private readonly string[] _singletonPath;
        private int _identityCounter = 0;
        private string _identityId;
        private IActorRef _singleton = null;
        private ICancelable _identityTimer = null;
        private ImmutableSortedSet<Member> _membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(MemberAgeOrdering.Descending);
        private ILoggingAdapter _log;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="singletonManagerPath">TBD</param>
        /// <param name="settings">TBD</param>
        public ClusterSingletonProxy(string singletonManagerPath, ClusterSingletonProxySettings settings)
        {
            _settings = settings;
            _singletonPath = (singletonManagerPath + "/" + settings.SingletonName).Split('/');
            _identityId = CreateIdentifyId(_identityCounter);

            Receive<ClusterEvent.CurrentClusterState>(s => HandleInitial(s));
            Receive<ClusterEvent.MemberUp>(m => Add(m.Member));
            Receive<ClusterEvent.MemberExited>(m => Remove(m.Member));
            Receive<ClusterEvent.MemberRemoved>(m =>
            {
                if (m.Member.UniqueAddress.Equals(_cluster.SelfUniqueAddress))
                    Context.Stop(Self);
                else
                    Remove(m.Member);
            });
            Receive<ClusterEvent.IMemberEvent>(m =>
            {
                /* do nothing */
            });
            Receive<ActorIdentity>(identity =>
                {
                    if (identity.Subject != null)
                    {
                        // if the new singleton is defined, deliver all buffered messages
                        var subject = identity.Subject;
                        Log.Info("Singleton identified at [{0}]", subject.Path);
                        _singleton = subject;
                        Context.Watch(subject);
                        CancelTimer();
                        SendBuffered();
                    }
                });
            Receive<TryToIdentifySingleton>(_ =>
                 {
                     var oldest = _membersByAge.FirstOrDefault();
                     if (oldest != null && _identityTimer != null)
                     {
                         var singletonAddress = new RootActorPath(oldest.Address) / _singletonPath;
                         Log.Debug("Trying to identify singleton at [{0}]", singletonAddress);
                         Context.ActorSelection(singletonAddress).Tell(new Identify(_identityId));
                     }
                 });
            Receive<Terminated>(terminated =>
                {
                    if (Equals(_singleton, terminated.ActorRef))
                    {
                        // buffering mode, identification of new will start when old node is removed
                        _singleton = null;
                    }
                });
            ReceiveAny(msg =>
                {
                    if (_singleton != null)
                    {
                        if (Log.IsDebugEnabled)
                            Log.Debug("Forwarding message of type [{0}] to current singleton instance at [{1}]", msg.GetType(), _singleton.Path);
                        _singleton.Forward(msg);
                    }
                    else
                        Buffer(msg);
                });
        }

        private ILoggingAdapter Log => _log ?? (_log = Context.GetLogger());

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            CancelTimer();
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            CancelTimer();
            _cluster.Unsubscribe(Self);
        }

        private void CancelTimer()
        {
            if (_identityTimer != null)
            {
                _identityTimer.Cancel();
                _identityTimer = null;
            }
        }

        private bool MatchingRole(Member member)
        {
            if (string.IsNullOrEmpty(_settings.Role)) return true;

            return member.HasRole(_settings.Role);
        }

        private void HandleInitial(ClusterEvent.CurrentClusterState state)
        {
            TrackChanges(() =>
                _membersByAge = state.Members
                    .Where(m => m.Status == MemberStatus.Up && MatchingRole(m))
                    .ToImmutableSortedSet(MemberAgeOrdering.Descending));
        }

        // Discard old singleton ActorRef and send a periodic message to self to identify the singleton.
        private void IdentifySingleton()
        {
            Log.Debug("Creating singleton identification timer...");
            _identityCounter++;
            _identityId = CreateIdentifyId(_identityCounter);
            _singleton = null;
            CancelTimer();
            _identityTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
                initialDelay: TimeSpan.Zero,
                interval: _settings.SingletonIdentificationInterval,
                receiver: Self,
                message: TryToIdentifySingleton.Instance,
                sender: Self);
        }

        private void TrackChanges(Action block)
        {
            var before = _membersByAge.FirstOrDefault();
            block();
            var after = _membersByAge.FirstOrDefault();

            // if the head has changed, I need to find the new singleton
            if (!Equals(before, after)) IdentifySingleton();
        }

        private void Add(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() =>
                {
                    _membersByAge = _membersByAge.Remove(member); //replace
                    _membersByAge = _membersByAge.Add(member);
                });
        }

        private void Remove(Member member)
        {
            if (MatchingRole(member))
                TrackChanges(() => _membersByAge = _membersByAge.Remove(member));
        }

        private string CreateIdentifyId(int i)
        {
            return "identify-singleton-" + string.Join("/", _singletonPath) + i;
        }

        private void Buffer(object message)
        {
            if (_settings.BufferSize == 0)
                Log.Debug("Singleton not available and buffering is disabled, dropping message [{0}]", message.GetType());
            else if (_buffer.Count == _settings.BufferSize)
            {
                var first = _buffer.Dequeue();
                Log.Debug("Singleton not available, buffer is full, dropping first message [{0}]", first.Key.GetType());
                _buffer.Enqueue(new KeyValuePair<object, IActorRef>(message, Sender));
            }
            else
            {
                Log.Debug("Singleton not available, buffering message type [{0}]", message.GetType());
                _buffer.Enqueue(new KeyValuePair<object, IActorRef>(message, Sender));
            }
        }

        private void SendBuffered()
        {
            Log.Debug("Sending buffered messages to current singleton instance");
            while (_buffer.Count != 0)
            {
                var pair = _buffer.Dequeue();
                _singleton.Tell(pair.Key, pair.Value);
            }
        }
    }
}
