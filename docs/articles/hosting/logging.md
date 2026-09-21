---
uid: hosting-logging
title: Microsoft.Extensions.Logging Integration
---

# Microsoft.Extensions.Logging Integration

## Logger Configuration Support

You can use `AkkaConfigurationBuilder` extension method called `ConfigureLoggers(Action<LoggerConfigBuilder>)` to configure how Akka.NET logger behave.

Example:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .ConfigureLoggers(setup =>
        {
            // Example: This sets the minimum log level
            setup.LogLevel = LogLevel.DebugLevel;
            
            // Example: Clear all loggers
            setup.ClearLoggers();
            
            // Example: Add the default logger
            // NOTE: You can also use setup.AddLogger<DefaultLogger>();
            setup.AddDefaultLogger();
            
            // Example: Add the ILoggerFactory logger
            // NOTE:
            //   - You can also use setup.AddLogger<LoggerFactoryLogger>();
            //   - To use a specific ILoggerFactory instance, you can use setup.AddLoggerFactory(myILoggerFactory);
            setup.AddLoggerFactory();
            
            // Example: Adding a serilog logger
            setup.AddLogger<SerilogLogger>();
        })
        .WithActors((system, registry) =>
        {
            var echo = system.ActorOf(act =>
            {
                act.ReceiveAny((o, context) =>
                {
                    Logging.GetLogger(context.System, "echo").Info($"Actor received {o}");
                    context.Sender.Tell($"{context.Self} rcv {o}");
                });
            }, "echo");
            registry.TryRegister<Echo>(echo); // register for DI
        });
});
```

A complete code sample can be viewed [here](https://github.com/akkadotnet/akka.net/tree/dev/src/examples/Hosting/Akka.Hosting.LoggingDemo).

Exposed properties are:

* `LogLevel`: Configure the Akka.NET minimum log level filter, defaults to `InfoLevel`
* `LogConfigOnStart`: When set to true, Akka.NET will log the complete HOCON settings it is using at start up, this can then be used for debugging purposes.

Currently supported logger methods:

* `ClearLoggers()`: Clear all registered logger types.
* `AddLogger<TLogger>()`: Add a logger type by providing its class type.
* `AddDefaultLogger()`: Add the default Akka.NET console logger.
* `AddLoggerFactory()`: Add the new `ILoggerFactory` logger.

## Microsoft.Extensions.Logging.ILoggerFactory Logging Support

You can now use `ILoggerFactory` from Microsoft.Extensions.Logging as one of the sinks for Akka.NET logger. This logger will use the `ILoggerFactory` service set up inside the dependency injection `ServiceProvider` as its sink.

## Serilog Message Formatting Support

If you're interested in using [Akka.Logger.Serilog](https://github.com/akkadotnet/Akka.Logger.Serilog), you can set Akka.NET's default logger and log message formatter to allow for Serilog's semantic logging to be enabled by default:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .ConfigureLoggers(setup =>
        {
            // Example: This sets the minimum log level
            setup.LogLevel = LogLevel.DebugLevel;
            
            // Example: Clear all loggers
            setup.ClearLoggers();
            
            // Add Serilog
            setup.AddLogger<SerilogLogger>();
            
            // use the default SerilogFormatter everywhere
            setup.WithDefaultLogMessageFormatter<SerilogLogMessageFormatter>();
        })
        .WithActors((system, registry) =>
        {
            var echo = system.ActorOf(act =>
            {
                act.ReceiveAny((o, context) =>
                {
                    Logging.GetLogger(context.System, "echo").Info($"Actor received {o}");
                    context.Sender.Tell($"{context.Self} rcv {o}");
                });
            }, "echo");
            registry.TryRegister<Echo>(echo); // register for DI
        });
});
```

This will eliminate the need to have to do `Context.GetLogger<SerilogLoggingAdapter>()` everywhere you want to use it.

## Microsoft.Extensions.Logging Log Event Filtering

There will be two log event filters acting on the final log input, the Akka.NET `akka.loglevel` setting and the `Microsoft.Extensions.Logging` settings, make sure that both are set correctly or some log messages will be missing.

To set up the `Microsoft.Extensions.Logging` log filtering, you will need to edit the `appsettings.json` file. Note that we also set the `Akka` namespace to be filtered at debug level in the example below.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information",
      "Akka": "Debug"
    }
  }
}
```

## Filtering Logs in Akka.NET

In Akka.NET 1.5.21, we introduced [log filtering for log messages based on the LogSource or the content of a log message](xref:logging#filtering-log-messages). Depending on your coding style, you can use this feature in Akka.Hosting in several ways.

1. Using the `LoggerConfigBuilder.WithLogFilter()` method.

   The `LoggerConfigBuilder.WithLogFilter()` method lets you set up the `LogFilterBuilder`

   ```csharp
   builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
   {
       configurationBuilder
           .ConfigureLoggers(loggerConfigBuilder =>
           {
               loggerConfigBuilder.WithLogFilter(filterBuilder =>
               {
                   filterBuilder.ExcludeMessageContaining("Test");
               });
           });
   });
   ```

2. Setting the `loggerConfigBuilder.LogFilterBuilder` property directly.

   ```csharp
   builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
   {
       configurationBuilder
           .ConfigureLoggers(loggerConfigBuilder =>
           {
               loggerConfigBuilder.LogFilterBuilder = new LogFilterBuilder();
               loggerConfigBuilder.LogFilterBuilder.ExcludeMessageContaining("Test");
           });
   });
   ```
