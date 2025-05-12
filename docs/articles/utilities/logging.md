---
uid: logging
title: Logging in Akka.NET
---

# Logging

> ![NOTE]
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

## Standard Loggers

Akka.NET comes with two built in loggers.

* __StandardOutLogger__
* __BusLogging__

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
> Be aware that `MinimalLogger` implementations are __NOT__ real actors and will __NOT__ have any
> access to the `ActorSystem` and all of its extensions. All logging done inside a `MinimalLogger`
> have to be done in as simple as possible manner since it is used to log how other loggers are
> behaving at the very start and very end of the `ActorSystem` life cycle.
>
> Note that `MinimalLogger` are __NOT__ interchangeable with other Akka.NET loggers and there can
> only be one `MinimalLogger` registered with the `ActorSystem` in the HOCON settings.

## Third Party Loggers

These loggers are also available as separate nuget packages

* __Akka.Logger.Serilog__ which logs using [serilog](http://serilog.net/). See [Detailed instructions on using Serilog](xref:serilog).
* __Akka.Logger.NLog__  which logs using [NLog](http://nlog-project.org/)
* __Microsoft.Extensions.Logging__ - which is [built into Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting#microsoftextensionslogging-integration).

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

The primary use case for this is supporting semantic logging across the board in your user-defined actors, which is something that [Akka.Logger.Serilog](xref:serilog) supports quite well.

However, maybe there are certain pieces of data you want to have injected into all of the log messages produced by Akka.NET internally - that's the sort of thing you can accomplish by customizing the `ILogMessageFormatter`:

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
