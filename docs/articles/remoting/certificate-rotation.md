---
uid: tls-certificate-rotation
title: Rotating TLS Certificates
---

# Rotating TLS Certificates

**New in Akka.NET v1.5.75:** A node can pick up a renewed TLS certificate while it runs. You install the new certificate, and connections the node builds a few seconds later use it. You do not restart the node or edit its config.

This article builds on [Network Security](xref:network-security). Read that first if TLS is new to you.

## What Rotation Does and Does Not Do

A node normally loads its certificate once, when it starts. To rotate, you give the node a _certificate source_ instead: a place to look for its certificate. The node asks the source each time it builds a connection.

Rotation does:

* Use a renewed certificate for new connections, usually within about 10 seconds of adding it. A folder or a store is read at most once every 10 seconds, in the background, and the node reuses the result in between. A callback is called every time. See [When a Renewed Certificate Is Used](#when-a-renewed-certificate-is-used) for the exact timing.
* Keep the old certificate as a fallback until you remove it.
* Keep using the last good certificate while it is in date, if a later look-up finds nothing usable. See [When a Look-Up Finds Nothing Usable](#when-a-look-up-finds-nothing-usable).

Rotation does not:

* Move open connections to the new certificate. They keep the certificate they started with until they close.
* Change which peers a node trusts. See [How Nodes Trust Each Other](#how-nodes-trust-each-other).
* Block a stolen key. Akka.Remote does not check certificate revocation.

Three sources can rotate:

| Source | How you set it up |
|--------|-------------------|
| A folder of certificate files | `TlsCertificateSource.FromFolder`, or a HOCON `path` that names a folder |
| A certificate store, found by common name | `TlsCertificateSource.FromStore`, or the HOCON `common-name` key |
| A callback that returns a certificate | `TlsCertificateSource.FromCallback` |

A fixed certificate object, a thumbprint, or a path to a single file still loads once at startup. A change to any of these needs a restart.

## Set It Up

You have two common choices. Look the certificate up in a certificate store by its common name (the name written in the certificate), or read it from a folder of files. A thumbprint (a hash that names one certificate) cannot do this job, because every renewed certificate has a new one.

### Akka.Hosting

Akka.Hosting is the recommended way. Give the options a certificate source.

**From a certificate store**, found by name. This example gives each server its own certificate, named for the server:

```csharp
var serverName = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).HostName;

builder.WithRemoting(options =>
{
    options.EnableSsl = true;
    options.Ssl.CertificateSource = TlsCertificateSource.FromStore(
        serverName, "My", StoreLocation.LocalMachine);
});
```

**From a folder of certificate files**, on any platform. The folder may hold PKCS#12 files (usually `.pfx`), PEM files (text files, such as `.pem` or `.crt`), or both:

```csharp
builder.WithRemoting(options =>
{
    options.EnableSsl = true;
    options.Ssl.CertificateSource = TlsCertificateSource.FromFolder("/etc/akka/tls", password: null);
});
```

`TlsCertificateSource` lives in the `Akka.IO` namespace. Set either `Ssl.CertificateSource` or `Ssl.X509Certificate`, not both. If you set both, the node fails at startup with a `ConfigurationException`.

Setting `Ssl.CertificateSource` turns TLS on unless `EnableSsl` is `false`. With `EnableSsl = false`, TLS stays off and the source is not used.

You can also set `Ssl.CertificateOptions.CommonName`, or point `Ssl.CertificateOptions.Path` at a folder, and the options write the HOCON for you. See [Changing an Existing Setup](#changing-an-existing-setup).

**If a library already called `WithRemoting`** and you cannot change that call, your own TLS options never reach the transport. Add a second call with only the TLS settings:

```csharp
builder.WithRemoting(new RemoteOptions
{
    EnableSsl = true,
    Ssl = new SslOptions
    {
        CertificateSource = TlsCertificateSource.FromFolder("/etc/akka/tls", password: null)
    }
});
```

The two calls combine, in either order. A setting that only one call sets stays as it is, so the library's host and port stay. A setting that both calls set takes the value from the later call. Leave `HostName` and `Port` unset in your call, and do not set `EnableSsl = false`. Give a certificate or a certificate source in one call only.

### HOCON

For a folder, point `path` at it:

```hocon
akka.remote.dot-netty.tcp {
  enable-ssl = true
  ssl.certificate {
    path = "/etc/akka/tls"
    password = "..."   # only if the files are protected
  }
}
```

`path` counts as a folder only if the folder exists when the node starts. If it does not, the node reads `path` as a single file.

The `password` key applies to PKCS#12 files and encrypted PEM keys. The `flags` key sets the key storage flags. It applies to PKCS#12 files on every platform, and to PEM certificates on Windows, where the node imports a PEM certificate and its key as PKCS#12. On Linux and macOS it has no effect on PEM certificates. `flags` is a HOCON key. In code, pass the key storage flags to the three-argument `TlsCertificateSource.FromFolder`. The Akka.Hosting options use the default key set.

For a certificate store, set `common-name`:

```hocon
akka.remote.dot-netty.tcp {
  enable-ssl = true
  ssl.certificate {
    common-name = "akka-node.contoso.com"
    store-name = "My"
    store-location = "local-machine"
  }
}
```

An empty `store-name` means `My`. `store-location` defaults to `current-user`.

If the `certificate` block sets more than one way to find the certificate, the node uses the first that applies, in this order:

1. `common-name`
2. `use-thumbprint-over-file = true`, with a `thumbprint`
3. A `path` that names a folder
4. A `path` that names a single file

HOCON cannot carry a custom validator. If you need one, use Akka.Hosting or code.

### Code

In code, pass the source to `DotNettySslSetup.FromSource`. A `DotNettySslSetup` takes precedence over the `ssl` block in HOCON, as it does with a fixed certificate.

**From a certificate store:**

[!code-csharp[RotationStoreInCode](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationStoreInCode)]

**From a folder of certificate files:**

[!code-csharp[RotationFolderInCode](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationFolderInCode)]

For a setup built with `FromSource`, the `Certificate` property throws an `InvalidOperationException` if you read it before the node has finished a look-up. After that, it returns the certificate the node last picked. Do not dispose it: the node still uses it.

To get the certificate from your own code, such as a secrets service, use `TlsCertificateSource.FromCallback`. See [Load Certificates From Azure Key Vault](#load-certificates-from-azure-key-vault). The node runs the callback on the connection path, once for every connection it opens or accepts, before it knows who the peer is. The callback must return quickly and must not block or call a network service. Keep the certificate in a field and refresh it somewhere else.

> [!WARNING]
> Keep the folder, or the common name, for this node's certificates only. The node does not check who issued a certificate it finds. If a self-signed certificate with the same common name sits in the store, or another certificate that qualifies sits in the folder, the node picks it whenever it was issued most recently. A certificate authority (CA) certificate with its private key usually has a key usage that allows only signing certificates, so the node rejects it and warns. Do not rely on that: a CA certificate with no key usage field qualifies. See [Allowed for the Job](#allowed-for-the-job). The log line that reports each pick names the certificate, so a wrong pick shows there. See [Check That It Worked](#check-that-it-worked).

## How a Node Picks Its Certificate

When the node builds a connection, it asks the source for a certificate. A folder or a store source answers from its last look-up, and reads the folder or the store at most once every 10 seconds, in the background. A callback is called every time. On each look-up, the node:

1. Takes the candidates. In a store, these are the certificates whose common name equals `common-name` in full, ignoring case: `akka-node` does not match `akka-node-extra`. In a folder, these are all the certificates in the files.
2. Keeps the candidates that are in date, allowed for the job, and have a private key the node can read.
3. Presents the one that was issued most recently, which is the one with the latest start date. If two have the same start date, the one that expires last wins, and then the one with the greater thumbprint. The choice never depends on the order of the files.

The node goes by start date, not end date, for two reasons. A certificate re-issued after a key leak can expire at the same time as the old one, and the node must still move to the new one. A CA may also shorten certificate lifetimes, so a newer certificate can expire sooner than an older one.

A certificate that is not valid yet is ignored until its start date. You can install a renewal early, and the node starts to use it from that date. The first look-up after that date picks it. That takes about a minute at most, and less when a connection asks, because a connection starts a look-up. See [When a Renewed Certificate Is Used](#when-a-renewed-certificate-is-used). An expired certificate is ignored too. The node compares all dates in UTC, so the time zone of the machine changes nothing.

### When a Renewed Certificate Is Used

A folder or a store is read in the background, at most once every 10 seconds. A look-up starts when a connection asks for the certificate, and the node also looks once a minute by itself, so a node that builds no connection still looks.

* A connection that finds the last look-up more than 10 seconds old starts a new look-up. It does not wait for it, and it still uses the certificate the node already had.
* Once a minute, the node starts a look-up by itself if the last one is more than 10 seconds old, and logs the change of certificate when that look-up finishes.
* Connections built after a look-up finishes use its pick.

So a renewed certificate is in use within about 10 seconds plus the time of one look-up, on a connection after the one that started the look-up. Every node that reads a folder or a store logs the change within about a minute of the renewed certificate being put in place, whether or not it builds a connection. If a connection uses the new certificate first, the node logs the change then.

A callback is different: the node calls it for every connection, and the connection uses what it returns. Because of that, the callback must return quickly: it runs before the node has authenticated the peer, so it must not block or call a network service.

### When a Look-Up Finds Nothing Usable

A look-up finds nothing usable when the folder is missing, empty or unreadable, the store cannot be opened, every candidate is rejected, or the callback throws. If the certificate the node used last is still in date, the node keeps using it and logs a warning. See [Check That It Worked](#check-that-it-worked). A certificate that your code disposed does not count as in date. The connection fails only when there is no earlier certificate, or the earlier one has expired or was disposed. At startup there is no earlier certificate, so a node that finds nothing usable at startup does not start.

Deleting the only certificate does not stop a running node from using it. Restart the node to drop it.

### Waiting and Failing

Only node startup waits for a look-up, and it waits at most 30 seconds from the start of the look-up. If the look-up has not finished by then, the node does not start. The error says that the look-up did not finish in time, and names the folder or the store. See [A Node Fails at Startup With "Did Not Finish in Time"](#a-node-fails-at-startup-with-did-not-finish-in-time).

A connection never waits. If the node has nothing usable to present (it has no pick, or the pick expired or was disposed), that connection fails at once with the reason the last look-up gave. Before the first look-up has finished there is no reason yet, and the error begins `No TLS certificate is available yet`. The node starts a look-up if none is running. See [A Node Logs "Cannot Build a TLS Connection"](#a-node-logs-cannot-build-a-tls-connection).

A look-up that does not finish, for example because a file on a network share does not answer, does not stop the node. The node keeps using the certificate it has, starts no second look-up, and logs a warning that the look-up "has been running since" a given time. See [The Node Logs "Look-Up Has Been Running Since"](#the-node-logs-look-up-has-been-running-since).

### Allowed for the Job

A certificate can list what it may be used for in two fields. Its _enhanced key usage_ (EKU) field names the jobs it may do. Two jobs matter here: server authentication, which proves the identity of the node that accepts a connection, and client authentication, which proves the identity of the node that opens one. Its _key usage_ field names what the key may do, such as sign data.

* With `require-mutual-authentication = true`, which is the default, a node uses one certificate for both jobs. The certificate must allow both client and server authentication. A certificate that allows only one of them is skipped.
* With mutual TLS off, the certificate needs to allow server authentication only.
* A certificate with no EKU field, or with the any-purpose usage, is allowed for both.
* If a certificate has a key usage field, it must permit digital signature. With mutual TLS off, a certificate with an RSA key may permit key encipherment instead. A certificate with no key usage field is allowed.

A CA certificate usually permits only signing certificates and signing certificate revocation lists. The node rejects a certificate with that key usage.

### Make a Test Certificate

For testing only. This makes a self-signed certificate that passes the checks above: it has a subject alternative name, both extended key usages, and the key usages `digitalSignature` and `keyEncipherment` (the second is for RSA keys). On Linux or macOS, with OpenSSL 1.1.1 or later:

```bash
openssl req -x509 -newkey rsa:2048 -nodes -keyout node.key -out node.crt -days 90 \
  -subj "/CN=akka-node.contoso.com" \
  -addext "basicConstraints=critical,CA:FALSE" \
  -addext "subjectAltName=DNS:akka-node.contoso.com" \
  -addext "extendedKeyUsage=serverAuth,clientAuth" \
  -addext "keyUsage=digitalSignature,keyEncipherment"
openssl pkcs12 -export -inkey node.key -in node.crt -out node.pfx -passout pass:
```

The `.pfx` has an empty password, so pass `password: null` to `TlsCertificateSource.FromFolder`. On Windows PowerShell, in an administrator shell, this puts the certificate in the local machine store:

```powershell
New-SelfSignedCertificate -Subject "CN=akka-node.contoso.com" -DnsName "akka-node.contoso.com" `
  -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy Exportable `
  -KeyUsage DigitalSignature, KeyEncipherment `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1,1.3.6.1.5.5.7.3.2") `
  -NotAfter (Get-Date).AddDays(90) -CertStoreLocation Cert:\LocalMachine\My
```

Nodes accept a self-signed certificate from a peer only if they skip chain validation (`suppress-validation = true`), pin the certificate (see [By a Fixed List](#by-a-fixed-list)), or trust it. See [How Nodes Trust Each Other](#how-nodes-trust-each-other).

### Certificate Files

A folder may hold PKCS#12 files and PEM files together.

* Akka.NET decides the format from the content, not the name. A file that contains `-----BEGIN` is PEM, even if text comes before the first block. Any other file is PKCS#12.
* A PEM certificate needs its private key, in the same file or in another file in the folder. Akka.NET pairs each certificate with the key that fits it, whatever the files are called. Keys can be RSA or ECDSA. A PEM certificate with no matching key in the folder gets a warning when it would otherwise have been the pick.
* `password` opens protected PKCS#12 files and encrypted PEM keys.
* The node reads only the files directly in the folder, not files in folders inside it. It ignores entries whose name starts with `..`, and they do not count toward the limit of 100 files below.
* The node follows symbolic links, and the checks below apply to the file the link points to. A secret mounted into a Kubernetes pod works, where each file is a link to `..data/name` and `..data` is a link to a dated folder.
* The node skips a file that is larger than 256 KB, that is empty, or that is not a regular file, such as a named pipe.
* The node reads only the first 100 files by name, and logs one warning about the rest.
* The PEM files of one folder give at most 100 certificates and 100 private keys in all. The node reads them in file name order, and logs one warning when there are more. It does not read the others, so a certificate whose key comes after the 100th key has no key in the folder.
* A file that is skipped, or cannot be read or parsed, gets a warning, and the other files still count. The node does not parse a file whose content failed to load again for 5 minutes, unless its content changes. The same goes for a file that holds a certificate whose private key the node could not open.
* If two files hold the same certificate, for example `node.cer` and `node.pfx`, the node uses the copy that has a usable private key.
* PEM files need .NET 6 or later. On .NET Framework, or any runtime older than .NET 6, the node skips PEM files and logs a warning.

## Rotate a Certificate

1. **Make sure peers will trust the new certificate.** If your certificates come from a CA and chain validation is on, peers already trust anything the CA signs, and you can skip this step. If your nodes pin thumbprints or trust a self-signed certificate, make every node trust the new certificate before you install it anywhere. See [How Nodes Trust Each Other](#how-nodes-trust-each-other).
2. **Add the renewed certificate on each node, beside the old one.**
   * Files: copy the renewed certificate into the folder. A direct copy is safe. If the node reads the file while it is still being written, it skips the file and warns that it failed to load. The other files still count, so the node keeps its certificate. It reads the file again when the content changes, and in any case after 5 minutes. To avoid even the warning, copy to a temporary name in the same folder that starts with `..`, which the node ignores, then rename it:

     ```bash
     cp node-2028.pfx /etc/akka/tls/..node-2028.pfx.tmp
     mv /etc/akka/tls/..node-2028.pfx.tmp /etc/akka/tls/node-2028.pfx
     ```

   * Store: install the renewed certificate with the same common name, and give the node's account read access to its private key.
3. **Wait about a minute, and check the log.** The node looks in the folder or the store once a minute by itself, and logs the change within about a minute, whether or not it builds a connection. See [Check That It Worked](#check-that-it-worked). A connection built in between starts a look-up when the last one is more than 10 seconds old, and still uses the old certificate. Connections built after the look-up finishes use the new certificate. Open connections keep the old certificate until they close. See [When a Renewed Certificate Is Used](#when-a-renewed-certificate-is-used).
4. **Remove the old certificate when you are ready.** Keep the new certificate in place. If you delete the only certificate, a running node keeps using it. See [When a Look-Up Finds Nothing Usable](#when-a-look-up-finds-nothing-usable).

You never overwrite anything. To undo a rotation, remove the new certificate. After the next look-up, new connections go back to the old one.

> [!WARNING]
> A node that dials a peer and fails the TLS handshake runs `CoordinatedShutdown` with exit code 79. If a node does not trust your new certificate, that node shuts down the first time it dials a node that presents it. Settle trust first.

You can stage a renewal with a start date in the future. The node ignores the certificate until that time. The first look-up after that time picks it, within about a minute, and sooner when a connection asks. Machines whose clocks differ by a few seconds can disagree for a moment about whether the certificate is valid yet, and a node that rejects a peer's certificate while dialing it shuts down. This is one more reason to make every node trust the new certificate first, and to keep the clocks of your machines in sync.

## Changing an Existing Setup

You change your setup once. After that, a new certificate needs no config change. You add the new certificate beside the old one, in the folder or the store, and the node uses it.

> [!WARNING]
> Check the certificate before you switch. A certificate source checks that the certificate may do the job. See [Allowed for the Job](#allowed-for-the-job). A single file `path`, a `thumbprint`, and an `X509Certificate` or `DotNettySslSetup` with a certificate do not check. With mutual TLS on, which is the default, a source needs a certificate that allows both server and client authentication. A certificate that allows server authentication only works as a single file, but if the same file sits in a folder, the node does not start. The error says `its enhanced key usage does not permit client authentication`.
>
> To print the extended key usage and key usage of a `.pfx` file on Linux or macOS (OpenSSL 1.1.1 or later, which asks for the file's password):
>
> ```bash
> openssl pkcs12 -in node.pfx -nokeys -clcerts | openssl x509 -noout -subject -ext extendedKeyUsage,keyUsage
> ```
>
> On Windows PowerShell:
>
> ```powershell
> $cert = Get-PfxCertificate -FilePath .\node.pfx
> $cert.EnhancedKeyUsageList | Select-Object FriendlyName
> $cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension] } | ForEach-Object { $_.KeyUsages }
> ```
>
> Look for both `TLS Web Server Authentication` and `TLS Web Client Authentication` (PowerShell: `Server Authentication` and `Client Authentication`). A certificate with no extended key usage at all is allowed. If it has a key usage field, that field must list `Digital Signature`.
>
> If client authentication is missing, issue a new certificate that allows both. Or, if the node does not need to present a client certificate, turn mutual TLS off on every node (`require-mutual-authentication = false`). A source then needs server authentication only. Mutual TLS off also means peers no longer check the certificate of the node that dials them. See [Network Security](xref:network-security#understanding-mutual-tls-mtls-vs-standard-tls-v1552). For a test certificate that qualifies, see [Make a Test Certificate](#make-a-test-certificate).

Roll the change out with a normal rolling restart. Once the certificate passes that check, nodes on the old setup and nodes on the new setup present the same certificate, so they keep connecting to each other. Before you start, check that the store or folder holds no other usable certificate that was issued after the one you use today. A thumbprint names one certificate, but the new setup picks the one issued most recently.

### Akka.Hosting

For a store lookup, replace the thumbprint with the common name. Keep `EnableSsl = true`, which TLS needs:

```csharp
// Before
builder.WithRemoting(options =>
{
    options.EnableSsl = true;
    options.Ssl.CertificateOptions = new SslCertificateOptions
    {
        UseThumbprintOverFile = true,
        Thumbprint = "2531c78c51e5041d02564697a88af8bc7a7ce3e3",
        StoreName = "My",
        StoreLocation = "local-machine"
    };
});

// After
builder.WithRemoting(options =>
{
    options.EnableSsl = true;
    options.Ssl.CertificateOptions = new SslCertificateOptions
    {
        CommonName = "akka-node.contoso.com",
        StoreName = "My",
        StoreLocation = "local-machine"
    };
});
```

For certificate files, point `Path` at the folder that holds them. If you set `X509Certificate` on `SslOptions` today, remove it and use `CertificateOptions` as above, or set `CertificateSource`.

A `CustomValidator` keeps working when you use a `CertificateSource`, a `CommonName` or a folder `Path`. It also applies to a thumbprint or a single file in the options, but those load once and do not rotate.

### HOCON With a Thumbprint

```hocon
# Before
certificate {
  use-thumbprint-over-file = true
  thumbprint = "2531c78c51e5041d02564697a88af8bc7a7ce3e3"
  store-name = "My"
  store-location = "local-machine"
}

# After
certificate {
  common-name = "akka-node.contoso.com"
  store-name = "My"
  store-location = "local-machine"
}
```

Remove `use-thumbprint-over-file` and `thumbprint`. Every other `ssl` setting stays as it is.

To get the common name of the certificate you use today:

```powershell
(Get-Item Cert:\LocalMachine\My\2531C78C51E5041D02564697A88AF8BC7A7CE3E3).GetNameInfo('SimpleName', $false)
```

### HOCON With a Certificate File

```hocon
# Before
certificate {
  path = "/etc/akka/tls/node.pfx"
  password = "..."
}

# After
certificate {
  path = "/etc/akka/tls"
  password = "..."
}
```

Point `path` at the folder that holds the file. To rotate, put the new file in the same folder. This works the same on Windows and Linux, and with a secret mounted into a container.

### Code

Replace the `DotNettySslSetup` constructor with `DotNettySslSetup.FromSource`. The other arguments keep their meaning.

[!code-csharp[RotationCodeBefore](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationCodeBefore)]

[!code-csharp[RotationCodeAfter](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationCodeAfter)]

For certificate files, pass `TlsCertificateSource.FromFolder(path, password)` as the source. If you use one of the shorter constructors today, pass the values it defaults to: mutual TLS on, hostname check off, no validator.

## How Nodes Trust Each Other

Rotation changes the certificate a node presents. Each node also decides which certificates it accepts, and that rule must not name a certificate you are about to replace. A renewed certificate has a new thumbprint.

### By CA and Name

A peer must present a certificate from a CA that the node trusts, with the name you expect. A renewed certificate passes with no change on any node, so this fits rotation best. Both helpers are in the `CertificateValidation` class that [Network Security](xref:network-security) describes.

[!code-csharp[RotationTrustByName](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationTrustByName)]

This example gives every node the same name. If each node has its own name, use `ValidateSubject` with a pattern such as `CN=akka-node-*` in place of the name check.

### By Your Own CA

Chain validation accepts a certificate from any CA the machine trusts. To accept only certificates that your own CA issued, add an issuer check:

[!code-csharp[RotationTrustOwnCa](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationTrustOwnCa)]

A renewed certificate from the same CA passes with no change.

### By a Fixed List

`PinnedCertificate` accepts a peer only if its certificate has one of the thumbprints you name. The list is fixed when the node starts, so a pin list cannot follow a renewed certificate. To rotate, declare the next certificate beside the current one, deploy that to every node, and only then install the next certificate.

[!code-csharp[RotationTrustPinnedList](../../../src/core/Akka.Docs.Tests/Configuration/TlsCertificateRotationSample.cs?name=RotationTrustPinnedList)]

## Load Certificates From Azure Key Vault

There are two ways.

**Let Azure deliver the certificate to the machine.** This needs no code. Point the node at where the certificate lands, with `common-name` or a folder `path` as shown above.

* On Windows, the Key Vault VM extension installs certificates into the Windows store, `LocalMachine\My` unless you set another.
* On Linux, the Key Vault VM extension writes PEM files, under `/var/lib/waagent/Microsoft.Azure.KeyVault.Store/` unless you set another folder. The certificate chain goes in one file and its private key in another. The node's account needs read access to both.
* In Kubernetes, the Secrets Store CSI driver mounts certificates into a pod as files. Turn on the driver's rotation, or the mounted files stay the same when Key Vault renews the certificate.

**Call Key Vault yourself.** The node asks for its certificate each time it builds a connection, so do not call the vault from the callback. Fetch on a timer, and hand the node the latest copy. The identity that calls the vault needs the `certificates/get` and `secrets/get` permissions. The certificate's policy in Key Vault must mark the key as exportable. If it does not, Key Vault leaves the private key out, and the node rejects the certificate because it has no private key.

```csharp
// Packages: Azure.Security.KeyVault.Certificates, Azure.Identity
public sealed class KeyVaultCertificate : IDisposable
{
    private readonly CertificateClient _client;
    private readonly string _name;
    private readonly ILogger _log;
    private readonly Timer _timer;
    private volatile X509Certificate2 _current;

    public KeyVaultCertificate(Uri vault, string name, TimeSpan refreshEvery, ILogger log)
    {
        _client = new CertificateClient(vault, new DefaultAzureCredential());
        _name = name;
        _log = log;
        _current = _client.DownloadCertificate(name).Value;   // fail at startup if the vault is unreachable
        _timer = new Timer(_ => Refresh(), null, refreshEvery, refreshEvery);
    }

    // The certificate with its private key, latest version.
    public X509Certificate2 Current => _current;

    private void Refresh()
    {
        try
        {
            var latest = _client.DownloadCertificate(_name).Value;

            // Do not dispose the old certificate: a handshake in progress may still use it.
            if (latest.Thumbprint != _current.Thumbprint)
                _current = latest;
        }
        catch (Exception ex)
        {
            // Keep the certificate we have and try again next time.
            _log.LogWarning(ex, "Could not refresh certificate {Name} from Key Vault", _name);
        }
    }

    public void Dispose() => _timer.Dispose();
}
```

Hand the node the latest copy with `FromCallback`:

```csharp
var vaultCertificate = new KeyVaultCertificate(
    new Uri("https://contoso.vault.azure.net/"), "akka-node", TimeSpan.FromMinutes(15), log);

var setup = DotNettySslSetup.FromSource(
    TlsCertificateSource.FromCallback(() => vaultCertificate.Current),
    suppressValidation: false,
    requireMutualAuthentication: true,
    validateCertificateHostname: false,
    customValidator: null);
```

When Key Vault renews the certificate, the next refresh picks up the new version, and new connections use it. If the vault cannot be reached, the node carries on with the certificate it has.

The node checks the certificate the callback returns each time: it must be in date, allowed for the job, and have a private key. If it is not, or the callback throws or returns null, the node keeps using the last certificate the callback gave it, while that one is in date, and logs a warning. If there is no such certificate, that connection fails. The callback may run on several threads at once. The node never disposes the certificate you return, so the callback's owner does.

## Check That It Worked

A node logs each change of certificate at the `Info` level. A line names a certificate by its thumbprint, then its subject and its expiry date. The first line appears when the node starts, and shows `none` as the old certificate:

```text
TLS client and server certificate changed from none to 2531C78C51E5041D02564697A88AF8BC7A7CE3E3 (subject 'CN=akka-node.contoso.com', expires 2027-03-01 12:00:00Z)
```

After you add a renewed certificate, every node that reads a folder or a store logs the change within about a minute, whether or not it builds a connection, so you can confirm a rotation from the logs. The node looks once a minute by itself and logs the change when that look-up finishes. If a connection starts a look-up sooner, the first connection built after it finishes logs the change instead. A node with a callback has no look-up: it logs the change on the first connection that gets the new certificate. The connection that starts a look-up logs nothing, because it still uses the old certificate:

```text
TLS client and server certificate changed from 2531C78C51E5041D02564697A88AF8BC7A7CE3E3 (subject 'CN=akka-node.contoso.com', expires 2027-03-01 12:00:00Z) to 9F2A4C71D3E85B06A1C94E27B8D05F3C6A71E4B2 (subject 'CN=akka-node.contoso.com', expires 2028-03-01 12:00:00Z)
```

Times are in UTC. The words after `TLS` name the job the certificate does, and they depend on the mutual TLS setting:

* With `require-mutual-authentication = true`, the default, a line starts `TLS client and server certificate`. The node uses one certificate in both directions, so one line covers connections it opens and connections it accepts.
* With mutual TLS off, a line starts `TLS server certificate`. The node never asks the source for a connection it opens, so a line covers the connections it accepts. The first line still appears when the node starts.

The examples here and below show the default.

If the node skips a certificate that would have won, because it was issued after the one in use, it logs a warning:

```text
TLS client and server certificate 9F2A4C71D3E85B06A1C94E27B8D05F3C6A71E4B2 (subject 'CN=akka-node.contoso.com', expires 2028-03-01 12:00:00Z) in file 'node-2028.pfx' was skipped: it has no private key this process can open
```

The reason can also be one of these:

* `its enhanced key usage does not permit client authentication`, or `server`, or `client and server`, for the use the certificate lacks.
* `its key usage does not permit digital signature`.
* `it has no private key in the folder`, for a PEM certificate with no matching key in the folder.

For a certificate from a store, the line has no `in file` part.

A file that will not load gets its own warning:

```text
TLS certificate skipped: file 'node-2028.pfx' failed to load: <reason from .NET>
```

The reason can also come from the node. An empty file, or one that is not a regular file, such as a named pipe, reads `file 'node-2028.pfx' failed to load: the file is empty or is not a regular file.` A file over the size limit reads `file 'node-2028.pfx' failed to load: the file is larger than 256 KB, so it is too large to be a certificate.` A file the node cannot read from disk reads `file 'node-2028.pfx' could not be read: <reason from .NET>`. The other files still count.

If the folder holds more than 100 files, the node reads the first 100 by name and warns about the rest:

```text
TLS certificate skipped: the folder holds 152 files and only the first 100 by name are read, so the 52 files from 'junk-099' on are not read.
```

If the PEM files in the folder hold more than 100 certificates or more than 100 private keys, the node reads the first 100 of each in file name order and warns about the rest:

```text
TLS certificate skipped: the PEM files in the folder hold more than 100 certificates, and only the first 100 certificates and the first 100 private keys in file name order are read, so the others are not read.
```

If a look-up finds nothing usable and the node keeps its last certificate, it logs a warning. The text after the colon is the reason, here a folder that is gone:

```text
TLS client and server certificate look-up failed, so the node keeps using the previous certificate 2531C78C51E5041D02564697A88AF8BC7A7CE3E3 (subject 'CN=akka-node.contoso.com', expires 2027-03-01 12:00:00Z): No certificate in folder '/etc/akka/tls' can be used as a client and server. The folder does not exist.
```

The node skips an expired certificate, and one that is not valid yet, without a warning.

Each warning repeats at most once every 5 minutes while its cause lasts. While the node uses the previous certificate because look-ups fail, it repeats the warning at that pace. See [When a Look-Up Finds Nothing Usable](#when-a-look-up-finds-nothing-usable). If more than 100 different warnings come within 5 minutes, the node drops the new ones and logs one line:

```text
Some TLS certificate warnings were not logged: more than 100 different warnings came within 5 minutes.
```

### See What the Node Sees

To list the candidates in a Windows store:

```powershell
Get-ChildItem Cert:\LocalMachine\My |
  Where-Object { $_.Subject -match '(^|, )CN=akka-node\.contoso\.com(,|$)' } |
  Sort-Object NotBefore -Descending |
  Select-Object Thumbprint, NotBefore, NotAfter, HasPrivateKey,
    @{ Name = 'Usage'; Expression = { $_.EnhancedKeyUsageList.FriendlyName -join ', ' } }
```

To check a PKCS#12 file:

```bash
openssl pkcs12 -in node.pfx -nokeys | openssl x509 -noout -subject -startdate -enddate -ext extendedKeyUsage
```

To check a PEM file:

```bash
openssl x509 -in node.pem -noout -subject -startdate -enddate -ext extendedKeyUsage
```

To see which certificate a node presents on a new connection:

```bash
openssl s_client -connect <host>:<port> </dev/null 2>/dev/null | openssl x509 -noout -fingerprint -sha1 -subject -dates
```

With mutual TLS on, the node asks the probe for a client certificate, and the probe has none. The node logs a `TLS handshake failed on channel` error for each probe. That error comes from the probe, not from the cluster, and the node stays up.

## Rotate With a Rolling Restart

You do not need this feature to avoid a full shutdown. With certificates from a CA and chain validation on, nodes on the old certificate and nodes on the new certificate work together. This holds when `validate-certificate-hostname` is off, which is the default since v1.5.53, or when your name check accepts the new certificate too.

1. Install the new certificate on every machine.
2. For each node in turn, put the new thumbprint in its config and restart it.

The feature in this article removes the config edit and the restart. This approach does not work with a fixed pin list. See [By a Fixed List](#by-a-fixed-list).

## Troubleshooting

### The Node Still Presents the Old Certificate

Check these, in order:

* **The setup cannot rotate.** A thumbprint, a single file path, and a fixed certificate object load once at startup. Switch to a folder, a common name or a callback.
* **The connection is old.** Open connections keep the certificate they started with. Only new connections use the new certificate.
* **The node has not looked yet.** A folder or a store is read in the background, at most once every 10 seconds when a connection asks, and once a minute by itself. The connection that starts a look-up still uses the old certificate. Connections built after the look-up finishes use the new one. The node logs the change within about a minute. Wait a minute and check again.
* **Another certificate was issued after the new one.** The node presents the certificate with the latest start date, whatever the end dates are. List the start dates, as shown above, and remove the certificate you do not want.
* **The look-up failed or is slow.** If the folder or the store cannot be read, the node keeps its last certificate and logs a warning that begins `TLS client and server certificate look-up failed`. If a look-up does not finish, the node logs a warning that the look-up has been running since a given time. Fix the folder or the store. The new certificate shows after the next look-up.
* **The node skipped the new certificate.** Look in the node's log for a warning that names it. With a store, run the PowerShell command above: the new certificate must have the exact common name, a start date in the past, a private key the node's account can read, an EKU that allows its job, and, if it has a key usage field, one that permits digital signature. With files, check that each PEM certificate has a key file in the same folder, and that no file is empty, larger than 256 KB, or past the first 100 files by name or past the first 100 PEM certificates or private keys.
* **The new certificate is not in effect yet.** A certificate whose start date is still ahead is skipped without a warning. The first look-up after the start date picks it.

### A Node Fails at Startup With "No Certificate Can Be Used"

A node that finds no usable certificate when it starts does not start. The error is a `ConfigurationException`, which may sit inside another exception. Its message begins `No certificate in` and names the folder or the store. It then says which job it needed, `can be used as a client and server` with mutual TLS on or `can be used as a server` with it off, and lists each rejected certificate and the reason, and each file that would not load. It lists at most 10 of them, and then says `N more not listed.` A message that says the folder does not exist or is empty, or that the store has no certificate with that name, points at the setup itself. If the node cannot open the store, the message reads `Could not open certificate store`. For a callback, the message is the one the callback threw, or says that it returned null or a certificate that cannot be used.

If `path` names a folder that does not exist when the node starts, the node reads it as a single file, and fails because it cannot find that file. With `TlsCertificateSource.FromFolder` in code, a missing folder fails the startup, and the message says `The folder does not exist.`

### A Node Fails at Startup With "Did Not Finish in Time"

The startup check waits for the first look-up, but not for more than 30 seconds from the start of the look-up. If the look-up is still running then, the node does not start. The message begins `The TLS certificate look-up in` and names the folder or the store:

```text
The TLS certificate look-up in folder '/etc/akka/tls' did not finish in time: it started at 2027-02-01 10:15:00Z and has not finished after 30 seconds, so there is no certificate to use as a client and server. A file in the folder that cannot be read, such as a file on a network share that does not answer, can cause this.
```

For a store, the message has no sentence about files. Find the file or the store that does not answer, fix it or remove it, and start the node again.

### A Node Logs "Cannot Build a TLS Connection"

After startup, a node that finds no usable certificate keeps using its last certificate while that one is in date. See [When a Look-Up Finds Nothing Usable](#when-a-look-up-finds-nothing-usable). Only when it has nothing usable to present does it fail the connection attempt, log an error, and stay up. That happens when it has no earlier certificate, or the earlier one has expired or was disposed, or its first look-up has not finished. The error ends with the cause:

```text
Cannot build a TLS connection to [akka.tcp://MySystem@10.0.0.5:4053]: No certificate in folder '/etc/akka/tls' can be used as a client and server. Rejected: 2531C78C51E5041D02564697A88AF8BC7A7CE3E3 (subject 'CN=akka-node.contoso.com', expires 2027-03-01 12:00:00Z) in file 'node.pfx': it expired at 2027-03-01 12:00:00Z.
```

For a connection the node accepts, it reads `Cannot build a TLS connection for the incoming connection from [...]:` and then the cause. The cause carries the same details as the startup failure, with the same job text. A peer that dials this node sees its TLS handshake fail, and a node that fails a handshake it started shuts down with exit code 79 (see below). Add a usable certificate.

A connection never waits for a look-up. When the node has no pick, or its pick has expired or was disposed, the connection fails at once with the reason the last look-up gave, and the node starts a look-up if none is running. A connection that finds the last look-up more than 10 seconds old starts a new one, and still fails with the reason of the last one. That reason stays the same from one look-up to the next while its cause lasts. If the last look-up picked a certificate that has since expired or was disposed, the cause reads `The TLS certificate <certificate> is no longer in date or was disposed, and no other was found.`, where `<certificate>` is the thumbprint, subject and expiry date. Connections built after a look-up that finds the certificate you added use it.

A node that builds no connection still reports this. When its own check once a minute finds nothing usable to present, it logs the cause by itself as an error, at most every 5 minutes while the cause lasts.

Before the first look-up has finished there is no reason yet. The node waits for that look-up at startup, so a running node does not show this, but reading the `Certificate` property early does. The text begins `No TLS certificate is available yet`:

```text
No TLS certificate is available yet to use as a client and server: the look-up in folder '/etc/akka/tls' has not finished. A connection does not wait for it; connections built after it finishes use its pick.
```

The node logs this error at most once a minute for each direction and each cause. For a failure the node words itself, the cause is its message. A certificate from your callback that cannot be used counts as one cause, whatever its thumbprint. For what your own callback throws, the cause is the exception type, so a message that differs on every call is logged once. If more than 100 different causes come within a minute, it drops the new ones and logs one line:

```text
Some "Cannot build a TLS connection" errors were not logged: more than 100 different causes came within a minute.
```

### The Node Logs "Look-Up Has Been Running Since"

A look-up that does not finish, for example because a file on a network share does not answer, does not stop the node. The node keeps using the certificate it has and starts no second look-up. When a connection, or the node's own check once a minute, finds that the look-up has run for more than 30 seconds, the node logs a warning:

```text
TLS certificate look-up in folder '/etc/akka/tls' has been running since 2027-02-01 10:15:00Z and has not finished, so the node keeps using the certificates it has and does not look again until it finishes. A file in the folder that cannot be read, such as a file on a network share that does not answer, can cause this.
```

The warning repeats at most every 5 minutes. For a store, it has no sentence about files. Fix the file, the share or the store that does not answer. The node uses the result when the look-up finishes. If the certificate the node has expires first, connections fail with the reason of the last look-up that finished, such as `The TLS certificate <certificate> is no longer in date or was disposed, and no other was found.`

### A Node Shut Down With Exit Code 79

The node dialed a peer, and the TLS handshake failed. The node logs `TLS handshake failed on channel`, then runs `CoordinatedShutdown` with exit code 79, and logs `Exiting with [ExitCode:79]`. The process exits only if `akka.coordinated-shutdown.exit-clr = on`. Then, with the default `terminate-actor-system = on`, it exits with code 0, or with 79 if the actor system takes longer than the `actor-system-terminate` phase timeout to stop. Make this node trust the peer's certificate, then start it again. See [Network Security](xref:network-security#a-node-shuts-down-with-exit-code-79).

### The Node Logs "PEM Needs .NET 6 or Later"

The folder holds PEM files, and the node runs on .NET Framework or a runtime older than .NET 6. The node skips the PEM files and logs this, at most every 5 minutes:

```text
TLS certificate folder '/etc/akka/tls' holds PEM files, such as 'node.pem'. They are skipped: PEM needs .NET 6 or later.
```

Use PKCS#12 files, or a certificate store, on those runtimes.

### Private Key Access on Windows

A renewed certificate has a new private key, and the node's account needs read access to it. Until it has that, the node keeps using the old certificate and logs a warning that names the one it skipped, with the reason `it has no private key this process can open`. You do not need to restart the node. It reads a store certificate whose key it could not open again at the next look-up. So when you grant the account access to the key, or install the certificate again with its key, the node picks it up after the next look-up. For a file, the node reads it again after 5 minutes, or at once when the file changes. This is the usual fix when the service account cannot read the new private key. See [Common Private Key Permission Issues](xref:network-security#common-private-key-permission-issues) for the commands.
