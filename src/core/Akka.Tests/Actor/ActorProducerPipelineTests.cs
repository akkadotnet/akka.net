//-----------------------------------------------------------------------
// <copyright file="ActorProducerPipelineTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class ActorProducerPipelineTests : AkkaSpec
    {
        #region internal test classes

        internal class TestException : Exception
        {
            public TestException(string message)
                : base(message)
            {
            }
        }

        internal class PlugActor : ActorBase
        {
            public List<string> PluginMessages { get; private set; }
            public PlugActor()
            {
                PluginMessages = new List<string>();
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(PluginMessages.ToArray());
                return true;
            }
        }

        internal class PlugActorA : PlugActor { }
        internal class PlugActorB : PlugActor { }

        internal class GenericPlugin<T> : ActorProducerPluginBase<T> where T : PlugActor
        {
            public override void AfterIncarnated(T actor, IActorContext context)
            {
                actor.PluginMessages.Add(typeof(T).ToString());
            }
        }

        internal class WorkingPlugin : ActorProducerPluginBase<PlugActor>
        {
            public override void AfterIncarnated(PlugActor actor, IActorContext context)
            {
                actor.PluginMessages.Add("working plugin");
            }
        }

        internal class FailingPlugin : ActorProducerPluginBase<PlugActor>
        {
            public override void AfterIncarnated(PlugActor actor, IActorContext context)
            {
                actor.PluginMessages.Add("failing plugin");
                throw new TestException("plugin failed");
            }
        }

        internal class OrderedPlugin : ActorProducerPluginBase<PlugActor>
        {
            private readonly int _index;

            public OrderedPlugin(int index)
            {
                _index = index;
            }

            public override void AfterIncarnated(PlugActor actor, IActorContext context)
            {
                actor.PluginMessages.Add("plugin-" + _index);
            }
        }

        internal class OrderedPlugin1 : OrderedPlugin
        {
            public OrderedPlugin1() : base(1) { }
        }

        internal class OrderedPlugin2 : OrderedPlugin
        {
            public OrderedPlugin2() : base(2) { }
        }

        internal class OrderedPlugin3 : OrderedPlugin
        {
            public OrderedPlugin3() : base(3) { }
        }

        internal sealed class StashStatus
        {
            public static readonly StashStatus Instance = new StashStatus();
            private StashStatus() { }
        }

        internal class StashingActor: ReceiveActor, IWithUnboundedStash
        {
            public StashingActor()
            {
                Receive<StashStatus>(status => Sender.Tell("actor stash is " + (Stash != null ? "initialized" : "uninitialized")));
                ReceiveAny(_ => Stash.Stash());
            }

            public IStash Stash { get; set; }
        }

        #endregion

        private ActorProducerPipelineResolver _resolver;

        public ActorProducerPipelineTests()
        {
            var extendedSystem = (ExtendedActorSystem)Sys;
            _resolver = extendedSystem.ActorPipelineResolver;
        }

        [Fact]
        public void Pipeline_application_should_survive_internal_plugin_exceptions()
        {
            _resolver.Register(new FailingPlugin());
            _resolver.Register(new WorkingPlugin());

            EventFilter.Exception<TestException>("plugin failed").ExpectOne(() =>
            {
                var actor = ActorOf<PlugActor>();
                var ask = actor.Ask<string[]>("plugins", TimeSpan.FromSeconds(1));

                ask.Result.ShouldOnlyContainInOrder("failing plugin", "working plugin");
            });
        }

        [Fact]
        public void Pipeline_should_not_allow_to_register_the_same_plugin_twice()
        {
            var pluginCount = _resolver.TotalPluginCount;

            _resolver.Register(new WorkingPlugin()).ShouldBeTrue();
            _resolver.Register(new WorkingPlugin()).ShouldBeFalse();

            Assert.Equal(pluginCount + 1, _resolver.TotalPluginCount);
        }

        [Fact]
        public void Pipeline_should_allow_to_register_multiple_generic_plugins_with_different_generic_types()
        {
            _resolver.Register(new WorkingPlugin()).ShouldBeTrue();
            _resolver.Register(new GenericPlugin<PlugActorA>()).ShouldBeTrue();
            _resolver.Register(new GenericPlugin<PlugActorB>()).ShouldBeTrue();

            var plugA = ActorOf<PlugActorA>();
            var plugB = ActorOf<PlugActorB>();

            plugA.Ask<string[]>("plugins", TimeSpan.FromSeconds(3)).Result.ShouldOnlyContainInOrder("working plugin", typeof(PlugActorA).ToString());
            plugB.Ask<string[]>("plugins", TimeSpan.FromSeconds(3)).Result.ShouldOnlyContainInOrder("working plugin", typeof(PlugActorB).ToString());
        }

        [Fact]
        public void Pipeline_application_should_apply_plugins_in_specified_order()
        {
            _resolver.Insert(0, new OrderedPlugin1()).ShouldBeTrue();
            _resolver.Insert(2, new OrderedPlugin3()).ShouldBeTrue();
            _resolver.Insert(1, new OrderedPlugin2()).ShouldBeTrue();

            var actor = ActorOf<PlugActor>();
            actor.Ask<string[]>("plugins", TimeSpan.FromSeconds(3)).Result.ShouldOnlyContainInOrder("plugin-1", "plugin-2", "plugin-3");
        }

        [Fact]
        public void DefaultPipeline_should_apply_stashing_to_actors_implementing_it()
        {
            var actor = ActorOf<StashingActor>();
            actor.Ask<string>(StashStatus.Instance, TimeSpan.FromSeconds(3)).Result.ShouldBe("actor stash is initialized");
        }

        [Fact]
        public void DefaultPipeline_should_unstash_all_terminated_actors_stashed_messages_on_stop()
        {
            // we'll send 3 int messages to stash by the actor and then stop it,
            // all stashed messages should then be unstashed back and sent to dead letters
            EventFilter.DeadLetter<int>().Expect(3, () =>
            {
                var actor = ActorOf<StashingActor>();
                // send some messages to stash
                actor.Tell(1);
                actor.Tell(2);
                actor.Tell(3);
                
                // stop actor
                actor.Tell(PoisonPill.Instance);
            });
        }
    }
}

