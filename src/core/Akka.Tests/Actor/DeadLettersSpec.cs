//-----------------------------------------------------------------------
// <copyright file="DeadLettersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests
{
    
    public class DeadLettersSpec : AkkaSpec
    {
        [Fact]
        public async Task Can_send_messages_to_dead_letters()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            Sys.DeadLetters.Tell("foobar");
            await ExpectMsgAsync<DeadLetter>(deadLetter=>deadLetter.Message.Equals("foobar"));
        }
    }
}

