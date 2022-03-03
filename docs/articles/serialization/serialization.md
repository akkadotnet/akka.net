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

### Configuring Serialization Bindings Programmatically

As of Akka.NET v1.4 it is now possible to bind serializers to their target types programmatically using the [`SerializationSetup` class](xref:Akka.Serialization.SerializationSetup).

First, we define a set of messages that all implement a common protocol and will be handled by the same serializer:

[!code-csharp[SerializationProtocol](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=Protocol)]

And then a custom [`SerializerWithStringManifest` implementation](xref:Akka.Serialization.SerializerWithStringManifest) to perform the serialization:

[!code-csharp[CustomSerializer](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=Serializer)]

With our application protocol and serializer defined we can now create some serialization bindings - these tell Akka.NET which serializer to use whenever we make any calls to serialize messages.

This is where we can use the [`SerializationSetup` class](xref:Akka.Serialization.SerializationSetup) to create statically typed serialization bindings that can be checked by the compiler, rather than defining them through HOCON.

[!code-csharp[SerializationSetup](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=SerializerSetup)]

The `SerializationSetup` takes a function with the following signature:

```csharp
Func<ExtendedActorSystem, ImmutableHashSet<SerializerDetails>>
```

The `ExtendedActorSystem` passed into this method is the `ActorSystem` you're configuring, as all `Serializer` classes in Akka.NET require an `ExtendedActorSystem` as a constructor argument.

For each serialization binding you wish to create, you need to create [a `SerializerDetails` object](xref:Akka.Serialization.SerializerDetails) using the `SerializerDetails.Create` method:

```csharp
public static SerializerDetails Create(string alias, Serializer serializer, ImmutableHashSet<Type> useFor)
```

The `string alias` is the same alias you'd configure in HOCON's `akka.actor.serializers` section - and the serializer configured via `SerializationSetup` can be consumed by other parts of Akka.NET by referencing this alias, so make sure you pick a unique name.

The `ImmutableHashSet<Type> useFor` is where you define your serialization type bindings - in this case, we bound the serializer to the `IAppProtocol` interface: any type that implements this interface will be handled by the `AppProtocolSerializer`.

Now that we've created our `SerializationSetup`, we need to actually pass this into our `ActorSystem` - to do this we will want to combine our `SerializationSetup` with a [`BootstrapSetup` instance that holds the rest of our HOCON configuration](xref:Akka.Actor.BootstrapSetup):

[!code-csharp[SerializationSetup](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=MergedSetup)]

And using the `ActorSystemSetup` produced by merged the `BootstrapSetup` and `SerializationSetup` together, we can create our `ActorSystem` and verify that the serialization bindings were configured correctly:

[!code-csharp[SerializationSetup](../../../src/core/Akka.Docs.Tests/Configuration/SerializationSetupDocSpec.cs?name=Verification)]

And that's how you can configure Akka.NET serialization programmatically.

> [!NOTE]
> There are other parts of Akka.NET that are possible to configure programmatically via `ActorSystemSetup`. [Read more about them here](xref:configuration).

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

### Creating New Serializers

A custom `Serializer` has to inherit from `Akka.Serialization.Serializer` and can be defined like this:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/Serialization/CreateCustomSerializer.cs?name=CustomSerialization)]

The only thing left to do for this class would be to fill in the serialization logic in the ``ToBinary(object)`` method and the deserialization logic in the ``FromBinary(byte[], Type)``.
Afterwards the configuration would need to be updated to reflect which name to bind to and the classes that use this
serializer.

### Programmatically Change NewtonSoft JSON Serializer Settings

You can change the JSON serializer behavior by using the `NewtonSoftJsonSerializerSetup` class to programmatically
change the settings used inside the Json serializer by passing it into the an `ActorSystemSetup`.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/Serialization/ProgrammaticJsonSerializerSetup.cs?name=CustomJsonSetup)]

Note that, while we try to keep everything to be compatible, there are no guarantee that your specific serializer settings use case is compatible with the rest of
Akka.NET serialization schemes; please test your system in a development environment before deploying it into production.

There are a couple limitation with this method, in that you can not change the `ObjectCreationHandling` and the `ContractResolver` settings
in the Json settings object. Those settings, by default, will always be overridden with `ObjectCreationHandling.Replace` and the [`AkkaContractResolver`](xref:Akka.Serialization.NewtonSoftJsonSerializer.AkkaContractResolver)
object respectively.

### Serializer with String Manifest

The `Serializer` illustrated above supports a class-based manifest (type hint).
For serialization of data that need to evolve over time, the [`SerializerWithStringManifest`](xref:Akka.Serialization.SerializerWithStringManifest) is recommended instead of `Serializer` because the manifest (type hint) is a `String` instead of a `Type`.
This means that the class can be moved/removed and the serializer can still deserialize old data by matching on the String.
This is especially useful for `Persistence`.

The manifest string can also encode a version number that can be used in `FromBinary` to deserialize in different ways to migrate old data to new domain objects.

If the data was originally serialized with `Serializer`, and in a later version of the system you change to [`SerializerWithStringManifest`](xref:Akka.Serialization.SerializerWithStringManifest), the manifest string will be the full class name if you used `IncludeManifest=true`, otherwise it will be the empty string.

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

### Deep Serialization of Actors

The recommended approach to do deep serialization of internal actor state is to use [Akka Persistence](xref:persistence-architecture).

## How to Setup Hyperion as the Default Serializer

Starting from Akka.NET v1.5, default Newtonsoft.Json serializer will be replaced in the favor of [Hyperion](https://github.com/akkadotnet/Hyperion). This change may break compatibility with older actors still using json serializer for remoting or persistence. If it's possible, it's advised to migrate to it already. To do so, first you need to reference hyperion serializer as NuGet package inside your project:

    Install-Package Akka.Serialization.Hyperion -pre

Then bind `hyperion` serializer using following HOCON configuration in your actor system settings:

```hocon
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

## Danger of Polymorphic Serializer

One of the danger of polymorphic serializers is the danger of unsafe object type injection into
the serialization/de-serialization chain. This issue applies to any type of polymorphic serializer,
including JSON, BinaryFormatter, etc. In Akka, this issue primarily affects developers who allow third parties to pass messages directly
to unsecured Akka.Remote endpoints, a [practice that we do not encourage](https://getakka.net/articles/remoting/security.html#akkaremote-with-virtual-private-networks).

Generally, there are two approaches you can take to alleviate this problem:

1. Implement a schema-based serialization that are contract bound, which is more expensive to setup at first but fundamentally faster and more secure.
2. Implement a filtering or blacklist to block dangerous types.

An example of using a schema-based serialization in Akka can be read under the title "Using Google
Protocol Buffers to Version State and Messages" in [this documentation](https://petabridge.com/cluster/lesson3)

Hyperion chose to implement the second approach by blacklisting a set of potentially dangerous types
from being deserialized:

* `System.Security.Claims.ClaimsIdentity`
* `System.Windows.Forms.AxHost.State`
* `System.Windows.Data.ObjectDataProvider`
* `System.Management.Automation.PSObject`
* `System.Web.Security.RolePrincipal`
* `System.IdentityModel.Tokens.SessionSecurityToken`
* `SessionViewStateHistoryItem`
* `TextFormattingRunProperties`
* `ToolboxItemContainer`
* `System.Security.Principal.WindowsClaimsIdentity`
* `System.Security.Principal.WindowsIdentity`
* `System.Security.Principal.WindowsPrincipal`
* `System.CodeDom.Compiler.TempFileCollection`
* `System.IO.FileSystemInfo`
* `System.Activities.Presentation.WorkflowDesigner`
* `System.Windows.ResourceDictionary`
* `System.Windows.Forms.BindingSource`
* `Microsoft.Exchange.Management.SystemManager.WinForms.ExchangeSettingsProvider`
* `System.Diagnostics.Process`
* `System.Management.IWbemClassObjectFreeThreaded`

Be warned that these class can be used as a man in the middle attack vector, but if you need
to serialize one of these class, you can turn off this feature using this inside your HOCON settings:

```hocon
akka.actor.serialization-settings.hyperion.disallow-unsafe-type = false
```
<!-- markdownlint-disable MD028 -->
> [!IMPORTANT]
> This feature is turned on as default since Akka.NET v1.4.24

> [!WARNING]
> Hyperion is __NOT__ designed as a safe serializer to be used in an open network as a client-server
> communication protocol, instead it is designed to be used as a server-server communication protocol,
> preferably inside a closed network system.
<!-- markdownlint-enable MD028 -->

## Cross Platform Serialization Compatibility in Hyperion

There are problems that can arise when migrating from old .NET Framework to the new .NET Core standard, mainly because of breaking namespace and assembly name changes between these platforms.
Hyperion implements a generic way of addressing this issue by transforming the names of these incompatible names during deserialization.

There are two ways to set this up, one through the HOCON configuration file, and the other by using the `HyperionSerializerSetup` class.

> [!NOTE]
> Only the first successful name transformation is applied, the rest are ignored.
> If you are matching several similar names, make sure that you order them from the most specific match to the least specific one.

### HOCON

HOCON example:

```hocon
akka.actor.serialization-settings.hyperion.cross-platform-package-name-overrides = {
  netfx = [
    {
      fingerprint = "System.Private.CoreLib,%core%",
      rename-from = "System.Private.CoreLib,%core%",
      rename-to = "mscorlib,%core%"
   }]
  netcore = [
    {
      fingerprint = "mscorlib,%core%",
      rename-from = "mscorlib,%core%",
      rename-to = "System.Private.CoreLib,%core%"
    }]
  net = [
    {
      fingerprint = "mscorlib,%core%",
      rename-from = "mscorlib,%core%",
      rename-to = "System.Private.CoreLib,%core%"
    }]
}
```

In the example above, we're addressing the classic case where the core library name was changed between `mscorlib` in .NET Framework to `System.Private.CoreLib` in .NET Core.
This transform is already included inside Hyperion as the default cross platform support, and used here as an illustration only.

The HOCON configuration section is composed of three object arrays named `netfx`, `netcore`, and `net`, each corresponds, respectively, to .NET Framework, .NET Core, and the new .NET 5.0 and beyond.
The Hyperion serializer will automatically detects the platform it is running on currently and uses the correct array to use inside its deserializer. For example, if Hyperion detects
that it is running under .NET framework, then it will use the `netfx` array to do its deserialization transformation.

The way it works that when the serializer detects that the type name contains the `fingerprint` string, it will replace the string declared in the `rename-from`
property into the string declared in the `rename-to`.

In code, we can write this behavior as:

```csharp
if(packageName.Contains(fingerprint)) packageName = packageName.Replace(rename-from, rename-to);
```

### HyperionSerializerSetup

This behavior can also be implemented programmatically by providing a `HyperionSerializerSetup` instance during `ActorSystem` creation.

```csharp
#if NETFRAMEWORK
var hyperionSetup = HyperionSerializerSetup.Empty
    .WithPackageNameOverrides(new Func<string, string>[]
    {
        str => str.Contains("System.Private.CoreLib,%core%")
            ? str.Replace("System.Private.CoreLib,%core%", "mscorlib,%core%") : str
    }
#elif NETCOREAPP
var hyperionSetup = HyperionSerializerSetup.Empty
    .WithPackageNameOverrides(new Func<string, string>[]
    {
        str => str.Contains("mscorlib,%core%")
            ? str.Replace("mscorlib,%core%", "System.Private.CoreLib,%core%") : str
    }
#endif

var bootstrap = BootstrapSetup.Create().And(hyperionSetup);
var system = ActorSystem.Create("actorSystem", bootstrap);
```

In the example above, we're using compiler directives to make sure that the correct name transform are used during compilation.

## Complex Object Serialization Using Hyperion

One of the limitation of a reflection based serializer is that it would fail to serialize
objects with complex internal looping references in its properties or fields and ended up throwing
a stack overflow exception as it tries to recurse through all the looping references, for example,
an `XmlDocument` class, but we needed to send them over the wire to another remote node in our
cluster.

While having a very complex internal structure, an `XmlDocument` object can be simplified into a
string that can be sent safely across the wire, but we would need to create a special code that
handles all of `XmlDocument` occurrences or make it so that it is stored in string format inside
our messages, and converting XML documents every time we needed to access this information is
an expensive operation that we would like to avoid while working with our code.

Hyperion introduces a simple adapter called `Surrogate` that can help with de/serializing these
type of complex objects as a man in the middle, intercepting the type and de/serialize them into
the much simpler type for wire transfer.

For this example, we would use these two classes, the class `Foo` is an imaginary "complex" class
that we want to send across the wire and the class `FooSurrogate` is the actual class that we're
serializing and send across the wire:

```c#
        public class Foo
        {
            public Foo(string bar)
            {
                Bar = bar;
                ComplexProperty = ComputeComplexProperty();
            }

            public string Bar { get; }
            public HighlyComplexComputedProperty ComplexProperty { get; }
            
            private  ComputeComplexProperty()
            {
                // ...
            }
        }
        
        public class FooSurrogate
        {
            public FooSurrogate(string bar)
            {
                Bar = bar;
            }

            public string Bar { get; }
        }
```

### Creating and Declaring `Surrogate` via HOCON

To create a serializer surrogate in HOCON, we would first create a class that inherits from
the `Surrogate` class:

```c#
    public class FooHyperionSurrogate : Surrogate
    {
        public FooHyperionSurrogate()
        {
            From = typeof(Foo);
            To = typeof(FooSurrogate);
            ToSurrogate = obj => new FooSurrogate(((Foo)obj).Bar);
            FromSurrogate = obj => new Foo(((FooSurrogate)obj).Bar);
        }
    }
```

This class will inform the Hyperion serializer to intercept any `Foo` class and instead of
reflecting actual fields and properties of `Foo` class, it will use the much simpler
`FooSurrogate` class instead. To tell Hyperion to use this information, we need to pass the
surrogate information inside the HOCON settings:

```hocon
akka.actor {
    serializers.hyperion = ""Akka.Serialization.HyperionSerializer, Akka.Serialization.Hyperion""
    serialization-bindings {
        ""System.Object"" = hyperion
    }
    serialization-settings.hyperion {
        surrogates = [
            ""MyAssembly.FooHyperionSurrogate, MyAssembly""
        ]
    }
}
```

### Creating and Declaring `Surrogate` Programmatically Using `HyperionSerializerSetup`

We can also use `HyperionSerializerSetup` to declare our surrogates:

```c#
var hyperionSetup = HyperionSerializerSetup.Empty
    .WithSurrogates(new [] { Surrogate.Create<Foo, FooSurrogate>(
        foo => new FooSurrogate(foo.Bar), 
        surrogate => new Foo(surrogate.Bar))
    });

var bootstrap = BootstrapSetup.Create().And(hyperionSetup);
var system = ActorSystem.Create("actorSystem", bootstrap);
```

Note that we do not need to declare any bindings in HOCON for this to work, and if you do,
`HyperionSerializerSetup` will override the HOCON settings with the one programmatically declared.
