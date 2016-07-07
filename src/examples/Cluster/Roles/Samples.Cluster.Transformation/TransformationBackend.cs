//-----------------------------------------------------------------------
// <copyright file="TransformationBackend.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;

namespace Samples.Cluster.Transformation
{
    public class TransformationBackend : UntypedActor
    {
        protected Akka.Cluster.Cluster Cluster = Akka.Cluster.Cluster.Get(Context.System);

        /// <summary>
        /// Need to subscribe to cluster changes
        /// </summary>
        protected override void PreStart()
        {
            Cluster.Subscribe(Self, new[] { typeof(ClusterEvent.MemberUp) });
            Cluster.RegisterOnMemberUp(() =>
            {
                // create routers and other things that depend on me being UP in the cluster
            });
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
            if (message is TransformationMessages.TransformationJob)
            {
                var job = (TransformationMessages.TransformationJob) message;
                Sender.Tell(new TransformationMessages.TransformationResult($"[{Self.Path.ToStringWithAddress(Cluster.SelfAddress)}]{job.ToString().ToUpper()}"), Self);
            }
            else if (message is ClusterEvent.CurrentClusterState)
            {
                var state = (ClusterEvent.CurrentClusterState) message;
                foreach (var member in state.Members)
                {
                    if (member.Status == MemberStatus.Up)
                    {
                        Register(member);
                    }
                }
            }
            else if (message is ClusterEvent.MemberUp)
            {
                var memUp = (ClusterEvent.MemberUp) message;
                Register(memUp.Member);
            }
            else
            {
                Unhandled(message);
            }
        }

        protected void Register(Member member)
        {
            if(member.HasRole("frontend"))
                Context.ActorSelection(member.Address + "/user/frontend").Tell(TransformationMessages.BACKEND_REGISTRATION, Self);
        }
    }
}

