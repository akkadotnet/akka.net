# Akka.NET

![Akka.NET logo](docs/shfb/icons/AkkaNetLogo.Normal.png)

[![Gitter](https://badges.gitter.im/Join%20Chat.svg)](https://gitter.im/akkadotnet/akka.net?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge) <br/>

**Akka.NET** is a professional-grade port of the popular Java/Scala framework [Akka](http://akka.io) distributed actor framework to .NET.

Akka.NET is a [.NET Foundation](https://dotnetfoundation.org/) project.

![.NET Foundation Logo](docs/images/dotnetfoundationhorizontal.svg)

## Build Status

| Stage                               	| Status                                                                                                                                                                                                                                                            	|
|-------------------------------------	|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------	|
| Build                               	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=Windows%20Build)](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)                                   	|
| NuGet Pack                          	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=NuGet%20Pack)](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)                                      	|
| .NET Framework Unit Tests           	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%20Framework%20Unit%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)       	|
| .NET Framework MultiNode Tests      	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%20Framework%20Multi-Node%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev) 	|
| .NET Core (Windows) Unit Tests      	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%20Core%20Unit%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)            	|
| .NET Core (Linux) Unit Tests        	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%20Core%20Unit%20Tests%20(Linux))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)              	|
| .NET Core (Windows) MultiNode Tests 	| [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%20Core%20Multi-Node%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)      	|
| .NET Core (Linux) MultiNode Tests   	|                                                                                                                                                                                                                                                                   	|
| Docs                                	| [![Build Status](https://dev.azure.com/petabridge/akkadotnet-tools/_apis/build/status/Akka.NET%20Docs?branchName=dev)](https://dev.azure.com/petabridge/akkadotnet-tools/_build/latest?definitionId=82&branchName=dev)                                            	|


### Documentation and resources

#### [Akka.NET Project Site](http://getakka.net)


### Install Akka.NET via NuGet

If you want to include Akka.NET in your project, you can [install it directly from NuGet](https://www.nuget.org/packages/Akka)

To install Akka.NET Distributed Actor Framework, run the following command in the Package Manager Console

```
PM> Install-Package Akka
PM> Install-Package Akka.Remote
```

And if you need F# support:

```
PM> Install-Package Akka.FSharp
```

## Builds
Please see [Building Akka.NET](http://getakka.net/community/building-akka-net.html).

To access nightly Akka.NET builds, please [see the instructions here](http://getakka.net/community/getting-access-to-nightly-builds.html).

## Support
If you need help getting started with Akka.NET, there's a number of great community resources online:

* Subscribe to the Akka.NET project feed on Twitter: https://twitter.com/AkkaDotNet  (@AkkaDotNet)
* Join the Akka.NET project Gitter chat: https://gitter.im/akkadotnet/akka.net
* Ask Akka.NET questions on Stack Overflow: http://stackoverflow.com/questions/tagged/akka.net

If you and your company are interested in getting professional Akka.NET support, you can [contact Petabridge for dedicated Akka.NET support](https://petabridge.com/).
