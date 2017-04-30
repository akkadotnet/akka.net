//-----------------------------------------------------------------------
// <copyright file="PropsCreationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class PropsCreationSpec : AkkaSpec
    {
        public class A { }

        public class B { }

        public class NoParamsActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        public class OneParamActor : UntypedActor
        {
            public OneParamActor(A blackhole)
            {
                Blackhole = blackhole;
            }

            public A Blackhole { get; }

            protected override void OnReceive(object message)
            {
            }
        }

        public class TwoParamsActor : UntypedActor
        {
            public TwoParamsActor(A blackhole, B blackhole2)
            {
                Blackhole = blackhole;
                Blackhole2 = blackhole2;
            }

            public A Blackhole { get; }
            public B Blackhole2 { get; }

            protected override void OnReceive(object message)
            {
            }
        }

        public PropsCreationSpec() : base(ConfigurationFactory.ParseString("akka.actor.serialize-creators = on"))
        {
        }

        [Fact]
        public void Props_must_work_with_actor_without_any_params()
        {
            var props = Props.Create<NoParamsActor>();
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_must_work_with_factory()
        {
            var props = Props.Create(() => new OneParamActor(new A()));
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_Must_work_with_TypeOf()
        {
            var props = Props.Create(typeof(OneParamActor), new A());
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_must_work_with_TypeOf_and_two_params()
        {
            var props = Props.Create(typeof(TwoParamsActor), new A(), new B());
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_must_work_with_generic_type()
        {
            var props = Props.Create<OneParamActor>(new A());
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_must_work_with_DynamicObject()
        {
            var props = Props.Dynamic<OneParamActor>(new {blackhole = new A()});
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_must_fill_remaining_values_from_resolver_when_work_with_DynamicObject()
        {
            var props = Props.Dynamic<TwoParamsActor>(new { blackhole = new A() });
            Sys.DependencyResolver.Register(typeof(B), typeof(B));
            Sys.ActorOf(props);
        }
    }
}
