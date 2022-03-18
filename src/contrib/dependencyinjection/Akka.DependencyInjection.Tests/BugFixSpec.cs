// //-----------------------------------------------------------------------
// // <copyright file="BugFix.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;
using static FluentAssertions.FluentActions;

namespace Akka.DependencyInjection.Tests
{
    public class BugFixSpec: AkkaSpec, IAsyncLifetime
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AkkaService _akkaService;
        
        public BugFixSpec(ITestOutputHelper output) : base(output)
        {
            var services = new ServiceCollection()
                .AddSingleton<AkkaService>()
                .AddHostedService<AkkaService>();
            
            _serviceProvider = services.BuildServiceProvider();
            _akkaService = _serviceProvider.GetRequiredService<AkkaService>();
        }

        [Fact(DisplayName = "DI should throw if DI provider does not contain required parameter")]
        public void ShouldThrowIfParameterInjectionFailed()
        {
            var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            var props = DependencyResolver.For(system).Props<TestDiActor>();
            
            Invoking(() => system.ActorOf(props, "testDIActor"))
                .Should().Throw<InvalidOperationException>().WithMessage("Unable to resolve service for type");
        }

        internal class TestDiActor : ReceiveActor
        {
            public TestDiActor(NotInServices doesNotExistInDi)
            {
            }
        }

        internal class NotInServices
        {
        }
        
        
        public async Task InitializeAsync()
        {
            await _akkaService.StartAsync(default);
        }

        public async Task DisposeAsync()
        {
            var sys = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            await sys.Terminate();
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