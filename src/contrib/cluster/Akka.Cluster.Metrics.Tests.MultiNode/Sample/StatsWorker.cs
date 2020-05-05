//-----------------------------------------------------------------------
// <copyright file="StatsWorker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Cluster.Metrics.Tests.MultiNode
{
    public class StatsWorker : ReceiveActor
    {
        private IImmutableDictionary<string, int> _cache = ImmutableDictionary<string, int>.Empty;

        public StatsWorker()
        {
            Receive<string>(word =>
            {
                int length;
                if (_cache.TryGetValue(word, out var cachedLength))
                {
                    length = cachedLength;
                }
                else
                {
                    length = word.Length;
                    _cache = _cache.Add(word, length);
                }
                
                Sender.Tell(length);
            });
        }
    }
}
