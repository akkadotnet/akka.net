#region akka-windows-service-actor
using Akka.Actor;
using System.Text.Json;

namespace AkkaWindowsService
{
    internal class MyActor : ReceiveActor
    {
        public MyActor()
        {
            Receive<Joke>(j => Console.WriteLine(JsonSerializer.Serialize(j, options: new JsonSerializerOptions { WriteIndented = true })));
        }
        public static Props Prop()
        {
            return Props.Create<MyActor>();
        }
    }
}
#endregion
