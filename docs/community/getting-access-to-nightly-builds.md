---
uid: nightly-builds
title: Akka.NET Nightly Builds
---

# Nightly & Developer Builds
If you're interested in working on the Akka.NET project or just want to try out the very latest Akka.NET edge releases, you can subscribe to the project's [MyGet](http://www.myget.org/) feed.

## Nightly MyGet Feed URL
Below is the URL for the Akka.NET MyGet feeds.

> **https://f.feedz.io/akkadotnet/akka/nuget/index.json**

To consume this MyGet feed in Visual Studio, [follow the steps outlined in the NuGet documentation for adding a package source to Visual Studio (and use the feed URL above)](http://docs.nuget.org/create/hosting-your-own-nuget-feeds).

Once you've done that you can use the Package Manager in Visual Studio and consume the latest packages:

![Consume pre-release nightly Akka.NET builds from Nuget](/images/nightly-builds.png)

> Make sure you allow for *pre-release* builds - otherwise you won't see the nightly builds!

## Accessing Nightly Symbols
If you want access to debug symbols for the Akka.NET nightly packages, you can access them here:

> **https://f.feedz.io/akkadotnet/akka/symbols**

Follow [these instructions for adding this to Visual Studio or JetBrains Rider](https://feedz.io/docs/package-types/symbols).

## Adding SourceLink Support for Debugging Akka.NET
Akka.NET supports [SourceLink](https://github.com/dotnet/sourcelink), which allows you to step directly into the source code associated with your local version of Akka.NET while debugging.

If you need help configuring Visual Studio to use SourceLink, please read: "[How to Configure Visual Studio to Use SourceLink to Step into NuGet Package Source](https://aaronstannard.com/visual-studio-sourcelink-setup/)"

## Build Frequency and Details

The nightly builds are generated nightly at midnight UTC if there have been modifications to the `dev` branch of Akka.NET since the previous build.
