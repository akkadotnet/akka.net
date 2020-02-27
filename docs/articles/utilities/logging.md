---
uid: logging
title: Logging
---

# Logging
For more info see real Akka's documentation: http://doc.akka.io/docs/akka/2.0/scala/logging.html

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
The above NLog components can be found on Nuget (https://www.nuget.org/packages/Akka.Logger.NLog/)

## Configuring Custom Loggers

To configure a custom logger inside your Akka.Config, you need to use a fully qualified .NET class name like this:

```hocon
akka {
    loggers = ["NameSpace.ClassName, AssemblyName"]
}
```

## Logging Unhandled messages

It is possible to configure akka so that Unhandled messages are logged as Debug log events for debug purposes. This can be achieved using the following configuration setting:

```hocon
akka {
    actor.debug.unhandled = on
}
```
## Example configuration
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
```
