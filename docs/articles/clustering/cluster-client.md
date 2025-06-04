---
uid: cluster-client
title: Cluster Client
---
# Cluster Client

An actor system that is not part of the cluster can communicate with actors somewhere in the cluster via this `ClusterClient`. The client can of course be part of another cluster. It only needs to know the location of one (or more) nodes to use as initial contact points. It will establish a connection to a ClusterReceptionist somewhere in the cluster. It will monitor the connection to the receptionist and establish a new connection if the link goes down. When looking for a new receptionist it uses fresh contact points retrieved from previous establishment, or periodically refreshed contacts, i.e. not necessarily the initial contact points.

> [!NOTE]
> `ClusterClient` should not be used when sending messages to actors that run within the same cluster. Similar functionality as the `ClusterClient` is provided in a more efficient way by Distributed Publish Subscribe in Cluster for actors that belong to the same cluster.

Also, note it's necessary to change akka.actor.provider from `Akka.Actor.LocalActorRefProvider` to `Akka.Remote.RemoteActorRefProvider` or `Akka.Cluster.ClusterActorRefProvider` when using the cluster client.

```hocon
  akka.actor.provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
```

  or this shorthand notation

```hocon
  akka.actor.provider = cluster
```

The receptionist is supposed to be started on all nodes, or all nodes with specified role, in the cluster. The receptionist can be started with the `ClusterClientReceptionist` extension or as an ordinary actor.

You can send messages via the `ClusterClient` to any actor in the cluster that is registered in the `DistributedPubSubMediator` used by the `ClusterReceptionist`. The `ClusterClientReceptionist` provides methods for registration of actors that should be reachable from the client. Messages are wrapped in `ClusterClient.Send`, `ClusterClient.SendToAll` or `ClusterClient.Publish`.

Both the `ClusterClient` and the `ClusterClientReceptionist` emit events that can be subscribed to. The `ClusterClient` sends out notifications in relation to having received a list of contact points from the `ClusterClientReceptionist`. One use of this list might be for the client to record its contact points. A client that is restarted could then use this information to supersede any previously configured contact points.

The `ClusterClientReceptionist` sends out notifications in relation to having received contact from a `ClusterClient`. This notification enables the server containing the receptionist to become aware of what clients are connected.

1. `ClusterClient.Send`: The message will be delivered to one recipient with a matching path, if any such exists. If several entries match the path the message will be delivered to one random destination. The `Sender` of the message can specify that local affinity is preferred, i.e. the message is sent to an actor in the same local actor system as the used receptionist actor, if any such exists, otherwise random to any other matching entry.
1. `ClusterClient.SendToAll`: The message will be delivered to all recipients with a matching path.
1. `ClusterClient.Publish`: The message will be delivered to all recipients Actors that have been registered as subscribers to the named topic.

Response messages from the destination actor are tunneled via the receptionist to avoid inbound connections from other cluster nodes to the client, i.e. the `Sender`, as seen by the destination actor, is not the client itself. The `Sender` of the response messages, as seen by the client, is deadLetters since the client should normally send subsequent messages via the `ClusterClient`. It is possible to pass the original sender inside the reply messages if the client is supposed to communicate directly to the actor in the cluster.

While establishing a connection to a receptionist the `ClusterClient` will buffer messages and send them when the connection is established. If the buffer is full the `ClusterClient` will drop old messages when new messages are sent via the client. The size of the buffer is configurable and it can be disabled by using a buffer size of 0.

It's worth noting that messages can always be lost because of the distributed nature of these actors. As always, additional logic should be implemented in the destination (acknowledgement) and in the client (retry) actors to ensure at-least-once message delivery.

## An Example

On the cluster nodes first start the receptionist. Note, it is recommended to load the extension when the actor system is started by defining it in the akka.extensions configuration property:

```hocon
akka.extensions = ["Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools"]
```

Next, register the actors that should be available for the client.

```csharp
RunOn(() =>
{
    var serviceA = Sys.ActorOf(Props.Create<Service>(), "serviceA");
    ClusterClientReceptionist.Get(Sys).RegisterService(serviceA);
}, host1);

RunOn(() =>
{
    var serviceB = Sys.ActorOf(Props.Create<Service>(), "serviceB");
    ClusterClientReceptionist.Get(Sys).RegisterService(serviceB);
}, host2, host3);
```

On the client you create the `ClusterClient` actor and use it as a gateway for sending messages to the actors identified by their path (without address information) somewhere in the cluster.

```csharp
RunOn(() =>
{
    var c = Sys.ActorOf(Client.ClusterClient.Props(
        ClusterClientSettings.Create(Sys).WithInitialContacts(initialContacts)), "client");
    c.Tell(new Client.ClusterClient.Send("/user/serviceA", "hello", localAffinity: true));
    c.Tell(new Client.ClusterClient.SendToAll("/user/serviceB", "hi"));
}, client);
```

The `initialContacts` parameter is a `IEnumerable<ActorPath>`, which can be created like this:

```csharp
var initialContacts = new List<ActorPath>()
{
    ActorPath.Parse("akka.tcp://OtherSys@host1:2552/system/receptionist"),
    ActorPath.Parse("akka.tcp://OtherSys@host2:2552/system/receptionist")
};

var settings = ClusterClientSettings.Create(Sys).WithInitialContacts(initialContacts);
```

You will probably define the address information of the initial contact points in configuration or system property. See also Configuration.

## ClusterClientReceptionist Extension

In the example above the receptionist is started and accessed with the `Akka.Cluster.Tools.Client.ClusterClientReceptionist` extension. That is convenient and perfectly fine in most cases, but it can be good to know that it is possible to start the `akka.cluster.client.ClusterReceptionist` actor as an ordinary actor and you can have several different receptionists at the same time, serving different types of clients.

Note that the `ClusterClientReceptionist` uses the `DistributedPubSub` extension, which is described in Distributed Publish Subscribe in Cluster.

It is recommended to load the extension when the actor system is started by defining it in the akka.extensions configuration property:

```hocon
akka.extensions = ["Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools"]
```

## Events

As mentioned earlier, both the `ClusterClient` and `ClusterClientReceptionist` emit events that can be subscribed to. The following code snippet declares an actor that will receive notifications on contact points (addresses to the available receptionists), as they become available. The code illustrates subscribing to the events and receiving the `ClusterClient` initial state.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/ClusterClient/ClientListener.cs?name=ClusterClient)]

Similarly we can have an actor that behaves in a similar fashion for learning what cluster clients contact a `ClusterClientReceptionist`:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/ClusterClient/ReceptionistListener.cs?name=ReceptionistListener)]

## Configuration

The `ClusterClientReceptionist` extension (or `ClusterReceptionistSettings`) can be configured with the following properties:

[!code-json[ConfigReference](../../../src/contrib/cluster/Akka.Cluster.Tools/Client/reference.conf)]

The 'akka.cluster.client' configuration properties are read by the `ClusterClientSettings` when created with a `ActorSystem` parameter. It is also possible to amend the `ClusterClientSettings` or create it from another config section with the same layout in the reference config. `ClusterClientSettings` is a parameter to the `ClusterClient.Props()` factory method, i.e. each client can be configured with different settings if needed.

## Failure Handling

When the cluster client is started it must be provided with a list of initial contacts which are cluster nodes where receptionists are running. It will then repeatedly (with an interval configurable by `establishing-get-contacts-interval`) try to contact those until it gets in contact with one of them. While running, the list of contacts are continuously updated with data from the receptionists (again, with an interval configurable with `refresh-contacts-interval`), so that if there are more receptionists in the cluster than the initial contacts provided to the client, the client will learn about them.

While the client is running it will detect failures in its connection to the receptionist by heartbeats if more than a configurable amount of heartbeats is missed the client will try to reconnect to its known set of contacts to find a receptionist it can access.

## When the Cluster Cannot Be Reached at All

### Watching ClusterClient Actor Termination

It is possible to make the cluster client stop entirely if it cannot find a receptionist it can talk to within a configurable interval. This is configured with the `reconnect-timeout`, which defaults to off. This can be useful when initial contacts are provided from some kind of service registry, cluster node addresses are entirely dynamic and the entire cluster might shut down or crash, be restarted on new addresses. Since the client will be stopped in that case a monitoring actor can watch it and upon `Terminate` a new set of initial contacts can be fetched and a new cluster client started.

# Contact Auto-Discovery Using Akka.Discovery

> [!NOTE]
> This feature can only be used with:
>
> * Akka.Management v1.5.27 or later.
> * Akka.Cluster.Hosting v1.5.27 or later
> * Akka.Cluster.Tools v1.5.27 or later

This feature is added in Akka.NET 1.5.27. Instead of watching for actor termination manually, you can leverage [Akka.Discovery](../discovery/index.md) to discover cluster client contact points inside a dynamic environment such as [Kubernetes](https://github.com/akkadotnet/Akka.Management/blob/dev/docs/articles/discovery/kubernetes.md), [AWS](https://github.com/akkadotnet/Akka.Management/blob/dev/docs/articles/discovery/aws.md), or anywhere else with [Azure Table](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/azure/Akka.Discovery.Azure/README.md)

## Contact Auto-Discovery Setup Using Akka.Hosting

Cluster client discovery API has been added in `Akka.Cluster.Hosting` v1.5.27. You can use the `.WithClusterClientDiscovery()` extension method to use the cluster client initial contact auto discovery feature.

### Example: Setting Up Contact Auto-Discovery With Akka.Discovery.KubernetesApi

On your cluster client node side, these are the code you'll need to implement:

```csharp
services.AddAkka("ClusterClientSys", (builder, provider) => {
  builder
    // This code sets up ClusterClient that works using Akka.Discovery
    .WithClusterClientDiscovery<MyClusterClientActorKey>(options => {
      // This is the Discovery plugin that will be used with ClusterClientDiscovery.
      options.DiscoveryOptions = new KubernetesDiscoveryOptions {
        // IMPORTANT:
        // This signals Akka.Hosting that this plugin **should not** be used for ClusterBootstrap
        IsDefaultPlugin = false,
      
        // IMPORTANT:
        // The ConfigPath property has to be different than the default discovery ConfigPath.
        // The actual name does not matter, but it has to be different than the default name "kubernetes-api"
        ConfigPath = "kubernetes-api-cluster-client",
      
        // IMPORTANT:
        // The PodLabelSelector property has to be different than the default k8s discovery 
        // PodLabelSelector, which defaults to "app={0}". The "{0}" is important because 
        // it will be used inside a String.Format()
        PodLabelSelector = "discovery={0}";
      };
      
      // This has to match the Kubernetes metadata label that we'll set in YAML
      options.ServiceName = "initial-contact";

      // his has to match the Kubernetes port name for the Akka.Management port
      options.PortName = "management";
    });
```

On the YAML side, you will need to change the Receptionist YAML and add a new metadata label to tag the pods:

```yaml
spec:
  template:
    metadata:
      labels:
        # Note that "discovery" matches the `PodLabelSelector` in `.WithClusterClientDiscovery()`
        # Note that "initial-contact" matches the `ServiceName` in `.WithClusterClientDiscovery()`
        discovery: initial-contact 
```

If you're not using ClusterBootstrap on the Receptionist side, you have to start Akka.Management. Skip this step if you're using ClusterBootstrap:

```csharp
services.AddAkka("ReceptionistSys", (builder, provider) => {
  builder.AddStartup(async (system, registry) => {
    await AkkaManagement.Get(system).Start();
  });
});
```

### Example: Setting Up Contact Auto-Discovery With Akka.Discovery.Azure

On your cluster client node side, these are the code you'll need to implement:

```csharp
services.AddAkka("ClusterClientSys", (builder, provider) => {
  builder
    // This code sets up ClusterClient that works using Akka.Discovery
    .WithClusterClientDiscovery<MyClusterClientActorKey>(options => {
      // This is the Discovery plugin that will be used with ClusterClientDiscovery.
      options.DiscoveryOptions = new AkkaDiscoveryOptions {
        // IMPORTANT:
        // This signals Akka.Hosting that this plugin **should not** be used for ClusterBootstrap
        IsDefaultPlugin = false,
      
        // IMPORTANT:
        // The ConfigPath property has to be different than the default discovery ConfigPath.
        // The actual name does not matter, but it has to be different than the default name "azure"
        ConfigPath = "azure-cluster-client",
        
        // IMPORTANT:
        // This discovery plugin **should not** participate in updating the Azure cluster member table
        ReadOnly = true,
        
        // IMPORTANT:
        // All service names for cluster client discovery should be the same.
        // If you're also using ClusterBootstrap, make sure that this name does not collide.  
        ServiceName = "cluster-client",
        
        // IMPORTANT:
        // All table names for cluster client discovery should be the same.
        // If you're also using ClusterBootstrap, make sure that this table name does not collide.  
        TableName = "akkaclusterreceptionists",
      };
      
      // This has to match the name we set inside the discovery options
      options.ServiceName = "cluster-client";
    })
      
    // If you're not using ClusterBootstrap in the cluster client side, you will need to add
    // these code
    .AddStartup(async (system, registry) => {
      await AkkaManagement.Get(system).Start();
    });
```

On the cluster client receptionist side, you will need to implement these code:

```csharp
services.AddAkka("ReceptionistSys", (builder, provider) => {
    
  builder
    // This is the Discovery plugin that will be used with ClusterClientDiscovery.
    .WithAzureDiscovery(options => {
      // IMPORTANT:
      // This signals Akka.Hosting that this plugin **should not** be used for ClusterBootstrap
      options.IsDefaultPlugin = false,
      
      // IMPORTANT:
      // The ConfigPath property has to be different than the default discovery ConfigPath.
      // The actual name does not matter, but it has to be different than the default name "azure"
      options.ConfigPath = "azure-cluster-client",
        
      // IMPORTANT:
      // All service names for cluster client discovery should be the same.
      // If you're also using ClusterBootstrap, make sure that this name does not collide.  
      options.ServiceName = "cluster-client",
        
      // IMPORTANT:
      // All table names for cluster client discovery should be the same.
      // If you're also using ClusterBootstrap, make sure that this table name does not collide.  
      options.TableName = "akkaclusterreceptionists",
    }

    // If you're not using ClusterBootstrap in the cluster client side, you will need to add
    // these code
    .AddStartup(async (system, registry) => {
      await AkkaManagement.Get(system).Start();
    });
});

```

## Contact Auto-Discovery Setup Using Hocon Configuration

The HOCON configuration to set these are:

```text
akka.cluster.client
{
  use-initial-contacts-discovery = false

  discovery
  {
    method = <method>
    actor-system-name = null
    receptionist-name = receptionist
    service-name = null
    port-name = null
    discovery-retry-interval = 1s
    discovery-timeout = 60s
  }
}
```

To enable contact auto-discovery, you will need to:

* Set `akka.cluster.client.use-initial-contacts-discovery` to true.
* Set `akka.cluster.client.discovery.service-name` that matches the service name of the Akka.Discovery extension that you used:
  * For [Akka.Discovery.KubernetesApi](https://github.com/akkadotnet/Akka.Management/blob/dev/docs/articles/discovery/kubernetes.md), this is the `pod-label-selector` HOCON setting or the `KubernetesDiscoveryOptions.PodLabelSelector` options property.
  * For [Akka.Discovery.AwsApi](https://github.com/akkadotnet/Akka.Management/blob/dev/docs/articles/discovery/aws.md), this is
    * **EC2**: the `akka.discovery.aws-api-ec2-tag-based.tag-key` HOCON setting or the Akka.Hosting `Ec2ServiceDiscoveryOptions.TagKey` options property.
    * **ECS**: the `akka.discovery.aws-api-ecs.tags` HOCON setting or the Akka.Hosting `EcsServiceDiscoveryOptions.Tags` options property.
  * For [Akka.Discovery.Azure](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/azure/Akka.Discovery.Azure/README.md), this is the `service-name` HOCON setting or the `AkkaDiscoveryOptions.ServiceName` options property.
* Set `akka.cluster.client.discovery.method` to a valid discovery method name listed under `akka.discovery`.
* Set `akka.cluster.client.discovery.actor-system-name` to the target cluster ActorSystem name.
* **OPTIONAL**. Set `akka.cluster.client.discovery,port-name` if the discovery extension that you're using depends on port names.
* **OPTIONAL**. Set `akka.cluster.client.discovery.receptionist-name` if you're using a non-default receptionist name.

## Using Akka.Discovery For Both Akka.Cluster.Tools.Client And Akka.Management.Cluster.Bootstrap

If you need to use Akka.Discovery with both ClusterClient AND ClusterBootstrap, you will have to **make sure** that you have **TWO** different Akka.Discovery settings living side-by-side under the `akka.discovery` HOCON setting section.

### Akka.Discovery.KubernetesApi Example

In your YAML file:

* Make sure that you tag the instances that will run the cluster client receptionists with an extra tag. If your ClusterBootstrap is tagged with the YAML value `metadata.labels.app: cluster`, then you will need to add another tag to the instances that runs the Receptionists, e.g. `metadata.labels.contact: cluster-client` like so:

  ```yaml
  metadata:
    labels:
      app: cluster
      contact: cluster-client
  ```

* Make sure you name the Akka.Management port

  ```yaml
  spec:
    template:
      spec:
        containers:
          ports:
          - containerPort: 8558 # This is the remoting port, change this to match yours
            protocol: TCP
            name: management # this is important
  ```

In your cluster client Akka.NET node HOCON settings:

* Copy the `akka.discovery.kubernetes-api` HOCON section and paste it above or under the original settings. You can also copy the value from [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi/reference.conf)
* Rename the HOCON section to `akka.discovery.kubernetes-api-cluster-client`. The key name does not matter, what matters is that the name does not collide with any other setting section name under `akka.discovery`.
* Make sure you change `akka.discovery.kubernetes-api-cluster-client.pod-label-selector` to "contact={0}" to match what we have in the YAML file.
* Make sure you change `akka.cluster.client.discovery.service-name` to "cluster-client" to match what we have in the YAML file.
* Make sure you change `akka.cluster.client.discovery.port-name` value to "management" to match what we have in the YAML file.
* Keep the `akka.discovery.method` HOCON value to "kubernetes-api", this is the discovery extension that will be used by ClusterBootstrap.
* Change the `akka.cluster.client.discovery.method` value from "\<method>" to "kubernetes-api-cluster-client", this is the discovery extension that will be used by ClusterClient. If not set, this will default to the value set in `akka.discovery.method`, which is **NOT** what we want.

### Akka.Discovery.Azure Example

In your cluster receptionist Akka.NET node HOCON settings:

* Copy the `akka.discovery.azure` HOCON section and paste it above or under the original settings. You can also copy the value from [here](https://github.com/akkadotnet/Akka.Management/blob/dev/src/discovery/azure/Akka.Discovery.Azure/reference.conf)
* Rename the HOCON section to `akka.discovery.azure-cluster-client`. The key name does not matter, what matters is that the name does not collide with any other setting section name under `akka.discovery`.
* Change `akka.discovery.azure-cluster-client.public-port` to the management port of the Akka.NET node.
* Change `akka.discovery.azure-cluster-client.service-name` to "cluster-client". The name does not matter, what matters is that this name **HAS** to match the service name we'll be using in `akka.cluster.client.discovery.service-name`.
* **[OPTIONAL]** change `akka.discovery.azure-cluster-client.table-name` to `akkaclusterreceptionists` to separate the discovery table from ClusterBootstrap entries.
* Make sure that you start the discovery extension in the receptionist side. This needs to be done because the extension is responsible for updating the Azure table.

  ```csharp
  Discovery.Get(myActorSystem).LoadServiceDiscovery("azure-cluster-client");
  ```

In your cluster client Akka.NET node HOCON settings:

* If you're **USING** ClusterBootstrap in the cluster client side:
  * You **WILL NEED** to perform the same HOCON configuration cloning process above.
  * Make sure you change `akka.cluster.client.discovery.service-name` to "cluster-client" to match what we have in the receptionist node HOCON file.
  * You **WILL NEED** to keep the `akka.discovery.method` HOCON value to "azure", this is the discovery extension that will be used by ClusterBootstrap.
  * Change the `akka.cluster.client.discovery.method` value from "\<method>" to "azure-cluster-client", this is the discovery extension that will be used by ClusterClient. If not set, this will default to the value set in `akka.discovery.method`, which is **NOT** what we want.
  * **[OPTIONAL]** **IF** you opt to change the table name in the receptionist side, you **WILL NEED** to change `akka.discovery.azure-cluster-client.table-name` to match the table name with the receptionist side, it was `akkaclusterreceptionists` in our example above
* If you're **NOT USING** ClusterBootstrap in the cluster client side:
  * You **DO NOT NEED** to perform the HOCON configuration cloning process as you're not using ClusterBootstrap.
  * Make sure you change `akka.cluster.client.discovery.service-name` to "cluster-client" to match what we have in the receptionist node HOCON file.
  * You can keep the `akka.discovery.method` HOCON value to "azure".
  * You can change the `akka.cluster.client.discovery.method` value from "\<method>" to "azure", this is the discovery extension that will be used by ClusterClient. If not set, this will default to the value set in `akka.discovery.method`, which will be the same.
  * **[OPTIONAL]** **IF** you opt to change the table name in the receptionist side, you **WILL NEED** to change `akka.discovery.azure-cluster-client.table-name` to match the table name with the receptionist side, it was `akkaclusterreceptionists` in our example above.
