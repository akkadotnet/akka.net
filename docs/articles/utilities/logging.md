---
uid: logging
title: Logging in Akka.NET
---

# Logging

> [!NOTE]
> For information on how to use Serilog with Akka.NET, we have a dedicated page for that: "[Using Serilog for Akka.NET Logging](xref:serilog)."

## How to Log

To log in an actor, create a logger and assign it to a private field:

```csharp
private readonly ILoggingAdapter _log = Logging.GetLogger(Context);
```

Use the `Debug`, `Info`, `Warning` and `Error` methods to log.

```csharp
_log.Debug("Some message");
```

## Semantic Logging

Semantic logging, also known as structured logging, treats each log statement as a message template plus a set of named property values rather than a pre-formatted string. Instead of writing:

```csharp
_log.Info($"User {userId} added item {itemId} to cart {cartId}");
```

The template and its arguments are passed to the logger separately:

```csharp
_log.Info("User {UserId} added item {ItemId} to cart {CartId}", userId, itemId, cartId);
```

The second form preserves `UserId`, `ItemId`, and `CartId` as named properties alongside the rendered message. Downstream sinks such as Seq, Elasticsearch, Splunk, and Application Insights can index and filter on those properties directly, without parsing the rendered message text.

### Native Semantic Logging Support

Since v1.5.57, Akka.NET's default [`ILogMessageFormatter`](xref:Akka.Event.ILogMessageFormatter) is the [`SemanticLogMessageFormatter`](xref:Akka.Event.SemanticLogMessageFormatter). It supports named placeholders (`{UserId}`, `{RequestId}`), positional placeholders (`{0}`, `{1}`), format specifiers (`{Value:N2}`), and alignment (`{Value,10}`). The `ILoggingAdapter` returned by `Context.GetLogger()` preserves the template and its arguments as a `LogMessage` instance and publishes them to the event stream, so the template remains intact until a logger actor handles the event. No additional package is required.

Serilog-specific destructuring operators (`{@Object}` and `{$Object}`) are not supported by the native formatter and are handled by the Serilog adapter instead.

Custom logger implementations can extract the properties from a `LogEvent` using [`LogEvent.TryGetProperties`](xref:Akka.Event.LogEventExtensions):

```csharp
if (logEvent.TryGetProperties(out var properties))
{
    // properties["UserId"], properties["ItemId"], etc.
    // forward to the structured sink
}
```

`TryGetProperties` returns the template-extracted properties together with any context properties attached via `WithContext` or `BeginScope`, merged into a single dictionary. See [Context Enrichment and Scopes](#context-enrichment-and-scopes) for details.

### Semantic Logging with Akka.Logger.Serilog

[Akka.Logger.Serilog](xref:serilog) is the original semantic logging integration for Akka.NET. The adapter passes the raw message template and its arguments directly to Serilog, which applies its own parser including the destructuring operators (`{@Object}` and `{$Object}`). Context attached via `WithContext` or `BeginScope` is forwarded to Serilog as `PropertyEnricher` instances.

The minimum configuration is a single HOCON line:

```hocon
akka {
    loglevel = INFO
    loggers = ["Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog"]
}
```

To have Akka.NET's rendered-string output (for example, from `StandardOutLogger` and `DefaultLogger`) honor Serilog's destructuring syntax as well, also set the formatter:

```hocon
akka.logger-formatter = "Akka.Logger.Serilog.SerilogLogMessageFormatter, Akka.Logger.Serilog"
```

See [Using Serilog for Akka.NET Logging](xref:serilog) for the full walkthrough, including ASP.NET Core hosting and output template configuration.

### Semantic Logging with Akka.Logger.NLog

[Akka.Logger.NLog](https://www.nuget.org/packages/Akka.Logger.NLog/) passes the raw message template and arguments to NLog's `LogEventInfo` and forwards every property returned by [`LogEvent.TryGetProperties`](xref:Akka.Event.LogEventExtensions). Named template placeholders and any properties attached via `WithContext` or `BeginScope` are available as NLog event properties that can be referenced in layouts and targets. The adapter also surfaces Akka.NET-specific values (`logSource`, `actorPath`, `threadId`) as event properties:

```xml
<targets>
  <target name="console"
          xsi:type="Console"
          layout="[${logger}] [${level:uppercase=true}] [${event-properties:item=logSource}] [${event-properties:item=actorPath}] [${event-properties:item=UserId}] : ${message}" />
</targets>
```

```hocon
akka {
    loglevel = INFO
    loggers = ["Akka.Logger.NLog.NLogLogger, Akka.Logger.NLog"]
}
```

### Semantic Logging Best Practices

The following practices help ensure that structured log output remains useful in production:

* **Prefer named placeholders over positional ones.** `{UserId}` is self-documenting, survives argument reordering, and produces a property name that is meaningful to query on.
* **Do not pre-render the message.** Using string interpolation (`$"..."`) or `string.Format` before calling the logger collapses the template variables into a single string, which prevents property extraction.
* **Use stable property names.** Property names become query selectors in dashboards and alert rules. Renaming `{UserId}` to `{userId}` later will silently break any existing queries against the old name.
* **Keep templates stable across call sites.** Sinks such as Seq group events by a hash of the template. `"User {UserId} logged in"` and `"User {UserId} ({Username}) logged in"` are considered two distinct event types, even though they describe the same event.
* **Attach durable context once, not on every log call.** Use [`WithContext`](#context-enrichment-and-scopes) or `BeginScope` for values such as `TenantId`, `CorrelationId`, and `ShardId` so that every log event from the adapter carries them automatically.
* **Avoid logging sensitive data as properties.** Log properties are persisted, indexed, and may be forwarded to external services. Do not include raw customer data, credentials, or bearer tokens without a redaction strategy.

## Context Enrichment and Scopes

Most log statements need to carry more than just the template arguments. Every event from a given actor, or every event handling a particular request, typically shares a few common fields such as a tenant id, shard id, or correlation id. `WithContext` and `BeginScope` allow these values to be attached to the logger once rather than repeated at every call site. The values appear in the rendered output and are merged into the property dictionary returned by [`LogEvent.TryGetProperties`](xref:Akka.Event.LogEventExtensions), so semantic sinks receive them together with the template properties.

[!code-csharp[LoggingContextExample](../../../src/core/Akka.Tests/Loggers/LoggingContextSpecs.cs?name=LoggingContextExample)]

Example output:

```text
[INFO][...][Thread 0007][akka://sys/user/a][Tenant=foo][Partition=12] Processing 42
```

The choice between `WithContext` and `BeginScope` depends on how long the context should live. `WithContext` returns a new `ILoggingAdapter` with the property attached, which is suitable for actor-scoped context such as a tenant, shard, or partition that can be stored in a field. `BeginScope` wraps the same pattern in an [`ILoggingAdapterScope`](xref:Akka.Event.ILoggingAdapterScope) for use with a `using` block, which is appropriate for context that should be limited to a single message handler, such as a correlation id or request id.

## Standard Loggers

Akka.NET comes with two built in loggers.

* **StandardOutLogger**
* **BusLogging**

### StandardOutLogger

`StandardOutLogger` is considered as a minimal logger and implements the `MinimalLogger` abstract
class. Its job is simply to output all `LogEvent`s emitted by the `EventBus` onto the console.
Since it is not an actual actor, ie. it doesn't need the `ActorSystem` to operate, it is also
used to log other loggers activity at the very start and very end of the `ActorSystem` life cycle.
You can change the minimal logger start and end life cycle behavior by changing the
`akka.stdout-loglevel` HOCON settings to `OFF` if you do not need these feature in your application.

### Advanced MinimalLogger Setup

You can also replace `StandardOutLogger` by making your own logger class with an empty constructor
that inherits/implements the `MinimalLogger` abstract class and passing the fully qualified class
name into the `akka.stdout-logger-class` HOCON settings.

> [!WARNING]
> Be aware that `MinimalLogger` implementations are **NOT** real actors and will **NOT** have any
> access to the `ActorSystem` and all of its extensions. All logging done inside a `MinimalLogger`
> have to be done in as simple as possible manner since it is used to log how other loggers are
> behaving at the very start and very end of the `ActorSystem` life cycle.
>
> Note that `MinimalLogger` are **NOT** interchangeable with other Akka.NET loggers and there can
> only be one `MinimalLogger` registered with the `ActorSystem` in the HOCON settings.

## Third Party Loggers

These loggers are also available as separate nuget packages

* **Akka.Logger.Serilog** which logs using [serilog](http://serilog.net/). See [Detailed instructions on using Serilog](xref:serilog).
* **Akka.Logger.NLog**  which logs using [NLog](http://nlog-project.org/)
* **Microsoft.Extensions.Logging** - which is [built into Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting#microsoftextensionslogging-integration).

Note that you need to modify the config as explained below.

### NLog Configuration

Example NLog configuration inside your app.config or web.config:

```hocon
akka {
    loggers = ["Akka.Logger.NLog.NLogLogger, Akka.Logger.NLog"]
}
```

The above NLog components can be found on Nuget (<https://www.nuget.org/packages/Akka.Logger.NLog/>)

## Configuring Custom Loggers

To configure a custom logger inside your Akka.Config, you need to use a fully qualified .NET class name like this:

```hocon
akka {
    loggers = ["NameSpace.ClassName, AssemblyName"]
}
```

Or using [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting), you can configure loggers programmatically using strongly typed references to the underlying logging classes:

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

### Customizing the `ILogMessageFormatter`

A new feature introduced in [Akka.NET v1.5](xref:akkadotnet-v15-whats-new), you now have the ability to customize the `ILogMessageFormatter` - the component responsible for formatting output written to all `Logger` implementations in Akka.NET.

Semantic logging no longer requires a custom formatter: since v1.5.57, the default formatter is [`SemanticLogMessageFormatter`](xref:Akka.Event.SemanticLogMessageFormatter), which handles named templates out of the box (see [Semantic Logging](#semantic-logging)). Swap in [Akka.Logger.Serilog](xref:serilog)'s formatter only when you need Serilog's destructuring operators (`{@Object}` and `{$Object}`) to apply to Akka.NET's own rendered output.

Custom formatters are still useful when you want to inject additional fields into every rendered log line produced by Akka.NET internally - that's the sort of thing you can accomplish by customizing the `ILogMessageFormatter`:

[!code-csharp[CustomLogMessageFormatter](../../../src/core/Akka.Tests/Loggers/CustomLogFormatterSpec.cs?name=CustomLogFormatter)]

This class will be responsible for formatting all log messages when they're written out to your configured sinks - once we configure it in HOCON using the `akka.logger-formatter` setting:

[!code-csharp[CustomLogMessageFormatter](../../../src/core/Akka.Tests/Loggers/CustomLogFormatterSpec.cs?name=CustomLogFormatterConfig)]

## Logging Unhandled Messages

It is possible to configure akka so that Unhandled messages are logged as Debug log events for debug purposes. This can be achieved using the following configuration setting:

```hocon
akka {
    actor.debug.unhandled = on
}
```

## Example Configuration

```hocon
akka {
  stdout-loglevel = DEBUG
  loglevel = DEBUG
  log-config-on-start = on
  actor {
    debug {
      receive = on
      autoreceive = on
      lifecycle = on
      event-stream = on
      unhandled = on
    }
  }
}
```

## Logging All Received Messages

It is possible to log all Receive'd messages, usually for debug purposes. This can be achieved by implementing the ILogReceive interface:

```c#
public class MyActor : ReceiveActor, ILogReceive
{
    public MyActor()
    {
        Receive<string>(s => Sender.Tell("ok"));
    }
}

...

// send a MyActor instance a string message
myActor.Tell("hello");
```

In your log, expect to see a line such as:

`[DEBUG]... received handled message hello from akka://test/deadLetters`

This logging can be toggled by configuring `akka.actor.debug.receive`.

## Filtering Log Messages

Since v1.5.21, Akka.NET supports for filtering log messages based on the `LogSource` or the content of a log message.

The goal of this feature is to allow users to run Akka.NET at more verbose logging settings (i.e. `LogLevel.Debug`) while not getting completely flooded with unhelpful noise from the Akka.NET logging system. You can use the [`LogFilterBuilder`](xref:Akka.Event.LogFilterBuilder) to exclude messages don't need while still keeping ones that you do.

### Configuring Log Filtering

[!code-csharp[Create LoggerSetup](../../../src/core/Akka.Tests/Loggers/LogFilterEvaluatorSpecs.cs?name=CreateLoggerSetup)]

We create a [`LogFilterBuilder`](xref:Akka.Event.LogFilterBuilder) prior to starting the `ActorSystem` and provide it with rules for which logs _should be excluded_ from any of Akka.NET's logged output - this uses the [`ActorSystemSetup`](xref:Akka.Actor.Setup.ActorSystemSetup) class functionality that Akka.NET supports for programmatic `ActorSystem` configuration:

[!code-csharp[Create ActorSystemSetup](../../../src/core/Akka.Tests/Loggers/LogFilterEvaluatorSpecs.cs?name=ActorSystemSetup)]

From there, we can create our `ActorSystem` with these rules enabled:

```csharp
ActorSystemSetup completeSetup = CustomLoggerSetup();

// start the ActorSystem with the LogFilterBuilder rules enabled
ActorSystem mySystem = ActorSystem.Create("MySys", completeSetup);
```

### Log Filtering Rules

There are two built-in types of log filtering rules:

* `ExcludeSource___` - filters logs based on the `LogSource`; this type of filtering is _very_ resource efficient because it doesn't require the log message to be expanded in order for filtering to work.
* `ExcludeMessage___` - filters logs based on the content of the message. More resource-intensive as it does require log messages to be fully expanded prior to filtering.

> [!NOTE]
> For an Akka.NET log to be excluded from the output logs, only one filter rule has to return a `LogFilterDecision.Drop`.

However, if that's not sufficient for your purposes we also support defining custom rules via the `LogFilterBase` class:

[!code-csharp[LogFilterBase](../../../src/core/Akka/Event/LogFilter.cs?name=LogFilterBase)]

You can filter log messages based on any of the accessibly properties, and for performance reasons any `LogFilterBase` that looks at `LogFilterType.Content` will be passed in the fully expanded log message as a `string?` via the optional `expandedMessage` property. This is done in order to avoid allocating the log message every time for each possible rule that might be evaluated.
