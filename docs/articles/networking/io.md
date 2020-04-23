---
uid: akka-io
title: I/O
---
# Akka I/O

The I/O extension provides an non-blocking, event driven API that matches the underlying transports mechanism.

## Getting Started
Every I/O Driver has a special actor, called the `manager`, that serves as an entry point for the API.
The `manager` for a particular driver is accessible through an extension method on `ActorSystem`. The following example shows how to get a reference to the TCP manager.

```csharp
using Akka.Actor;
using Akka.IO;

...

var system = ActorSystem.Create("example");
var manager = system.Tcp();
```

## TCP Driver

### Client Connection

To create a connection an actor sends a `Tcp.Connect` message to the TCP Manager.
Once the connection is established the connection actor sends a `Tcp.Connected` message to the `commander`, which registers the `connection handler` by replying with a `Tcp.Register` message.

Once this handshake is completed, the handler and connection communicate with `Tcp.WriteCommand` and `Tcp.Received` messages.

The following diagram illustrate the actors involved in establishing and handling a connection.

![TCP Connection](/images/io-tcp-client.png)

The following example shows a simple Telnet client. The client send lines entered in the console to the TCP connection, and write data received from the network to the console.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/TelnetClient.cs?name=telnet-client)]

### Server Connection

To accept connections, an actor sends an `Tcp.Bind` message to the TCP manager, passing the `bind handler` in the message.
The `bind commander` will receive a `Tcp.Bound` message when the connection is listening.

The `bind handler` will receive a `Tcp.Connected` message for each accepted connection, and needs to register the connection handler by replying with a `Tcp.Register` message. Thereafter it proceeds the same as a client connection.

The following diagram illustrate the actor and messages.

![TCP Connection](/images/io-tcp-server.png)

The following code example shows a simple server that echo's data received from the network.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/EchoServer.cs?name=echo-server)]

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/EchoConnection.cs?name=echo-connection)]
