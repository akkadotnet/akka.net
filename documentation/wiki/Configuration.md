---
layout: wiki
title: Configuration
---
# HOCON Configuration

Read more about the [[HOCON]] spec.

## Example config
```hocon
akka {
    actor {
        default-dispatcher.throughput = 20
    }
    remote {
          helios.tcp {
              transport-class = 
                "Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote"
              transport-protocol = tcp
              port = 8091
              hostname = "127.0.0.1"
          }
    }
}
```

## Using hardcoded HOCON config in C# API
```csharp
var config = ConfigurationFactory.ParseString(@"
akka.remote.helios.tcp {
              transport-class = 
           ""Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote""
              transport-protocol = tcp
              port = 8091
              hostname = ""127.0.0.1""
          }");

var system = ActorSystem.Create("Mysystem",config);
```

## Using a .NET config file:

>**Warning:**<br/>
Be extra careful when copying the configuration from hardcoded strings. Be on alert for double quotation marks, which may cause failures during ActorSystem creation.<br/>
Example:<br/>
The below string in the .config file is incorrect.<br/>
`provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""`<br/>
This is the correct form.<br/>
`provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"`

### App.config
```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <configSections>
    <section name="akka" 
             type="Akka.Configuration.Hocon.AkkaConfigurationSection, Akka" />
  </configSections>

  <akka>
    <hocon>
      <![CDATA[
          akka {
            log-config-on-start = off
            stdout-loglevel = INFO
            loglevel = ERROR
            actor {
              provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
              debug {
                  receive = on
                  autoreceive = on
                  lifecycle = on
                  event-stream = on
                  unhandled = on
              }
            }
            remote {
              helios.tcp {
                  transport-class = 
            "Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote"
                  #applied-adapters = []
                  transport-protocol = tcp
                  port = 8091
                  hostname = "127.0.0.1"
              }
            log-remote-lifecycle-events = INFO
          }
      ]]>
    </hocon>
  </akka>
</configuration>
```
We can now use the `ConfigurationManager` to retrieve the "akka" section and use it's `AkkaConfig` property,
which contains the configuration in a form that is used by the ActorSystem.
```csharp
var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
var system = ActorSystem.Create("Mysystem", section.AkkaConfig);
```

## Fluent Configuration

>**Warning!**<br/>
The fluent configuration support is experimental and might change or be removed.

```csharp
var fluentConfig = FluentConfig.Begin()
    .StdOutLogLevel(LogLevel.DebugLevel)
    .LogConfigOnStart(true)
    .LogLevel(LogLevel.ErrorLevel)   
    .LogLocal(
        receive: true,
        autoReceive: true,
        lifecycle: true,
        eventStream: true,
        unhandled: true
    )
    .LogRemote(
        lifecycleEvents: LogLevel.DebugLevel,
        receivedMessages: true,
        sentMessages: true
    )
    .StartRemotingOn("localhost", 8081)
    .Build();

var system = ActorSystem.Create("MyServer", fluentConfig);
```

The fluent configuration support is a simple builder ontop of the HOCON configuration.
This is because Akka.NET uses HOCON internally inside most of it's modules, such as routers and remoting.

Fluent configuration is based on extension methods on the `FluentConfig` class, this makes it possible to supply new config methods for different modules w/o altering the configuration API.