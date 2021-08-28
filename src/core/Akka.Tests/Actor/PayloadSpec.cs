using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class PayloadSpec : AkkaSpec
    {
        sealed class Echo
        {
            public readonly Payload Payload;

            public Echo(Payload payload)
            {
                Payload = payload;
            }

            public Echo(object content)
            {
                Payload = new Payload(content);
            }
        }

        sealed class EchoActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch(message)
                {
                    case Echo msg:
                        Sender.Tell(msg.Payload);
                        break;
                }
            }
        }

        sealed class SomeMessgage
        {
            public object Value { get; }

            public SomeMessgage(object value)
            {
                Value = value;
            }
        }

        [Fact]
        public void payload_should_equal_when_serialized()
        {
            var p1 = new Payload("Hello world", Sys);

            var p2 = new Payload("Hello" + " " + "world", Sys);

            p1.Serialize(Sys);

            p2.Serialize(Sys);

            p1.ShouldBe(p2);
        }

        [Fact]
        public void payload_should_equal_when_deserialized()
        {
            var p1 = new Payload("Hello world", Sys);

            var p2 = new Payload("Hello" + " " + "world", Sys);

            p1.ShouldBe(p2);
        }

        [Fact]
        public void payload_should_not_equal_when_serialized_and_deserialized()
        {
            var p1 = new Payload("Hello world", Sys);

            var p2 = new Payload("Hello" + " " + "world", Sys);

            p1.Serialize(Sys);

            p1.ShouldNotBe(p2);
        }

        [Fact]
        public void payload_should_received_deserialized()
        {
            var actor = Sys.ActorOf<EchoActor>();

            actor.Tell(new Echo(new SomeMessgage("Something")));

            ExpectMsg<SomeMessgage>(m => ((string)m.Value) == "Something");
        }
    }

    
}
