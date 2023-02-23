// //-----------------------------------------------------------------------
// // <copyright file="QuerySettings.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Configuration;

namespace Akka.Persistence.Query.Sql
{
    public sealed class Retry
    {
        public static readonly Retry Instance = new Retry();
        private Retry() { }
    }

    public sealed class OperationTimedOut
    {
        public static readonly OperationTimedOut Instance = new OperationTimedOut();
        private OperationTimedOut() { }
    }
    
    public sealed class QuerySettings
    {
        public QuerySettings(Config config) 
            : this(
                config.GetTimeSpan("query-operation-timeout", TimeSpan.FromSeconds(10)),
                config.GetInt("max-operation-retries", 10),
                config.GetFloat("exponential-backoff-multiplier", 2.0f),
                config.GetTimeSpan("max-exponential-backoff-time", TimeSpan.FromSeconds(30)))
        { }
        
        public QuerySettings(TimeSpan operationTimeout, int maxRetries, float backoffMultiplier, TimeSpan maxBackoff)
        {
            OperationTimeout = operationTimeout;
            MaxRetries = maxRetries;
            MaxBackoff = maxBackoff;
            BackoffMultiplier = backoffMultiplier;
        }

        public TimeSpan OperationTimeout { get; }
        public int MaxRetries { get; }
        public float BackoffMultiplier { get; }
        public TimeSpan MaxBackoff { get; }
        public TimeSpan BackoffTime { get; } = TimeSpan.FromMilliseconds(200);
    }
}