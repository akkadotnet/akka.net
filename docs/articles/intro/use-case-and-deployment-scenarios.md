---
uid: use-case-and-deployment-scenarios
title: Use-case and Deployment Scenarios
---
# Use-case and Deployment Scenarios

## Console Application

```csharp
PM> install-package Akka
PM> install-package Akka.Remote
```

```csharp
using Akka;
using Akka.Actor;
using Akka.Configuration;

namespace Foo.Bar
{
    class Program
    {
        static void Main(string[] args)
        {
            //configure remoting for localhost:8081
            var fluentConfig = FluentConfig.Begin()
                .StartRemotingOn("localhost", 8081)
                .Build();

            using (var system = ActorSystem.Create("my-actor-server", fluentConfig))
            {
                //start two services
                var service1= system.ActorOf<Service1>("service1");
                var service2 = system.ActorOf<Service2>("service2");
                Console.ReadKey();
            }
        }
    }
}
```

## ASP.NET

### Creating the Akka.NET resources

Hosting inside an ASP.NET application is easy. The Global.asax would be the designated place to start.

```csharp
public class MvcApplication : System.Web.HttpApplication
{
    protected static ActorSystem ActorSystem;
    //here you would store your toplevel actor-refs
    protected static IActorRef MyActor;

    protected void Application_Start()
    {
        //your mvc config. Does not really matter if you initialise
        //your actor system before or after

        ActorSystem = ActorSystem.Create("app");
        //here you would register your toplevel actors
        MyActor = ActorSystem.ActorOf<MyActor>();
    }
}
```

As you can see the main point here is keeping a static reference to your `ActorSystem` . This ensures it won't be accidentally garbage collected and gets disposed and created with the start and stop events of your web application. 

> [!WARNING]
> When you are hosting inside of `IIS`, the application pool your app lives in could be stopped and started at the whim of `IIS`. This means your `ActorSystem` could be stopped at any given time.

Typically you use a very lightweight `ActorSystem` inside ASP.NET applications, and offload heavy-duty work to a separate Windows Service via Akka.Remote / Akka.Cluster.

### Interaction between Controllers and Akka.NET
In the sample below, we use an Web API Controller:
```csharp
public class SomeController  : ApiController
{
      //expose your endpoint as async
      public async Task<SomeResult> Post(SomeRequest someRequest)
      {
           //send a message based on your incoming arguments to one of the actors you created earlier
           //and await the result by sending the message to `Ask`
           var result = await MvcApplication.MyActor.Ask<SomeResult>(new SomeMessage(someRequest.SomeArg1,someRequest.SomeArg2));
           return result;
      }
}
```

## Windows Service

For windows service deployment it is recommended to use [TopShelf](http://topshelf.readthedocs.org/en/latest/index.html)
to build your Windows Services. It radically simplifies hosting Windows Services.

The quickest way to get started with TopShelf is by creating a Console Application. Which would look like this:

#### Program.cs
```csharp
using Akka.Actor;
using Topshelf;

class Program
{
    static void Main(string[] args)
    {
        HostFactory.Run(x =>
        {
            x.Service<MyActorService>(s =>
            {
                s.ConstructUsing(n => new MyActorService());
                s.WhenStarted(service => service.Start());
                s.WhenStopped(service => service.Stop());
                //continue and restart directives are also available
            });

            x.RunAsLocalSystem();
            x.UseAssemblyInfoForServiceInfo();
        });
    }
}

/// <summary>
/// This class acts as an interface between your application and TopShelf
/// </summary>
public class MyActorService
{
    private ActorSystem mySystem;

    public void Start()
    {
        //this is where you setup your actor system and other things
        mySystem = ActorSystem.Create("MySystem");
    }

    public async void Stop()
    {
        //this is where you stop your actor system
        await mySystem.Terminate();
    }
}
```

The above example is the simplest way imaginable, but there are other styles of integration with TopShelf that give you more control.

Installing with Topshelf is as easy as calling `myConsoleApp.exe install` on the command line.

For all the options and settings check out their [docs](http://topshelf.readthedocs.org/en/latest/index.html).

## Azure PaaS Worker Role

The following sample assumes that you have created a new Azure PaaS Cloud Service that contains a single
empty Worker Role. The Cloud Service project templates are added to Visual Studio by installing the 
[Azure .Net SDK](http://azure.microsoft.com/en-gb/downloads/).

The Worker Role implementation can be tested locally using the Azure Compute Emulator before deploying to the cloud. The MSDN Azure article ["Using Emulator Express to Run and Debug a Cloud Service Locally"](https://msdn.microsoft.com/en-us/library/azure/dn339018.aspx) describes this in more detail.

The Azure PaaS Worker Role implementation is very similar to the [Windows Service](#windows-service). 
The quickest way to get started with Akka.Net is to create a simple Worker Role which invokes the top-level
user-actor in the RunAsync() method, as follows:

#### WorkerRole.cs
```csharp
using Akka.Actor;

namespace MyActorWorkerRole
{
    public class WorkerRole : RoleEntryPoint
    {
        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private readonly ManualResetEvent runCompleteEvent = new ManualResetEvent(false);

        private ActorSystem _actorSystem;

        public override bool OnStart()
        {
            // Setup the Actor System
            _actorSystem = ActorSystem.Create("MySystem");

            return (base.OnStart());
        }

        public override void OnStop()
        {
            this.cancellationTokenSource.Cancel();
            this.runCompleteEvent.WaitOne();

            // Shutdown the Actor System
            _actorSystem.Shutdown();

            base.OnStop();
        }

        public override void Run()
        {
            try
            {
                this.RunAsync(this.cancellationTokenSource.Token).Wait();
            }
            finally
            {
                this.runCompleteEvent.Set();
            }
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            // Create an instance to the top-level user Actor
            var workerRoleActor = _actorSystem.ActorOf<WorkerRoleActor>("WorkerRole");

            // Send a message to the Actor
            workerRoleActor.Tell(new WorkerRoleMessage("Hello World!"));

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
    }
}
```
