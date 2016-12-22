﻿//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Util;

namespace Akka.Pattern
{
    /// <summary>
    /// Actor used to supervise actors with ability to restart them after back-off timeout occurred. 
    /// It's designed for cases when i.e. persistent actor stops due to journal unavailability or failure. 
    /// In this case it better to wait before restart.
    /// </summary>
    public class BackoffSupervisor : UntypedActor
    {
        #region Messages

        /// <summary>
        /// Request <see cref="BackoffSupervisor"/> with this message to receive <see cref="CurrentChild"/> response with current child.
        /// </summary>
        [Serializable]
        public sealed class GetCurrentChild
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly GetCurrentChild Instance = new GetCurrentChild();
            private GetCurrentChild() { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class CurrentChild
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Ref;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ref">TBD</param>
            public CurrentChild(IActorRef @ref)
            {
                Ref = @ref;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class StartChild : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly StartChild Instance = new StartChild();
            private StartChild() { }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ResetRestartCount : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Current;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="current">TBD</param>
            /// <returns>TBD</returns>
            public ResetRestartCount(int current)
            {
                Current = current;
            }
        }

        #endregion

        private readonly Props _childProps;
        private readonly string _childName;
        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly double _randomFactor;

        private int _restartCount = 0;
        private IActorRef _child = null;

        /// <summary>
        /// Initializes a new instance of the <see cref="BackoffSupervisor"/> class.
        /// </summary>
        /// <param name="childProps">TBD</param>
        /// <param name="childName">TBD</param>
        /// <param name="minBackoff">TBD</param>
        /// <param name="maxBackoff">TBD</param>
        /// <param name="randomFactor">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="minBackoff"/> is negative or greater than <paramref name="maxBackoff"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// This exception is thrown if the given <paramref name="randomFactor"/> isn't between 0.0 and 1.0.
        /// </exception>
        public BackoffSupervisor(Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            if (minBackoff <= TimeSpan.Zero) throw new ArgumentException("MinBackoff must be greater than 0", nameof(minBackoff));
            if (maxBackoff < minBackoff) throw new ArgumentException("MaxBackoff must be greater than MinBackoff", nameof(maxBackoff));
            if (randomFactor < 0.0 || randomFactor > 1.0) throw new ArgumentOutOfRangeException(nameof(randomFactor), "RandomFactor must be between 0.0 and 1.0");

            _childProps = childProps;
            _childName = childName;
            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            StartChildActor();
            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void OnReceive(object message)
        {
            if (message is Terminated)
            {
                var terminated = (Terminated)message;
                if (_child != null && _child.Equals(terminated.ActorRef))
                {
                    _child = null;
                    var rand = 1.0 + ThreadLocalRandom.Current.NextDouble() * _randomFactor;
                    TimeSpan restartDelay;
                    if (_restartCount >= 30)
                        restartDelay = _maxBackoff; // duration overflow protection (> 100 years)
                    else
                    {
                        var max = Math.Min(_maxBackoff.Ticks, _minBackoff.Ticks * Math.Pow(2, _restartCount)) * rand;
                        if (max >= Double.MaxValue) restartDelay = _maxBackoff;
                        else restartDelay = new TimeSpan((long)max);
                    }

                    Context.System.Scheduler.ScheduleTellOnce(restartDelay, Self, StartChild.Instance, Self);
                    _restartCount++;
                }
                else Unhandled(message);
            }
            else if (message is StartChild)
            {
                StartChildActor();
                Context.System.Scheduler.ScheduleTellOnce(_minBackoff, Self, new ResetRestartCount(_restartCount), Self);
            }
            else if (message is ResetRestartCount)
            {
                var restartCount = (ResetRestartCount)message;
                if (restartCount.Current == _restartCount) _restartCount = 0;
            }
            else if (message is GetCurrentChild)
            {
                Sender.Tell(new CurrentChild(_child));
            }
            else
            {
                if (_child != null) _child.Forward(message);
                else Context.System.DeadLetters.Forward(message);
            }
        }

        private void StartChildActor()
        {
            if (_child == null) _child = Context.Watch(Context.ActorOf(_childProps, _childName));
        }
    }
}