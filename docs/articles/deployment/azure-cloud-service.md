---
uid: azure-cloud-service
title: Azure Cloud Service (Deprecated)
---

# Azure Cloud Service (Deprecated)

> [!IMPORTANT]
> Cloud Services are largely deprecated in Azure now.

The following sample assumes that you have created a new Azure PaaS Cloud Service that contains a single
empty Worker Role. The Cloud Service project templates are added to Visual Studio by installing the
[Azure .Net SDK](http://azure.microsoft.com/en-gb/downloads/).

The Worker Role implementation can be tested locally using the Azure Compute Emulator before deploying to the cloud. The MSDN Azure article ["Using Emulator Express to Run and Debug a Cloud Service Locally"](https://msdn.microsoft.com/en-us/library/azure/dn339018.aspx) describes this in more detail.

The Azure PaaS Worker Role implementation is very similar to the [Windows Service](xref:windows-service).
The quickest way to get started with Akka.Net is to create a simple Worker Role which invokes the top-level
user-actor in the RunAsync() method, as follows:

## WorkerRole.cs

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
