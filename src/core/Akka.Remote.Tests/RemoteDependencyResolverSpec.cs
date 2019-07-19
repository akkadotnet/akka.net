#region copyright
//-----------------------------------------------------------------------
// <copyright file="RemoteDependencyResolverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests
{
    public class CustomRemoteDependencyResolver : DependencyResolver
    {
        public override void ConfigureDependencies(ExtendedActorSystem system, IServiceCollection services)
        {
            base.ConfigureDependencies(system, services);

            services.AddTransient<RemoteDependencyResolverSpec.IActorDependency, RemoteDependencyResolverSpec.ActorDependency>();
            services.AddTransient<RemoteDependencyResolverSpec.EchoActor>();
        }
    }
    
    public class RemoteDependencyResolverSpec : AkkaSpec
    {
        private static Config TestConfig = ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp.hostname = ""127.0.0.1""
            akka.remote.dot-netty.tcp.port = 2551
            akka.actor.deployment {
                /service2 {
                  router = round-robin-pool
                  nr-of-instances = 3
                  remote = ""akka.tcp://remote@127.0.0.1:2552""
                }
            }        
        ");
        public RemoteDependencyResolverSpec(ITestOutputHelper output)
            : base(output, TestConfig)
        {

        }

        [Fact]
        public void Remote_deployment_should_work_with_dependency_injection()
        {
            var remoteConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = remote
                akka.dependency-resolver-class = ""Akka.Remote.Tests.CustomRemoteDependencyResolver, Akka.Remote.Tests""
                akka.remote.dot-netty.tcp.hostname = ""127.0.0.1""
                akka.remote.dot-netty.tcp.port = 2552");
            using (var remoteSystem = ActorSystem.Create("remote", remoteConfig.WithFallback(TestKit.Xunit2.TestKit.DefaultConfig)))
            {
                InitializeLogger(remoteSystem);
                var props = Props.Create<EchoActor>();

                // dependency resolver is not configured locally, but we still can create actor
                // by specifying its arguments explicitly
                var localActor = Sys.ActorOf(Props.Create(() => new EchoActor(new ActorDependency(Sys))), "service1");
                localActor.Tell("A");
                ExpectMsg("AkkaSpec::service1 replied A");
                
                // remote deploy behind the router 
                var remoteActor = Watch(Sys.ActorOf(props.WithRouter(FromConfig.Instance), "service2"));
                remoteActor.Tell("B");
                ExpectMsg("remote::$a replied B");
                remoteActor.Tell("C");
                ExpectMsg("remote::$b replied C");
                remoteActor.Tell("D");
                ExpectMsg("remote::$c replied D");
                remoteActor.Tell("E");
                ExpectMsg("remote::$a replied E");
                
                remoteActor.Tell(PoisonPill.Instance);
                ExpectTerminated(remoteActor);
            }
        }
        
        public sealed class EchoActor : ReceiveActor
        {
            public EchoActor(IActorDependency dependency)
            {
                Receive<string>(msg =>
                {
                    var actorSystemName = dependency.ActorSystemName();
                    Sender.Tell($"{actorSystemName}::{Self.Path.Name} replied {msg}");
                });
            }
        }
        
        public interface IActorDependency
        {
            string ActorSystemName();
        }
        
        public class ActorDependency : IActorDependency
        {
            private readonly ActorSystem _system;

            public ActorDependency(ActorSystem system)
            {
                _system = system;
            }

            public string ActorSystemName() => _system.Name;
        }
    }
}