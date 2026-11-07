using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace Akka.Cluster.Hosting.Tests;

public class ClusterSingletonSpecs
{
    public ClusterSingletonSpecs(ITestOutputHelper output)
    {
        Output = output;
    }

    public ITestOutputHelper Output { get; }
    
    private class MySingletonActor : ReceiveActor
    {
        public static Props MyProps => Props.Create(() => new ClusterSingletonSpecs.MySingletonActor());

        public MySingletonActor()
        {
            ReceiveAny(_ => Sender.Tell(_));
        }
    }

    [Fact]
    public async Task Should_launch_ClusterSingletonAndProxy()
    {
        // arrange
        using var host = await TestHelper.CreateHost(
            builder => { builder.WithSingleton<ClusterSingletonSpecs.MySingletonActor>("my-singleton", MySingletonActor.MyProps); },
            new ClusterOptions(){ Roles = new[] { "my-host" }}, Output);

        var registry = host.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = registry.Get<ClusterSingletonSpecs.MySingletonActor>();

        // act
        
        // verify round-trip to the singleton proxy and back
        // the proxy buffers until the singleton exists, which needs the node to be Up and Oldest first
        var respond = await singletonProxy.Ask<string>("hit", TimeSpan.FromSeconds(30));

        // assert
        Assert.Equal("hit", respond);

        await host.StopAsync();
    }

    [Fact(DisplayName = "Should launch singleton manager and proxy at the appropriate path (no manager name, actor props)")]
    public async Task ClusterSingletonAndProxyWithNoManagerNameTest()
    {
        using var host = await TestHelper.CreateHost(
            builder =>
            {
                builder.WithSingleton<MySingletonActor>(
                    singletonName: "my-singleton", 
                    actorProps: MySingletonActor.MyProps);
            },
            new ClusterOptions
            {
                Roles = new[] { "my-host" }
            }, Output);

        var system = host.Services.GetRequiredService<ActorSystem>();
        var registry = host.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = await registry.GetAsync<MySingletonActor>();
        
        var address = Cluster.Get(system).SelfAddress;
        var expectedSingletonPath = new RootActorPath(address) / "user" / "my-singleton" / "my-singleton";
        var singletonSelector = system.ActorSelection(expectedSingletonPath);

        await AssertSingletonSelectionAsync(singletonSelector);

        Assert.Equal("akka://TestSys/user/my-singleton-proxy", singletonProxy.Path.ToString());

        await host.StopAsync();
    }

    private static async Task AssertSingletonSelectionAsync(ActorSelection singletonSelector)
    {
        var startTime = DateTime.UtcNow;
        // The node has to join itself, be promoted to Up by the leader, become Oldest and only then
        // start the singleton. On a busy CI agent that regularly takes more than a few seconds.
        var timeout = TimeSpan.FromSeconds(30);
        await Test();
        return;

        async Task Test()
        {
            // might take multiple tries to resolve the singleton if it hasn't been created yet
            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    var identify = await singletonSelector.ResolveOne(250.Milliseconds());
                    Assert.NotEqual(ActorRefs.Nobody, identify);
                    return;
                }
                catch (Exception)
                {
                    // not there yet; back off briefly and try again
                    await Task.Delay(100.Milliseconds());
                }
            }
            
            throw new AskTimeoutException("Failed to resolve singleton within timeout");
        }
    }

    [Fact(DisplayName = "Should launch singleton manager and proxy at the appropriate path (no manager name, actor factory)")]
    public async Task ClusterSingletonAndProxyWithNoManagerNameAndFactoryTest()
    {
        using var host = await TestHelper.CreateHost(
            builder =>
            {
                builder.WithSingleton<MySingletonActor>(
                    singletonName: "my-singleton", 
                    propsFactory: (_, _, _) => MySingletonActor.MyProps);
            },
            new ClusterOptions
            {
                Roles = new[] { "my-host" }
            }, Output);

        var system = host.Services.GetRequiredService<ActorSystem>();
        var registry = host.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = await registry.GetAsync<MySingletonActor>();
        
        var address = Cluster.Get(system).SelfAddress;
        var expectedSingletonPath = new RootActorPath(address) / "user" / "my-singleton" / "my-singleton";
        var singletonSelector = system.ActorSelection(expectedSingletonPath);

        await AssertSingletonSelectionAsync(singletonSelector);

        Assert.Equal("akka://TestSys/user/my-singleton-proxy", singletonProxy.Path.ToString());

        await host.StopAsync();
    }

    [Fact(DisplayName = "Should launch singleton manager and proxy at the appropriate path (with manager name, actor props)")]
    public async Task ClusterSingletonAndProxyWithManagerNameTest()
    {
        using var host = await TestHelper.CreateHost(
            builder =>
            {
                builder.WithSingleton<MySingletonActor>(
                    singletonManagerName: "my-singleton",
                    singletonName: "singleton",
                    actorProps: MySingletonActor.MyProps);
            },
            new ClusterOptions
            {
                Roles = new[] { "my-host" }
            }, Output);

        var system = host.Services.GetRequiredService<ActorSystem>();
        var registry = host.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = await registry.GetAsync<MySingletonActor>();
        
        var address = Cluster.Get(system).SelfAddress;
        var expectedSingletonPath = new RootActorPath(address) / "user" / "my-singleton" / "singleton";
        var singletonSelector = system.ActorSelection(expectedSingletonPath);

        await AssertSingletonSelectionAsync(singletonSelector);

        Assert.Equal("akka://TestSys/user/singleton-proxy", singletonProxy.Path.ToString());

        await host.StopAsync();
    }

    [Fact(DisplayName = "Should launch singleton manager and proxy at the appropriate path (with manager name, actor factory)")]
    public async Task ClusterSingletonAndProxyWithManagerNameAndFactoryTest()
    {
        using var host = await TestHelper.CreateHost(
            builder =>
            {
                builder.WithSingleton<MySingletonActor>(
                    singletonManagerName: "my-singleton",
                    singletonName: "singleton",
                    propsFactory: (_, _, _) => MySingletonActor.MyProps);
            },
            new ClusterOptions
            {
                Roles = new[] { "my-host" }
            }, Output);

        var system = host.Services.GetRequiredService<ActorSystem>();
        var registry = host.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = await registry.GetAsync<MySingletonActor>();
        
        var address = Cluster.Get(system).SelfAddress;
        var expectedSingletonPath = new RootActorPath(address) / "user" / "my-singleton" / "singleton";
        var singletonSelector = system.ActorSelection(expectedSingletonPath);

        await AssertSingletonSelectionAsync(singletonSelector);

        Assert.Equal("akka://TestSys/user/singleton-proxy", singletonProxy.Path.ToString());

        await host.StopAsync();
    }

    [Fact(DisplayName = "WithSingletonProxy should work with no manager name")]
    public async Task Should_launch_ClusterSingleton_and_Proxy_separately()
    {
        // arrange

        var singletonOptions = new ClusterSingletonOptions() { Role = "my-host" };
        using var singletonHost = await TestHelper.CreateHost(
            builder => { builder.WithSingleton<ClusterSingletonSpecs.MySingletonActor>("my-singleton", MySingletonActor.MyProps, singletonOptions, createProxyToo:false); },
            new ClusterOptions(){ Roles = new[] { "my-host" }}, Output);

        var singletonSystem = singletonHost.Services.GetRequiredService<ActorSystem>();
        var address = Cluster.Get(singletonSystem).SelfAddress;
        
        using var singletonProxyHost =  await TestHelper.CreateHost(
            builder => { builder.WithSingletonProxy<ClusterSingletonSpecs.MySingletonActor>("my-singleton", singletonOptions); },
            new ClusterOptions(){ Roles = new[] { "proxy" }, SeedNodes = new []{ address.ToString() } }, Output);
        
        var registry = singletonProxyHost.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = registry.Get<ClusterSingletonSpecs.MySingletonActor>();
        
        // act
        
        // verify round-trip to the singleton proxy and back
        // the proxy buffers until the singleton exists, which needs the node to be Up and Oldest first
        var respond = await singletonProxy.Ask<string>("hit", TimeSpan.FromSeconds(30));

        // assert
        Assert.Equal("hit", respond);

        await Task.WhenAll(singletonHost.StopAsync(), singletonProxyHost.StopAsync());
    }

    [Fact(DisplayName = "WithSingletonProxy should work with manager name")]
    public async Task SeparateProxyWithManagerNameTest()
    {
        // arrange

        var singletonOptions = new ClusterSingletonOptions() { Role = "my-host" };
        using var singletonHost = await TestHelper.CreateHost(
            builder =>
            {
                builder.WithSingleton<MySingletonActor>(
                    singletonManagerName: "my-singleton", 
                    singletonName: "singleton", 
                    actorProps: MySingletonActor.MyProps, 
                    options: singletonOptions, 
                    createProxyToo:false);
            },
            new ClusterOptions
            {
                Roles = new[] { "my-host" }
            }, Output);

        var singletonSystem = singletonHost.Services.GetRequiredService<ActorSystem>();
        var address = Cluster.Get(singletonSystem).SelfAddress;
        
        using var singletonProxyHost =  await TestHelper.CreateHost(
            builder =>
            {
                builder.WithSingletonProxy<MySingletonActor>(
                    singletonManagerName: "my-singleton",
                    singletonName: "singleton", 
                    options: singletonOptions);
            },
            new ClusterOptions
            {
                Roles = new[] { "proxy" }, 
                SeedNodes = new []{ address.ToString() }
            }, Output);
        
        var registry = singletonProxyHost.Services.GetRequiredService<ActorRegistry>();
        var singletonProxy = await registry.GetAsync<MySingletonActor>();

        // act
        
        // verify round-trip to the singleton proxy and back
        // two nodes: the proxy host has to join the singleton host over seed nodes, reach Up, and
        // the proxy has to locate the singleton before this round-trip can complete
        var respond = await singletonProxy.Ask<string>("hit", 30.Seconds());

        // assert
        Assert.Equal("hit", respond);

        await Task.WhenAll(singletonHost.StopAsync(), singletonProxyHost.StopAsync());
    }
}