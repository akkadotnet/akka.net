//-----------------------------------------------------------------------
// <copyright file="FlowDocTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit.Xunit2;
using Xunit;

namespace DocsExamples.Streams
{
    public class FlowDocTests : TestKit
    {
        [Fact]
        public void Source_prematerialization()
        {
            #region source-prematerialization

            var matPoweredSource =
                Source.ActorRef<string>(bufferSize: 100, overflowStrategy: OverflowStrategy.Fail);

            (IActorRef, Source<string, NotUsed>) materialized = matPoweredSource.PreMaterialize(Sys.Materializer());

            var actorRef = materialized.Item1;
            var source = materialized.Item2;

            actorRef.Tell("hit");

            // pass source around for materialization
            source.RunWith(Sink.ForEach<string>(Console.WriteLine), Sys.Materializer());

            #endregion
        }
    }
}
