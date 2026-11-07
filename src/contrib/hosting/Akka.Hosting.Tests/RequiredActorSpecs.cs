using System;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Hosting.Tests;

public class RequiredActorSpecs
{
    public sealed class MyActorType : ReceiveActor
    {
        public MyActorType()
        {
            ReceiveAny(_ => Sender.Tell(_));
        }
    }

    public sealed class MyConsumer
    {
        private readonly IActorRef _actor;

        public MyConsumer(IRequiredActor<MyActorType> actor)
        {
            _actor = actor.ActorRef;
        }

        public async Task<string> Say(string word)
        {
            return await _actor.Ask<string>(word, TimeSpan.FromSeconds(3));
        }
    }
    
    public class MissingActor{}
    
    public sealed class BadConsumer
    {
        private readonly IActorRef _actor;

        public BadConsumer(IRequiredActor<MissingActor> actor)
        {
            _actor = actor.ActorRef;
        }

        public async Task<string> Say(string word)
        {
            return await _actor.Ask<string>(word, TimeSpan.FromSeconds(3));
        }
    }
    
    [Fact]
    public async Task ShouldRetrieveRequiredActorFromIServiceProvider()
    {
        // arrange
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("MySys", (builder, provider) =>
                {
                    builder.WithActors((system, registry) =>
                    {
                        var actor = system.ActorOf(Props.Create(() => new MyActorType()), "myactor");
                        registry.Register<MyActorType>(actor);
                    });
                });
                services.AddScoped<MyConsumer>();
            })
            .Build();
            await host.StartAsync();

        // act
        var myConsumer = host.Services.GetRequiredService<MyConsumer>();
        var input = "foo";
        var spoken = await myConsumer.Say(input);

        // assert
        spoken.Should().Be(input);
    }
    
    [Fact]
    public async Task ShouldFailRetrieveRequiredActorWhenNotDefined()
    {
        // arrange
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("MySys", (builder, provider) =>
                {
                    builder.WithActors((system, registry) =>
                    {
                        var actor = system.ActorOf(Props.Create(() => new MyActorType()), "myactor");
                        registry.Register<MyActorType>(actor);
                    });
                });
                services.AddScoped<BadConsumer>();
            })
            .Build();
        await host.StartAsync();

        // act
        Action shouldThrow = () => host.Services.GetRequiredService<BadConsumer>();
        
        // assert
        shouldThrow.Should().Throw<MissingActorRegistryEntryException>();
    }

    [Fact]
    public async Task ShouldNotCacheNobodyAfterWhenWaitedForRegistration()
    {
        // arrange
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("MySys", (builder, _) =>
                {
                    builder.WithActors((system, registry) =>
                    {
                        var actor = system.ActorOf(Props.Create(() => new MyActorType()), "myactor");
                        registry.Register<MyActorType>(actor);
                    });
                });
            })
            .Build();

        var myRequiredActor = host.Services.GetRequiredService<IRequiredActor<MyActorType>>();

        var task = myRequiredActor.GetAsync();
        task.IsCompletedSuccessfully.Should().BeFalse();

        await host.StartAsync();
        _ = await task;

        // act
        var cachedActorRef = await myRequiredActor.GetAsync();

        // assert
        cachedActorRef.Should().NotBeOfType<Nobody>();
    }
    
    [Fact]
    public async Task ShouldNotCacheNobodyBeforeRegistrationWithSyncActorRef()
    {
        // arrange
        using var host = new HostBuilder()
            .ConfigureServices(services =>
            {
                services.AddAkka("MySys", (builder, _) =>
                {
                    builder.WithActors((system, registry) =>
                    {
                        var actor = system.ActorOf(Props.Create(() => new MyActorType()), "myactor");
                        registry.Register<MyActorType>(actor);
                    });
                });
            })
            .Build();

        var myRequiredActor = host.Services.GetRequiredService<IRequiredActor<MyActorType>>();

        Action shouldThrow = () => _ = myRequiredActor.ActorRef;

        shouldThrow.Should().Throw<MissingActorRegistryEntryException>();

        await host.StartAsync();

        // act
        var cachedActorRef = await myRequiredActor.GetAsync();

        // assert
        cachedActorRef.Should().NotBeOfType<Nobody>();
    }
}