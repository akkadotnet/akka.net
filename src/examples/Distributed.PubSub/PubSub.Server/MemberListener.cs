using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using Akka.Event;

namespace PubSub.Server
{
    public class MemberListener : ReceiveActor
    {
        private readonly ILoggingAdapter _log;
        private readonly Cluster _cluster;
        private HashSet<Address> _nodes = new HashSet<Address>();

        public MemberListener()
        {
            _log = Context.GetLogger();
            _cluster = Cluster.Get(Context.System);

            Receive<ClusterEvent.CurrentClusterState>(state =>
            {
                _nodes = state.Members
                    .Where(m => m.Status == MemberStatus.Up)
                    .Select(m => m.Address)
                    .ToHashSet();
            });
            
            Receive<ClusterEvent.MemberUp>(member =>
            {
                _nodes.Add(member.Member.Address);
                _log.Info("Member is Up: {0}. {1} nodes in cluster.", member.Member.Address, _nodes.Count);
            });

            Receive<ClusterEvent.MemberRemoved>(member =>
            {
                _nodes.Remove(member.Member.Address);
                _log.Info("Member is Removed: {0}. {1} nodes in cluster.", member.Member.Address, _nodes.Count);
            });
        }

        protected override void PreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);
            _cluster.Subscribe(Self, typeof(ClusterEvent.IMemberEvent));
        }

        protected override void PostStop()
        {
            base.PostStop();
            _cluster.Unsubscribe(Self);
        }
        
        
    }
}