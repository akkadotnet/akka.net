using Xunit;
using Akka.Event;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Tests
{
    
    public class DeadLettersSpec : AkkaSpec
    {
        [Fact]
        public void CanSendMessagesToDeadLetters()
        {
            sys.EventStream.Subscribe(testActor, typeof(DeadLetter));
            sys.DeadLetters.Tell("foobar");
            var message = queue.Take();
            Assert.IsType<DeadLetter>(message);
            var deadLetter = (DeadLetter)message;
            var payload = (string)deadLetter.Message;
            Assert.Equal("foobar", payload);
        }
    }
}
