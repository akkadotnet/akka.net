using System;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class DeadLetterListener.
    /// </summary>
    public class DeadLetterListener : UntypedActor
    {
        /// <summary>
        ///     The event stream
        /// </summary>
        private readonly EventStream eventStream = Context.System.EventStream;

        /// <summary>
        ///     The maximum count
        /// </summary>
        private readonly int maxCount = Context.System.Settings.LogDeadLetters;

        /// <summary>
        ///     The count
        /// </summary>
        private int count;

        protected override void PostRestart(Exception reason)
        {
        }

        protected override void PreStart()
        {
            eventStream.Subscribe(Self, typeof (DeadLetter));
        }

        protected override void PostStop()
        {
            eventStream.Unsubscribe(Self);
        }

        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override void OnReceive(object message)
        {
            var deadLetter = (DeadLetter) message;
            ActorRef snd = deadLetter.Sender;
            ActorRef rcp = deadLetter.Recipient;
            count++;
            bool done = maxCount != int.MaxValue && count >= maxCount;
            string doneMsg = done ? ", no more dead letters will be logged" : "";
            if (!done)
            {
                string rcpPath = "";
                if (rcp == ActorRef.NoSender)
                    rcpPath = "NoSender";

                string sndPath = "";
                if (snd == ActorRef.NoSender)
                    sndPath = "NoSender";

                eventStream.Publish(new Info(rcpPath, rcp.GetType(),
                    string.Format("Message {0} from {1} to {2} was not delivered. {3} dead letters encountered.{4}",
                        deadLetter.Message.GetType().Name, sndPath, rcpPath, count, doneMsg)));
            }
            if (done)
            {
                Self.Stop();
            }
        }
    }
}