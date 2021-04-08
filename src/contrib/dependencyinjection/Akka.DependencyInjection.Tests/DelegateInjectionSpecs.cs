using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public void DI_should_be_able_to_retrieve_singleton_using_delegate()
        {
            var actor = _serviceProvider.GetRequiredService<EchoActorProvider>()();

            var task = actor.Ask("echo");
            task.Wait(TimeSpan.FromSeconds(3));
            task.Result.ShouldBe("echo");
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
                var setup = ServiceProviderSetup.Create(_serviceProvider)
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
