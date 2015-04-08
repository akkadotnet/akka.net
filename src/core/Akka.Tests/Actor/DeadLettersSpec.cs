//-----------------------------------------------------------------------
// <copyright file="DeadLettersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

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
