//-----------------------------------------------------------------------
// <copyright file="FlowSelectManySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSelectManySpec : ScriptedTest
    {
        private readonly ActorMaterializerSettings settings;
        private readonly ActorMaterializer materializer;

        public FlowSelectManySpec(ITestOutputHelper output) : base(output)
        {
            settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(initialSize: 2, maxSize: 16);
            materializer = Sys.Materializer();
        }

        [Fact]
        public void SelectMany_should_map_and_concat()
        {
            var script = Script.Create(
                Tuple.Create<ICollection<int>, ICollection<int>>(new[] { 0 }, new int[0]),
                Tuple.Create<ICollection<int>, ICollection<int>>(new[] { 1 }, new[] { 1 }),
                Tuple.Create<ICollection<int>, ICollection<int>>(new[] { 2 }, new[] { 2, 2 }),
                Tuple.Create<ICollection<int>, ICollection<int>>(new[] { 3 }, new[] { 3, 3, 3 }),
                Tuple.Create<ICollection<int>, ICollection<int>>(new[] { 2 }, new[] { 2, 2 }),
                Tuple.Create<ICollection<int>, ICollection<int>>(new[] { 1 }, new[] { 1 }));

            var random = ThreadLocalRandom.Current.Next(1, 10);
            for (int i = 0; i < random; i++)
                RunScript(script, settings, a => a.SelectMany(x => Enumerable.Range(1, x).Select(_ => x)));
        }

        [Fact]
        public void SelectMany_should_map_and_concat_grouping_with_slow_downstream()
        {
            var subscriber = this.CreateManualProbe<int>();
            var input = new[]
            {
                new[] {1, 2, 3, 4, 5},
                new[] {6, 7, 8, 9, 10},
                new[] {11, 12, 13, 14, 15},
                new[] {16, 17, 18, 19, 20},
            };

            Source
                .From(input)
                .SelectMany(x => x)
                .Select(x =>
                {
                    Thread.Sleep(10);
                    return x;
                })
                .RunWith(Sink.FromSubscriber(subscriber), materializer);

            var subscription = subscriber.ExpectSubscription();
            subscription.Request(100);
            for (int i = 1; i <= 20; i++)
                subscriber.ExpectNext(i);

            subscriber.ExpectComplete();
        }

        [Fact]
        public void SelectMany_should_be_able_to_resume()
        {
            var exception = new Exception("TEST");

            Source
                .From(Enumerable.Range(1, 5))
                .SelectMany(x =>
                {
                    if (x == 3) throw exception;
                    else return new[] {x};
                })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(this.SinkProbe<int>(), materializer)
                .Request(4).ExpectNext(1, 2, 4, 5)
                .ExpectComplete();
        }
    }
}