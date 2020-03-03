//-----------------------------------------------------------------------
// <copyright file="RouterInCodeSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.Routing;

namespace Akka.Cluster.Metrics.Tests
{
    public class RouterInCodeSample : ActorBase
    {
        public void Sample1()
        {
            // <RouterInCodeSample1>
            var totalInstances = 100;
            var routeesPaths = new []{ "/user/factorialBackend", "" };
            var allowLocalRoutees = true;
            var useRoles = "backend";
            IActorRef backend = Context.ActorOf(
                new ClusterRouterGroup(
                    new AdaptiveLoadBalancingGroup(MemoryMetricsSelector.Instance), 
                    new ClusterRouterGroupSettings(totalInstances, routeesPaths, allowLocalRoutees, useRoles)
                ).Props(), 
                "factorialBackendRouter2");
            // </RouterInCodeSample1>
        }

        public void Sample2()
        {
            // <RouterInCodeSample2>
            var totalInstances = 100;
            var maxInstancesPerNode = 3;
            var allowLocalRoutees = false;
            var useRoles = "backend";
            IActorRef backend = Context.ActorOf(
                new ClusterRouterPool(
                        new AdaptiveLoadBalancingPool(CpuMetricsSelector.Instance, 0),
                        new ClusterRouterPoolSettings(totalInstances, maxInstancesPerNode, allowLocalRoutees, useRoles))
                    .Props(Props.Create<FactorialBackend>()),
                "factorialBackendRouter3");
            // </RouterInCodeSample2>
        }

        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            throw new System.NotImplementedException();
        }
    }
}
