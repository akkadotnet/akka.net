//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
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
            public static readonly GetCurrentChild Instance = new GetCurrentChild();
            private GetCurrentChild() { }
        }

        [Serializable]
        public sealed class CurrentChild
        {
            public readonly IActorRef Ref;

            public CurrentChild(IActorRef @ref)
            {
                Ref = @ref;
            }
        }

        [Serializable]
        public sealed class StartChild
        {
            public static readonly StartChild Instance = new StartChild();
            private StartChild() { }
        }

        [Serializable]
        public sealed class ResetRestartCount
        {
            public readonly int Current;

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

        public BackoffSupervisor(Props childProps, string childName, TimeSpan minBackoff, TimeSpan maxBackoff, double randomFactor)
        {
            if (minBackoff <= TimeSpan.Zero) throw new ArgumentException("MinBackoff must be greater than 0");
            if (maxBackoff < minBackoff) throw new ArgumentException("MaxBackoff must be greater than MinBackoff");
            if (randomFactor < 0.0 || randomFactor > 1.0) throw new ArgumentException("RandomFactor must be between 0.0 and 1.0");

            _childProps = childProps;
            _childName = childName;
            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;
        }

        protected override void PreStart()
        {
            StartChildActor();
            base.PreStart();
        }

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