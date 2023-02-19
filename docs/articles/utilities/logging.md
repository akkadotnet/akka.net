---
uid: logging
title: Logging in Akka.NET
---

# Logging

> ![NOTE]
> For info

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
