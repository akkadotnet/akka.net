//-----------------------------------------------------------------------
// <copyright file="StatsService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Cluster.Metrics.Tests.MultiNode
{
    public class StatsService : ActorBase
    {
        private readonly IActorRef _workerRouter;
        
        public StatsService()
        {
            // This router is used both with lookup and deploy of routees. If you
            // have a router with only lookup of routees you can use Props.empty
            // instead of Props.Create<StatsWorker>
            _workerRouter = Context.ActorOf(FromConfig.Instance.Props(Props.Create<StatsWorker>()), "workerRouter");
        }
        
        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            if (!(message is StatsJob statsJob) || string.IsNullOrEmpty(statsJob.Text))
                return false;

            var words = statsJob.Text.Split();
            var replyTo = Sender;
            // create actor that collects replies from workers
            var aggregator = Context.ActorOf(Props.Create(() => new StatsAggregator(words.Length, replyTo)));
            foreach (var word in words)
            {
                _workerRouter.Tell(new ConsistentHashableEnvelope(word, word), aggregator);
            }

            return true;
        }
    }

    public class StatsAggregator : ActorBase
    {
        private readonly int _expectedResults;
        private readonly IActorRef _replyTo;
        
        private IImmutableList<int> _results = ImmutableList<int>.Empty;

        public StatsAggregator(int expectedResults, IActorRef replyTo)
        {
            _expectedResults = expectedResults;
            _replyTo = replyTo;
            
            Context.SetReceiveTimeout(3.Seconds());
        }
        
        /// <inheritdoc />
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case int wordCount:
                    _results = _results.Add(wordCount);
                    if (_results.Count == _expectedResults)
                    {
                        var meanWordLength = _results.Sum() * 1.0 / _results.Count;
                        _replyTo.Tell(new StatsResult(meanWordLength));
                        Context.Stop(Self);
                    }
                    return true;
                case ReceiveTimeout _:
                    _replyTo.Tell(new JobFailed("Service unavailable, try again later"));
                    Context.Stop(Self);
                    return true;
            }

            return false;
        }
    }
}
