using System;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class DeadLetterListener.
    /// </summary>
    public class DeadLetterListener : ActorBase
    {
        /// <summary>
        ///     The event stream
        /// </summary>
        private readonly EventStream _eventStream = Context.System.EventStream;

        /// <summary>
        ///     The maximum count
        /// </summary>
        private readonly int _maxCount = Context.System.Settings.LogDeadLetters;

        /// <summary>
        ///     The count
        /// </summary>
        private int _count;

        protected override void PostRestart(Exception reason)
        {
        }

        protected override void PreStart()
        {
            _eventStream.Subscribe(Self, typeof (DeadLetter));
        }

        protected override void PostStop()
        {
            _eventStream.Unsubscribe(Self);
        }

        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        protected override bool Receive(object message)
        {
            var deadLetter = (DeadLetter)message;
            ActorRef snd = deadLetter.Sender;
            ActorRef rcp = deadLetter.Recipient;
            _count++;
            bool done = _maxCount != int.MaxValue && _count >= _maxCount;
            string doneMsg = done ? ", no more dead letters will be logged" : "";
            if (!done)
            {
                string rcpPath = rcp == ActorRef.NoSender ? "NoSender" : rcp.Path.ToString();
                string sndPath = snd == ActorRef.NoSender ? "NoSender" : snd.Path.ToString();

                _eventStream.Publish(new Info(rcpPath, rcp.GetType(),
                    string.Format("Message {0} from {1} to {2} was not delivered. {3} dead letters encountered.{4}",
                        deadLetter.Message.GetType().Name, sndPath, rcpPath, _count, doneMsg)));
            }
            if (done)
            {
                Self.AsInstanceOf<InternalActorRef>().Stop();
            }
            return true;
        }
    }
}