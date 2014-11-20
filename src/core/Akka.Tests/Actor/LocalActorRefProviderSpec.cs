using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Configuration;
using Akka.TestKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using Xunit.Extensions;

namespace Akka.Tests.Actor
{
    public class LocalActorRefProviderSpec : AkkaSpec
    {
        private static readonly string _akkaSpecConfig = @"
            akka {
              actor {
                default-dispatcher {
                  executor = ""thread-pool-executor""
                  thread-pool-executor {
                    core-pool-size-min = 16
                    core-pool-size-max = 16
                  }
                }
              }
            }";

        public LocalActorRefProviderSpec()
            : base(_akkaSpecConfig)
        {
                
        }

        [Fact]
        public void A_LocalActorRefs_ActorCell_must_not_retain_its_original_Props_when_Terminated()
        {
            var parent = Sys.ActorOf(Props.Create(() => new ParentActor()));
            parent.Tell("GetChild", TestActor);
            var child = ExpectMsg<ActorRef>();
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
                var timeout = TimeSpan.FromSeconds(5);
                var address = "new-actor" + i;
                var actors = new object[4].Select(x => Task<ActorRef>.Run(() => Sys.ActorOf(Props.Create(() => new EmptyActor()), address))).ToArray();
                Task.WhenAll(actors).ContinueWith(a => { }).Wait();
                Assert.True(actors.Any(x => x.Status == TaskStatus.RanToCompletion));
                Assert.True(actors.Any(x => x.Status == TaskStatus.Faulted));
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
        [InlineData("$hello", "conform")]
        [InlineData("a%", "conform")]
        [InlineData("a?", "conform")]
        [InlineData("åäö", "conform")]
        public void An_ActorRefFactory_must_throw_suitable_exceptions_for_malformed_actor_names(string name, string expectedExceptionMessageSubstring)
        {
            var exception = Assert.Throws<InvalidActorNameException>(() =>
                {
                    Sys.ActorOf(Props.Empty, name);
                });
            Assert.Contains(expectedExceptionMessageSubstring, exception.Message, StringComparison.InvariantCultureIgnoreCase);
        }

        private class ActorWithDuplicateChild : ActorBase
        {
            protected override bool Receive(object message)
            {
                if (message == "")
                {
                    var a = Context.ActorOf(Props.Empty, "duplicate");
                    var b = Context.ActorOf(Props.Empty, "duplicate");
                    return true;
                }
                return false;
            }
        }

        private class EmptyActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                return false;
            }
        }

        private class ParentActor : ActorBase
        {
            private readonly ActorRef childActorRef;

            public ParentActor()
            {
                this.childActorRef = Context.ActorOf(Props.Empty);
            }

            protected override bool Receive(object message)
            {
                if (message == "GetChild")
                {
                    Sender.Tell(this.childActorRef);
                    return true;
                }
                return false;
            }
        }
    }
}
