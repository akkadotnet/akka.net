//-----------------------------------------------------------------------
// <copyright file="MyActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
