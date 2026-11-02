# Akka.Cluster.Hosting

This module provides `Akka.Hosting` ease-of-use extension methods for [`Akka.Cluster`](https://getakka.net/articles/clustering/cluster-overview.html), [`Akka.Cluster.Sharding`](https://getakka.net/articles/clustering/cluster-sharding.html), and `Akka.Cluster.Tools`.

## Content

- [Akka.Cluster](https://getakka.net/articles/clustering/cluster-overview.html)
  - [WithClustering()](#withclustering-method)
    - [Configure A Cluster With Split-Brain Resolver](#configure-a-cluster-with-split-brain-resolver-sbr)
    - [Using Lease-Majority Split Brain Resolver Strategy](#using-lease-majority-split-brain-resolver-strategy)
- [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html)
  - [WithShardRegion()](#withshardregion-method)
    - [Using Lease With Cluster Sharding](#using-lease-with-cluster-sharding)
  - [WithShardRegionProxy()](#withshardregionproxy-method)
- [Distributed Publish-Subscribe](https://getakka.net/articles/clustering/distributed-publish-subscribe.html)
  - [WithDistributedPubSub()](#withdistributedpubsub-method)
- [Cluster Singleton](https://getakka.net/articles/clustering/cluster-singleton.html)
  - [WithSingleton()](#withsingleton-method)
    - [Using Lease With Cluster Singleton](#using-lease-with-cluster-singleton)
  - [WithSingletonProxy()](#withsingletonproxy-method)
- [Cluster Client](https://getakka.net/articles/clustering/cluster-client.html)
  - [WithClusterClient()](#withclusterclient-method)
  - [WithClusterClientReceptionist()](#withclusterclientreceptionist-method)

# Akka.Cluster Extension Methods

## WithClustering Method

An extension method to add [Akka.Cluster](https://getakka.net/articles/clustering/cluster-overview.html) support to the `ActorSystem`.

```csharp
public static AkkaConfigurationBuilder WithClustering(
    this AkkaConfigurationBuilder builder, 
    ClusterOptions options = null);
```

### Parameters
* `options` __ClusterOptions__

  Optional. Akka.Cluster configuration parameters.

### Example
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithRemoting("localhost", 8110)
        .WithClustering(new ClusterOptions { 
            Roles = new[] { "myRole" },
            SeedNodes = new[] { Address.Parse("akka.tcp://MyActorSystem@localhost:8110")},
            SplitBrainResolver = SplitBrainResolverOption.Default
        });
});

var app = builder.Build();
app.Run();
```

The code above will start [`Akka.Cluster`](https://getakka.net/articles/clustering/cluster-overview.html) with [`Akka.Remote`](https://getakka.net/articles/remoting/index.html) at localhost domain port 8110 and joins itself through the configured `SeedNodes` to form a single node cluster. The `ClusterOptions` class lets you configure the node roles and the seed nodes it should join at start up.

### Configure A Cluster With Split-Brain Resolver (SBR)

The __ClusterOptions.SplitBrainResolver__ property lets you configure a SBR. There are four different strategies that the SBR can use, to set one up you will need to pass in one of these class instances:

| Strategy name  | Option class          |
|----------------|-----------------------|
| Keep Majority  | `KeepMajorityOption`  |
| Static-Quorum  | `StaticQuorumOption`  |
| Keep Oldest    | `KeepOldestOption`    |
| Lease Majority | `LeaseMajorityOption` |

You can also pass in `SplitBrainResolverOption.Default` for the default SBR setting that uses the Keep Majority strategy with no role defined.

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithClustering(new ClusterOptions { 
            SplitBrainResolver = new KeepMajorityOption{ Role = "myRole" },
        });
});
```

### Using Lease-Majority Split-Brain Resolver Strategy

In order to use `LeaseMajorityOption` you will need to provide an instance of the option class of the `Lease` module you're going to use in the `LeaseMajorityOption.LeaseImplementation` property. 

- For [`Akka.Coordination.KubernetesApi`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/kubernetes/Akka.Coordination.KubernetesApi), this is an instance of `Akka.Coordination.KubernetesApi.KubernetesLeaseOption` class.

  ```csharp
  builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
  {
      var leaseOptions = new KubernetesLeaseOption();

      configurationBuilder
          .WithClustering(new ClusterOptions { 
              SplitBrainResolver = new LeaseMajorityOption{
                LeaseImplementation = leaseOptions,
              },
          })
          .WithKubernetesLease(leaseOptions);
  });
  ```

- For [`Akka.Coordination.Azure`](https://github.com/akkadotnet/Akka.Management/tree/dev/src/coordination/azure/Akka.Coordination.Azure) this is an instance of `Akka.Coordination.Azure.AzureLeaseOption` class.

  ```csharp
  builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
  {
      var leaseOptions = new AzureLeaseOption {
          ConnectionString = "<Your-Azure-Blob-storage-connection-string>",
          ContainerName = "<Your-Azure-Blob-storage-container-name>";
      };

      configurationBuilder
          .WithClustering(new ClusterOptions { 
              SplitBrainResolver = new LeaseMajorityOption{
                LeaseImplementation = leaseOptions,
              },
          })
          .WithAzureLease(leaseOptions);
  });
  ```

# Akka.Cluster.Sharding Extension Methods

## WithShardRegion Method

An extension method to set up [Cluster Sharding](https://getakka.net/articles/clustering/cluster-sharding.html). Starts a `ShardRegion` actor for the given entity `typeName` and registers the ShardRegion `IActorRef` with `TKey` in the `ActorRegistry` for this `ActorSystem`.

## Overloads
```csharp
public static AkkaConfigurationBuilder WithShardRegion<TKey>(
    this AkkaConfigurationBuilder builder, 
    string typeName, 
    Func<string, Props> entityPropsFactory, 
    IMessageExtractor messageExtractor, 
    ShardOptions shardOptions);
```

```csharp
public static AkkaConfigurationBuilder WithShardRegion<TKey>(
    this AkkaConfigurationBuilder builder,
    string typeName,
    Func<string, Props> entityPropsFactory, 
    ExtractEntityId extractEntityId, 
    ExtractShardId extractShardId,
    ShardOptions shardOptions);
```

```csharp
public static AkkaConfigurationBuilder WithShardRegion<TKey>(
    this AkkaConfigurationBuilder builder,
    string typeName,
    Func<ActorSystem, IActorRegistry, Func<string, Props>> compositePropsFactory, 
    IMessageExtractor messageExtractor, 
    ShardOptions shardOptions);
```

````csharp
public static AkkaConfigurationBuilder WithShardRegion<TKey>(
    this AkkaConfigurationBuilder builder,
    string typeName,
    Func<ActorSystem, IActorRegistry, Func<string, Props>> compositePropsFactory, 
    ExtractEntityId extractEntityId,
    ExtractShardId extractShardId, 
    ShardOptions shardOptions);
````
### Type Parameters
* `TKey`

  The type key to use to retrieve the `IActorRef` for this `ShardRegion` from the `ActorRegistry`.

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `typeName` __string__ 

  The name of the entity type

* `entityPropsFactory` __Func<string, Props>__

  Function that, given an entity id, returns the `Actor.Props` of the entity actors that will be created by the `Sharding.ShardRegion`

* `compositePropsFactory` __Func<ActorSystem, IActorRegistry, Func<string, Props>>__

  A delegate function that takes an `ActorSystem` and an `ActorRegistry` as parameters and returns a `Props` factory. Used when the `Props` factory either depends on another actor or needs to access the `ActorSystem` to set the `Props` up.

* `messageExtractor` __IMessageExtractor__

  An `IMessageExtractor` interface implementation to extract the entity id, shard id, and the message to send to the entity from the incoming message.

* `extractEntityId` __ExtractEntityId__

  Partial delegate function to extract the entity id and the message to send to the entity from the incoming message, if the partial function does not match the message will be `unhandled`, i.e.posted as `Unhandled` messages on the event stream

* `extractShardId` __ExtractShardId__

  Delegate function to determine the shard id for an incoming message, only messages that passed the `extractEntityId` will be used

* `shardOptions` __ShardOptions__

  The set of options for configuring `ClusterShardingSettings`

### Example
```csharp
public class EchoActor : ReceiveActor
{
    private readonly string _entityId;
    public EchoActor(string entityId)
    {
        _entityId = entityId;
        ReceiveAny(message => {
            Sender.Tell($"{Self} rcv {message}");
        });
    }
}

public class Program
{
    private const int NumberOfShards = 5;
    
    private static Option<(string, object)> ExtractEntityId(object message)
        => message switch {
            string id => (id, id),
            _ => Option<(string, object)>.None
        };

    private static string? ExtractShardId(object message)
        => message switch {
            string id => (id.GetHashCode() % NumberOfShards).ToString(),
            _ => null
        };
        
    private static Props PropsFactory(string entityId)
        => Props.Create(() => new EchoActor(entityId));
        
    public static void Main(params string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
        {
            configurationBuilder
                .WithRemoting(hostname: "localhost", port: 8110)
                .WithClustering(new ClusterOptions{SeedNodes = new []{ Address.Parse("akka.tcp://MyActorSystem@localhost:8110"), }})
                .WithShardRegion<Echo>(
                    typeName: "myRegion",
                    entityPropsFactory: PropsFactory, 
                    extractEntityId: ExtractEntityId,
                    extractShardId: ExtractShardId,
                    shardOptions: new ShardOptions());
        });

        var app = builder.Build();

        app.MapGet("/", async (context) =>
        {
            var echo = context.RequestServices.GetRequiredService<ActorRegistry>().Get<Echo>();
            var body = await echo.Ask<string>(
                    message: context.TraceIdentifier, 
                    cancellationToken: context.RequestAborted)
                .ConfigureAwait(false);
            await context.Response.WriteAsync(body);
        });

        app.Run();    
    }
}
```

### Using Lease With Cluster Sharding

To use the cluster sharding lease feature, you will need to pass in the lease option into the `shardOptions` parameter:

```csharp
var leaseOptions = new AzureLeaseOption {
    ConnectionString = "<Your-Azure-Blob-storage-connection-string>",
    ContainerName = "<Your-Azure-Blob-storage-container-name>";
};

configurationBuilder
    .WithRemoting(hostname: "localhost", port: 8110)
    .WithClustering(new ClusterOptions{SeedNodes = new []{ Address.Parse("akka.tcp://MyActorSystem@localhost:8110"), }})
    .WithShardRegion<Echo>(
        typeName: "myRegion",
        entityPropsFactory: PropsFactory, 
        extractEntityId: ExtractEntityId,
        extractShardId: ExtractShardId,
        shardOptions: new ShardOptions{
          LeaseImplementation = leaseOptions,
        })
    .WithAzureLease(leaseOptions);

```

## WithShardRegionProxy Method

An extension method to start a `ShardRegion` proxy actor that points to a `ShardRegion` hosted on a different role inside the cluster and registers the `IActorRef` with `TKey` in the `ActorRegistry` for this `ActorSystem`.

## Overloads

```csharp
public static AkkaConfigurationBuilder WithShardRegionProxy<TKey>(
    this AkkaConfigurationBuilder builder,
    string typeName, 
    string roleName, 
    ExtractEntityId extractEntityId, 
    ExtractShardId extractShardId);
```

```csharp
public static AkkaConfigurationBuilder WithShardRegionProxy<TKey>(
    this AkkaConfigurationBuilder builder,
    string typeName, 
    string roleName,
     IMessageExtractor messageExtractor);
```

### Type Parameters
* `TKey`

  The type key to use to retrieve the `IActorRef` for this `ShardRegion` from the `ActorRegistry`.

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `typeName` __string__

  The name of the entity type

* `roleName` __string__

  The role of the Akka.Cluster member that is hosting the target `ShardRegion`.

* `messageExtractor` __IMessageExtractor__

  An `IMessageExtractor` interface implementation to extract the entity id, shard id, and the message to send to the entity from the incoming message.

* `extractEntityId` __ExtractEntityId__

  Partial delegate function to extract the entity id and the message to send to the entity from the incoming message, if the partial function does not match the message will be `unhandled`, i.e.posted as `Unhandled` messages on the event stream

* `extractShardId` __ExtractShardId__

  Delegate function to determine the shard id for an incoming message, only messages that passed the `extractEntityId` will be used

# Akka.Cluster.Tools Extension Methods

## WithDistributedPubSub Method

An extension method to start [`Distributed Publish Subscribe`](https://getakka.net/articles/clustering/distributed-publish-subscribe.html) on this node immediately upon `ActorSystem` startup. Stores the pub-sub mediator `IActorRef` in the `ActorRegistry` using the `DistributedPubSub` key.

```csharp
public static AkkaConfigurationBuilder WithDistributedPubSub(
    this AkkaConfigurationBuilder builder,
    string role);
```

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `role` __string__

  Specifies which role `DistributedPubSub` will broadcast gossip to. If this value is left blank then ALL roles will be targeted.

## WithSingleton Method

An extension method to start [Cluster Singleton](https://getakka.net/articles/clustering/cluster-singleton.html). Creates a new [Singleton Manager](https://getakka.net/articles/clustering/cluster-singleton.html#singleton-manager) to host an actor created via `actorProps`.

If `createProxyToo` is set to _true_ then this method will also create a `ClusterSingletonProxy` that will be added to the `ActorRegistry` using the key `TKey`. Otherwise this method will register nothing with the `ActorRegistry`.

```csharp
public static AkkaConfigurationBuilder WithSingleton<TKey>(
    this AkkaConfigurationBuilder builder,
    string singletonName, 
    Props actorProps, 
    ClusterSingletonOptions options = null, 
    bool createProxyToo = true);
```

### Type Parameters
* `TKey`

  The key type to use for the `ActorRegistry` when `createProxyToo` is set to _true_.

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `singletonName` __string__

The name of this singleton instance. Will also be used in the `ActorPath` for the `ClusterSingletonManager` and optionally, the `ClusterSingletonProxy` created by this method.

* `actorProps` __Props__

The underlying actor type. __SHOULD NOT BE CREATED USING `ClusterSingletonManager.Props`__

* `options` __ClusterSingletonOptions__

Optional. The set of options for configuring both the `ClusterSingletonManager` and optionally, the `ClusterSingletonProxy`.

* `createProxyToo` __bool__

When set to _true_, creates a `ClusterSingletonProxy` that automatically points to the `ClusterSingletonManager` created by this method.

### Using Lease With Cluster Singleton

To use the cluster singleton lease feature, you will need to pass in the lease option into the `options` parameter:

```csharp
Props propsFactory(ActorSystem system, IActorRegistry registry, IDependencyResolver resolver)
  => Props.Create(() => new EchoActor());

var leaseOptions = new AzureLeaseOption {
    ConnectionString = "<Your-Azure-Blob-storage-connection-string>",
    ContainerName = "<Your-Azure-Blob-storage-container-name>";
};

configurationBuilder
    .WithRemoting()
    .WithClustering()
    .WithSingleton<Echo>(
        singletonName: "singleton",
        propsFactory: propsFactory, 
        options: new ClusterSingletonOptions {
          LeaseImplementation = leaseOptions,
        })
    .WithAzureLease(leaseOptions);

```

## WithSingletonProxy Method

An extension method to create a [Cluster Singleton Proxy](https://getakka.net/articles/clustering/cluster-singleton.html#singleton-proxy) and adds it to the `ActorRegistry` using the given `TKey`.

```csharp
public static AkkaConfigurationBuilder WithSingletonProxy<TKey>(
    this AkkaConfigurationBuilder builder,
    string singletonName, 
    ClusterSingletonOptions options = null, 
    string singletonManagerPath = null);
```

### Type Parameters
* `TKey`

  The key type to use for the `ActorRegistry`.

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `singletonName` __string__

  The name of this singleton instance. Will also be used in the `ActorPath` for the `ClusterSingletonManager` and optionally, the `ClusterSingletonProxy` created by this method.

* `options` __ClusterSingletonOptions__

  Optional. The set of options for configuring the `ClusterSingletonProxy`.

* `singletonManagerPath` __string__

  Optional. By default Akka.Hosting will assume the `ClusterSingletonManager` is hosted at "/user/{singletonName}" - but if for some reason the path is different you can use this property to override that value.

## WithClusterClientReceptionist Method

Configures a [Cluster Client](https://getakka.net/articles/clustering/cluster-client.html) `ClusterClientReceptionist` for the `ActorSystem`

```csharp
public static AkkaConfigurationBuilder WithClusterClientReceptionist(
    this AkkaConfigurationBuilder builder,
    string name = "receptionist",
    string role = null);
```

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `name` __string__

Actor name of the ClusterReceptionist actor under the system path, by default it is "/system/receptionist"

* `role` __string__

Checks that the receptionist only start on members tagged with this role. All members are used if set to _null_.

## WithClusterClient Method

Creates a [Cluster Client](https://getakka.net/articles/clustering/cluster-client.html) and adds it to the `ActorRegistry` using the given `TKey`.

## Overloads

```csharp
public static AkkaConfigurationBuilder WithClusterClient<TKey>(
    this AkkaConfigurationBuilder builder,
    IList<ActorPath> initialContacts);
```

```csharp
public static AkkaConfigurationBuilder WithClusterClient<TKey>(
    this AkkaConfigurationBuilder builder,
    IEnumerable<Address> initialContactAddresses,
    string receptionistActorName = "receptionist");
```

```csharp
public static AkkaConfigurationBuilder WithClusterClient<TKey>(
    this AkkaConfigurationBuilder builder,
    IEnumerable<string> initialContacts);
```

### Parameters

* `builder` __AkkaConfigurationBuilder__

  The builder instance being configured.

* `initialContacts` __IList<ActorPath>__, __IEnumerable<string>__

  List of `ClusterClientReceptionist` actor path in `ActorPath` or `string` form that will be used as a seed to discover all of the receptionists in the cluster.

* `initialContactAddresses` __IEnumerable<Address>__

  List of node addresses where the `ClusterClientReceptionist` are located that will be used as seed  to discover all of the receptionists in the cluster.

* `receptionistActorName` __string__

  The name of the `ClusterClientReceptionist` actor. Defaults to "receptionist"