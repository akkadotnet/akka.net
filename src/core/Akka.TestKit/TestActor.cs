// -----------------------------------------------------------------------
//  <copyright file="TestActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util;

namespace Akka.TestKit;

/// <summary>
///     TBD
/// </summary>
public static class TestActor
{
    /// <summary>
    ///     A delegate that returns <c>true</c> if the <paramref name="message" /> should be ignored.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>TBD</returns>
    public delegate bool Ignore(object message);

    /// <summary>
    ///     TBD
    /// </summary>
    public static AutoPilot NoAutoPilot => TestKit.NoAutoPilot.Instance;

    /// <summary>
    ///     TBD
    /// </summary>
    public static AutoPilot KeepRunning => TestKit.KeepRunning.Instance;

    /// <summary>
    ///     TBD
    /// </summary>
    public static NullMessageEnvelope NullMessage => NullMessageEnvelope.Instance;


    /// <summary>
    ///     Message that is supposed to be sent to a <see cref="TestActor" /> to make it ignore
    ///     received messages. If the delegate specified on the constructor returns
    ///     <c>true</c> the message will be ignored by <see cref="TestActor" />
    /// </summary>
    public class SetIgnore : INoSerializationVerificationNeeded
    {
        /// <summary>
        ///     TBD
        /// </summary>
        /// <param name="ignore">TBD</param>
        public SetIgnore(Ignore ignore)
        {
            Ignore = ignore;
        }

        /// <summary>
        ///     TBD
        /// </summary>
        public Ignore Ignore { get; }
    }

    /// <summary>
    ///     Message that is supposed to be sent to a <see cref="TestActor" /> to make it watch
    ///     the specified actor and receive death notifications,
    ///     i.e. <see cref="Terminated" /> messages.
    /// </summary>
    public class Watch : INoSerializationVerificationNeeded
    {
        public Watch(IActorRef actorToWatch)
        {
            Actor = actorToWatch;
        }

        public IActorRef Actor { get; }
    }

    internal sealed class WatchAck : INoSerializationVerificationNeeded
    {
        public static readonly WatchAck Instance = new();

        private WatchAck()
        {
        }
    }

    /// <summary>
    ///     Message that is supposed to be sent to a <see cref="TestActor" /> to make it unwatch
    ///     a previously watched actor.
    /// </summary>
    public class Unwatch : INoSerializationVerificationNeeded
    {
        public Unwatch(IActorRef actorToUnwatch)
        {
            Actor = actorToUnwatch;
        }

        public IActorRef Actor { get; }
    }

    /// <summary>
    ///     INTERNAL API
    /// </summary>
    internal sealed class UnwatchAck : INoSerializationVerificationNeeded
    {
        public static readonly UnwatchAck Instance = new();

        private UnwatchAck()
        {
        }
    }

    /// <summary>
    ///     Message that is supposed to be sent to a <see cref="TestActor" />
    ///     to install an AutoPilot to drive the <see cref="TestActor" />: the AutoPilot
    ///     will be run for each received message and can be used to send or forward
    ///     messages, etc. Each invocation must return the AutoPilot for the next round.
    /// </summary>
    public class SetAutoPilot : INoSerializationVerificationNeeded
    {
        /// <summary>
        ///     TBD
        /// </summary>
        /// <param name="autoPilot">TBD</param>
        public SetAutoPilot(AutoPilot autoPilot)
        {
            AutoPilot = autoPilot;
        }

        /// <summary>
        ///     TBD
        /// </summary>
        public AutoPilot AutoPilot { get; }
    }

    /// <summary>
    ///     Message which is intended to allow TestKit to spawn a child actor
    /// </summary>
    public class Spawn : INoSerializationVerificationNeeded
    {
        public readonly Option<string> _name;
        public readonly Props _props;

        public readonly Option<SupervisorStrategy> _supervisorStrategy;

        public Spawn(Props props, Option<string> name, Option<SupervisorStrategy> supervisorStrategy)
        {
            _props = props;
            _name = name;
            _supervisorStrategy = supervisorStrategy;
        }

        /// <summary>
        ///     Using the given context, create an actor of the given _props, and optionally naming it with _name
        /// </summary>
        public IActorRef Apply(IActorRefFactory context)
        {
            if (_name.HasValue) return context.ActorOf(_props, _name.Value);
            return context.ActorOf(_props);
        }
    }
}