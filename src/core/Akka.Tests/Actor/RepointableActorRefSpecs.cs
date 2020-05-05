//-----------------------------------------------------------------------
// <copyright file="RepointableActorRefSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class RepointableActorRefSpecs : AkkaSpec
    {
        public class Bug2182Actor : ReceiveActor, IWithUnboundedStash
        {
            public Bug2182Actor()
            {
                Receive<string>(str => str.Equals("init"), s => Become(Initialize));
                ReceiveAny(o => Stash.Stash());
                Self.Tell("init");
            }

            private void Initialize()
            {
                Self.Tell("init2");
                Receive<string>(str => str.Equals("init2"), s =>
                {
                    Become(Set);
                    Stash.UnstashAll();
                });
                ReceiveAny(o => Stash.Stash());
            }

            private void Set()
            {
                ReceiveAny(o => Sender.Tell(o));
            }

            public IStash Stash { get; set; }
        }

        /// <summary>
        /// Fixes https://github.com/akkadotnet/akka.net/pull/2182
        /// </summary>
        [Fact]
        public void Fix2128_RepointableActorRef_multiple_enumerations()
        {
            var actor = Sys.ActorOf(Props.Create(() => new Bug2182Actor()).WithDispatcher("akka.test.calling-thread-dispatcher"), "buggy");
            actor.Tell("foo");
            ExpectMsg("foo");
        }
    }
}

