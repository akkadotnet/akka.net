---
uid: akkadotnet-v14-upgrade-advisories
title: Akka.NET v1.4 Upgrade Advisories
---

# Akka.NET v1.4 Upgrade Advisories

This document contains specific upgrade suggestions, warnings, and notices that you will want to pay attention to when upgrading between versions within the Akka.NET v1.4 roadmap.

## Upgrading to Akka.NET v1.4.20 From Older Versions

> [!NOTE]
> This is an edge-case issue that only affects users who are sending primitive data types (i.e. `int`, `long`, `string`) directly over Akka.Remote or Akka.Persistence.

Between Akka.NET v1.4.19 and [v1.4.20](https://github.com/akkadotnet/akka.net/releases/tag/1.4.20) we introduced a regression in the wire format of the "primitive" serializer in Akka.NET v1.4.20 - the serializer responsible for transmitting `int`, `long`, and `string` as stand-alone messages in Akka.Remote and Akka.Persistence.

The error message would look like this in clusters that are running a combination of v1.4.20 and any previous versions of Akka.NET:

> `Serializer not defined for message with serializer id [6] and manifest []. Transient association error (association remains live). Cannot find manifest class [S] for serializer with id [17].`

You can see the PR that introduced this regression here: <https://github.com/akkadotnet/akka.net/issues/4986>

This change was originally introduced to assist with cross-platform wire compatibility between .NET Framework and .NET Core, because Microsoft changed the names of all primitive types between the two runtimes when .NET Core was originally introduced.

**Workarounds**:

To work around this issue, if you're affected by it (most users are not:)

* Upgrade all of the nodes to v1.4.20 or later at once;
* Or upgrade directly to Akka.NET v1.4.26 or later, which resolves this issue and prevents the regression from occurring.

## Upgrading to Akka.NET v1.4.26 From Older Versions

> [!NOTE]
> This is an edge-case issue that only affects users who are sending primitive data types (i.e. `int`, `long`, `string`) directly over Akka.Remote or Akka.Persistence.

In Akka.NET v1.4.26 we have introduced a new setting:

```hocon
akka.actor.serialization-settings.primitive.use-legacy-behavior = on
```

This setting is set of `on` by default and it resolves the backwards compatibility issue introduced in the "primitives" serializer described in our [v1.4.20 upgrade advisory](#upgrading-to-akkanet-v1420-from-older-versions).

> [!IMPORTANT]
> If you have:
>
> * Previously upgraded to Akka.NET v1.4.20+ and you have not run into any issues;
> * You have not yet upgraded to Akka.NET v1.4.20+; and
> * You _do not_ plan on running both .NET Framework and .NET Core in the same cluster
> Then you can safely upgrade to v1.4.26 using your normal deployment process.

If you are running a mixed .NET Core and .NET Framework cluster, see the process below.

### Deploying v1.4.26 Into Mixed .NET Core and .NET Framework Environments

_However_, if you are attempting to run a mixed-mode cluster - i.e. some services running on .NET Framework and some running on .NET Core, you will eventually want to turn this setting to `off` in order to facilitate smooth operation between both platforms.

#### Already Deployed v1.4.20 or Later

If you've already deployed v1.4.20 and you have not had any issues with the primitives serializer, do the following:

1. Before you upgrade to v1.4.26 or later set `akka.actor.serialization-settings.primitive.use-legacy-behavior = off` - so any future serialization of primitives will be handled correctly in a cross-platform way;
2. Run your normal deployment process.

#### Have Not Deployed v1.4.20 or Later

If you have not previously deployed to v1.4.20 or later, then do the following:

1. Deploy once with `akka.actor.serialization-settings.primitive.use-legacy-behavior = on` (the default);
2. Once all nodes are completely upgraded to v1.4.26 and later, prepare a second deployment with `akka.actor.serialization-settings.primitive.use-legacy-behavior = off`;
3. Complete the second deployment - you will be fine with all rolling upgrades moving forward.
