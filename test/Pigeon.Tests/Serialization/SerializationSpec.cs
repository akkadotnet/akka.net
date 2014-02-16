using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Actor;

namespace Pigeon.Tests.Serialization
{
    [TestClass]
    public class SerializationSpec : AkkaSpec
    {
        public class SomeMessage
        {
            public ActorRef ActorRef { get; set; }
        }

        [TestMethod]
        public void CanSerializeActorRef()
        {
            //TODO: this test fails when running all of the tests
            //looks like we either need another serializer than fastJSON, or be able to make fastJSON context aware.
            //(it blows up when trying to deserialze actorrefs since there have been multiple actor systems registering actorref as a custom type)
            //fastJSON is problematic also because it wants each subtype to be registered. we need to register ActorRef only, and not all subtypes
            var message = new SomeMessage
            {
                ActorRef = testActor,
            };

            var serializer = sys.Serialization.FindSerializerFor(message);
            var serialized = serializer.ToBinary(message);
            var deserialized = (SomeMessage)serializer.FromBinary(serialized, typeof(SomeMessage));

            Assert.AreSame(testActor, deserialized.ActorRef);
        }
    }
}
