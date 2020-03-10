//-----------------------------------------------------------------------
// <copyright file="DaemonMsgCreateSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Linq.Expressions;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.Routing;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Serialization
{
    public class DaemonMsgCreateSerializerSpec : AkkaSpec
    {
        class MyActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                
            }
        }

        private readonly Akka.Serialization.Serialization _ser;
        private readonly IActorRef _supervisor;

        public DaemonMsgCreateSerializerSpec()
            : base(@"
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp {
                hostname = 127.0.0.1
                port = 0
            }        
        ")
        {
            _ser = Sys.Serialization;
            _supervisor = Sys.ActorOf(Props.Create<MyActor>(), "supervisor");
        }

        [Fact]
        public void Serialization_must_resolve_DaemonMsgCreateSerializer()
        {
            _ser.FindSerializerForType(typeof(DaemonMsgCreate)).GetType().ShouldBe(typeof(DaemonMsgCreateSerializer));
        }

        [Fact]
        public void Serialization_must_serialize_and_deserialize_DaemonMsgCreate_with_FromClassCreator()
        {
            VerifySerialization(new DaemonMsgCreate(Props.Create<MyActor>(), new Deploy(), "foo", _supervisor));
        }

        [Fact]
        public void Serialization_must_serialize_and_deserialize_DaemonMsgCreate_with_function_creator()
        {
            VerifySerialization(new DaemonMsgCreate(Props.Create(() => new MyActor()), new Deploy(), "foo", _supervisor));
        }

        [Fact]
        public void Serialization_must_serialize_and_deserialize_DaemonMsgCreate_with_Deploy_and_RouterConfig()
        {
            var decider = Decider.From(
              Directive.Escalate);

            var supervisorStrategy = new OneForOneStrategy(3, TimeSpan.FromSeconds(10), decider);
            var deploy1 = new Deploy("path1",
                ConfigurationFactory.ParseString("a=1"),
                new RoundRobinPool(5, null, supervisorStrategy, null),
                new RemoteScope(new Address("akka", "Test", "host1", 1921)),
                "mydispatcher");
            var deploy2 = new Deploy("path2",
                ConfigurationFactory.ParseString("a=2"),
                FromConfig.Instance,
                new RemoteScope(new Address("akka", "Test", "host2", 1922)),
                Deploy.NoDispatcherGiven);
            VerifySerialization(new DaemonMsgCreate(Props.Create<MyActor>().WithDispatcher("my-disp").WithDeploy(deploy1), deploy2, "foo", _supervisor));
        }

        #region Helper methods

        private void VerifySerialization(DaemonMsgCreate msg)
        {
            var daemonMsgSerializer = _ser.FindSerializerFor(msg);
            var binary = daemonMsgSerializer.ToBinary(msg);
            var actual = (DaemonMsgCreate) _ser.Deserialize(binary, daemonMsgSerializer.Identifier, typeof (DaemonMsgCreate));
            
            AssertDaemonMsgCreate(msg, actual);
        }

        private void AssertDaemonMsgCreate(DaemonMsgCreate expected, DaemonMsgCreate actual)
        {
            Assert.Equal(expected.Props.GetType(), actual.Props.GetType());
            Assert.Equal(expected.Props.Arguments.Length, actual.Props.Arguments.Length);
// ReSharper disable once ReturnValueOfPureMethodIsNotUsed
            actual.Props.Arguments.Zip(expected.Props.Arguments, (g, e) =>
            {
                if (e is Expression)
                {
                }
                else
                {
                    Assert.Equal(g, e);
                }
                return g;
            });
            Assert.Equal(expected.Props.Deploy,actual.Props.Deploy);
            Assert.Equal(expected.Deploy, actual.Deploy);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal(expected.Supervisor, actual.Supervisor);
        }

        #endregion
    }
}

