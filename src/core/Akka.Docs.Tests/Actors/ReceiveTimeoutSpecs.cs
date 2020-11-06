using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.Xunit2;
using FluentAssertions;
using Xunit;

namespace DocsExamples.Actors
{
    
    public class ReceiveTimeoutSpecs : TestKit
    {
        // <ReceiveTimeoutActor>
        /// <summary>
        /// Used to query if a <see cref="ReceiveTimeout"/> has been observed.
        ///
        /// Can't influence the <see cref="ReceiveTimeout"/> since it implements
        /// <see cref="INotInfluenceReceiveTimeout"/>.
        /// </summary>
        public class CheckTimeout : INotInfluenceReceiveTimeout { }
        public class ReceiveTimeoutActor : ReceiveActor
        {
            private bool _timedOut = false;

            public ReceiveTimeoutActor()
            {
                // if we don't 
                Receive<ReceiveTimeout>(_ =>
                {
                    _timedOut = true;
                });

                Receive<CheckTimeout>(_ =>
                {
                    Sender.Tell(_timedOut);
                });
            }

            protected override void PreStart()
            {
                Context.SetReceiveTimeout(TimeSpan.FromMilliseconds(100));
            }
        }
        // <ReceiveTimeoutActor>

        [Fact]
        public async Task ShouldReceiveTimeoutActors()
        {
            var receiveTimeout = Sys.ActorOf(Props.Create(() => new ReceiveTimeoutActor()), "receive-timeout");
            var timedout1 = await receiveTimeout.Ask<bool>(new CheckTimeout(), TimeSpan.FromMilliseconds(500));
            timedout1.Should().BeFalse();

            await Task.Delay(200); // wait 200 ms

            var timedout2 = await receiveTimeout.Ask<bool>(new CheckTimeout(), TimeSpan.FromMilliseconds(500));
            timedout2.Should().BeTrue();
        }
    }
}
