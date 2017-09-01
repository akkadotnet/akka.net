//-----------------------------------------------------------------------
// <copyright file="PropsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.TestKit;
using System;
using System.Linq;
using Xunit;

namespace Akka.Tests.Actor
{
    public class PropsSpec : AkkaSpec
    {
        [Fact]
        public void Props_must_create_actor_from_type()
        {
            var props = Props.Create<PropsTestActor>();
            TestActorRef<PropsTestActor> actor = new TestActorRef<PropsTestActor>(Sys, props);
            Assert.IsType<PropsTestActor>(actor.UnderlyingActor);
        }

        [Fact]
        public void Props_must_create_actor_by_expression()
        {
            var props = Props.Create(() => new PropsTestActor());
            IActorRef actor = Sys.ActorOf(props);
            Assert.NotNull(actor);
        }
        
        [Fact]
        public void Props_created_without_strategy_must_have_it_null()
        {
            var props = Props.Create(() => new PropsTestActor());
            Assert.Null(props.SupervisorStrategy);
        }

        [Fact]
        public void Props_created_with_strategy_must_have_it_set()
        {
            var strategy = new OneForOneStrategy(_ => Directive.Stop);
            var props = Props.Create(() => new PropsTestActor(), strategy);

            Assert.Equal(strategy, props.SupervisorStrategy);
        }

        [Fact]
        public void Props_created_with_null_type_must_throw()
        {
            Type missingType = null;
            object[] args = new object[0];
            var argsEnumerable = Enumerable.Empty<object>();
            var defaultStrategy = SupervisorStrategy.DefaultStrategy;
            var defaultDeploy = Deploy.Local;

            Props p = null;

            Assert.Throws<ArgumentNullException>("type", () => p = Props.Create(missingType, args));
            Assert.Throws<ArgumentNullException>("type", () => p = Props.Create(missingType));
            Assert.Throws<ArgumentNullException>("type", () => p = Props.Create(missingType, defaultStrategy, argsEnumerable));
            Assert.Throws<ArgumentNullException>("type", () => p = Props.Create(missingType, defaultStrategy, args));
            Assert.Throws<ArgumentNullException>("type", () => p = Props.Create(missingType, argsEnumerable));
            Assert.Throws<ArgumentNullException>("type", () => p = Props.Create(missingType, args));
        }
        
        private class PropsTestActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                return true;
            }
        }
    }
}

