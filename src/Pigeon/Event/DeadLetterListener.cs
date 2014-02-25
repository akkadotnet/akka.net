using Akka.Actor;
using Akka.Event;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{
    /*
package akka.event

import akka.actor.Actor
import akka.actor.DeadLetter
import akka.event.Logging.Info

class DeadLetterListener extends Actor {

  val eventStream = context.system.eventStream
  val maxCount = context.system.settings.LogDeadLetters
  var count = 0

  override def preStart(): Unit =
    eventStream.subscribe(self, classOf[DeadLetter])

  // don't re-subscribe, skip call to preStart
  override def postRestart(reason: Throwable): Unit = ()

  // don't remove subscription, skip call to postStop, no children to stop
  override def preRestart(reason: Throwable, message: Option[Any]): Unit = ()

  override def postStop(): Unit =
    eventStream.unsubscribe(self)

  def receive = {
    case DeadLetter(message, snd, rcp) ⇒
      count += 1
      val done = maxCount != Int.MaxValue && count >= maxCount
      val doneMsg = if (done) ", no more dead letters will be logged" else ""
      eventStream.publish(Info(rcp.path.toString, rcp.getClass,
        s"Message [${message.getClass.getName}] from $snd to $rcp was not delivered. [$count] dead letters encountered$doneMsg. " +
          "This logging can be turned off or adjusted with configuration settings 'akka.log-dead-letters' " +
          "and 'akka.log-dead-letters-during-shutdown'."))
      if (done) context.stop(self)
  }

}

*/
    public class DeadLetterListener : UntypedActor
    {
        private EventStream eventStream = Context.System.EventStream;
        private int maxCount = Context.System.Settings.LogDeadLetters;
        private int count = 0;

        protected override void PostRestart(Exception cause)
        {

        }
        protected override void PreStart()
        {
            eventStream.Subscribe(Self, typeof(DeadLetter));
        }
        protected override void PostStop()
        {
            eventStream.Unsubscribe(Self);
        }

        protected override void OnReceive(object message)
        {
            var deadLetter = (DeadLetter)message;
            var snd = deadLetter.Sender;
            var rcp = deadLetter.Recipient;
            count++;
            var done = maxCount != int.MaxValue && count >= maxCount;
            var doneMsg = done ? ", no more dead letters will be logged" : "";
            if (!done)
            {
                var rcpPath = "";
                if (rcp == ActorRef.NoSender)
                    rcpPath = "NoSender";

                var sndPath = "";
                if (snd == ActorRef.NoSender)
                    sndPath = "NoSender";

                eventStream.Publish(new Info(rcpPath, rcp.GetType(), string.Format("Message {0} from {1} to {2} was not delivered. {3} dead letters encountered.{4}", deadLetter.Message.GetType().Name, sndPath, rcpPath, count, doneMsg)));
            }
            if (done)
            {
                Self.Stop();
            }
        }
    }
}
