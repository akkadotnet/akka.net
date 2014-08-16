using Akka.Actor;
using Akka.TestKit;
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
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            Sys.DeadLetters.Tell("foobar");
            ExpectMsg<DeadLetter>(deadLetter=>deadLetter.Message.Equals("foobar"));
        }
    }
}
