//-----------------------------------------------------------------------
// <copyright file="Bug2751Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Xunit;
using System.Threading;

namespace Akka.Tests.Actor.Dispatch
{
    /// <summary>
    /// Verifies that https://github.com/akkadotnet/akka.net/issues/2751 has been resolved
    /// </summary>
    public class Bug2751Spec : AkkaSpec
    {

        private static CancellationToken Token => TestContext.Current.CancellationToken;
        private class StopActor : ReceiveActor
        {
            private readonly IActorRef _testActor;

            public StopActor(IActorRef testActor)
            {
                _testActor = testActor;
                Receive<string>(s =>
                {
                    if (s == "stop")
                    {
                        Self.Tell("Hello");
                        Context.Stop(Self);
                    }
                    else
                    {
                        _testActor.Tell(s);
                    }
                });
            }
        }

        [Fact]
        public async Task ShouldReceiveSysMsgBeforeUserMsg()
        {
            var stopper = Sys.ActorOf(Props.Create(() => new StopActor(TestActor)));
            stopper.Tell("stop");
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(250), cancellationToken: Token);
            Watch(stopper);
            await ExpectTerminatedAsync(stopper, cancellationToken: Token);
            await ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100), cancellationToken: Token);
        }
    }

}
