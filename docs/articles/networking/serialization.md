---
uid: serialization
title: Serialization
---

# Serialization

One of the core concepts of any actor system like Akka.NET is the notion of message passing between actors.
Since Akka.NET is network transparent, these actors can be located locally or remotely. As such the system needs a common
exchange format to package messages into so that it can send them to receiving actors. 
In Akka.NET, messages are plain objects and thus easily converted to a byte array. 
The process of converting objects into byte arrays is known as serialization.

Akka.NET itself uses `Protocol Buffers` to serialize internal messages (i.e. cluster gossip messages). 
However, the serialization mechanism in Akka.NET allows you to write custom serializers and to define which serializer to use for what.
As shown in the examples further down this page, these serializers can be mixed and matched depending on preference or need.

There are many other uses for serialization other than messaging. 
It's possible to use these serializers for ad-hoc purposes as well.

## Usage

### Configuration
For Akka.NET to know which `Serializer` to use when (de-)serializing objects, two sections need to be defined in the application's configuration. 
The `akka.actor.serializers` section is where names are associated to implementations of the `Serializer` to use.

```hocon
akka {
  actor {
     serializers {
        json = "Akka.Serialization.NewtonSoftJsonSerializer"
        bytes = "Akka.Serialization.ByteArraySerializer"
     }
  }
}
```

The `akka.actor.serialization-bindings` section is where object types are associated to a `Serializer` by the names defined in the previous section.

```hocon
akka {
  actor {
     serializers {
        json = "Akka.Serialization.NewtonSoftJsonSerializer"
        bytes = "Akka.Serialization.ByteArraySerializer"
        myown = "MySampleProject.MySerializer, MyAssembly"
     }

    serialization-bindings {
      "System.Byte[]" = bytes
      "System.Object" = json
      "MySampleProject.MyOwnSerializable, MyAssembly" = myown
    }
  }
}
```

In case of ambiguity, such as a message implements several of the configured classes, the most specific configured class will be used, i.e. the one of which all other candidates are superclasses. 
If this condition cannot be met, because e.g. `ISerializable` and `MyOwnSerializable` both apply and neither is a
subtype of the other, a warning will be issued.

Akka.NET provides serializers for POCO's (Plain Old C# Objects) and for `Google.Protobuf.IMessage` by default, so you don't usually need to add configuration for that.

### Verification
Normally, messages sent between local actors (i.e. same CLR) do not undergo serialization.
For testing, it may be desirable to force serialization on all messages, both remote and local. 
If you want to do this to verify that your messages are serializable, you can enable the following config option:

```hocon
akka {
  actor {
    serialize-messages = on
  }
}
```
If you want to verify that your `Props` are serializable, you can enable the following config option:

```hocon
akka {
  actor {
    serialize-creators = on
  }
}`
```

> [!WARNING]
> We recommend having these config options turned on only when you're running tests. 
Turning these options on in production is pointless, as it would negatively impact the performance of local message passing without giving any gain.

### Programmatic
As mentioned previously, Akka.NET uses serialization for message passing.
However the system is much more robust than that. 
To programmatically (de-)serialize objects using Akka.NET serialization, a reference to the main serialization class is all that is needed.

```csharp
using Akka.Actor;
using Akka.Serialization;
ActorSystem system = ActorSystem.Create("example");

// Get the Serialization Extension
Serialization serialization = system.Serialization;

// Have something to serialize
string original = "woohoo";

// Find the Serializer for it
Serializer serializer = serialization.FindSerializerFor(original);

// Turn it into bytes
byte[] bytes = serializer.ToBinary(original);

// Turn it back into an object,
// the nulls are for the class manifest and for the classloader
string back = (string)serializer.FromBinary(bytes, original.GetType());

// Voilá!
Assert.AreEqual(original, back);
```

## Customization
Akka.NET makes it extremely easy to create custom serializers to handle a wide variety of scenarios. 
All serializers in Akka.NET inherit from `Akka.Serialization.Serializer`. 
So, to create a custom serializer, all that is needed is a class that inherits from this base class.

### Creating new Serializers
A custom `Serializer` has to inherit from `Akka.Serialization.Serializer` and can be defined like this:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/Serialization/CreateCustomSerializer.cs?name=CustomSerialization)]

The only thing left to do for this class would be to fill in the serialization logic in the ``ToBinary(object)`` method and the deserialization logic in the ``FromBinary(byte[], Type)``. 
Afterwards the configuration would need to be updated to reflect which name to bind to and the classes that use this
serializer.

### Serializer with String Manifest
The `Serializer` illustrated above supports a class-based manifest (type hint). 
For serialization of data that need to evolve over time, the `SerializerWithStringManifest` is recommended instead of `Serializer` because the manifest (type hint) is a `String` instead of a `Type`. 
This means that the class can be moved/removed and the serializer can still deserialize old data by matching on the String. 
This is especially useful for `Persistence`.

The manifest string can also encode a version number that can be used in `FromBinary` to deserialize in different ways to migrate old data to new domain objects.

If the data was originally serialized with `Serializer`, and in a later version of the system you change to `SerializerWithStringManifest`, the manifest string will be the full class name if you used `IncludeManifest=true`, otherwise it will be the empty string.

This is how a `SerializerWithStringManifest` looks:
[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/Serialization/MyOwnSerializer2.cs?name=CustomSerialization)]

You must also bind it to a name in your `Configuration` and then list which classes that should be serialized using it.

It's recommended to throw `SerializationException` in `FromBinary` if the manifest is unknown. This makes it possible to introduce new message types and send them to nodes that don't know about them. This is typically needed when performing rolling upgrades, i.e. running a cluster with mixed versions for while. `SerializationException` is treated as a transient problem in the TCP based remoting layer. The problem will be logged and message is dropped. Other exceptions will tear down the TCP connection because it can be an indication of corrupt bytes from the underlying transport.

### Serializing ActorRefs
All actors are serializable using the default protobuf serializer, but in cases where custom serializers are used, we need to know how to (de-)serialize them properly. 
In the general case, the local address to be used depends on the type of remote address which shall be the recipient of the serialized
information.
Use `Serialization.SerializedActorPath(actorRef)` like this:

```csharp
using Akka.Actor;
using Akka.Serialization;
// Serialize
// (beneath toBinary)
string id = Serialization.SerializedActorPath(theActorRef);

// Then just serialize the identifier however you like

// Deserialize
// (beneath fromBinary)
IActorRef deserializedActorRef = extendedSystem.Provider.ResolveActorRef(id);
// Then just use the IActorRef
```

This assumes that serialization happens in the context of sending a message through the remote transport. 
There are other uses of serialization, though, e.g. storing actor references outside of an actor application (database, etc.).
In this case, it is important to keep in mind that the address part of an actor's path determines how that actor is communicated with. Storing a local actor path might be the right choice if the retrieval happens in the same logical context, but it is not enough when deserializing it on a different network host: for that it would need to include the system's remote transport address.
An actor system is not limited to having just one remote transport per se, which makes this question a bit more interesting. 
To find out the appropriate address to use when sending to `remoteAddr` you can use `IActorRefProvider.GetExternalAddressFor(remoteAddr)` like this:

```csharp
public class ExternalAddress : ExtensionIdProvider<ExternalAddressExtension>
{
    public override ExternalAddressExtension CreateExtension(ExtendedActorSystem system) =>
        new ExternalAddressExtension(system);
}

public class ExternalAddressExtension : IExtension
{
    private readonly ExtendedActorSystem _system;

     public ExternalAddressExtension(ExtendedActorSystem system)
     {
        _system = system;
     }

    public Address AddressFor(Address remoteAddr)
    {
        return _system.Provider.GetExternalAddressFor(remoteAddr) 
             ?? throw new InvalidOperationException($"cannot send to {remoteAddr}");
    }
}

public class Test
{
    private ExtendedActorSystem ExtendedSystem =>
        ActorSystem.Create("test").AsInstanceOf<ExtendedActorSystem>();

    public string SerializeTo(IActorRef actorRef, Address remote)
    {
        return actorRef.Path.ToSerializationFormatWithAddress(
            new ExternalAddress().Get(ExtendedSystem).AddressFor(remote));
    }
}

```

> [!NOTE]
> `ActorPath.ToSerializationFormatWithAddress` differs from `ToString` if the address does not already have `host` and `port` components, i.e. it only inserts address information for local addresses.
> `ToSerializationFormatWithAddress` also adds the unique id of the actor, which will change when the actor is stopped and then created again with the same name.
Sending messages to a reference pointing the old actor will not be delivered to the new actor. If you do not want this behavior, e.g. in case of long term storage of the reference, you can use `ToStringWithAddress`, which does not include the unique id.

This requires that you know at least which type of address will be supported by the system which will deserialize the resulting actor reference; if you have no concrete address handy you can create a dummy one for the right protocol using `new Address(protocol, "", "", 0)` (assuming that the actual transport used is as lenient as Akka's `RemoteActorRefProvider`).

### Deep serialization of Actors
The recommended approach to do deep serialization of internal actor state is to use [Akka Persistence](xref:persistence-architecture).

## How to setup Hyperion as default serializer

Starting from Akka.NET v1.5, default Newtonsoft.Json serializer will be replaced in the favor of [Hyperion](https://github.com/akkadotnet/Hyperion). This change may break compatibility with older actors still using json serializer for remoting or persistence. If it's possible, it's advised to migrate to it already. To do so, first you need to reference hyperion serializer as NuGet package inside your project:

    Install-Package Akka.Serialization.Hyperion -pre

Then bind `hyperion` serializer using following HOCON configuration in your actor system settings:

```
akka {
  actor {
    serializers {
      hyperion = "Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion"
    }
    serialization-bindings {
      "System.Object" = hyperion
    }
  }
}
```
