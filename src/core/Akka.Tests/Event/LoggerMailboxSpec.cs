//-----------------------------------------------------------------------
// <copyright file="LoggerMailboxSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Event
{
    public class LoggerMailboxSpec : AkkaSpec
    {
        [Fact]
        public void CleanUp_drains_queue()
        {
            var loggerMailbox = new LoggerMailbox(ActorRefs.Nobody, Sys);
            loggerMailbox.SetActor((ActorCell)TestActor.AsInstanceOf<ActorRefWithCell>().Underlying); // mailboxes won't cleanup without an actorcell set
            loggerMailbox.Enqueue(TestActor, new Envelope("foo", TestActor));

            loggerMailbox.NumberOfMessages.ShouldBe(1);

            loggerMailbox.CleanUp();

            loggerMailbox.NumberOfMessages.ShouldBe(0);

        }
    }
}
