Akka has a built-in Extension for serialization, and it is both possible to use the built-in serializers and to write your own.

The serialization mechanism is both used by Akka internally to serialize messages, and available for ad-hoc serialization of whatever you might need it for.

##Usage
Configuration
For Akka to know which Serializer to use for what, you need edit your Configuration, in the "akka.actor.serializers"-section you bind names to implementations of the akka.serialization.Serializer you wish to use, like this:
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
After you've bound names to different implementations of Serializer you need to wire which classes should be serialized using which Serializer, this is done in the "akka.actor.serialization-bindings"-section:

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
You only need to specify the name of an interface or abstract base class of the messages. In case of ambiguity, i.e. the message implements several of the configured classes, the most specific configured class will be used, i.e. the one of which all other candidates are superclasses. If this condition cannot be met, because e.g. java.io.Serializable and MyOwnSerializable both apply and neither is a subtype of the other, a warning will be issued.

Akka.NET provides serializers for POCO's by default, so normally you don't need to add configuration for that.

Verification
If you want to verify that your messages are serializable you can enable the following config option:
```hocon
akka {
  actor {
    serialize-messages = on
  }
}
```
>**Warning**<br/>
We only recommend using the config option turned on when you're running tests. It is completely pointless to have it turned on in other scenarios.

If you want to verify that your Props are serializable you can enable the following config option:

```hocon
akka {
  actor {
    serialize-creators = on
  }
}`
```

>**Warning**<br/>
We only recommend using the config option turned on when you're running tests. It is completely pointless to have it turned on in other scenarios.

### Programmatic
If you want to programmatically serialize/deserialize using Akka Serialization, here's some examples:

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
string back = (string) serializer.FromBinary(bytes);
 
// Voilá!
Assert.AreEqual(original, back);
```

### Customization
So, lets say that you want to create your own Serializer, you saw the docs.serialization.MyOwnSerializer in the config example above?

###Creating new Serializers
First you need to create a class definition of your Serializer, which is done by extending Akka.Serialization.Serializer, like this:
```csharp
using Akka.Actor.*;
using Akka.Serialization;
public class MyOwnSerializer : Serializer 
{
  // This is whether "FromBinary" requires a "clazz" or not
  public override bool IncludeManifest() {
    return false;
  }
 
  // Pick a unique identifier for your Serializer,
  // you've got a couple of billions to choose from,
  // 0 - 16 is reserved by Akka itself
  public override int Identifier() {
    return 1234567;
  }
 
  // "ToBinary" serializes the given object to an Array of Bytes
  public override byte[] ToBinary(object obj) {
    // Put the code that serializes the object here
    // ... ...
  }
 
  // "fromBinary" deserializes the given array,
  // using the type hint (if any, see "IncludeManifest" above)
  public override object FromBinaryJava(byte[] bytes, Type type) {
    // Put your code that deserializes here
    // ... ...
  }
}
```
Then you only need to fill in the blanks, bind it to a name in your Configuration and then list which classes that should be serialized using it.

### Serializing ActorRefs
All ActorRefs are serializable using default serializer, but in case you are writing your own serializer, you might want to know how to serialize and deserialize them properly. In the general case, the local address to be used depends on the type of remote address which shall be the recipient of the serialized information. Use Serialization.serializedActorPath(actorRef) like this:
```
import akka.actor.*;
import akka.serialization.*;
// Serialize
// (beneath toBinary)
String identifier = Serialization.serializedActorPath(theActorRef);
 
// Then just serialize the identifier however you like
 
// Deserialize
// (beneath fromBinary)
final ActorRef deserializedActorRef = extendedSystem.provider().resolveActorRef(
  identifier);
// Then just use the ActorRef
```
This assumes that serialization happens in the context of sending a message through the remote transport. There are other uses of serialization, though, e.g. storing actor references outside of an actor application (database, etc.). In this case, it is important to keep in mind that the address part of an actor’s path determines how that actor is communicated with. Storing a local actor path might be the right choice if the retrieval happens in the same logical context, but it is not enough when deserializing it on a different network host: for that it would need to include the system’s remote transport address. An actor system is not limited to having just one remote transport per se, which makes this question a bit more interesting. To find out the appropriate address to use when sending to remoteAddr you can use ActorRefProvider.getExternalAddressFor(remoteAddr) like this:
```chsarp
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

>**Note**<br/>
`ActorPath.ToSerializationFormatWithAddress` differs from `ToString` if the address does not already have host and port components, i.e. it only inserts address information for local addresses.

`ToSerializationFormatWithAddress` also adds the unique id of the actor, which will change when the actor is stopped and then created again with the same name. Sending messages to a reference pointing the old actor will not be delivered to the new actor. If you do not want this behavior, e.g. in case of long term storage of the reference, you can use `ToStringWithAddress`, which does not include the unique id.

This requires that you know at least which type of address will be supported by the system which will deserialize the resulting actor reference; if you have no concrete address handy you can create a dummy one for the right protocol using new Address(protocol, "", "", 0) (assuming that the actual transport used is as lenient as Akka’s RemoteActorRefProvider).

There is also a default remote address which is the one used by cluster support (and typical systems have just this one); you can get it like this:
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
###Deep serialization of Actors
The recommended approach to do deep serialization of internal actor state is to use Akka Persistence.