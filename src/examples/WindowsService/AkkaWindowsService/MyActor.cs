﻿// -----------------------------------------------------------------------
//  <copyright file="MyActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#region akka-windows-service-actor

using System.Text.Json;
using Akka.Actor;

namespace AkkaWindowsService;

internal class MyActor : ReceiveActor
{
    public MyActor()
    {
        Receive<Joke>(j =>
            Console.WriteLine(JsonSerializer.Serialize(j, new JsonSerializerOptions { WriteIndented = true })));
    }

    public static Props Prop()
    {
        return Props.Create<MyActor>();
    }
}

#endregion