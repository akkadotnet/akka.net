For more info see real Akka's documentation: http://doc.akka.io/docs/akka/2.0/scala/logging.html

## How to Log
To log in an actor, create a logger and assign it to a private field:

``` csharp
private readonly LoggingAdapter _log = Logging.GetLogger(Context);
```

Use the `Debug`, `Info`, `Warn` and `Error` methods to log.
``` csharp
_log.Debug("Some message");
```

## Standard Loggers
Akka .NET comes with two built in loggers.

* __StandardOutLogger__
* __BusLogging__

## Contrib Loggers
These loggers are also available as separate nuget packages

* __Akka.Logger.slf4net__ which logs using [slf4net](https://github.com/englishtown/slf4net)
* __Akka.Logger.Serilog__ which logs using [serilog](http://serilog.net/). See [Detailed instructions on using Serilog](Serilog).
* __Akka.Logger.NLog__  which logs using [NLog](http://nlog-project.org/)

Note that you need to modify the config as explained below.

## Configuring Custom Loggers

To configure a custom logger inside your Akka.Config, you need to use a fully qualified .NET class name like this:

```hocon
akka {
    loggers = ["NameSpace.ClassName,AssemblyName"]
}
```

## Logging Unhandled messages

It is possible to configure akka so that Unhandled Messages are logged as Debug log events for debug purposes. This can be achieved using the following configuration setting:

```hocon
akka {
    actor.debug.unhandled = on
}
```