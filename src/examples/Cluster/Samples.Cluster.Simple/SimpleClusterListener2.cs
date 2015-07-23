//-----------------------------------------------------------------------
// <copyright file="SimpleClusterListener2.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using Akka.Event;

namespace Samples.Cluster.Simple
{
    /*TODO: this class is not used*/public class SimpleClusterListener2 : UntypedActor
    {
        protected ILoggingAdapter Log = Context.GetLogger();
        protected Akka.Cluster.Cluster Cluster = Akka.Cluster.Cluster.Get(Context.System);

        /// <summary>
        /// Need to subscribe to cluster changes
        /// </summary>
        protected override void PreStart()
        {
            Cluster.Subscribe(Self, new[] { typeof(ClusterEvent.IMemberEvent), typeof(ClusterEvent.UnreachableMember) });
        }

        /// <summary>
        /// Re-subscribe on restart
        /// </summary>
        protected override void PostStop()
        {
            Cluster.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            if (message is ClusterEvent.CurrentClusterState)
            {
                var state = (ClusterEvent.CurrentClusterState) message;
                Log.Info("Current members: {0}", state.Members);
            }
            else if (message is ClusterEvent.MemberUp)
            {
                var up = message as ClusterEvent.MemberUp;
                var mem = up;
                Log.Info("Member is Up: {0}", mem.Member);
            }
            else if (message is ClusterEvent.UnreachableMember)
            {
                var unreachable = (ClusterEvent.UnreachableMember)message;
                Log.Info("Member detected as unreachable: {0}", unreachable.Member);
            }
            else if (message is ClusterEvent.MemberRemoved)
            {
                var removed = (ClusterEvent.MemberRemoved)message;
                Log.Info("Member is Removed: {0}", removed.Member);
            }
            else if (message is ClusterEvent.IMemberEvent)
            {
                //IGNORE                
            }
            else
            {
                Unhandled(message);
            }
        }
    }
}

