using System;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class ActorRefBackpressureSinkSpec : AkkaSpec
    {
        #region internal classes
        
        public sealed class TriggerAckMessage
        {
            public static readonly TriggerAckMessage Instance = new TriggerAckMessage();
            private TriggerAckMessage() { }
        }

        private sealed class Fw : ReceiveActor
        {
            public Fw(IActorRef aref)
            {
                Receive<string>(s => s == InitMessage, s =>
                {
                    Sender.Tell(AckMessage);
                    aref.Forward(InitMessage);
                });
                Receive<string>(s => s == CompleteMessage, s =>
                {
                    aref.Forward(InitMessage);
                });
                Receive<int>(i =>
                {
                    Sender.Tell(AckMessage);
                    aref.Forward(i);
                });
            }
        }

        private sealed class Fw2 : ReceiveActor
        {
            public Fw2(IActorRef aref)
            {
                var actorRef = ActorRefs.NoSender;
                Receive<TriggerAckMessage>(_ => actorRef.Tell(AckMessage));
                ReceiveAny(msg =>
                {
                    actorRef = Sender;
                    aref.Tell(msg);
                });
            }
        }

        #endregion

        public const string InitMessage = "start";
        public const string CompleteMessage = "complete";
        public const string AckMessage = "ack";

        public ActorRefBackpressureSinkSpec(ITestOutputHelper output) : base(output)
        {
        }

        private IActorRef CreateActor<T>() => Sys.ActorOf(Props.Create(typeof (T), TestActor).WithDispatcher("akka.test.stream-dispatcher"));

        [Fact]
        public void ActorBackpressureSink_should_send_the_elements_to_the_ActorRef()
        {
            var fw = CreateActor<Fw>();
            Source.From(new[] {1, 2, 3});
        }

        [Fact]
        public void ActorBackpressureSink_should_send_the_elements_to_the_ActorRef2()
        {

        }

        [Fact]
        public void ActorBackpressureSink_should_cancel_stream_when_actor_terminates()
        {

        }

        [Fact]
        public void ActorBackpressureSink_should_send_message_only_when_backpressure_received()
        {

        }
    }
}