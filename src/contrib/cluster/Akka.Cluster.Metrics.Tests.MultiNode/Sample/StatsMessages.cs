//-----------------------------------------------------------------------
// <copyright file="StatsMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Cluster.Metrics.Tests.MultiNode
{
    [Serializable]
    public sealed class StatsJob
    {
        public StatsJob(string text)
        {
            Text = text;
        }

        public string Text { get; }
    }

    [Serializable]
    public sealed class StatsResult
    {
        public StatsResult(double meanWordLength)
        {
            MeanWordLength = meanWordLength;
        }

        public double MeanWordLength { get; }
    }

    [Serializable]
    public sealed class JobFailed
    {
        public JobFailed(string reason)
        {
            Reason = reason;
        }

        public string Reason { get; }
    }
}
