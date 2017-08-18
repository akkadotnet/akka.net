---
uid: serilog
title: Serilog
---

# Using Serilog

## Setup
Install the package __Akka.Logger.Serilog__ to utilize
[Serilog](http://serilog.net/)

```
PM> Install-Package Akka.Logger.Serilog
```

This will also install the required Serilog packages.

Next, you'll need to configure the global `Log.Logger` and also specify to use
the logger in the config when creating the system, for example like this:
```csharp
var logger = new LoggerConfiguration()
	.WriteTo.ColoredConsole()
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

To log using Serilog syntax you need to use the `SerilogLogMessageFormatter`:
```csharp
var log = Context.GetLogger(new SerilogLogMessageFormatter());
...
log.Info("The value is {Counter}", counter);
```
## HOCON configuration

In order to be able to change log level without the need to recompile, we need to employ some sort of configuration.  To use Serilog via HOCON configuration, add the following to the App.config

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

The code can then be updated as follows removing the inline HOCON.  Additionally, in this example, we use Serilog's ability to configure itself through the App.config.  For further information see [Serilog AppSettings](https://github.com/serilog/serilog/wiki/AppSettings)

```csharp
var logger = new LoggerConfiguration()
                .ReadFrom.AppSettings()
                .CreateLogger();            
Serilog.Log.Logger = logger;
var system = ActorSystem.Create("my-test-system");
```



