using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Tests.Serialization
{
    [TestClass]
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

        [TestMethod]
        public void CanSerializeActorRef()
        {
            var message = new SomeMessage
            {
                ActorRef = testActor,
            };

            var serializer = sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

            Assert.AreSame(testActor, deserialized.ActorRef);
        }

        //[TestMethod]
        //public void CanSerializeFutureActorRef()
        //{
        //    sys.EventStream.Subscribe(testActor,typeof(object));
        //    var empty = sys.ActorOf<EmptyActor>();
        //    empty.Ask("hello");
        //    var f = (FutureActorRef)queue.Take();


        //    var message = new SomeMessage
        //    {
        //        ActorRef = f,
        //    };

        //    var serializer = sys.Serialization.FindSerializerFor(message);
        //    var serialized = serializer.ToBinary(message);
        //    var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

        //    Assert.AreSame(f, deserialized.ActorRef);
        //}
    }
}
