using Akka.TestKit;
using Xunit;
using Akka.Tests.TestUtils;
using Akka.Actor;
using Akka.Configuration;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests
{
    
    public class ActorCellSpec : AkkaSpec
    {
        public class SomeUserMessage : Comparable
        {
            public string A { get; set; }
            public int B { get; set; }
            public Guid C { get; set; }
        }
       [Fact]
       public void DoesNotSerializesUserMessagesWhenSerializeAllMessagesIsOff()
       {
            var message = new SomeUserMessage
            {
                A = "abc",
                B = 123,
                C = Guid.Empty
            };
            testActor.Tell(message);

            var result = queue.Take();

            Assert.False(sys.Settings.SerializeAllMessages);
            Assert.Equal(message, result);
            Assert.Same(message, result);
        }

       [Fact]
       public void SerializesUserMessagesWhenSerializeAllMessagesIsOn()
       {
           var config = ConfigurationFactory.ParseString(@"akka.actor.serialize-messages = on");
           var sys = ActorSystem.Create("test",config);
           testActor = sys.ActorOf(Props.Create(() => new TestActor(queue,messages)));      

           var message = new SomeUserMessage
           {
               A = "abc",
               B = 123,
               C = Guid.Empty
           };
           testActor.Tell(message);

           var result = queue.Take();

           Assert.True(sys.Settings.SerializeAllMessages);
           Assert.Equal(message, result);
           Assert.NotSame(message, result);
       }
    }
}
