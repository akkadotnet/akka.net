using System;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class GuaranteedDeliveryCrashSpec : AkkaSpec
    {

        #region internal classes
        internal class StoppingStrategySupervisor : ActorBase
        {
            private readonly ActorRef _testProbe;
            private readonly ActorRef _crashingActor;

            public StoppingStrategySupervisor(ActorRef testProbe)
            {
                _testProbe = testProbe;
                _crashingActor = Context.ActorOf(Props.Create(() => new CrashingActor(testProbe)), "CrashingActor");
            }

            protected override bool Receive(object message)
            {
                _crashingActor.Forward(message);
                return true;
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(10, TimeSpan.FromSeconds(10), reason =>
                {
                    if(reason is IllegalActorStateException) return Directive.Stop;
                    return Actor.SupervisorStrategy.DefaultDecider(reason);
                });
            }
        }

        internal class Message
        {
            public static readonly Message Instance = new Message();
            private Message() {}
        }
        internal class CrashMessage
        {
            public static readonly CrashMessage Instance = new CrashMessage();
            private CrashMessage() { }
        }

        internal class SendingMessage
        {
            public SendingMessage(long deliveryId, bool isRecovering)
            {
                IsRecovering = isRecovering;
                DeliveryId = deliveryId;
            }

            public long DeliveryId { get; private set; }
            public bool IsRecovering { get; private set; }
        }

        internal class CrashingActor : PersistentActorBase
        {
            private readonly ActorRef _testProbe;
            private LoggingAdapter _adapter;

            LoggingAdapter Log { get { return _adapter ?? (_adapter = Context.GetLogger()); } }

            public CrashingActor(ActorRef testProbe)
            {
                _testProbe = testProbe;
            }

            protected override bool Receive(object message)
            {
                throw new NotImplementedException();
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is Message) Send();
                else if (message is CrashMessage)
                {
                    Log.Debug("Crash it!");
                    throw new IllegalStateException("Intentionally crashed");
                }
                else
                {
                    Log.Debug("Recover message: " + message);
                }

                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                if(message is Message) Persist(message as Message, _ => Send());
                else if (message is CrashMessage) Persist(message as Message, _ => { });
                else return false;

                return true;
            }

            private void Send()
            {
                Deliver(_testProbe.Path);
            }
        }
        #endregion

        public GuaranteedDeliveryCrashSpec()
        {
            
        }

        [Fact]
        public void GuaranteedDelivery_should_not_send_when_actor_crashes()
        {
            var testProbe = CreateTestProbe();
            var supervisor = Sys.ActorOf(Props.Create(() => new StoppingStrategySupervisor(testProbe.Ref)), "supervisor");
            supervisor.Tell(Message.Instance);
            testProbe.ExpectMsg<SendingMessage>();

            supervisor.Tell(CrashMessage.Instance);
            var deathProbe = CreateTestProbe();
            deathProbe.Watch(supervisor);
            Sys.Stop(supervisor);
            deathProbe.ExpectTerminated(supervisor);

            testProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(250));
            Sys.ActorOf(Props.Create(() => new StoppingStrategySupervisor(testProbe.Ref)), "supervisor");
            testProbe.ExpectNoMsg(TimeSpan.FromSeconds(1));
        }
    }
}