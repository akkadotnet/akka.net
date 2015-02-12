using Akka.Actor;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Stashing
{
    class Program
    {
        static void Main(string[] args)
        {
            // This code will generate a new request every 500 ms
            // Random timeouts will cause requests to be stashed and processed later
            // Every 30 requests, an unhandled exception will be thrown and the request won't be retried

            var system = ActorSystem.Create("MySystem");
            var handler = system.ActorOf<RequestHandler>();

            var cts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                int id = 1;

                while (!cts.IsCancellationRequested)
                {
                    handler.Tell(new Request { ID = id++ });
                    await Task.Delay(500);
                }
            });

            Console.WriteLine("Press ENTER to stop generating requests...");
            Console.ReadLine();
            cts.Cancel();

            Console.WriteLine("Press ENTER to exit...");
            Console.ReadLine();
        }
    }
}
