//-----------------------------------------------------------------------
// <copyright file="DeadLetterListener.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        protected override void PostRestart(Exception reason)
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
                    string.Format("Message {0} from {1} to {2} was not delivered. {3} dead letters encountered.{4}",
                        deadLetter.Message.GetType().Name, sndPath, rcpPath, _count, doneMsg)));
            }

            if (done)
            {
                ((IInternalActorRef) Self).Stop();
            }

            return true;
        }
    }
}

