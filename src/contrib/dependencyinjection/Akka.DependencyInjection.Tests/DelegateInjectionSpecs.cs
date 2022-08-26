﻿//-----------------------------------------------------------------------
// <copyright file="DelegateInjectionSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DependencyInjection.Tests
{
    public class DelegateInjectionSpecs : AkkaSpec
    {
        public delegate IActorRef EchoActorProvider();

        private readonly IServiceProvider _serviceProvider;

        public static IServiceProvider CreateProvider()
        {
            var services = new ServiceCollection()
                .AddSingleton<AkkaService>()
                .AddHostedService<AkkaService>()
                .AddSingleton<EchoActorProvider>(p =>
                {
                    var system = p.GetRequiredService<AkkaService>();
                    var actor = system.ActorSystem.ActorOf<EchoActor>("echoActor");
                    return () => actor;
                });
            
            var provider = services.BuildServiceProvider();
            var akkaService = provider.GetRequiredService<AkkaService>();
            akkaService.StartAsync(default).Wait();

            return provider;
        }

        public DelegateInjectionSpecs(ITestOutputHelper output) : base(output)
        {
            _serviceProvider = CreateProvider();
        }

        [Fact]
        public async Task DI_should_be_able_to_retrieve_singleton_using_delegate()
        {
            var actor = _serviceProvider.GetRequiredService<EchoActorProvider>()();

            var task = actor.Ask("echo");
            task.Wait(TimeSpan.FromSeconds(3));
            task.Result.ShouldBe("echo");

            var sys = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            await sys.Terminate();
        }

        [Fact]
        public async Task DI_should_be_able_to_retrieve_singleton_using_delegate_from_inside_actor()
        {
            var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            var actor = system.ActorOf(ParentActor.Props(system));
            
            var task = actor.Ask("echo");
            task.Wait(TimeSpan.FromSeconds(3));
            task.Result.ShouldBe("echo");

            var sys = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            await sys.Terminate();
        }

        internal class ParentActor : UntypedActor
        {
            public static Props Props(ActorSystem system) =>
                DependencyResolver.For(system).Props<ParentActor>();

            private readonly IActorRef _echoActor;

            public ParentActor(IServiceProvider provider)
            {
                _echoActor = provider.GetRequiredService<EchoActorProvider>()();
            }

            protected override void OnReceive(object message)
            {
                _echoActor.Forward(message);
            }
        }

        internal class EchoActor : ReceiveActor
        {
            public static Props Props() => Akka.Actor.Props.Create<EchoActor>();

            public EchoActor()
            {
                Receive<string>(msg =>
                {
                    Sender.Tell(msg);
                });
            }
        }

        internal class AkkaService : IHostedService
        {
            public ActorSystem ActorSystem { get; private set; }

            private readonly IServiceProvider _serviceProvider;

            public AkkaService(IServiceProvider serviceProvider)
            {
                _serviceProvider = serviceProvider;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                var setup = DependencyResolverSetup.Create(_serviceProvider)
                    .And(BootstrapSetup.Create().WithConfig(TestKitBase.DefaultConfig));

                ActorSystem = ActorSystem.Create("TestSystem", setup);
                return Task.CompletedTask;
            }

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                await ActorSystem.Terminate();
            }
        }
    }
}
