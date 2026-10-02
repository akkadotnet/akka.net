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
