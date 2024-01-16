//-----------------------------------------------------------------------
// <copyright file="TestActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;

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
            public Watch(IActorRef actorToWatch, TaskCompletionSource<bool> watchCompleted)
            {
                Actor = actorToWatch;
                WatchCompleted = watchCompleted;
            }
            
            public IActorRef Actor { get; }

            public TaskCompletionSource<bool> WatchCompleted { get; }
        }

        /// <summary>
        /// Message that is supposed to be sent to a <see cref="TestActor"/> to make it unwatch 
        /// a previously watched actor.
        /// </summary>
        public class Unwatch : INoSerializationVerificationNeeded
        {
            public Unwatch(IActorRef actorToUnwatch, TaskCompletionSource<bool> unwatchCompleted)
            {
                Actor = actorToUnwatch;
                UnwatchCompleted = unwatchCompleted;
            }
            
            public IActorRef Actor { get; }
            
            public TaskCompletionSource<bool> UnwatchCompleted { get; }
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

        /// <summary>
        /// Message which is intended to allow TestKit to spawn a child actor
        /// </summary>
        public class Spawn : INoSerializationVerificationNeeded
        {
            public readonly Props _props;

            public readonly Option<string> _name;

            public readonly Option<SupervisorStrategy> _supervisorStrategy;

            public Spawn(Props props, Option<string> name, Option<SupervisorStrategy> supervisorStrategy)
            {
                _props = props;
                _name = name;
                _supervisorStrategy = supervisorStrategy;
            }

            /// <summary>
            /// Using the given context, create an actor of the given _props, and optionally naming it with _name
            /// </summary>
            public IActorRef Apply(IActorRefFactory context)
            {
                if (_name.HasValue)
                {
                    return context.ActorOf(_props, _name.Value);
                }
                return context.ActorOf(_props);
            }
        }
    }
}
