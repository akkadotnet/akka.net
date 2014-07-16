using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Tests.Serialization
{
    
    public class SerializationSpec : AkkaSpec
    {
        public class EmptyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Context.System.EventStream.Publish(Sender);
            }
        }
        public class SomeMessage
        {
            public ActorRef ActorRef { get; set; }
        }

        [Fact]
        public void CanSerializeActorRef()
        {
            var message = new SomeMessage
            {
                ActorRef = testActor,
            };

            var serializer = sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

            Assert.Same(testActor, deserialized.ActorRef);
        }

        [Fact]
        public void CanSerializeSingletonMessages()
        {
            var message = Terminate.Instance;

            var serializer = sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (Terminate)serializer.FromBinary(serialized, typeof(Terminate));

            Assert.NotNull(deserialized);
        }

        //TODO: find out why this fails on build server
#if TMPFIX
        [Fact]
        public void CanSerializeFutureActorRef()
        {
            sys.EventStream.Subscribe(testActor, typeof(object));
            var empty = sys.ActorOf<EmptyActor>();
            empty.Ask("hello");
            var f = (FutureActorRef)queue.Take();


            var message = new SomeMessage
            {
                ActorRef = f,
            };

            var serializer = sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

            Assert.Same(f, deserialized.ActorRef);
        }
#endif
    }
}
