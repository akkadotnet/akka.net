---
layout: wiki
title: Serilog
---
# Using Serilog

## Setup
Install the package __Akka.Logger.Serilog__ to utilize [Serilog](http://serilog.net/)

``` 
PM> Install-Package Akka.Logger.Serilog
```

This will also install the required Serilog packages.

Next, you'll need to configure the global `Log.Logger` and also specify to use the logger in the config when creating the system, for example like this:
``` C#
var logger = new LoggerConfiguration()
	.WriteTo.ColoredConsole()
	.MinimumLevel.Information()
	.CreateLogger();
Serilog.Log.Logger = logger;
var system = ActorSystem.Create("my-test-system", "akka { logLevel=INFO,  loggers=[\"Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog\"]}");
```

## Logging
To log inside an actor, using the normal `string.Format()` syntax, get the logger and log:
``` C#
var log = Context.GetLogger();
...
log.Info("The value is {0}", counter);
```

To log using Serilog syntax you need to use the `SerilogLogMessageFormatter`:
``` C#
var log = Context.GetLogger(new SerilogLogMessageFormatter());
...
log.Info("The value is {Counter}", counter);
```
