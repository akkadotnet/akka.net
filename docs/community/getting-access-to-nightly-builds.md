---
uid: nightly-builds
title: Akka.NET Nightly Builds
---

# Nightly & Developer Builds
If you're interested in working on the Akka.NET project or just want to try out the very latest Akka.NET edge releases, you can subscribe to the project's [MyGet](http://www.myget.org/) feed.

## Nightly MyGet Feed URL
Below is the URL for the Akka.NET MyGet feeds.

> **https://www.myget.org/F/akkadotnet/api/v2**

To consume this MyGet feed in Visual Studio, [follow the steps outlined in the NuGet documentation for adding a package source to Visual Studio (and use the feed URL above)](http://docs.nuget.org/create/hosting-your-own-nuget-feeds).

Once you've done that you can use the Package Manager in Visual Studio and consume the latest packages:

![Consume pre-release nightly Akka.NET builds from Nuget](/images/nightly-builds.png)

> Make sure you allow for *pre-release* builds - otherwise you won't see the nightly builds!

## Using Symbol Source
If you want to use SymbolSource, this is the URL for the symbol feed:

> **https://nuget.symbolsource.org/MyGet/akkadotnet**

Follow [these instructions for adding this to Visual Studio](http://www.symbolsource.org/Public/Home/VisualStudio).

## Build Frequency and Details

The nightly builds are generated nightly at midnight UTC if there have been modifications to the `dev` branch of Akka.NET since the previous build.