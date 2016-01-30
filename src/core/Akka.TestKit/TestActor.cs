//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    public static class TestActor
    {
        /// <summary>
        /// A delegate that returns <c>true</c> if the <paramref name="message"/> should be ignored.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns></returns>
        public delegate bool Ignore(object message);

        public static AutoPilot NoAutoPilot { get { return TestKit.NoAutoPilot.Instance; } }
        public static AutoPilot KeepRunning { get { return TestKit.KeepRunning.Instance; } }
        public static NullMessageEnvelope NullMessage { get { return NullMessageEnvelope.Instance; } }


        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/> to make it ignore 
        /// received messages. If the delegate specified on the constructor returns
        /// <c>true</c> the message will be ignored by <see cref="TestActor"/>
        /// </summary>
        public class SetIgnore : INoSerializationVerificationNeeded
        {
            private readonly Ignore _ignore;

            public SetIgnore(Ignore ignore) { _ignore = ignore; }

            public Ignore Ignore { get { return _ignore; } }
        }

        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/> to make it watch 
        /// the specified actor and receive death notifications, 
        /// i.e. <see cref="Terminated"/> messages.
        /// </summary>
        public class Watch : INoSerializationVerificationNeeded
        {
            private readonly IActorRef _actorToWatch;

            public Watch(IActorRef actorToWatch) { _actorToWatch = actorToWatch; }

            public IActorRef Actor { get { return _actorToWatch; } }
        }

        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/> to make it unwatch 
        /// a previously watched actor.
        /// </summary>
        public class Unwatch : INoSerializationVerificationNeeded
        {
            private readonly IActorRef _actorToUnwatch;

            public Unwatch(IActorRef actorToUnwatch) { _actorToUnwatch = actorToUnwatch; }

            public IActorRef Actor { get { return _actorToUnwatch; } }
        }

        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/>
        /// to install an AutoPilot to drive the <see cref="TestActor"/>: the AutoPilot 
        /// will be run for each received message and can be used to send or forward 
        /// messages, etc. Each invocation must return the AutoPilot for the next round.
        /// </summary>
        public class SetAutoPilot : INoSerializationVerificationNeeded
        {
            private readonly AutoPilot _autoPilot;

            public SetAutoPilot(AutoPilot autoPilot) { _autoPilot = autoPilot; }

            public AutoPilot AutoPilot { get { return _autoPilot; } }
        }
    }
}

