//-----------------------------------------------------------------------
// <copyright file="LocalActorRefProviderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.TestKit;
using Xunit;
using Akka.TestKit.TestActors;

namespace Akka.Tests.Actor
{
    public class LocalActorRefProviderSpec : AkkaSpec
    {
        [Fact]
        public void A_LocalActorRefs_ActorCell_must_not_retain_its_original_Props_when_Terminated()
        {
            var parent = Sys.ActorOf(Props.Create(() => new ParentActor()));
            parent.Tell("GetChild", TestActor);
            var child = ExpectMsg<IActorRef>();
            var childPropsBeforeTermination = ((LocalActorRef)child).Underlying.Props;
            Assert.Equal(Props.Empty, childPropsBeforeTermination);
            Watch(parent);
            Sys.Stop(parent);
            ExpectTerminated(parent);
            AwaitAssert(() =>
                {
                    var childPropsAfterTermination = ((LocalActorRef)child).Underlying.Props;
                    Assert.NotEqual(childPropsBeforeTermination, childPropsAfterTermination);
                    Assert.Equal(ActorCell.TerminatedProps, childPropsAfterTermination);
                });
        }

        [Fact]
        public void An_ActorRefFactory_must_only_create_one_instance_of_an_actor_with_a_specific_address_in_a_concurrent_environment()
        {
            var impl = (ActorSystemImpl)Sys;
            var provider = impl.Provider;

            Assert.IsType<LocalActorRefProvider>(provider);

            for (var i = 0; i < 100; i++)
            {
                var timeout = Dilated(TimeSpan.FromSeconds(5));
                var address = "new-actor" + i;
                var actors = Enumerable.Range(0, 4).Select(x => Task.Run(() => Sys.ActorOf(Props.Create(() => new BlackHoleActor()), address))).ToArray();
                // Use WhenAll with empty ContinueWith to swallow all exceptions, so we can inspect the tasks afterwards.
                Task.WhenAll(actors).ContinueWith(a => { }).Wait(timeout);
                Assert.True(actors.Any(x => x.Status == TaskStatus.RanToCompletion && x.Result != null), "Failed to create any Actors");
                Assert.True(actors.Any(x => x.Status == TaskStatus.Faulted && x.Exception.InnerException is InvalidActorNameException), "Succeeded in creating all Actors. Some should have failed.");
            }
        }

        [Fact]
        public void An_ActorRefFactory_must_only_create_one_instance_of_an_actor_from_within_the_same_message_invocation()
        {
            var supervisor = Sys.ActorOf(Props.Create<ActorWithDuplicateChild>());
            EventFilter.Exception<InvalidActorNameException>(message: "Actor name \"duplicate\" is not unique!").ExpectOne(() =>
                {
                    supervisor.Tell("");
                });
        }

        [Theory]
        [InlineData("", "empty")]
        [InlineData("$hello", "not start with `$`")]
        [InlineData("a%", "Illegal actor name")]
        [InlineData("%3","Illegal actor name")]
        [InlineData("%xx","Illegal actor name")]
        [InlineData("%0G","Illegal actor name")]
        [InlineData("%gg","Illegal actor name")]
        [InlineData("%","Illegal actor name")]
        [InlineData("%1t","Illegal actor name")]
        [InlineData("a?","Illegal actor name")]
        [InlineData("üß","include only ASCII")]
        [InlineData("åäö", "Illegal actor name")]
        public void An_ActorRefFactory_must_throw_suitable_exceptions_for_malformed_actor_names(string name, string expectedExceptionMessageSubstring)
        {
            var exception = Assert.Throws<InvalidActorNameException>(() =>
                {
                    Sys.ActorOf(Props.Empty, name);
                });
            Assert.Contains(expectedExceptionMessageSubstring, exception.Message, StringComparison.OrdinalIgnoreCase);
        }

        private class ActorWithDuplicateChild : ActorBase
        {
            protected override bool Receive(object message)
            {
                if (message as string == "")
                {
                    var a = Context.ActorOf(Props.Empty, "duplicate");
                    var b = Context.ActorOf(Props.Empty, "duplicate");
                    return true;
                }
                return false;
            }
        }

        private class ParentActor : ActorBase
        {
            private readonly IActorRef childActorRef;

            public ParentActor()
            {
                this.childActorRef = Context.ActorOf(Props.Empty);
            }

            protected override bool Receive(object message)
            {
                if (message as string == "GetChild")
                {
                    Sender.Tell(this.childActorRef);
                    return true;
                }
                return false;
            }
        }
    }
}

