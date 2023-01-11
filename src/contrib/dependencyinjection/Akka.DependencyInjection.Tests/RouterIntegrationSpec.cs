// -----------------------------------------------------------------------
//  <copyright file="BugFix6318Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DependencyInjection.Tests
{
    public class RouterIntegrationSpec: IAsyncLifetime
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AkkaService _akkaService;
        private readonly ITestOutputHelper _output;
        private TestKit.Xunit2.TestKit _testKit;
        
        public RouterIntegrationSpec(ITestOutputHelper output)
        {
            _output = output;
            var services = new ServiceCollection()
                .AddSingleton<InjectedService>()
                .AddSingleton<AkkaService>()
                .AddHostedService<AkkaService>();
            
            _serviceProvider = services.BuildServiceProvider();
            _akkaService = _serviceProvider.GetRequiredService<AkkaService>();
        }

        [Fact(DisplayName = "DI should work with ConsistentHashingPool router")]
        public void ShouldWorkWithConsistentHashingPoolTest()
        {
            TestDiActor.Counter.Reset();
            var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            var probe = _testKit.CreateTestProbe(system);
            system.EventStream.Subscribe(probe, typeof(Error));

            var props = DependencyResolver.For(system).Props<TestDiActor>().WithRouter(new ConsistentHashingPool(100));
            var actor = system.ActorOf(props.WithDeploy(Deploy.Local), "testDIActorRouter");

            var counterHash = new HashSet<long>();
            foreach (var i in Enumerable.Range(0, 500))
            {
                var msg = new ConsistentHashableEnvelope(GetMessage.Instance, i);
                actor.Tell(msg, probe);
                var result = probe.ExpectMsg<Message>();
                result.Value.Should().Be("I was injected");
                result.Counter.Should().BeGreaterOrEqualTo(0).And.BeLessThan(100);
                counterHash.Add(result.Counter);
            }

            counterHash.Count.Should().BeGreaterOrEqualTo(50); // at least half of the 100 possible routes have to be hit
        }
        
        [Fact(DisplayName = "DI should work with RoundRobinPool router")]
        public void ShouldWorkWithRoundRobinPoolTest()
        {
            TestDiActor.Counter.Reset();
            var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            var probe = _testKit.CreateTestProbe(system);
            system.EventStream.Subscribe(probe, typeof(Error));

            var props = DependencyResolver.For(system).Props<TestDiActor>().WithRouter(new RoundRobinPool(100));
            var actor = system.ActorOf(props.WithDeploy(Deploy.Local), "testDIActorRouter");

            var counterHash = new HashSet<long>();
            foreach (var i in Enumerable.Range(0, 100))
            {
                var msg = new ConsistentHashableEnvelope(GetMessage.Instance, i);
                actor.Tell(msg, probe);
                var result = probe.ExpectMsg<Message>();
                result.Value.Should().Be("I was injected");
                result.Counter.Should().BeGreaterOrEqualTo(0).And.BeLessThan(100);
                counterHash.Add(result.Counter);
            }

            // all 100 possible routes have to be hit
            foreach (var i in Enumerable.Range(0, 100))
            {
                counterHash.Should().Contain(i);
            }
        }

        [Fact(DisplayName = "DI should work with RandomPool router")]
        public void ShouldWorkWithRandomPoolTest()
        {
            TestDiActor.Counter.Reset();
            var system = _serviceProvider.GetRequiredService<AkkaService>().ActorSystem;
            var probe = _testKit.CreateTestProbe(system);
            system.EventStream.Subscribe(probe, typeof(Error));

            var props = DependencyResolver.For(system).Props<TestDiActor>().WithRouter(new RandomPool(100));
            var actor = system.ActorOf(props.WithDeploy(Deploy.Local), "testDIActorRouter");

            var counterHash = new HashSet<long>();
            foreach (var i in Enumerable.Range(0, 500))
            {
                var msg = new ConsistentHashableEnvelope(GetMessage.Instance, i);
                actor.Tell(msg, probe);
                var result = probe.ExpectMsg<Message>();
                result.Value.Should().Be("I was injected");
                result.Counter.Should().BeGreaterOrEqualTo(0).And.BeLessThan(100);
                counterHash.Add(result.Counter);
            }

            counterHash.Count.Should().BeGreaterOrEqualTo(50); // at least half of the 100 possible routes have to be hit
        }
        
        public async Task InitializeAsync()
        {
            await _akkaService.StartAsync(default);
            _testKit = new TestKit.Xunit2.TestKit(_akkaService.ActorSystem, _output);
        }

        public async Task DisposeAsync()
        {
            await _akkaService.StopAsync();
        }
        
        internal class TestDiActor : ReceiveActor
        {
            public static readonly AtomicCounter Counter = new AtomicCounter(0);

            public TestDiActor(InjectedService injected)
            {
                long count = Counter.GetAndIncrement();
                Receive<GetMessage>(_ => Sender.Tell(new Message{Value = injected.Message, Counter = count}));
            }
        }

        internal class Message
        {
            public string Value { get; set; }
            public long Counter { get; set; }
        }
        
        internal class GetMessage
        {
            public static readonly GetMessage Instance = new GetMessage();
            private GetMessage()
            { }
        }
        
        internal class InjectedService
        {
            public string Message => "I was injected";
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

            public async Task StopAsync(CancellationToken cancellationToken = default)
            {
                await ActorSystem.Terminate();
            }
        }
    }
}