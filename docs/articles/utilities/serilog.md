---
uid: serilog
title: Serilog
---

# Using Serilog

## Setup

Install the package __Akka.Logger.Serilog__ via nuget to utilize
[Serilog](https://serilog.net/), this will also install the required Serilog package dependencies.

```console
PM> Install-Package Akka.Logger.Serilog
```

### ASP.NET Core

If you are using Akka.NET in an ASP.NET Core application, follow the [Serilog guide](https://github.com/serilog/serilog-aspnetcore) to setup a logger injection. Akka.NET also works with Serilog configured using [two-stage initialization](https://github.com/serilog/serilog-aspnetcore#two-stage-initialization).

It is important to remember that logging level is first controlled by a Serilog sink, and only then by Akka.NET, as described in this [document](https://github.com/serilog/serilog/wiki/Configuration-Basics):

```text
Logger vs. sink minimums - it is important to realize that the logging level can only be raised for sinks, not lowered. So, if the logger's MinimumLevel is set to Information then a sink with Debug as its specified level will still only see Information level events.
```

Thus, if Akka.NET log level is `DEBUG`, but a Serilog sink log level is `INFO`, all debug messages from Akka will be filtered out. You can set the default logging level on host startup:

```csharp
public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args)
        .UseSerilog((hostingContext, services, loggerConfiguration) => 
           (Environment.GetEnvironmentVariable("MY_AKKA_APP__DEFAULT_LOG_LEVEL") switch
            {
                "INFO" => loggerConfiguration.MinimumLevel.Information(),
                "WARN" => loggerConfiguration.MinimumLevel.Warning(),
                "ERROR" => loggerConfiguration.MinimumLevel.Error(),
                "DEBUG" => loggerConfiguration.MinimumLevel.Debug(),
                _ => loggerConfiguration.MinimumLevel.Information()
            })
            .WriteTo.Console()
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .EnrichWithCommonProperties(...)
            ... // continue logger configuration);
```

`Serilog.Log.Logger` will be initialized during host bootstrap and Akka.NET will use the final configured logger.

## Example

The following example uses Serilog's __Console__ sink available via nuget, there are wide range of other sinks available depending on your needs, for example a rolling log file sink.  See serilog's documentation for details on these.

```console
PM> Install-Package Serilog.Sinks.Console
```

Next, you'll need to configure the global `Log.Logger` and also specify to use
the logger in the config when creating the system, for example like this:

```csharp
var logger = new LoggerConfiguration()
    .WriteTo.Console()
    .MinimumLevel.Information()
    .CreateLogger();

Serilog.Log.Logger = logger;

var system = ActorSystem.Create("my-test-system", "akka { loglevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}");
```

## Logging

To log inside an actor, using the normal `string.Format()` syntax, get the
logger and log:

```csharp
var log = Context.GetLogger();
...
log.Info("The value is {0}", counter);
```

Or alternatively

```csharp
var log = Context.GetLogger();
...
log.Info("The value is {Counter}", counter);
```

## Extensions

The package __Akka.Logger.Serilog__ also includes the extension method `ForContext()` for `ILoggingAdapter` (the object returned by `Context.GetLogger()`). This is analogous to Serilog's `ForContext()` but instead of returning a Serilog `ILogger` it returns an Akka.NET `ILoggingAdapter`. This instance acts as contextual logger that will attach a property to all events logged through it.

However, in order to use it, the parent `ILoggingAdapter` must be constructed through another included generic extension method for `GetLogger()`. For example,

```csharp
using Akka.Logger.Serilog;
...
private readonly ILoggingAdapter _logger = Context.GetLogger<SerilogLoggingAdapter>();
...
private void ProcessMessage(string correlationId)
{
    var contextLogger = _logger.ForContext("CorrelationId", correlationId);
    contextLogger.Info("Processing message");
}
```

If the configured output template is, for example, `"[{CorrelationId}] {Message}{NewLine}"`, and the parameter `correlationId` is `"1234"` then the resulting log would contain the line `[1234] Processing message`.

```csharp
// configure sink with an output template
var logger = new LoggerConfiguration()
    .WriteTo.Console(outputTemplate: "[{CorrelationId}] {Message}{NewLine}")
    .MinimumLevel.Information()
    .CreateLogger();
```

## Formatting OutputTemplate

There are few properties that one can use in their `OutputTemplate` for logger configuration:

* `ActorPath` - contains the current actor's path.
* `LogSource` - also contains `ActorPath`, however it can also have other values such as:
  * `<TypeName> (<ActorSystemName>)`
  * `<string> (<ActorSystemName>)`
* `Thread` - thread id on which given log action was executed.

### Example OutputTemplate

```console
[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{ActorPath}] [{SourceContext}] {Message}{NewLine}{Exception}
```

## HOCON Configuration

In order to be able to change log level without the need to recompile, we need to employ some sort of application configuration.  To use Serilog via HOCON configuration, add the following to the __App.config__ of the project.

```xml
<configSections>    
    <section name="akka" type="Akka.Configuration.Hocon.AkkaConfigurationSection, Akka" />
</configSections>

...

<akka>
    <hocon>
      <![CDATA[
      akka { 
        loglevel=INFO,
        loggers=["Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog"]
      }
    ]]>
    </hocon>
  </akka>

```

The code can then be updated as follows removing the inline HOCON from the actor system creation code.  Note in the following example, if a minimum level is not specified, Information level events and higher will be processed.  Please read the documentation for [Serilog](https://serilog.net/) configuration for more details on this.  It is also possible to move serilog configuration to the application configuration, for example if using a rolling log file sink, again, browsing the serilog documentation is the best place for details on that feature.  

```csharp
var logger = new LoggerConfiguration()
                .WriteTo.Console()
                .MinimumLevel.Information()
                .CreateLogger();

Serilog.Log.Logger = logger;

var system = ActorSystem.Create("my-test-system");
```
