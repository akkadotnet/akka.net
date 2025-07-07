// -----------------------------------------------------------------------
//  <copyright file="ShardSupervisionStrategy.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internal;

namespace Akka.Cluster.Sharding;

public class ShardSupervisionStrategy: OneForOneStrategy
{
    public ShardSupervisionStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, Func<Exception, Directive> localOnlyDecider) 
        : base(maxNrOfRetries, withinTimeRange, localOnlyDecider)
    {
    }

    public ShardSupervisionStrategy(int? maxNrOfRetries, TimeSpan? withinTimeRange, IDecider decider) 
        : base(maxNrOfRetries, withinTimeRange, decider)
    {
    }

    public ShardSupervisionStrategy(int maxNrOfRetries, int withinTimeMilliseconds, Func<Exception, Directive> localOnlyDecider, bool loggingEnabled = true) 
        : base(maxNrOfRetries, withinTimeMilliseconds, localOnlyDecider, loggingEnabled)
    {
    }

    public ShardSupervisionStrategy(int maxNrOfRetries, int withinTimeMilliseconds, IDecider decider, bool loggingEnabled = true) 
        : base(maxNrOfRetries, withinTimeMilliseconds, decider, loggingEnabled)
    {
    }

    public ShardSupervisionStrategy(Func<Exception, Directive> localOnlyDecider) 
        : base(localOnlyDecider)
    {
    }

    public ShardSupervisionStrategy(Func<Exception, Directive> localOnlyDecider, bool loggingEnabled = true) 
        : base(localOnlyDecider, loggingEnabled)
    {
    }

    public ShardSupervisionStrategy(IDecider decider) 
        : base(decider)
    {
    }

    public ShardSupervisionStrategy()
    {
    }

    public override void ProcessFailure(IActorContext context, bool restart, IActorRef child, Exception cause, ChildRestartStats stats, IReadOnlyCollection<ChildRestartStats> children)
    {
        if (restart && stats.RequestRestartPermission(MaxNumberOfRetries, WithinTimeRangeMilliseconds))
            RestartChild(child, cause, suspendFirst: false);
        else
        {
            var reason = restart 
                ? $"entity failed repeatedly within {WithinTimeRangeMilliseconds} ms, exceeding the supervisor strategy maximum restart count of {MaxNumberOfRetries}" 
                : "entity stopped by Directive.Stop decision";
            context.Self.Tell(new SupervisorStopDirectivePassivation(child, reason, cause));
            context.Stop(child);
        }
    }
}