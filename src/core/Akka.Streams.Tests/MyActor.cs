// -----------------------------------------------------------------------
//  <copyright file="MyActor.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;

public sealed class MyActor : ReceiveActor
{
    public MyActor()
    {
        ReceiveAsync<string>(
            s => s == "hello",
            async _ =>
            {
                await Source.Single(1)
                    .Select(x => x + 1)
                    .ToMaterialized(Sink.ForEach<int>(_ => { }), Keep.Right)
                    .Run(Context.System.Materializer());
            });
    }
}
