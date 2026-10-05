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

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/TelnetClient.cs?name=telnetClient)]

### TLS Connections

Akka.IO TCP can authenticate and encrypt a connection before it reports `Tcp.Connected`. Set `Tls` on an individual `Tcp.Connect` or `Tcp.Bind` command to enable TLS for that connection or listener; commands without TLS settings continue to use plaintext TCP.

For server-authenticated TLS, configure the server certificate on the listener and disable the default client-certificate requirement. On the client, provide the server name used for SNI and enable hostname validation:

```csharp
var serverTls = new TlsServerSettings(serverCertificate)
{
    RequireMutualAuthentication = false
};

var clientTls = new TlsClientSettings
{
    RequireMutualAuthentication = false,
    TargetHost = "example.com",
    ValidateCertificateHostname = true
};

manager.Tell(new Tcp.Bind(server, new IPEndPoint(IPAddress.Any, 8443))
{
    Tls = serverTls
});

manager.Tell(new Tcp.Connect(new DnsEndPoint("example.com", 8443))
{
    Tls = clientTls
});
```

The default certificate policy validates the certificate chain. Hostname validation is disabled by default for compatibility; set `ValidateCertificateHostname` to `true` and configure `TargetHost` when the server name should be checked. `SuppressValidation` ignores chain errors only, so it does not disable an enabled hostname check. Set `CustomValidator` to replace the built-in certificate decision; a missing certificate required for mutual TLS remains a handshake failure.

Mutual TLS is enabled by default. Supply a client certificate to `TlsClientSettings` and keep `RequireMutualAuthentication` enabled on both peers to require and validate certificates in both directions. The certificate objects remain owned by the caller and must stay valid while connections use them. A failed or timed-out handshake does not produce `Tcp.Connected`; an outbound connection reports `Tcp.CommandFailed`, and an inbound connection is closed while its listener continues accepting other clients. TLS never falls back to plaintext.

### Server Connection

To accept connections, an actor sends an `Tcp.Bind` message to the TCP manager, passing the `bind handler` in the message.
The `bind commander` will receive a `Tcp.Bound` message when the connection is listening.

The `bind handler` will receive a `Tcp.Connected` message for each accepted connection, and needs to register the connection handler by replying with a `Tcp.Register` message. Thereafter it proceeds the same as a client connection.

The following diagram illustrate the actor and messages.

![TCP Connection](/images/io-tcp-server.png)

The following code example shows a simple server that echo's data received from the network.

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/EchoServer.cs?name=echoServer)]

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/EchoConnection.cs?name=echoConnection)]

### TCP Listener Statistics

If you're building a long-running TCP server you can subscribe to the `TcpListener` actor's statistics via the `Tcp.SubscribeToTcpListenerStats` message:

[!code-csharp[Main](../../../src/core/Akka.Docs.Tests/Networking/IO/EchoServer.cs?name=echoServerWithStats)]

This will result in a `Tcp.TcpListenerStatistics` message being delivered with updated, rolling statistics once every 10 seconds or so roughly. Each independent `TcpListener` maintains its own statistics and they can support an arbitrary number of subscribers.
