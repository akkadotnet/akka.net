//-----------------------------------------------------------------------
// <copyright file="DeadLetterListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an actor responsible for listening to <see cref="DeadLetter"/> messages and logging them using the <see cref="EventStream"/>.
    /// </summary>
    public class DeadLetterListener : ActorBase
    {
        private readonly EventStream _eventStream = Context.System.EventStream;
        private readonly int _maxCount = Context.System.Settings.LogDeadLetters;
        private int _count;

        /// <summary>
        /// Don't re-subscribe, skip call to preStart
        /// </summary>
        protected override void PostRestart(Exception reason)
        {
        }

        /// <summary>
        /// Don't remove subscription, skip call to postStop, no children to stop
        /// </summary>
        protected override void PreRestart(Exception reason, object message)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            _eventStream.Subscribe(Self, typeof (DeadLetter));
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            _eventStream.Unsubscribe(Self);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            var deadLetter = (DeadLetter)message;
            var snd = deadLetter.Sender;
            var rcp = deadLetter.Recipient;

            _count++;

            var done = _maxCount != int.MaxValue && _count >= _maxCount;
            var doneMsg = done ? ", no more dead letters will be logged" : "";

            if (!done)
            {
                var rcpPath = rcp == ActorRefs.NoSender ? "NoSender" : rcp.Path.ToString();
                var sndPath = snd == ActorRefs.NoSender ? "NoSender" : snd.Path.ToString();

                _eventStream.Publish(new Info(rcpPath, rcp.GetType(),
                    $"Message [{deadLetter.Message.GetType().Name}] from {sndPath} to {rcpPath} was not delivered. [{_count}] dead letters encountered {doneMsg}." +
                    "This logging can be turned off or adjusted with configuration settings 'akka.log-dead-letters' " +
                    "and 'akka.log-dead-letters-during-shutdown'."));
            }

            if (done) Context.Stop(Self);
            return true;
        }
    }
}

