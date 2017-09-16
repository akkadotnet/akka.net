//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class TestActor
    {
        /// <summary>
        /// A delegate that returns <c>true</c> if the <paramref name="message"/> should be ignored.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <returns>TBD</returns>
        public delegate bool Ignore(object message);

        /// <summary>
        /// TBD
        /// </summary>
        public static AutoPilot NoAutoPilot { get { return TestKit.NoAutoPilot.Instance; } }
        /// <summary>
        /// TBD
        /// </summary>
        public static AutoPilot KeepRunning { get { return TestKit.KeepRunning.Instance; } }
        /// <summary>
        /// TBD
        /// </summary>
        public static NullMessageEnvelope NullMessage { get { return NullMessageEnvelope.Instance; } }


        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/> to make it ignore 
        /// received messages. If the delegate specified on the constructor returns
        /// <c>true</c> the message will be ignored by <see cref="TestActor"/>
        /// </summary>
        public class SetIgnore : INoSerializationVerificationNeeded
        {
            private readonly Ignore _ignore;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ignore">TBD</param>
            public SetIgnore(Ignore ignore) { _ignore = ignore; }

            /// <summary>
            /// TBD
            /// </summary>
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

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actorToWatch">TBD</param>
            public Watch(IActorRef actorToWatch) { _actorToWatch = actorToWatch; }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Actor { get { return _actorToWatch; } }
        }

        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/> to make it unwatch 
        /// a previously watched actor.
        /// </summary>
        public class Unwatch : INoSerializationVerificationNeeded
        {
            private readonly IActorRef _actorToUnwatch;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actorToUnwatch">TBD</param>
            public Unwatch(IActorRef actorToUnwatch) { _actorToUnwatch = actorToUnwatch; }

            /// <summary>
            /// TBD
            /// </summary>
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

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="autoPilot">TBD</param>
            public SetAutoPilot(AutoPilot autoPilot) { _autoPilot = autoPilot; }

            /// <summary>
            /// TBD
            /// </summary>
            public AutoPilot AutoPilot { get { return _autoPilot; } }
        }
    }
}