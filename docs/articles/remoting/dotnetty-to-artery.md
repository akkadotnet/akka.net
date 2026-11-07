---
uid: dotnetty-to-artery
title: Migrating From DotNetty to Artery
---

# Migrating From DotNetty to Artery

This guide moves an existing Akka.Remote or Akka.Cluster application from classic DotNetty
remoting to the Artery TCP transport in Akka.NET v1.6. It includes TLS and mutual TLS (mTLS).
For the wider framework upgrade, start with the
[v1.6 getting started guide](https://github.com/akkadotnet/akka.net/blob/dev/GETTING_STARTED_V1.6.md).

Artery is experimental and off by default in the v1.6 beta. It requires .NET 10, as do all
v1.6 packages. The TLS and Hosting APIs below require a build containing
[#8791](https://github.com/akkadotnet/akka.net/pull/8791); `1.6.0-beta6` and earlier do not
include them. Classic DotNetty remains available when Artery is disabled.

## Plan the Transport Switch

Classic DotNetty and Artery use different wire protocols. One actor system selects one of
them; enabling Artery does not create a bridge to classic peers. Every member of a cluster,
and every external remoting client that communicates with it, must use the same transport.
A node-by-node rolling upgrade that mixes classic and Artery cannot maintain that cluster.

For a coordinated switch, drain application work, shut down the classic deployment, and start
the nodes and clients with the Artery configuration and addresses. If the application needs
parallel old and new deployments, keep them as separate clusters and use an application
protocol to transfer work between them. Plan rollback with the same transport boundary:
restoring a classic deployment also restores its classic client and seed-node addresses.

Inventory configured actor selections, seed nodes, remote deployment targets, router target
nodes, and addresses stored outside the process. Previously serialized actor references
retain their old addresses; do not expect a transport switch to rewrite them. Recreate
references against the new deployment where the application uses them.

## Replace the Transport Configuration

A classic cluster node might start with:

```hocon
akka {
    actor.provider = cluster
    remote.dot-netty.tcp {
        hostname = "127.0.0.1"
        port = 2552
    }
    cluster.seed-nodes = ["akka.tcp://MySystem@127.0.0.1:2552"]
}
```

The corresponding plaintext Artery node uses:

```hocon
akka {
    actor.provider = cluster
    remote.artery {
        enabled = on
        transport = tcp
        canonical.hostname = "127.0.0.1"
        canonical.port = 25520
    }
    cluster.seed-nodes = ["akka://MySystem@127.0.0.1:25520"]
}
```

These loopback examples are for local development. Give each deployed node a reachable
hostname and port, and configure the same reachable seed-node addresses on its peers. Use
`actor.provider = remote` instead of `cluster` for an application using remoting without
Akka.Cluster; omit the cluster settings in that case. Keep the existing actor-system name
unless the application is deliberately changing it.

| Classic configuration | Artery configuration |
| --- | --- |
| `akka.remote.dot-netty.tcp.hostname` | `akka.remote.artery.canonical.hostname`. |
| `akka.remote.dot-netty.tcp.port` | `akka.remote.artery.canonical.port`; default changes from `2552` to `25520`. You may explicitly keep an existing port during a coordinated switch. |
| `akka.remote.enabled-transports` | Select Artery with `akka.remote.artery.enabled = on`; do not add it as a classic transport driver. |
| `akka.remote.dot-netty.tcp.maximum-frame-size` | Review `akka.remote.artery.advanced.maximum-frame-size` against actual serialized message sizes; the protocols have different envelope overhead. |
| `akka.remote.dot-netty.tcp.ssl` | Create an `ArteryTlsSetup` or set the Hosting TLS profile, as described below. |
| Classic buffer, batching, connection, and transport-adapter settings | Review the Artery settings individually; DotNetty-specific keys do not tune Artery. |

Start with Artery's defaults for lanes and queues. Treat performance tuning as a separate
measurement after connectivity, serialization, and application behavior work. The
[reference configuration](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote/Configuration/Remote.conf)
lists the supported `akka.remote.artery` settings.

### Check Bind and Advertised Addresses

The current Artery TCP implementation uses `canonical.hostname` and `canonical.port` both
to bind the listener and to construct the address advertised to peers. It does not expose
separate `bind.hostname` or `bind.port` settings. A canonical port of `0` binds an available
port and advertises the actual bound port; use a stable port for configured seed nodes.

Classic deployments using `hostname = 0.0.0.0` with a different `public-hostname`, or a
different `public-port` behind port mapping, need an address plan before migrating. Those
DotNetty settings do not configure Artery. Choose a hostname that resolves to a local
interface for binding and is reachable by peers; do not advertise `0.0.0.0` as a peer address.

### Update Every Remote Address

Artery uses `akka://` for both plaintext and TLS connections. TLS is selected by the node's
configuration, not by an address prefix.

| Classic address | Artery address with its default port |
| --- | --- |
| `akka.tcp://MySystem@node-a:2552/user/worker` | `akka://MySystem@node-a:25520/user/worker` |
| `akka.ssl.tcp://MySystem@node-a:2552/user/worker` | `akka://MySystem@node-a:25520/user/worker`, with TLS configured on each node. |

Apply the change to seed nodes and external clients as well as application actor paths.
Keeping the old port does not preserve wire compatibility. A local actor path such as
`/user/worker` remains local and needs no transport-address change.

## Configure Akka.Hosting

Reference `Akka.Remote.Hosting` at the same v1.6 version as the other Akka packages and
import `Akka.Remote.Hosting`. Replace the classic `WithRemoting` transport configuration
with `WithArteryRemoting`. It enables Artery and accepts `HostName`, `Port`, and an optional
`Tls` profile. Configure clustering through the existing cluster builder as usual: the
Artery helper preserves a Cluster provider, while a local or unspecified provider becomes
Remote. Omitted host and port values come from the application's configuration.

This compiled example supplies an mTLS profile to the Hosting builder:

[!code-csharp[ArteryTlsHosting](../../../src/core/Akka.Docs.Tests/Networking/ArteryTlsExamples.cs?name=arteryTlsHosting)]

For plaintext Artery, omit `options.Tls` and the certificate/profile construction. If the
builder already has an `ArteryTlsSetup`, leaving `Tls` unset preserves it; remove that setup
as well when intentionally configuring plaintext.

## Migrate TLS and Mutual TLS

Enabling Artery does not copy TLS settings from DotNetty. Applications that previously used
TLS must supply an Artery TLS profile on every participating node before switching
transports. Without that profile, Artery uses plaintext TCP.

`ArteryTlsSettings.Mutual(certificate, peerPolicy)` presents the node certificate and
requires a peer certificate in both directions. The certificate needs an accessible RSA or
ECDSA private key and must be suitable for client and server authentication. Use
`ArteryTlsSettings.ServerOnly(serverCertificate, outboundServerPolicy)` for the equivalent
of disabling the inbound client-certificate requirement: each node still presents a server
certificate and validates its peers' server certificates on outgoing connections.

| DotNetty TLS setting | Artery TLS equivalent |
| --- | --- |
| Certificate file, password, and key-storage flags | `TlsCertificateLoader.LoadPkcs12FromFile(...)`; pass its result to the profile factory. Byte-array loading is also available. |
| Certificate thumbprint, store name, and store location | `TlsCertificateLoader.LoadFromStore(...)`; select `validOnly` independently of peer validation. |
| An existing `X509Certificate2` | Pass the caller-owned certificate directly to `Mutual(...)` or `ServerOnly(...)`. |
| `require-mutual-authentication = on` / `off` | `Mutual(...)` / `ServerOnly(...)`. |
| `suppress-validation = off` | `TlsPeerPolicy.SystemTrust()` for operating-system certificate-chain trust. |
| `suppress-validation = on` | Choose explicit pins with `PinnedCertificates(...)` or a complete custom trust callback. |
| `validate-certificate-hostname = on` | Add `.And(TlsCertificateValidation.ValidateHostname())` to the outbound server policy. |
| `CustomValidator` and `CertificateValidation` helpers | `TlsPeerPolicy.CustomTrust(...)` and `TlsCertificateValidation` helpers. Adapt an existing callback delegate with a lambda using the same five arguments. |

`SystemTrust()` by itself leaves hostname checking off, matching DotNetty's default.
Use the three-argument `ArteryTlsSettings.Mutual(certificate, inboundClientPolicy,
outboundServerPolicy)` factory when inbound and outbound identity rules differ. Outbound
hostname validation uses the remote endpoint's hostname; inbound clients do not have an
independently known hostname to infer from their address. For private-CA trust, install the
CA in the operating system trust store. Pins alone do not imply chain, expiry, revocation,
or hostname checks. `CustomTrust(...)` replaces the trust decision, and `.And(...)` adds
restrictions to it.

This compiled console example loads a PKCS#12 certificate and creates an actor system with
mTLS and system chain trust. It needs `node.pfx`, its password in `NODE_CERT_PASSWORD`, and
the namespaces shown in the linked source file. Change the hostname and provider for the
deployment being migrated:

[!code-csharp[ArteryTlsMigration](../../../src/core/Akka.Docs.Tests/Networking/ArteryTlsExamples.cs?name=arteryTlsMigration)]

Keep caller-owned certificates alive until the actor system and its connections have
stopped. TLS protects every Artery channel, including ordinary, control, and large-message
traffic. Handshake failure closes the connection without falling back to plaintext. Peer
authentication does not itself authorize cluster membership or actor access.

See [TLS for Akka.IO, Akka.Streams, and Artery](tls-for-io-streams-artery.md) for separate
inbound/outbound policies, pin rotation, certificate loading, and custom validation examples.
The [mini-guide's settings table](https://github.com/akkadotnet/akka.net/blob/dev/GETTING_STARTED_V1.6.md#migrating-dotnetty-tls-settings)
also maps the original DotNetty option names.

## Verify the Migrated Deployment

Before accepting traffic, verify that each actor system advertises the intended `akka://`
address, all cluster members reach `Up`, and external clients can resolve the new actor
paths. Exercise ordinary request/reply traffic, remote death watch, peer restart, and
graceful shutdown with the application's serializers and payload sizes. An idle association
reconnects when a new send or actor resolution supplies demand.

For TLS deployments, verify an accepted peer and a deliberately rejected peer so the
configured trust policy is exercised. For large payloads, check frame limits on both peers;
Artery's optional large-message stream isolates traffic but does not fragment oversized
messages. Configure `large-message-destinations` consistently on both sides, and account
for the lack of ordering between ordinary and large-message streams to the same actor.

The transport switch does not require adopting `SerializerV2` or source-generated
serializers. Keep application serializer IDs, manifests, and bindings consistent between
communicating nodes, and review the separate
[v1.6 breaking changes](https://github.com/akkadotnet/akka.net/blob/dev/BREAKING_CHANGES_V1.6.md)
for the framework upgrade. TCP is the only current Artery transport; QUIC and actor-reference
compression are not supplied by this migration.
