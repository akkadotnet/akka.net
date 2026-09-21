---
uid: hosting-opentelemetry
title: OpenTelemetry Trace Correlation
---

# OpenTelemetry Trace Correlation

Akka.NET processes log events asynchronously, which means `Activity.Current` does not flow across actor mailbox boundaries. To preserve trace correlation, Akka.Hosting captures the `ActivityContext` at log creation time and includes it in the log state. The `AkkaTraceContextProcessor` then applies that context to OpenTelemetry `LogRecord`s so exporters can correlate logs with traces.

Minimal setup:

```csharp
using Akka.Hosting;
using Akka.Hosting.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault()
        .AddService("my-service"));

    // Register before exporters
    options.AddAkkaTraceCorrelation();

    // Add OTLP exporter if you have not configured it elsewhere.
    // Your mileage may vary; use the OpenTelemetry configuration that fits your app.
    options.AddOtlpExporter();
});

builder.Services.AddAkka("MySystem", configBuilder =>
{
    configBuilder.ConfigureLoggers(setup =>
    {
        setup.ClearLoggers();
        setup.AddLoggerFactory();
    });
});
```

See the demo projects under [`src/examples/Hosting`](https://github.com/akkadotnet/akka.net/tree/dev/src/examples/Hosting) in the Akka.NET repository for a working Aspire setup.
