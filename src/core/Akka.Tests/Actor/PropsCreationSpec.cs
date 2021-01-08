//-----------------------------------------------------------------------
// <copyright file="PropsCreationSpec.cs" company="Akka.NET Project">
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
        public void Props_Must_WorkWithActorWithoutAnyParams()
        {
            var props = Props.Create<NoParamsActor>();
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_Must_WorkWithFactory()
        {
            var props = Props.Create(() => new OneParamActor(new A()));
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_Must_WorkWithTypeOf()
        {
            var props = Props.Create(typeof(OneParamActor), new A());
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_Must_WorkWithTypeOfAndTwoParams()
        {
            var props = Props.Create(typeof(TwoParamsActor), new A(), new B());
            Sys.ActorOf(props);
        }

        [Fact]
        public void Props_Must_WorkWithGenericType()
        {
            var props = Props.Create<OneParamActor>(new A());
            Sys.ActorOf(props);
        }
    }
}
