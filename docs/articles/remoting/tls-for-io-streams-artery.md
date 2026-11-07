---
uid: akka-io-tls
title: TLS for Akka.IO, Akka.Streams, and Artery
---

# TLS for Akka.IO, Akka.Streams, and Artery

This guide documents TLS for Akka.IO TCP, Akka.Streams TCP, and Artery remoting, all of which configure TLS in code with immutable settings and peer trust policies.

Akka.IO TLS is configured in code with an `X509Certificate2` and immutable settings objects. Akka.IO does not read a TLS schema from HOCON or use the DotNetty remoting settings described in [Network Security](security.md). Your application can load certificates from its configured source and pass the certificate object to Akka.IO.

## Choose Server-only TLS or Mutual TLS

TLS encrypts the connection and authenticates peers according to the policies you configure. In server-only TLS, the client authenticates the server and the listener accepts clients without certificates. In mutual TLS, the client and server each present and validate a certificate.

Use `TlsClientSettings.ServerOnly` with `TlsServerSettings.ServerOnly` for server-only TLS. Supply an operating-system-trusted server certificate and validate the server name when it is part of your identity requirements:

[!code-csharp[TlsServerOnly](../../../src/core/Akka.Docs.Tests/Networking/IO/TlsExamples.cs?name=tlsServerOnly)]

For mutual TLS, use the `Mutual` factory on both sides. The client settings take the client's certificate and server validation policy; the listener settings take the server certificate and client validation policy:

[!code-csharp[TlsMutual](../../../src/core/Akka.Docs.Tests/Networking/IO/TlsExamples.cs?name=tlsMutual)]

The factories do not decide whether an authenticated certificate is authorized to join a cluster. Akka.IO TLS only authenticates the connection according to its configured certificate policies; applications and cluster protocols must enforce authorization separately.

## Select a Peer Trust Policy

`TlsPeerPolicy.SystemTrust()` uses the operating system's certificate-chain trust. It ignores only a TLS name-mismatch error; it does not check a host name by itself. For an outgoing connection, add `TlsCertificateValidation.ValidateHostname()` to check the remote server name. The connection endpoint supplies the default target host. `WithTargetHost` is an optional override for when the endpoint address differs from the DNS identity to send as SNI and validate, such as when connecting by IP address. Leave it unset when the endpoint already uses the desired DNS name. To use a private CA with system trust, install that CA in the operating system's trusted root store and use `SystemTrust()`.

`TlsPeerPolicy.PinnedCertificates(...)` accepts certificates whose leaf thumbprint matches one of the supplied pins. A pin is a complete trust decision by itself: it does not imply certificate-authority trust, expiry checks, revocation checks, or hostname validation. This is useful for a known self-signed certificate; add `ValidateHostname` when the name must also match:

[!code-csharp[TlsPinnedCertificate](../../../src/core/Akka.Docs.Tests/Networking/IO/TlsExamples.cs?name=tlsPinnedCertificate)]

Add checks with `.And(...)` when those checks are also required. For example, `SystemTrust().And(TlsCertificateValidation.ValidateSubject("CN=service.example"))` keeps system chain trust and additionally restricts the certificate subject. The private-CA example assumes that CA is installed in the platform trust store; the issuer rule narrows peers that already pass chain validation and does not add a trust anchor. A subject or issuer match alone is not a trust anchor. The compiled examples also show pin trust narrowed by hostname:

[!code-csharp[TlsTrustPolicyComposition](../../../src/core/Akka.Docs.Tests/Networking/IO/TlsExamples.cs?name=tlsTrustPolicyComposition)]

`TlsPeerPolicy.CustomTrust(callback)` replaces the built-in trust decision with your callback. The callback receives the presented certificate, chain when available, remote peer, original `SslPolicyErrors`, and Akka logger. Callbacks run synchronously and may run concurrently when settings are reused across connections, so make them thread-safe and reentrant. Policy immutability does not make captured callback state immutable; its lifetime and safety remain the caller's responsibility. Decide explicitly which conditions establish trust, and return `false` for anything the application should reject. `And(...)` callbacks run only after the trust callback accepts and can narrow acceptance; they cannot make a rejected peer trusted.

The TLS implementation rejects a missing certificate when one is required before calling a custom validator. A callback returning `true` does not override that requirement. Exceptions from a callback fail the handshake. Treat peer details received by a callback as unauthenticated until the policy accepts them.

Use `CustomTrust(callback)` when the application needs a complete trust decision that the built-in policies do not express. This example combines system chain errors with an application-specific subject and issuer rule, and adds an explicit hostname check. It also sets an SNI target for a connection that uses a different endpoint address:

[!code-csharp[TlsCustomTrust](../../../src/core/Akka.Docs.Tests/Networking/IO/TlsExamples.cs?name=tlsCustomTrust)]

## Connection and Certificate Lifetime

TLS is enabled for a command only when its `Tls` property is set. A command without TLS settings uses plaintext TCP; Akka.IO does not fall back to plaintext when a configured TLS handshake fails.

An outgoing `Tcp.Connected` is sent only after the TLS handshake succeeds. The handshake timeout defaults to 10 seconds and can be changed with `WithHandshakeTimeout`. If connecting or the handshake fails or times out, the commander receives the existing `Tcp.CommandFailed` message. For an incoming connection, `Tcp.Bound` reports that the listener is ready before any individual client handshake completes. An incoming connection is delivered to its handler only after its TLS handshake succeeds; a failed or timed-out handshake closes that connection while the listener remains available for other clients.

`TlsClientSettings` and `TlsServerSettings` are immutable. Their `X509Certificate2` values remain owned by the caller. Keep each certificate and its private key available until the actor system and every transport using the settings have stopped; do not dispose it immediately after creating a settings object or sending a TCP command.

`TlsCertificateLoader` loads PKCS#12 certificates from a file or byte array, or finds a certificate in an X.509 store. It requires an accessible RSA or ECDSA private key and returns a caller-owned certificate. Dispose every returned certificate after the systems and pending connections that use it have stopped. This compiled example shows each source:

[!code-csharp[TlsCertificateLoading](../../../src/core/Akka.Docs.Tests/Networking/IO/TlsExamples.cs?name=tlsCertificateLoading)]

## Use TLS With Akka.Streams

Akka.Streams uses the same `TlsClientSettings` and `TlsServerSettings` as Akka.IO. Use `BindTls` or `BindAndHandleTls` for listeners and `OutgoingConnectionTls` for clients. The outgoing methods accept either an `EndPoint` or a host and port. Existing plaintext `Bind`, `BindAndHandle`, and `OutgoingConnection` methods keep their current behavior.

This example starts a server-only TLS echo listener. `BindAndHandleTls` completes when the listener is ready; each accepted connection reaches the handler only after its TLS handshake succeeds:

[!code-csharp[TlsStreamsServer](../../../src/core/Akka.Docs.Tests/Streams/StreamTcpTlsDocTests.cs?name=tls-server)]

An outgoing graph's connection materialized value completes after authentication. Handshake rejection, timeout, cancellation, and stream shutdown use the existing Akka.IO connection lifecycle. This client sends a payload using the supplied client settings. Construct the policy and target host as shown above:

[!code-csharp[TlsStreamsClient](../../../src/core/Akka.Docs.Tests/Streams/StreamTcpTlsDocTests.cs?name=tls-client)]

TLS does not change Streams back-pressure or half-close behavior. Accepted connections still expose the same bidirectional byte flow, so completing its write side leaves the read side available when half-close is enabled. See [Working With Streaming IO](../streams/workingwithstreamingio.md) for the TCP Streams API and graph patterns.

## Use TLS With Artery Remoting

Artery TLS uses the same Akka.Streams TCP transport as the other Streams TCP APIs. Add one `ArteryTlsSetup` containing an immutable `ArteryTlsSettings` profile to the actor-system setup. When that setup is present, Artery uses TLS for inbound connections and every outbound channel, including control, ordinary, and large-message traffic. Without it, Artery continues to use plaintext TCP. A failed TLS handshake closes that connection; Artery does not retry it as plaintext.

TLS profiles and peer-validation callbacks are configured in code; there is no Artery TLS HOCON schema. Existing HOCON remains available for Artery addresses and other transport settings.

For mutual TLS, each node presents a certificate that has both client and server authentication usage. The inbound client policy and outbound server policy can be the same or distinct. Each node validates the identity of the certificate presented by its peer, while the policies decide which certificates are trusted. Artery derives the outgoing TLS target host from the remote endpoint for SNI and name validation; the policy must include `TlsCertificateValidation.ValidateHostname()` when DNS identity validation is required. The local Artery bind hostname does not override that remote identity.

This direct setup example adds TLS alongside the application's existing Artery configuration:

[!code-csharp[ArteryTlsDirectSetup](../../../src/core/Akka.Docs.Tests/Networking/ArteryTlsExamples.cs?name=arteryTlsDirectSetup)]

Artery can also use server-only TLS when clients should authenticate the server while the listener accepts connections without client certificates. Configure every participating node with a server certificate and an outbound server policy that trusts its peers. `ServerOnly` affects the inbound client-certificate requirement; it does not turn off validation of the remote server certificate on outgoing connections.

`WithArteryRemoting` provides the same profile through Akka.Hosting. It enables Artery and allows explicit bind host and port overrides. Omitted host and port values remain under the application's existing configuration. The extension preserves an already selected Cluster, Remote, or custom actor-ref provider; a local or unspecified provider is changed to Remote so that remoting is active. Setting `Tls` adds or replaces the typed Artery TLS setup. Leaving it null preserves an `ArteryTlsSetup` supplied directly to the builder.

[!code-csharp[ArteryTlsHosting](../../../src/core/Akka.Docs.Tests/Networking/ArteryTlsExamples.cs?name=arteryTlsHosting)]

Artery TLS policies authenticate transport peers; they do not authorize cluster membership or actor access. Configure trust and application-level authorization for those separate decisions. Keep the profile's certificate and private key alive until the actor system and its remoting transport have stopped, then dispose the caller-owned certificate.
