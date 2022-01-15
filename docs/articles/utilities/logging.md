---
uid: logging
title: Logging
---

# Logging

For more info see real Akka's documentation: <http://doc.akka.io/docs/akka/2.0/scala/logging.html>

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

## Contrib Loggers

These loggers are also available as separate nuget packages

* __Akka.Logger.slf4net__ which logs using [slf4net](https://github.com/englishtown/slf4net)
* __Akka.Logger.Serilog__ which logs using [serilog](http://serilog.net/). See [Detailed instructions on using Serilog](xref:serilog).
* __Akka.Logger.NLog__  which logs using [NLog](http://nlog-project.org/)

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
