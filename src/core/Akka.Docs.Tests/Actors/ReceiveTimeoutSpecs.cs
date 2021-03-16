//-----------------------------------------------------------------------
// <copyright file="ReceiveTimeoutSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
            private readonly TimeSpan _inactivityTimeout;

            public ReceiveTimeoutActor(TimeSpan inactivityTimeout, IActorRef receiver)
            {
                _inactivityTimeout = inactivityTimeout;
                
                // if we don't 
                Receive<ReceiveTimeout>(_ =>
                {
                    receiver.Tell("timeout");
                });
            }

            protected override void PreStart()
            {
                Context.SetReceiveTimeout(_inactivityTimeout);
            }
        }
        // </ReceiveTimeoutActor>

        [Fact]
        public async Task ShouldReceiveTimeoutActors()
        {
            var receiveTimeout = Sys.ActorOf(
                Props.Create(() => new ReceiveTimeoutActor(TimeSpan.FromMilliseconds(100), TestActor)), 
                "receive-timeout");
            
            // should not receive timeout initially
            ExpectNoMsg(TimeSpan.FromMilliseconds(50));
            
            // then should receive timeout due to inactivity
            ExpectMsg("timeout", TimeSpan.FromSeconds(30));
        }
    }
}
