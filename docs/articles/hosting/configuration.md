---
uid: hosting-configuration
title: Microsoft.Extensions.Configuration Integration
---

# Microsoft.Extensions.Configuration Integration

## IConfiguration to HOCON Adapter

The `AddHocon` extension method can convert `Microsoft.Extensions.Configuration` `IConfiguration` into HOCON `Config` instance and adds it to the ActorSystem being configured.

* Unlike `IConfiguration`, all HOCON key names are **case sensitive**.
* **Unless enclosed inside double quotes**, all "." (period) in the `IConfiguration` key will be treated as a HOCON object key separator
* `IConfiguration` **does not support object composition**, if you declare the same key multiple times inside multiple configuration providers (JSON/environment variables/etc), **only the last one declared will take effect**.
* For environment variable configuration provider:
  * "__" (double underline) will be converted to "." (period).
  * "_" (single underline) will be converted to "-" (dash).
  * If all keys are composed of integer parseable keys, the whole object is treated as an array

**Example:**

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "akka": {
    "cluster": {
      "roles": [ "front-end", "back-end" ],
      "min-nr-of-members": 3,
      "log-info": true
    }
  }    
}
```

Environment variables:

```powershell
AKKA__ACTOR__TELEMETRY__ENABLE=true
AKKA__CLUSTER__SEED_NODES__0=akka.tcp//mySystem@localhost:4055
AKKA__CLUSTER__SEED_NODES__1=akka.tcp//mySystem@localhost:4056
AKKA__CLUSTER__SEED_NODE_TIMEOUT=00:00:05
```

Note the integer parseable key inside the seed-nodes configuration, seed-nodes will be parsed as an array. These environment variables will be parsed as HOCON settings:

```hocon
akka {
  actor {
    telemetry.enabled: on
  }
  cluster {
    seed-nodes: [ 
      "akka.tcp//mySystem@localhost:4055",
      "akka.tcp//mySystem@localhost:4056" 
    ]
    seed-node-timeout: 5s
  }
}
```

Example code:

```csharp
/*
Both appsettings.json and environment variables are combined
into HOCON configuration:

akka {
  actor.telemetry.enabled: on
  cluster {
    roles: [ "front-end", "back-end" ]
    seed-nodes: [ 
      "akka.tcp//mySystem@localhost:4055",
      "akka.tcp//mySystem@localhost:4056" 
    ]
    min-nr-of-members: 3
    seed-node-timeout: 5s
    log-info: true
  }
}
*/
var host = new HostBuilder()
    .ConfigureHostConfiguration(builder =>
    {
        // Setup IConfiguration to load from appsettings.json and
        // environment variables
        builder
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("mySystem", (builder, provider) =>
            {
                // convert IConfiguration to HOCON
                var akkaConfig = context.Configuration.GetSection("akka");
                builder.AddHocon(akkaConfig, HoconAddMode.Prepend); 
            });
    });
```

### Special Characters and Case Sensitivity

This advanced usage of the `IConfiguration` adapter is solely used for edge cases where HOCON key capitalization needs to be preserved, such as declaring serialization binding. Note that when you're using this feature, none of the keys are normalized, you will have to write all of your keys in a HOCON compatible way.

`appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "akka": {
    "\"Key.With.Dots\"": "Key Value",
    "cluster": {
      "roles": [ "front-end", "back-end" ],
      "min-nr-of-members": 3,
      "log-info": true
    }
  }    
}
```

Note that "Key.With.Dots" needs to be inside escaped double quotes, this is a HOCON requirement that preserves the "." (period) inside HOCON property names.

Environment variables:

```powershell
PS C:/> [Environment]::SetEnvironmentVariable('akka__actor__telemetry__enabled', 'true')
PS C:/> [Environment]::SetEnvironmentVariable('akka__actor__serialization_bindings__"System.Object"', 'hyperion')
PS C:/> [Environment]::SetEnvironmentVariable('akka__cluster__seed_nodes__0', 'akka.tcp//mySystem@localhost:4055')
PS C:/> [Environment]::SetEnvironmentVariable('akka__cluster__seed_nodes__1', 'akka.tcp//mySystem@localhost:4056')
PS C:/> [Environment]::SetEnvironmentVariable('akka__cluster__seed_node_timeout', '00:00:05')
```

Note that:

1. All of the environment variable names are in lower case, except "System.Object" where it needs to preserve name capitalization.
2. To set serialization binding via environment variable, you have to use "." (period) instead of "__" (double underscore), this might be problematic for some shell scripts and there is no way of getting around this.

Example code:

```csharp
/*
Both appsettings.json and environment variables are combined
into HOCON configuration:

akka {
  "Key.With.Dots": Key Value
  actor {
    telemetry.enabled: on
    serialization-bindings {
      "System.Object" = hyperion
    }
  }
  cluster {
    roles: [ "front-end", "back-end" ]
    seed-nodes: [ 
      "akka.tcp//mySystem@localhost:4055",
      "akka.tcp//mySystem@localhost:4056" 
    ]
    min-nr-of-members: 3
    seed-node-timeout: 5s
    log-info: true
  }
}
*/
var host = new HostBuilder()
    .ConfigureHostConfiguration(builder =>
    {
        // Setup IConfiguration to load from appsettings.json and
        // environment variables
        builder
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("mySystem", (builder, provider) =>
            {
                // convert IConfiguration to HOCON
                var akkaConfig = context.Configuration.GetSection("akka");
                // Note the last method argument is set to false
                builder.AddHocon(akkaConfig, HoconAddMode.Prepend, false); 
            });
    });
```
