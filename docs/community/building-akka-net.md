---
layout: docs.hbs
title: Building and Distributing Akka.NET
---
Akka.Net has an official [NuGet package](http://www.nuget.org/packages/Akka).

To install Akka.NET, run the following command in the Package Manager Console:
````
   PM> Install-Package Akka -Pre
````

You can also build it locally from the source code.

## Building Akka.NET with Fake

The build as been ported to [Fake](http://fsharp.github.io/FAKE/) to make it
even easier to compile.

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

The build uses the last version number specified in the
[RELEASE_NOTES.md](https://github.com/akkadotnet/akka.net/blob/dev/RELEASE_NOTES.md)
file.

The release notes are also used in nuget packages.

### Running tests

To run unit tests from the command line, run the following command:

````
build test
````

### Running MultiNodeTests

To run the multiple node specifications from the command line, run the following
command:

````
build multinodetests
````

To run the multinode specifications for a subset of akka, you can supply a filter:

````
build multinodetests spec-assembly=<filter>
````

For example to run only the specifications for Akka.Remote the command would be:

````
build multinodetests spec-assembly=remote
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

### Detailed Help from command line

The command line supplies some detailed help on the usage of build.

````
build help
build helpnuget
build helpdocs
build helpmultinodetests
````
    
