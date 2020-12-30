﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using Akka.Actor;
using Akka.Cluster.Metrics;
using Akka.Cluster.Routing;
using Akka.Event;
using Akka.Routing;

namespace Samples.Cluster.Metrics.Common
{
    public class FactorialFrontend : ReceiveActor
    {
        private readonly IActorRef _backend;
        private readonly int _upToN;
        private readonly bool _repeat;
        private readonly ILoggingAdapter _log;

        public FactorialFrontend(int upToN, bool repeat)
        {
            var paths = new List<string>
            {
                "/user/factorialBackend-1",
                "/user/factorialBackend-2",
                "/user/factorialBackend-3",
                "/user/factorialBackend-4",
                "/user/factorialBackend-5",
                "/user/factorialBackend-6"
            };

            _backend = Context.System.ActorOf(
                new ClusterRouterGroup(
                        local: new AdaptiveLoadBalancingGroup(MixMetricsSelector.Instance),
                        settings: new ClusterRouterGroupSettings(
                            10,
                            ImmutableHashSet.Create(paths.ToArray()),
                            allowLocalRoutees: false,
                            useRole: "backend"))
                    .Props(), "factorialBackendRouter");


            _upToN = upToN;
            _repeat = repeat;
            _log = Context.GetLogger();

            Receive<(int, BigInteger)>(msg =>
            {
                var (n, factorial) = msg;
                if (n == _upToN)
                {
                    _log.Debug($"{n}! = {factorial}");
                    if(repeat)
                        SendJobs();
                    else
                        Context.Stop(Self);
                }
            });

            Receive<ReceiveTimeout>(_ =>
            {
                _log.Info("Timeout");
                SendJobs();
            });
        }

        protected override void PreStart()
        {
            SendJobs();
            if (_repeat)
                Context.SetReceiveTimeout(TimeSpan.FromSeconds(10));
        }

        private void SendJobs()
        {
            _log.Info($"Starting batch of factorials up to [{_upToN}]");
            foreach (var n in Enumerable.Range(1, _upToN))
                _backend.Tell(n);
        }
    }
}
