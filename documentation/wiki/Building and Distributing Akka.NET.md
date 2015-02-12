---
layout: wiki
title: Building and Distributing Akka.NET
---
Akka.Net has an official beta [NuGet package](http://www.nuget.org/packages/Akka).

To install Akka.net, run the following command in the Package Manager Console:
````
   PM> Install-Package Akka -Pre
````

You can also build it locally from the source code.

## Building Akka.NET with Fake

The build as been ported to [Fake](http://fsharp.github.io/FAKE/) to make it even easier to compile.

Clone the source code from GitHub (currently only on the dev branch):

````
    git clone https://github.com/akkadotnet/akka.net.git -b dev
````

## Running build task

There is no need to install anything specific before running the build.

Once in the directory, run the build.cmd with the target All:

````
     build all
````

The ```all``` targets runs the following targets in order:
* Build
* Test
* Nuget

### Version management

The build uses the last version number specified in the [RELEASE_NOTES.md](https://github.com/akkadotnet/akka.net/blob/dev/RELEASE_NOTES.md) file.

The release notes are also used in nuget packages.

### Running tests

To run unit tests from the command line, run the following command:

````
    build test
````

### Creating Nuget distributions

To create nuget packages locally, run the following command:

````
    build nuget
````

To create and publish packages to nuget.org, specify the nuget key:
````
    build nuget nugetkey=<key>
````

or to run also unit tests before publishing:
````
   build all nugetkey=<key>
````

## Building using Rake/Albacore

Akka.Net can also use [Albacore (Rake)](http://albacorebuild.net/) for builds. The commands are simple, but you will need to install Ruby on your system in order to execute any Rake commands.

We recommend using the [Rails Installer for Windows](http://railsinstaller.org/).

### Running Build Tasks
To run a basic build a Pigeon, open a command prompt to the root directory of your Pigeon repository and type the following command:

````
C:\akka.net> rake
````

This will automatically invoke a simple MSBuild of your local copy of Pigeon.

### Version Management
Akka.NET's Rake file includes the ability to increment the version number for new releases.

Akka.NET follows the standard .NET version number convention: `{Major Version}.{Minor Version}.{Revision}.{Build}`.

You can modify the build number using the following commands:

* `rake bump_build_number` - bumps the **build** number.
* `rake bump_revision_number` - bumps the **revision** number.
* `rake bump_minor_version_number` - bumps the **minor version** number.
* `rake bump_major_version_number` - bumps the **major version** number.

### Creating NuGet Distributions
To create a NuGet distribution, you can run the following command:

````
C:\> rake nuget
````

This will create a new instance of all Akka.NET NuGet packages in the `/build/` folder, using Release configuration settings.

If you need to create a Debug configuration NuGet package, then you can use the `nuget_debug` command instead.