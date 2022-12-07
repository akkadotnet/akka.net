//-----------------------------------------------------------------------
// <copyright file="ActorTelemetry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Event;

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
    }
    
    // Create ActorTelemetryEvent messages for the following events: starting an actor, stopping an actor, restarting an actor
    public sealed class ActorStarted : IActorTelemetryEvent
    {
        internal ActorStarted(IActorRef subject, Type actorType)
        {
            Subject = subject;
            ActorType = actorType;
        }

        public IActorRef Subject { get; }
        public Type ActorType { get; }
    }
    
    /// <summary>
    /// Reason for stopping or restarting.
    /// </summary>
    public sealed class Reason
    {
        public Reason(string type, string message = null)
        {
            Type = type;
            Message = message ?? string.Empty;
        }

        public string Type { get; }

        public string Message { get; }
    }

    public static class ReasonExtensions
    {
        public static Reason ToReason(this Exception ex)
        {
            return new Reason(ex.GetType().Name, ex.Message);
        }
    }
    
    /// <summary>
    /// Event emitted when actor shuts down.
    /// </summary>
    public sealed class ActorStopped : IActorTelemetryEvent
    {
        internal ActorStopped(IActorRef subject, Type actorType)
        {
            Subject = subject;
            ActorType = actorType;
        }

        public IActorRef Subject { get; }
        public Type ActorType { get; }
    }
    
    /// <summary>
    /// Emitted when an actor restarts.
    /// </summary>
    public sealed class ActorRestarted : IActorTelemetryEvent
    {
        internal ActorRestarted(IActorRef subject, Type actorType, Reason reason)
        {
            Subject = subject;
            ActorType = actorType;
            Reason = reason;
        }

        public IActorRef Subject { get; }
        public Type ActorType { get; }
        
        public Reason Reason { get; }
    }
}