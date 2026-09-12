using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Cluster.Hosting.Tests;

public class ClusterSingletonWithDiSpecs : Akka.Hosting.TestKit.TestKit
{
    #region Actor and DI impls
    
    
    public interface IMyThing
    {
        string ThingId { get; }
    }

    public sealed class ThingImpl : IMyThing
    {
        public ThingImpl(string thingId)
        {
            ThingId = thingId;
        }

        public string ThingId { get; }
    }
    
    private class MySingletonDiActor : ReceiveActor
    {
        private readonly IMyThing _thing;
        
        public MySingletonDiActor(IMyThing thing)
        {
            _thing = thing;
            ReceiveAny(_ => Sender.Tell(_thing.ThingId));
        }
    }
    
    #endregion

    private readonly TaskCompletionSource _tcs = new(TimeSpan.FromSeconds(3));
    
    public ClusterSingletonWithDiSpecs(ITestOutputHelper output) : base(output: output)
    {
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        services.AddSingleton<IMyThing>(new ThingImpl("foo1"));
        base.ConfigureServices(context, services);
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.ConfigureHost(configurationBuilder =>
        {
            configurationBuilder.WithSingleton<MySingletonDiActor>("my-singleton",
                (_, _, dependencyResolver) => dependencyResolver.Props<MySingletonDiActor>());
        },  new ClusterOptions(){ Roles = new[] { "my-host" }}, _tcs, Output!);
    }

    [Fact]
    public async Task Should_launch_ClusterSingletonAndProxy_with_DI_delegate()
    {
        // arrange
        await _tcs.Task; // wait for cluster to start

        var registry = Host.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = registry.Get<MySingletonDiActor>();
        var thing = Host.Services.GetRequiredService<IMyThing>();

        // act
        
        // verify round-trip to the singleton proxy and back
        var respond = await singletonProxy.Ask<string>("hit", TimeSpan.FromSeconds(3));

        // assert
        Assert.Equal(thing.ThingId, respond);
    }
}