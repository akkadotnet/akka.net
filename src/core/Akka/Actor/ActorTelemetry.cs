//-----------------------------------------------------------------------
// <copyright file="ActorTelemetry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

#nullable enable
namespace Akka.Actor
{
    /// <summary>
    /// A set of events designed to provide some basic telemetry functions for monitoring actor lifecycles.
    ///
    /// We want to track actor starts, stops, and restarts. More detailed metrics, such as mailbox size or message
    /// processing rates will require something like Phobos [https://phobos.petabridge.com/].
    /// </summary>
    /// <remarks>
    /// Not intended to be sent across network boundaries - should only be processed locally via the <see cref="EventStream"/>.
    /// </remarks>
    public interface IActorTelemetryEvent : INoSerializationVerificationNeeded, INotInfluenceReceiveTimeout
    {
        /// <summary>
        /// The actor who emitted this event.
        /// </summary>
        IActorRef Subject {get;}
        
        /// <summary>
        /// The implementation type for this actor.
        /// </summary>
        Type ActorType { get; }
        
        /// <summary>
        /// A type name override for the actor
        /// </summary>
        public string ActorTypeOverride { get; }
    }
    
    // Create ActorTelemetryEvent messages for the following events: starting an actor, stopping an actor, restarting an actor
    public sealed class ActorStarted : IActorTelemetryEvent
    {
        public ActorStarted(IActorRef subject, Type actorType, string? actorTypeOverride = null)
        {
            Subject = subject;
            ActorType = actorType;
            
            if(actorTypeOverride is not null)
                ActorTypeOverride = actorTypeOverride;
        }

        public IActorRef Subject { get; }
        public Type ActorType { get; }
        public string ActorTypeOverride { get; } = string.Empty;
    }

    /// <summary>
    /// Event emitted when actor shuts down.
    /// </summary>
    public sealed class ActorStopped : IActorTelemetryEvent
    {
        public ActorStopped(IActorRef subject, Type actorType, string? actorTypeOverride = null)
        {
            Subject = subject;
            ActorType = actorType;
            
            if(actorTypeOverride is not null)
                ActorTypeOverride = actorTypeOverride;
        }

        public IActorRef Subject { get; }
        public Type ActorType { get; }
        public string ActorTypeOverride { get; } = string.Empty;
    }
    
    /// <summary>
    /// Emitted when an actor restarts.
    /// </summary>
    public sealed class ActorRestarted : IActorTelemetryEvent
    {
        public ActorRestarted(IActorRef subject, Type actorType, Exception reason, string? actorTypeOverride = null)
        {
            Subject = subject;
            ActorType = actorType;
            Reason = reason;
            
            if(actorTypeOverride is not null)
                ActorTypeOverride = actorTypeOverride;
        }

        public IActorRef Subject { get; }
        public Type ActorType { get; }
        public string ActorTypeOverride { get; } = string.Empty;
        
        public Exception Reason { get; }
    }
}
