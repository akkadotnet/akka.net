//-----------------------------------------------------------------------
// <copyright file="FusingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests
{
    public class FusingSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FusingSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static object GetInstanceField(Type type, object instance, string fieldName)
        {
            BindingFlags bindFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Static;
            FieldInfo field = type.GetField(fieldName, bindFlags);
            return field.GetValue(instance);
        }
        
        [Fact]
        public void A_SubFusingActorMaterializer_must_work_with_asynchronous_boundaries_in_the_subflows()
        {
            var async = Flow.Create<int>().Select(x => x*2).Async();
            var t = Source.From(Enumerable.Range(0, 10))
                .Select(x => x*10)
                .MergeMany(5, i => Source.From(Enumerable.Range(i, 10)).Via(async))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Distinct().OrderBy(i => i).ShouldAllBeEquivalentTo(Enumerable.Range(0, 199).Where(i => i%2 == 0));
        }

        [Fact]
        public void A_SubFusingActorMaterializer_must_use_multiple_actors_when_there_are_asynchronous_boundaries_in_the_subflows_manual ()
        {
            string RefFunc()
            {
                var bus = (BusLogging)GraphInterpreter.Current.Log;
                return GetInstanceField(typeof(BusLogging), bus, "_logSource") as string;
            }

            var async = Flow.Create<int>().Select(x =>
            {
                TestActor.Tell(RefFunc());
                return x;
            }).Async();
            var t = Source.From(Enumerable.Range(0, 10))
                .Select(x =>
                {
                    TestActor.Tell(RefFunc());
                    return x;
                })
                .MergeMany(5, i => Source.Single(i).Via(async))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            t.Wait(TimeSpan.FromSeconds(3));
            t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(0, 10));

            var refs = ReceiveN(20);
            // main flow + 10 subflows
            refs.Distinct().Should().HaveCount(11);
        }

        [Fact]
        public void A_SubFusingActorMaterializer_must_use_multiple_actors_when_there_are_asynchronous_boundaries_in_the_subflows_combinator()
        {
            string RefFunc()
            {
                var bus = (BusLogging)GraphInterpreter.Current.Log;
                return GetInstanceField(typeof(BusLogging), bus, "_logSource") as string;
            }

            var flow = Flow.Create<int>().Select(x =>
            {
                TestActor.Tell(RefFunc());
                return x;
            });
            var t = Source.From(Enumerable.Range(0, 10))
                .Select(x =>
                {
                    TestActor.Tell(RefFunc());
                    return x;
                })
                .MergeMany(5, i => Source.Single(i).Via(flow.Async()))
                .Grouped(1000)
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

            t.Wait(TimeSpan.FromSeconds(3));
            t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(0, 10));

            var refs = ReceiveN(20);
            // main flow + 10 subflows
            refs.Distinct().Should().HaveCount(11);
        }
    }
}
