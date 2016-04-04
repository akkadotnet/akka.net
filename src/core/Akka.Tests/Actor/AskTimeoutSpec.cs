//-----------------------------------------------------------------------
// <copyright file="AskTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Akka.Actor;
using Akka.TestKit;

using Xunit;

namespace Akka.Tests.Actor
{
    public class AskTimeoutSpec : AkkaSpec
    {

        public class SleepyActor : UntypedActor
        {

            protected override void OnReceive(object message)
            {
                Thread.Sleep(5000);
                Sender.Tell(message);
            }

        }

        public AskTimeoutSpec()
            : base(@"akka.actor.ask-timeout = 100ms")
        {}

        [Fact]
        public async Task Ask_should_honor_config_specified_timeout()
        {
            var actor = Sys.ActorOf<SleepyActor>();
            try
            {
                await actor.Ask<string>("should time out");
                Assert.True(false, "the ask should have timed out");
            }
            catch (Exception e)
            {
                Assert.True(e is TaskCanceledException);
            }
        }

    }
}
