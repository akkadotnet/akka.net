//-----------------------------------------------------------------------
// <copyright file="Bugfix3903Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Remote.Tests.Serialization
{
    public class Bugfix3903Spec : AkkaSpec
    {
        // hocon config enabling akka.remote
        private static readonly Config Config = @"akka.actor.provider = remote
                                                    akka.remote.dot-netty.tcp.hostname = localhost
                                                    akka.remote.dot-netty.tcp.port = 0";

        public Bugfix3903Spec(ITestOutputHelper outputHelper) : base(Config, outputHelper)
        {
        }

        #region Internal Types

        // parent actor type that will remotely deploy a child actor type onto a specific address
        private class ParentActor : ReceiveActor
        {
            // message type that includes an Address
            public class DeployChild
            {
                public DeployChild(Address address)
                {
                    Address = address;
                }

                public Address Address { get; }
            }

            public ParentActor()
            {
                Receive<DeployChild>(s =>
                {
                    // props to deploy an EchoActor at the address specified in DeployChild
                    var props = Props.Create<EchoActor>().WithDeploy(new Deploy(new RemoteScope(s.Address)));
                    var child = Context.ActorOf(props, "child");
                    Sender.Tell(child);
                });
            }
        }

        internal class EchoActor : ReceiveActor
        {
            public class Fail
            {
                public static readonly Fail Instance = new Fail();
                private Fail(){}
            }
            
            public EchoActor()
            {
                // receive message that will cause this actor to fail
                Receive<Fail>(s =>
                {
                    throw new ApplicationException("fail");
                });
                ReceiveAny(o => Sender.Tell(o));
            }
        }

        #endregion

        // a test where Sys starts a ParentActor and has it remotely deploy an EchoActor onto a second ActorSystem
        [Fact]
        public async Task ParentActor_should_be_able_to_deploy_EchoActor_to_remote_system()
        {
            // create a second ActorSystem
            var system2 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
            InitializeLogger(system2);
            try
            {
                // create a supervision strategy that will send a message to the TestActor including the exception of the child that failed
                var strategy = new OneForOneStrategy(ex =>
                {
                    TestActor.Tell(ex);
                    return Directive.Stop;
                });

                // create a ParentActor in the first ActorSystem
                var parent = Sys.ActorOf(Props.Create<ParentActor>().WithSupervisorStrategy(strategy), "parent");

                // have the ParentActor remotely deploy an EchoActor onto the second ActorSystem
                var child = await parent
                    .Ask<IActorRef>(new ParentActor.DeployChild(
                        system2.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress), RemainingOrDefault).ConfigureAwait(false);

                // assert that Child is a remote actor reference
                child.Should().BeOfType<RemoteActorRef>();
                Watch(child);
                
                // send a message to the EchoActor and verify that it is received
                (await child.Ask<string>("hello", RemainingOrDefault).ConfigureAwait(false)).Should().Be("hello");
                
                // cause the child to crash
                child.Tell(EchoActor.Fail.Instance);
                var exception = ExpectMsg<ApplicationException>();
                exception.Message.Should().Be("fail");
                ExpectTerminated(child);
            }
            finally
            {
                // shut down the second ActorSystem
                Shutdown(system2);
            }
        }
    }
}
