---
uid: serialization
title: Serialization
---

# Serialization

One of the core concepts of any actor system like Akka.NET is the notion of
message passing between actors. Since Akka.NET is network transparent, these
actors can be located locally or remotely. As such the system needs a common
exchange format to package messages into so that it can send them to receiving
actors. In Akka.NET, messages are plain objects and thus easily converted to a
byte array. The process of converting objects into byte arrays is known as
serialization.

Akka.NET comes with several built-in serializers. However cases will arise that
require the need for other serializing options. The framework provides for
custom serializers to be written. As shown in the examples further down this
page, these serializers can be mixed and matched depending on preference
or need.

There are many other uses for serialization other than messaging. It's possible
to use these serializers for ad-hoc purposes as well.

## Usage

### Configuration
For Akka.NET to know which `Serializer` to use when (de-)serializing objects,
two sections need to be defined in the application's configuration. The
"akka.actor.serializers" section is where names are associated to
implementations of the `Serializer` to use.

```hocon
akka {
  actor {
     serializers {
        json = "Akka.Serialization.NewtonSoftJsonSerializer"
        java = "Akka.Serialization.JavaSerializer" # not used, reserves serializer identifier
        bytes = "Akka.Serialization.ByteArraySerializer"
     }
  }
}
```

The "akka.actor.serialization-bindings" section is where object types are
associated to a `Serializer` by the names defined in the previous section.

```hocon
akka {
  actor {
     serializers {
        json = "Akka.Serialization.NewtonSoftJsonSerializer"
        java = "Akka.Serialization.JavaSerializer" # not used, reserves serializer identifier
        bytes = "Akka.Serialization.ByteArraySerializer"
        myown = "Acme.Inc.MySerializer, MyAssembly"
     }

    serialization-bindings {
      "System.Byte[]" = bytes
      "System.Object" = json
      "Acme.Inc.MyMessage, MyAssembly" = myown
    }
  }
}
```

In case of ambiguity, a message implements several of the configured
classes, the most specific configured class will be used, i.e. the one of
which all other candidates are superclasses. If this condition cannot be met,
because e.g. ISerializable and MyOwnSerializable both apply and neither is a
subtype of the other, a warning will be issued.

Akka.NET provides serializers for POCO's (Plain Old C# Objects) by default, so
normally you don't need to add configuration for that.

### Verification
To verify that messages are serializable, the "serialize-message" option can be
enabled.
```hocon
akka {
  actor {
    serialize-messages = on
  }
}
```
> [!WARNING]
> We only recommend enabling this config option when running tests. It is completely pointless to have it turned on in other scenarios.

To verify that an actor's `Props` are serializable, the "serialize-creators"
option can be enabled.

```hocon
akka {
  actor {
    serialize-creators = on
  }
}`
```

> [!WARNING]
> We only recommend enabling this config option when running tests. It is completely pointless to have it turned on in other scenarios.

### Programmatic
As mentioned previously, Akka.NET uses serialization for message passing.
However the system is much more robust than that. To programmatically
(de-)serialize objects using Akka.NET serialization, a reference to the
main serialization class is all that is needed.

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

### Customization
Akka.NET makes it extremely easy to create custom serializers to handle a wide
variety of scenarios. All serializers in Akka.NET inherit from
`Akka.Serialization.Serializer`. So to create a custom serializer, all that is
needed is a class that inherits from this base class.

### Creating a custom serializer
In the second [example](xref:configuration) on this page, a custom serializer
`Acme.Inc.MySerializer` is configured. That custom serializer can be defined
like the following:

```csharp
using Akka.Actor;
using Akka.Serialization;
public class MySerializer : Serializer
{
    /// <summary>
    /// Determines whether the deserializer needs a type hint to deserialize
    /// an object.
    /// </summary>
    public override bool IncludeManifest()
    {
      return false;
    }

    /// <summary>
    /// Completely unique value to identify this implementation of the
    /// <see cref="Serializer"/> used to optimize network traffic
    /// </summary>
    public override int Identifier()
    {
      return 1234567; // 0 - 16 is reserved by Akka itself
    }

    // <summary>
    // Serializes the given object into a byte array
    // </summary>
    /// <param name="obj">The object to serialize </param>
    /// <returns>A byte array containing the serialized object</returns>
    public override byte[] ToBinary(object obj)
    {
      // Put the code that serializes the object here
      // ... ...
    }

    /// <summary>
    /// Deserializes a byte array into an object using the type hint
    // (if any, see "IncludeManifest" above)
    /// </summary>
    /// <param name="bytes">The array containing the serialized object</param>
    /// <param name="type">The type hint of the object contained in the array</param>
    /// <returns>The object contained in the array</returns>
    public override object FromBinary(byte[] bytes, Type type)
    {
      // Put your code that deserializes here
      // ... ...
    }
}
```

The only thing left to do for this class would be to fill in the serialization
logic in the ``ToBinary(object)`` method and the deserialization logic in the
``FromBinary(byte[], Type)``. Afterwards the configuration would need to be
updated to reflect which name to bind to and the classes that use this
serializer.

### Serializing Actors
All actors are serializable using the default serializer, but in cases were
custom serializers are used, we need to know how to (de-)serialize them
properly. In the general case, the local address to be used depends on the
type of remote address which shall be the recipient of the serialized
information. Use `Serialization.SerializedActorPath(actorRef)` like this:

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

This assumes that serialization happens in the context of sending a message
through the remote transport. There are other uses of serialization, though,
e.g. storing actor references outside of an actor application (database, etc.).
In this case, it is important to keep in mind that the address part of an
actor's path determines how that actor is communicated with. Storing a local
actor path might be the right choice if the retrieval happens in the same
logical context, but it is not enough when deserializing it on a different
network host: for that it would need to include the system's remote transport
address. An actor system is not limited to having just one remote transport
per se, which makes this question a bit more interesting. To find out the
appropriate address to use when sending to remoteAddr you can use  
`IActorRefProvider.GetExternalAddressFor(remoteAddr)` like this:

.. includecode:: code/docs/serialization/ExternalAddress.cs

<!--TODO: convert this java snippet to c#
```csharp
public class ExternalAddressExt implements Extension {
  private final ExtendedActorSystem system;

  public ExternalAddressExt(ExtendedActorSystem system) {
    this.system = system;
  }

  public Address getAddressFor(Address remoteAddress) {
    final scala.Option<Address> optAddr = system.provider()
      .getExternalAddressFor(remoteAddress);
    if (optAddr.isDefined()) {
      return optAddr.get();
    } else {
      throw new UnsupportedOperationException(
        "cannot send to remote address " + remoteAddress);
    }
  }
}

public class ExternalAddress extends
  AbstractExtensionId<ExternalAddressExt> implements ExtensionIdProvider {
  public static final ExternalAddress ID = new ExternalAddress();

  public ExternalAddress lookup() {
    return ID;
  }

  public ExternalAddressExt createExtension(ExtendedActorSystem system) {
    return new ExternalAddressExt(system);
  }
}

public class ExternalAddressExample {
  public String serializeTo(ActorRef ref, Address remote) {
    return ref.path().toSerializationFormatWithAddress(
        ExternalAddress.ID.get(system).getAddressFor(remote));
  }
}
```
-->

> [!NOTE]
> `ActorPath.ToSerializationFormatWithAddress` differs from `ToString` if the address does not already have host and port components, i.e. it only inserts address information for local addresses.

`ToSerializationFormatWithAddress` also adds the unique id of the actor, which
will change when the actor is stopped and then created again with the same name.
Sending messages to a reference pointing the old actor will not be delivered to
the new actor. If you do not want this behavior, e.g. in case of long term
storage of the reference, you can use `ToStringWithAddress`, which does not
include the unique id.

This requires that you know at least which type of address will be supported by
the system which will deserialize the resulting actor reference; if you have no
concrete address handy you can create a dummy one for the right protocol using
new Address(protocol, "", "", 0) (assuming that the actual transport used is as
lenient as Akka's RemoteActorRefProvider).

There is also a default remote address which is the one used by cluster support
(and typical systems have just this one); you can get it like this:

.. includecode:: code/docs/serialization/DefaultAddress.cs

<!--TODO: convert this java snippet to c#
```csharp
public class DefaultAddressExt implements Extension {
  private final ExtendedActorSystem system;

  public DefaultAddressExt(ExtendedActorSystem system) {
    this.system = system;
  }

  public Address getAddress() {
    return system.provider().getDefaultAddress();
  }
}

public class DefaultAddress extends
    AbstractExtensionId<DefaultAddressExt> implements ExtensionIdProvider {
  public static final DefaultAddress ID = new DefaultAddress();

  public DefaultAddress lookup() {
    return ID;
  }

  public DefaultAddressExt createExtension(ExtendedActorSystem system) {
    return new DefaultAddressExt(system);
  }
}
```
-->

### How to setup Hyperion as default serializer

Starting from Akka.NET v1.5, default Newtonsoft.Json serializer will be replaced in the favor of [Hyperion](https://github.com/akkadotnet/Hyperion). At the present moment, wire is required by some of the newer plugins (like `Akka.Cluster.Tools`). This change may break compatibility with older actors still using json serializer for remoting or persistence. If it's possible, it's advised to migrate to it already. To do so, first you need to reference hyperion serializer as NuGet package inside your project:

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

### Deep serialization of Actors
The recommended approach to do deep serialization of internal actor state is
to use [Akka Persistence](xref:Persistence).
