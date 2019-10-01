---
uid: remote-transports
title: Transports
---

# Akka.Remote Transports
In the [Akka.Remote overview](xref:remote-overview) we introduced the concept of "transports" for Akka.Remote.

A "transport" refers to an actual network transport, such as TCP or UDP. By default Akka.Remote uses a [DotNetty](https://github.com/Azure/DotNetty) TCP transport, but you could write your own transport and use that instead of you wish.

In this section we'll expand a bit more on what transports are and how Akka.Remote can support multiple transports simultaneously.

## What are Transports?
Transports in Akka.Remote are abstractions on top of actual network transports, such as TCP and UDP sockets, and in truth transports have pretty simple requirements.

> [!NOTE]
> Most of the information below are things you, as an Akka.NET user, do not need to care about 99% of the time. Feel free to skip to the [Akka.Remote's Built-in Transports](#akkaremotes-built-in-transports) section.

Transports **do not need to care** about:
* **Serialization** - that's handled by Akka.NET itself;
* **Connection-oriented behavior** - the association process inside Akka.Remote ensures this, even over connection-less transports like UDP;
* **Reliable delivery** - for system messages this is handled by Akka.Remote and for user-defined messages this is taken care of at the application level through something like the [`AtLeastOnceDeliveryActor`](xref:at-least-once-delivery) class, part of Akka.Persistence;
* **Handling network failures** - all a transport needs to do is forward that information back up to Akka.Remote.

Transports **do need to care** about:
* **IP addressing and ports** - all Akka.NET endpoints have to be resolved to a reachable IP address and port number;
* **Message delivery** - getting bytes from point A to point B;
* **Message framing** - distinguishing individual messages within a network stream;
* **Disconnect and error reporting** - let Akka.Remote know when a disconnect or transport error occurred;
* **Preserving message order (optimal)** - some transports like UDP make no such guarantees, but in general it's recommended that the underlying transport preserve the write-order of messages on the read-side;
* **DNS resolution (optional)** - sockets don't use [DNS](https://en.wikipedia.org/wiki/Domain_Name_System) out of the box, but in order to make lives of Akka.NET users easy it's recommended that transports support DNS resolution. All built-in Akka.NET transports support DNS resolution by default.

Transports are just plumbing for Akka.Remote - they carry out their tasks and keep things simple and performant.

## Akka.Remote's Built-in Transports
Out of the box Akka.NET uses a socket-based transport built on top of the [DotNetty](https://github.com/Azure/DotNetty).

> [!NOTE]
> DotNetty supports both TCP and UDP, but currently only TCP support is included within Akka.NET. TCP is what most Akka.Remote and Akka.Cluster users use.

To enable the DotNetty TCP transport, we need to add a section for it inside our `remote` section in [HOCON configuration](xref:configuration):

```xml
akka {  
    actor {
        provider = remote
    }
    remote {
         dot-netty.tcp {
            port = 8081 #bound to a specific port
            hostname = localhost
        }
    }
}
```

## Using Custom Transports
Akka.Remote supports the ability to load third-party-defined transports at startup time - this is accomplished through defining a transport-specific configuration section within the `akka.remote` section in HOCON.

Let's say, for instance, you found a third party NuGet package that implemented [Google's Quic protocol](http://blog.chromium.org/2013/06/experimenting-with-quic.html) and wanted to use that transport inside your Akka.Remote application. Here's how you'd configure your application to use it.

```xml
akka{
    remote {
        enabled-transports = ["akka.remote.google-quic"]
        google-quic {
            transport-class = "Google.Quic.QuicTransport, Akka.Remote.Quic"
            applied-adapters = []
            transport-protocol = quic
            port = 0
            hostname = localhost 
        }
    }
}
```

You'd define a custom HOCON section (`akka.remote.google-quic`) and let Akka.Remote know that it should read that section for a transport definition inside `akka.remote.enabled-transports`.

> [!NOTE]
> To implement a custom transport yourself, you need to implement the [`Akka.Remote.Transport.Transport` abstract class](xref:Akka.Remote.Transport.Transport).

One important thing to note is the `akka.remote.google-quic.transport-protocol` setting - this specifies the address scheme you will use to address remote actors via the Quic protocol.

A remote address for an actor on this transport will look like:

    akka.quic://MySystem@localhost:9001/user/actor #quic
    akka.tcp://MySystem@localhost:9002/user/actor #helios.tcp

So the protocol you use in your remote `ActorSelection`s will need to use the string provided inside the `transport-protocol` block in your HOCON configuration.

## Running Multiple Transports Simultaneously
One of the most productive features of Akka.Remote is its ability to allow you to support multiple transports simultaneously within a single `ActorSystem`.

Suppose we created support for an http transport - here's what running both the DotNetty TCP transport and our *imaginary* http transport at the same time would look like in HOCON configuration.

```xml
akka{
    remote {
        enabled-transports = ["akka.remote.dot-netty.tcp", "akka.remote.magic.http"]
        dot-netty.tcp {
            port = 8081
            hostname = localhost
        }
        magic.http {
            port = 8082 # needs to be on a different port or IP than TCP
            hostname = localhost
        }
    }
}
```

Both TCP and HTTP are enabled in this scenario. But how do I know which transport is being used when I send a message to a `RemoteActorRef`? That's indicated by the protocol scheme used in the `Address` of the remote actor:

    akka.tcp://MySystem@localhost:8081/user/actor #dot-netty.tcp
    akka.http://MySystem@localhost:8082/user/actor #magic.http

So if you want to send a message to a remote actor over HTTP, you'd write something like this:

```csharp
var as = MyActorSystem.ActorSelection("akka.http://RemoteSystem@localhost:8082/user/actor");
as.Tell("remote message!"); //delivers message to remote system, if they're also using this transport
```

## Transport Caveats and Constraints
There are a couple of important caveats to bear in mind with transports in Akka.Remote.

* Each transport must have its own distinct protocol scheme (`transport-protocol` in HOCON) - no two transports can share the same scheme.
* Only one instance of a given transport can be active at a time, for the reason above.

### Separating Physical IP Address from Logical Address
One common DevOps issue that comes up often with Akka.Remote is something along the lines of the following:

> "I want to be able to send a message to `machine1.foobar.com` as my `ActorSystem` inbound endpoint, but the socket can't bind to that domain name (it can only bind to an IP.) How do I make it so I can send messages from other remote systems to `machine1.foobar.com`?

This can be solved through a built-in configuration property that is supported on every Akka.Remote transport, including third-party ones called the `public-hostname` property.

For instance, we can bind a DotNetty TCP transport to listen on all addresses (`0.0.0.0`) so we can accept messages from multiple network interfaces (which is more common in server-side environments than you might think) but still register itself as listening on `machine1.foobar.com`:

```xml
akka{
    remote {
        enabled-transports = ["akka.remote.dot-netty.tcp", "akka.remote.dot-netty.udp"]
        dot-netty.tcp {
            port = 8081
            hostname = 0.0.0.0 # listen on all interfaces
            public-hostname = "machine1.foobar.com"
        }
    }
}
```

This configuration allows the `ActorSystem`'s DotNetty TCP transport to listen on all interfaces, but when it associates with a remote system it'll tell the remote system that it's address is actually `machine1.foobar.com`.

Why is this distinction important? Why do we care about registering an publicly accessible hostname with our `ActorSystem`? Because in the event that other systems need to connect or reconnect to this process, *they need to have a reachable address.*

By default, Akka.Remote assumes that `hostname` is publicly accessible and will use that as the `public-hostname` value. But in the even that it's not AND some of your Akka.NET applications might need to contact this process then you need to set a publicly accessible hostname. 

## Additional Resources
* [Message Framing](http://blog.stephencleary.com/2009/04/message-framing.html) by Stephen Cleary
