---
uid: network-security
title: Network Security
---

# Akka.Remote Security

## Important Context: When You Need TLS

**Akka.Remote is designed for internal cluster communication and should NOT be exposed to the public internet.** Most Akka.NET deployments run within:

* Private networks (VPNs, VPCs)
* Internal data centers
* Kubernetes clusters with network policies
* Behind firewalls with strict ingress rules

### When TLS Is Optional

For many deployments, TLS is not strictly necessary:

* **Internal networks only** - If your cluster runs entirely within a trusted network boundary
* **Development/staging environments** - Where data sensitivity is low
* **Kubernetes with network policies** - Where the container network provides isolation

### When TLS Is Recommended

You should enable TLS when:

* **Crossing network boundaries** - Communication between data centers or cloud regions
* **Public internet transit** - Any traffic over public networks (even with VPN)
* **Compliance requirements** - PCI-DSS, HIPAA, or other regulatory needs
* **Defense-in-depth** - Additional security layer even on private networks
* **Multi-tenant environments** - Shared infrastructure with other applications

## Security Layers

Akka.Remote security operates on three complementary layers:

1. **Network Isolation** - Using VPNs or private networks to restrict which machines can reach your actor systems
2. **Transport Encryption** - Using TLS to encrypt all communication between nodes
3. **Authentication** - Using mutual TLS to verify the identity of all connecting nodes

You should use **all three layers** in production for defense-in-depth security.

## TLS (Transport Layer Security) Overview

TLS encryption was introduced in Akka.NET v1.2 with the DotNetty transport. It provides:

**What TLS Protects Against:**

* Eavesdropping (all messages are encrypted)
* Man-in-the-middle attacks (certificates verify server identity)
* Network packet injection (cryptographic integrity checks)

**What TLS Does NOT Protect Against:**

* Misconfigured certificates (see startup validation below)
* Compromised private keys (rotate certificates regularly)
* Application-level authorization (implement this separately)

## Certificate Validation: Independent Control

**New in Akka.NET v1.5.52+:** Certificate validation is now split into two independent settings for greater flexibility.

### Two Types of Validation

1. **Chain Validation** (`suppress-validation`) - Validates certificate against trusted CAs
2. **Hostname Validation** (`validate-certificate-hostname`) - Validates certificate CN/SAN matches target hostname

These settings are **independent** and can be configured separately based on your deployment scenario.

### Chain Validation

The `suppress-validation` setting controls whether the certificate chain is validated against trusted root CAs.

**Default Certificate Stores Used:**

When `suppress-validation = false`, .NET's `SslStream` validates certificates against the operating system's trusted root certificate stores:

* **Windows**: Uses the [Windows Certificate Store](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/local-machine-and-current-user-certificate-stores) - specifically the `Trusted Root Certification Authorities` store
* **Linux**: Uses the system's CA bundle (typically `/etc/ssl/certs/ca-certificates.crt` or `/etc/pki/tls/certs/ca-bundle.crt`)
* **macOS**: Uses the Keychain Access Trusted Certificates

The validation process follows [RFC 5280 (X.509 PKI Certificate and CRL Profile)](https://datatracker.ietf.org/doc/html/rfc5280) and [RFC 6125 (Service Identity Verification)](https://datatracker.ietf.org/doc/html/rfc6125).

#### Enabled (Recommended)

When `suppress-validation = false` (the default when SSL is enabled):

**What it validates:**

* Certificate chain against system trusted root CAs
* Certificate expiration dates
* Certificate hasn't been revoked (if CRL/OCSP configured)

**Does NOT validate:**

* Hostname matching (see Hostname Validation section below)

**When to use:** Always in production and any networked environment.

#### Disabled (Use With Caution)

When `suppress-validation = true`:

**What it skips:**

* Certificate chain validation (accepts self-signed certificates)
* Expiration date checks
* CA trust checks

**When it's acceptable:**

* Local development on `localhost` only
* Automated testing with self-signed test certificates
* Initial TLS setup/debugging before obtaining proper certificates

**When it's NOT acceptable:**

* Any production environment
* Any network-accessible environment (dev, staging, QA)
* Any environment processing sensitive data
* Any multi-tenant environment

### Hostname Validation

**New in v1.5.52+:** The `validate-certificate-hostname` setting controls whether the certificate CN/SAN must match the target hostname.

**IMPORTANT: This setting defaults to `false` (disabled).** Hostname validation is NOT performed by default to support common Akka.NET deployment patterns like mutual TLS with per-node certificates and IP-based connections.

#### Disabled (Default)

When `validate-certificate-hostname = false` (the default):

**What it does:**

* Skips hostname validation
* Only validates certificate chain (if `suppress-validation = false`)

**When to use:**

* **Mutual TLS with per-node certificates** - Each node has its own unique certificate
* **IP-based connections** - Connecting via IP addresses instead of DNS names
* **Dynamic service discovery** - Hostnames change frequently (Kubernetes, auto-scaling)
* **Internal P2P clusters** - All nodes are trusted and mutually authenticated

**This is the default** for backward compatibility and to support common Akka.NET cluster patterns.

#### Enabled

When `validate-certificate-hostname = true`:

**What it validates:**

* Certificate CN (Common Name) or SAN (Subject Alternative Name) must match the target hostname
* Traditional TLS hostname validation as used in HTTPS

**When to use:**

* **Client-server architecture** - Clients connecting to known server hostnames
* **Shared certificates** - Same certificate used across multiple nodes
* **DNS-based connections** - Connecting via stable DNS names
* **Maximum security** - Traditional browser-like TLS validation

### Validation Mode Combinations

| suppress-validation | validate-certificate-hostname | Use Case |
|---------------------|-------------------------------|----------|
| `false` | `false` | **Common**: Mutual TLS clusters with per-node certs |
| `false` | `true` | **Traditional**: Client-server TLS with DNS names |
| `true` | `false` | **Dev/Test**: Self-signed certs, no hostname checks |
| `true` | `true` | **Test Only**: Self-signed certs WITH hostname validation |

### Self-Signed Certificates: The Right Way

If you must use self-signed certificates (development/testing):

#### Option 1: Trust the Self-Signed CA (Better)

```powershell
# Generate self-signed CA
$ca = New-SelfSignedCertificate -Subject "CN=Dev-CA" -CertStoreLocation Cert:\CurrentUser\My -KeyUsage CertSign

# Export and import to Trusted Root
Export-Certificate -Cert $ca -FilePath dev-ca.cer
Import-Certificate -FilePath dev-ca.cer -CertStoreLocation Cert:\LocalMachine\Root

# Generate server cert signed by CA
New-SelfSignedCertificate -Subject "CN=localhost" -Signer $ca -CertStoreLocation Cert:\LocalMachine\My
```

**Configuration:**

```hocon
akka.remote.dot-netty.tcp.ssl {
  suppress-validation = false  # ✓ Still validates, but trusts your CA
  certificate {
    use-thumbprint-over-file = true
    thumbprint = "server-cert-thumbprint"
  }
}
```

**Pros:**

* Maintains validation checks
* Catches expiration/configuration errors
* More realistic test environment

#### Option 2: Suppress Validation (Quick but Dangerous)

```hocon
akka.remote.dot-netty.tcp.ssl {
  suppress-validation = true  # ⚠️ Development ONLY
  certificate {
    path = "self-signed.pfx"
    password = "password"
  }
}
```

**Pros:**

* Quick setup
* No certificate installation needed

**Cons:**

* Doesn't catch real configuration errors
* False sense of security
* Easy to accidentally deploy to production

**WARNING:** Never commit `suppress-validation = true` to version control for production configs. Use environment-specific configuration files.

## Certificate Configuration

### Option 1: Certificate File (Recommended for Development)

```hocon
akka.remote.dot-netty.tcp {
  enable-ssl = true
  ssl {
    suppress-validation = false  # IMPORTANT: Never use true in production!
    certificate {
      path = "path/to/certificate.pfx"
      password = "certificate-password"
      # Optional: Specify key storage flags
      flags = [ "exportable" ]
    }
  }
}
```

**When to use:** Development, testing, containerized environments where you can mount certificate files.

**Pros:**

* Easy to deploy with containers
* Simple to version control (store path, not certificate)
* Works well with configuration management tools

**Cons:**

* Certificate files can be copied if filesystem is compromised
* Requires file system access for certificate deployment

### Option 2: Windows Certificate Store (Recommended for Production)

```hocon
akka.remote.dot-netty.tcp {
  enable-ssl = true
  ssl {
    suppress-validation = false
    certificate {
      use-thumbprint-over-file = true
      thumbprint = "2531c78c51e5041d02564697a88af8bc7a7ce3e3"
      store-name = "My"
      store-location = "local-machine"  # or "current-user"
    }
  }
}
```

**When to use:** Windows production environments, enterprise deployments with centralized certificate management.

**Pros:**

* Leverages Windows ACL for private key protection
* Integrates with enterprise PKI infrastructure
* Supports hardware security modules (HSM)
* Private keys can be marked as non-exportable

**Cons:**

* Windows-specific (not portable to Linux)
* Requires administrative access for certificate installation
* More complex initial setup

**Finding Your Thumbprint:**

1. Open `certlm.msc` (Local Machine) or `certmgr.msc` (Current User)
2. Navigate to Personal > Certificates
3. Double-click your certificate
4. Go to Details tab
5. Scroll to Thumbprint field
6. Copy the value (remove spaces)

## Startup Certificate Validation (v1.5.52+)

**New in Akka.NET v1.5.52:** The transport now validates certificate configuration at startup, preventing runtime failures.

### What It Validates

The startup validation verifies:

* Certificate exists in the specified location
* Certificate has a private key associated
* Application has permissions to access the private key
* Private key is accessible for both RSA and ECDSA algorithms

This fail-fast validation prevents runtime TLS handshake failures by detecting certificate configuration problems during system initialization.

### Common Private Key Permission Issues

**Symptom:** "SSL certificate private key exists but cannot be accessed"

**Cause:** Application user lacks permissions to the private key file in Windows certificate store.

**Solution:** Grant private key access to your application user:

```powershell
# Find the certificate
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object {$_.Thumbprint -eq "YOUR_THUMBPRINT"}

# Get private key file location
$keyPath = $cert.PrivateKey.CspKeyContainerInfo.UniqueKeyContainerName
$keyFullPath = "C:\ProgramData\Microsoft\Crypto\RSA\MachineKeys\$keyPath"

# Grant read permissions
$acl = Get-Acl $keyFullPath
$permission = "DOMAIN\AppUser","Read","Allow"
$accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
$acl.AddAccessRule($accessRule)
Set-Acl $keyFullPath $acl
```

## Mutual TLS Authentication (v1.5.52+)

**New in Akka.NET v1.5.52:** Support for mutual TLS (mTLS) where both client and server must authenticate with certificates.

### Standard TLS vs Mutual TLS

**Standard TLS (Server Authentication Only):**

```mermaid
sequenceDiagram
    participant Client
    participant Server

    Client->>Server: Connect (no certificate)
    Server->>Client: Send server certificate
    Client->>Client: Validate server certificate
    Client->>Server: Accept connection
    Note over Client,Server: Encrypted communication established
```

**Mutual TLS (Client + Server Authentication):**

```mermaid
sequenceDiagram
    participant Client
    participant Server

    Client->>Server: Connect with client certificate
    Server->>Client: Send server certificate
    Client->>Client: Validate server certificate
    Server->>Server: Validate client certificate
    Client->>Server: Accept connection
    Server->>Client: Accept connection
    Note over Client,Server: Mutually authenticated encryption established
```

### Configuration

The following example shows how to configure mutual TLS:

[!code-csharp[MutualTlsConfig](../../../src/core/Akka.Docs.Tests/Configuration/TlsConfigurationSample.cs?name=MutualTlsConfig)]

For production with Windows Certificate Store:

[!code-csharp[WindowsCertStoreConfig](../../../src/core/Akka.Docs.Tests/Configuration/TlsConfigurationSample.cs?name=WindowsCertStoreConfig)]

### When to Enable Mutual TLS

**Enable mutual TLS when:**

* All nodes are under your control (typical Akka.NET cluster)
* You need defense-in-depth security
* Compliance requires bidirectional authentication (PCI-DSS, HIPAA, etc.)
* You want to prevent misconfigured nodes from joining

**Disable mutual TLS when:**

* Clients cannot provide certificates (rare in Akka.NET)
* You're using client-server architecture where clients are untrusted
* Backward compatibility with older clients required

**Default is TRUE for security-by-default posture.**

### Security Benefits of Mutual TLS

1. **Prevents Asymmetric Connectivity Issues**
   * Without mutual TLS: A node with broken certificate can connect OUT to cluster (client TLS succeeds)
   * With mutual TLS: Node cannot connect without working certificate (enforced both ways)

2. **Defense-in-Depth**
   * Startup validation prevents broken servers
   * Mutual TLS prevents broken clients
   * Both together provide complete protection

3. **Identity Verification**
   * Every node must prove it owns the certificate
   * Prevents certificate theft attacks (attacker needs private key)

## Configuration Examples and Security Analysis

### INSECURE: Development/Testing Only

[!code-csharp[DevTlsConfig](../../../src/core/Akka.Docs.Tests/Configuration/TlsConfigurationSample.cs?name=DevTlsConfig)]

**Why this is bad:**

* `suppress-validation = true` accepts ANY certificate (even self-signed or expired)
* Vulnerable to man-in-the-middle attacks
* No client authentication

**When to use:** Local development only, never in any environment accessible from network.

### GOOD: Standard TLS for Production

[!code-csharp[StandardTlsConfig](../../../src/core/Akka.Docs.Tests/Configuration/TlsConfigurationSample.cs?name=StandardTlsConfig)]

**Security level:** Medium-High

* Server proves identity to clients
* All traffic encrypted
* Startup validation prevents misconfigurations
* Suitable when mutual TLS is not feasible

### BEST: Mutual TLS for Maximum Security

```hocon
akka.remote.dot-netty.tcp {
  enable-ssl = true
  ssl {
    suppress-validation = false  # Validates all certificates (default when SSL enabled)
    require-mutual-authentication = true  # Requires client certs (default when SSL enabled since v1.5.52)
    validate-certificate-hostname = false  # DEFAULT: Hostname validation disabled (suitable for P2P with per-node certs)
    certificate {
      use-thumbprint-over-file = true
      thumbprint = "2531c78c51e5041d02564697a88af8bc7a7ce3e3"
      store-name = "My"
      store-location = "local-machine"
    }
  }
}
```

**Note:** When SSL is enabled, both `suppress-validation = false` and `require-mutual-authentication = true` are the secure defaults (since v1.5.52), so you only need to explicitly set them if overriding.

**About hostname validation:**

* Set `validate-certificate-hostname = false` for peer-to-peer clusters with per-node certificates (default)
* Set `validate-certificate-hostname = true` for client-server architectures with DNS-based connections

**Security level:** Maximum

* Both client and server prove identity
* All traffic encrypted
* Prevents misconfigured nodes from connecting
* Defense-in-depth security
* Recommended for all production deployments

### Configuration with Hostname Validation Enabled

For client-server architectures where all nodes connect via DNS names and share the same certificate:

```hocon
akka.remote.dot-netty.tcp {
  enable-ssl = true
  ssl {
    suppress-validation = false
    require-mutual-authentication = true
    validate-certificate-hostname = true  # Enable traditional TLS hostname validation
    certificate {
      use-thumbprint-over-file = true
      thumbprint = "2531c78c51e5041d02564697a88af8bc7a7ce3e3"
      store-name = "My"
      store-location = "local-machine"
    }
  }
}
```

**When to use hostname validation:**

* Your cluster uses stable DNS names (not IPs)
* All nodes share the same certificate (CN matches DNS names)
* You want browser-like TLS validation behavior
* Client-server architecture rather than P2P mesh

## Untrusted Mode

In addition to TLS, Akka.Remote supports "untrusted mode" which prevents clients from sending system-level messages:

```hocon
akka.remote {
  untrusted-mode = true

  # Whitelist specific actors that can receive remote messages
  trusted-selection-paths = [
    "/user/api-handler",
    "/user/public-endpoint"
  ]
}
```

**When to enable:**

* You're exposing Akka.Remote to untrusted clients
* You want to prevent remote actor creation/supervision
* Defense against malicious remote commands

**Note:** This does NOT replace TLS encryption. Use both together.

## Virtual Private Networks (VPNs)

The best practice for network security is to make the network itself secure. Run Akka.Remote on private networks that require VPN access.

**Why VPNs matter:**

* Restricts who can even attempt to connect
* Provides network-level access control
* Adds authentication layer before TLS
* Protects against network scanning/discovery

### VPN Options

**Self-Hosted:**

* [WireGuard](https://www.wireguard.com/) - Modern, fast, simple to configure
* [OpenVPN](https://openvpn.net/) - Mature, widely supported

**Cloud Provider VPNs:**

* [AWS Virtual Private Cloud (VPC)](https://aws.amazon.com/vpc/)
* [Azure Virtual Networks (VNet)](https://azure.microsoft.com/en-us/services/virtual-network/)
* [Google Cloud VPC](https://cloud.google.com/vpc)

**Managed Solutions:**

* [Tailscale](https://tailscale.com/) - Zero-config VPN mesh networking
* [ZeroTier](https://www.zerotier.com/) - Software-defined networking

## Troubleshooting

### Error: "SSL Certificate Private Key Exists but Cannot Be Accessed"

**Cause:** Application lacks permissions to private key file.

**Fix:** Run PowerShell script above to grant permissions.

### Error: "The Remote Certificate Is Invalid According to the Validation Procedure"

**Cause:** Certificate validation failed (expired, wrong CA, hostname mismatch).

**Fix:**

* Verify certificate is not expired: `Get-ChildItem Cert:\LocalMachine\My`
* Check certificate CN/SAN matches hostname
* For testing only: Set `suppress-validation = true` to identify if it's a validation issue

### Error: "TLS Handshake Failed" with No Client Certificate

**Cause:** Server requires mutual TLS but client didn't provide certificate.

**Fix:**

* Ensure all nodes have `require-mutual-authentication` set consistently
* Verify client certificate is configured correctly
* Check client application has private key access

### Error: "RemoteCertificateNameMismatch" - Hostname Validation Failure

**Full error message:**

```text
TLS certificate validation failed (full validation):
  - Certificate name mismatch
    - RemoteCertificateNameMismatch: The hostname being connected to does not match
      the hostname(s) on the server certificate.

Certificate Details:
  Subject: CN=node1.example.com
  Issuer: CN=My-CA
  Valid: 2025-01-01 to 2026-01-01

Connection target: 192.168.1.100:4053
```

**Cause:** Certificate CN/SAN doesn't match the target hostname/IP address.

**Common scenarios:**

1. **Connecting via IP but certificate has DNS name**
   * Connecting to: `192.168.1.100`
   * Certificate CN: `node1.example.com`

2. **Per-node certificates in P2P cluster**
   * Node A cert CN: `node-a.cluster.local`
   * Node B cert CN: `node-b.cluster.local`
   * Each node's certificate doesn't match the other node's hostname

**Fix:**

Option 1 (Recommended for P2P clusters): Disable hostname validation

```hocon
akka.remote.dot-netty.tcp.ssl {
  validate-certificate-hostname = false  # Allow per-node certs
}
```

Option 2: Use certificates with matching CN/SAN

```bash
# Ensure certificate CN matches connection target
# For IP connections, add IP SAN to certificate:
New-SelfSignedCertificate -Subject "CN=node1" `
  -DnsName "node1", "node1.example.com" `
  -TextExtension @("2.5.29.17={text}IPAddress=192.168.1.100")
```

Option 3: Connect via DNS names that match certificate CN

```hocon
akka.remote.dot-netty.tcp {
  hostname = "node1.example.com"  # Must match cert CN
}
```

### Error: "UntrustedRoot" - Certificate Chain Validation Failure

**Full error message:**

```text
TLS/SSL certificate validation failed:
  - Certificate chain validation errors
    - UntrustedRoot: A certificate chain processed, but terminated in a root
      certificate which is not trusted by the trust provider.

Certificate Details:
  Subject: CN=localhost
  Issuer: CN=localhost (self-signed)
```

**Cause:** Certificate is self-signed or signed by untrusted CA.

**Fix:**

Option 1 (Development only): Suppress chain validation

```hocon
akka.remote.dot-netty.tcp.ssl {
  suppress-validation = true  # WARNING: Development only!
}
```

Option 2 (Recommended): Trust the CA certificate

```powershell
# Windows: Import CA to Trusted Root store
Import-Certificate -FilePath ca.cer -CertStoreLocation Cert:\LocalMachine\Root

# Linux: Add to system CA bundle
sudo cp ca.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

### Understanding TLS Error Messages (v1.5.52+)

Since v1.5.52, TLS handshake failures provide detailed diagnostic information including:

* **Error category** (chain validation, hostname mismatch, etc.)
* **Specific SSL policy error** with explanation
* **Certificate details** (subject, issuer, validity period)
* **Connection context** (local/remote addresses)
* **Actionable recommendations**

**Example comprehensive error:**

```text
TLS handshake failed on channel [127.0.0.1:4053->127.0.0.1:54321](Id=...)

Detailed TLS Error:
  - Certificate chain validation errors
    - UntrustedRoot: A certificate chain processed, but terminated in a root
      certificate which is not trusted by the trust provider.
  - Certificate name mismatch
    - RemoteCertificateNameMismatch: The hostname being connected to does not
      match the hostname(s) on the server certificate.

Certificate Information:
  Subject: CN=node-test
  Issuer: CN=node-test (self-signed)
  Serial Number: 1A2B3C4D5E6F
  Valid From: 2025-01-01 00:00:00 UTC
  Valid To: 2026-01-01 00:00:00 UTC
  Thumbprint: 2531c78c51e5041d02564697a88af8bc7a7ce3e3

Recommendations:
  - For development: Set 'suppress-validation = true' (testing only!)
  - For production: Install certificate in trusted root store
  - For hostname issues: Set 'validate-certificate-hostname = false' if using
    per-node certificates or IP-based connections
```

## Additional Resources

* [Windows Firewall Configuration Best Practices](https://learn.microsoft.com/en-us/windows/security/operating-system-security/network-security/windows-firewall/best-practices-configuring)
* [TLS 1.2 Specification (RFC 5246)](https://datatracker.ietf.org/doc/html/rfc5246)
* [OWASP Transport Layer Security Cheat Sheet](https://cheatsheetseries.owasp.org/cheatsheets/Transport_Layer_Security_Cheat_Sheet.html)

---

**Related:**

* [Akka.Remote Configuration](xref:akka-remote-configuration)
* [DotNetty Transport](https://github.com/Azure/DotNetty)
