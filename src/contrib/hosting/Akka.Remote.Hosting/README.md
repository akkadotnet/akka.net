# Akka Remoting Akka.Hosting Extensions

## WithRemoting() Method

An extension method to add [Akka.Remote](https://getakka.net/articles/remoting/index.html) support to the `ActorSystem`.

```csharp
public static AkkaConfigurationBuilder WithRemoting(
    this AkkaConfigurationBuilder builder,
    string hostname = null,
    int? port = null,
    string publicHostname = null,
    int? publicPort = null);
```

### Parameters
* `hostname` __string__

  Optional. The hostname to bind Akka.Remote upon.

  __Default__: `IPAddress.Any` or "0.0.0.0"

* `port` __int?__

  Optional. The port to bind Akka.Remote upon.

  __Default__: 2552

* `publicHostname` __string__

  Optional. If using hostname aliasing, this is the host we will advertise.

  __Default__: Fallback to `hostname`

* `publicPort` __int?__

  Optional. If using port aliasing, this is the port we will advertise.

  __Default__: Fallback to `port`

### Example

```csharp
using var host = new HostBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("remotingDemo", (builder, provider) =>
        {
            builder.WithRemoting("127.0.0.1", 4053);
        });
    }).Build();

await host.RunAsync();
```

## SSL/TLS Configuration

Akka.Remote supports SSL/TLS encryption for secure communication between actor systems. Starting with Akka.NET v1.5.55, you can provide custom certificate validation callbacks using the `CertificateValidation` helper class.

```csharp
using System.Security.Cryptography.X509Certificates;
using Akka.Remote.Transport.DotNetty;

var certificate = new X509Certificate2("/path/to/certificate.pfx", "certificate-password");

using var host = new HostBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("secureSystem", (builder, provider) =>
        {
            builder.WithRemoting(options =>
            {
                options.HostName = "127.0.0.1";
                options.Port = 4053;
                options.EnableSsl = true;
                options.Ssl.X509Certificate = certificate;

                // Use built-in validators for common scenarios
                options.Ssl.CustomValidator = CertificateValidation.Combine(
                    CertificateValidation.ValidateChain(),
                    CertificateValidation.ValidateSubject("CN=*.mycompany.com")
                );
            });
        });
    }).Build();

await host.RunAsync();
```

Available `CertificateValidation` methods: `ValidateChain()`, `ValidateHostname()`, `PinnedCertificate()`, `ValidateSubject()`, `ValidateIssuer()`, and `Combine()`.

`CustomValidator` also applies when the certificate is set through `options.Ssl.CertificateOptions` with a file `Path` or a `Thumbprint`. Hosting loads that certificate itself and passes it to `DotNettySslSetup`. If `CustomValidator` is set but the options name no certificate, `WithRemoting` throws a `ConfigurationException`.

### Renewing the certificate without a restart

Set `Ssl.CertificateSource`, and a node asks for its certificate each time it builds a connection. Add the renewed certificate beside the old one and new connections use it. Existing connections keep the certificate they started with. A folder or a store is read in the background, so a new certificate shows within about a minute, and sooner when connections are being built. If a look-up finds no usable certificate, the node keeps using the last one it picked while that one is in date.

Setting a certificate source turns TLS on unless `EnableSsl` is `false`. With `EnableSsl = false`, TLS stays off and the source is not used.

```csharp
using Akka.IO;
using System.Security.Cryptography.X509Certificates;

builder.WithRemoting(options =>
{
    options.HostName = "0.0.0.0";
    options.Port = 4053;
    options.EnableSsl = true;

    // Pick the valid certificate with this common name that was issued last.
    options.Ssl.CertificateSource = TlsCertificateSource.FromStore(
        "node.mycompany.com", "My", StoreLocation.LocalMachine);

    // Or read a folder of PKCS#12 or PEM files:
    // options.Ssl.CertificateSource = TlsCertificateSource.FromFolder("/etc/akka/tls", "certificate-password");
});
```

Set either `CertificateSource` or `X509Certificate`, not both.

A source checks that the certificate is allowed for the job. With mutual TLS on, which is the default, it must allow both server and client authentication. `X509Certificate`, a single file `Path` and a `Thumbprint` are not checked this way, so check your certificate before you switch. A certificate that is not allowed is skipped with a warning, and a node with no other certificate does not start.

Without a `CustomValidator`, you can also set `CertificateOptions.CommonName`, or point `CertificateOptions.Path` at a folder, and the options write the HOCON for you. When you set a `CustomValidator` too, the options build the matching source so the validator applies.
