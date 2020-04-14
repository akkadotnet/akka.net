using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace AkkaSandbox
{
  public class Program
  {
    static void Main(string[] args)
    {
      CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
    Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) =>
    {
      services.AddHostedService<HelloWorldHostedService>();
    });
  }

  public class HelloWorldHostedService : IHostedService
  {
    public Task StartAsync(CancellationToken cancellationToken)
    {
      Console.WriteLine("Starting the host.");

      var system = ActorSystem.Create("testSystem");
      var firstRef = system.ActorOf(Props.Create<PrintMyActorRefActor>(), "first-actor");
      Console.WriteLine($"First: {firstRef}");
      firstRef.Tell("printit", ActorRefs.NoSender);

      return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
      Console.WriteLine("Stopping the host.");

      return Task.CompletedTask;
    }
  }

  public class PrintMyActorRefActor : UntypedActor
  {
    protected override void OnReceive(object message)
    {
      switch (message)
      {
        case "printit":
          IActorRef secondRef = Context.ActorOf(Props.Empty, "second-actor");
          Console.WriteLine($"Second: {secondRef}");
          break;
      }
    }
  }
}
