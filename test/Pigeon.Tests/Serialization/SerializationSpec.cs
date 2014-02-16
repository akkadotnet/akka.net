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
