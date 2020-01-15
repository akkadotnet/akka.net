//-----------------------------------------------------------------------
// <copyright file="TransformationBackend.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            switch (message)
            {
                case TransformationMessages.TransformationJob job:
                    Sender.Tell(new TransformationMessages.TransformationResult($"[{Self.Path.ToStringWithAddress(Cluster.SelfAddress)}]{job.ToString().ToUpper()}"), Self);
                    break;
                case ClusterEvent.CurrentClusterState state:
                {
                    foreach (var member in state.Members)
                    {
                        if (member.Status == MemberStatus.Up)
                        {
                            Register(member);
                        }
                    }

                    break;
                }
                case ClusterEvent.MemberUp memUp:
                    Register(memUp.Member);
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        protected void Register(Member member)
        {
            if(member.HasRole("frontend"))
                Context.ActorSelection(member.Address + "/user/frontend").Tell(TransformationMessages.BACKEND_REGISTRATION, Self);
        }
    }
}

