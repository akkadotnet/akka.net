//-----------------------------------------------------------------------
// <copyright file="IScheduledMsg.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Annotations;

namespace Akka.Actor.Scheduler;

/// <summary>
/// Marker interface used to indicate the presence of a scheduled message from the
/// classic scheduler API.
/// </summary>
/// <remarks>
/// Made public so these messages can be filtered for telemetry purposes
/// </remarks>
[InternalApi]
public interface IScheduledTellMsg : IWrappedMessage, INoSerializationVerificationNeeded
{
}

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class ScheduledTellMsg : IScheduledTellMsg
{
    public ScheduledTellMsg(object message)
    {
        Message = message;
    }
    public object Message { get; }
}

/// <summary>
/// INTERNAL API
/// </summary>
internal sealed class ScheduledTellMsgNoInfluenceReceiveTimeout : IScheduledTellMsg, INotInfluenceReceiveTimeout
{
    public ScheduledTellMsgNoInfluenceReceiveTimeout(object message)
    {
        Message = message;
    }

    public object Message { get; }
}