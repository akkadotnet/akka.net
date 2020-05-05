//-----------------------------------------------------------------------
// <copyright file="FlowStatefulSelectManySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using Xunit;
using static Akka.Streams.Tests.Dsl.TestConfig;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowStatefulSelectManySpec : ScriptedTest
    {
        private ActorMaterializer Materializer { get; }

        public FlowStatefulSelectManySpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static readonly Exception Ex = new TestException("Test");

        [Fact]
        public void A_StatefulSelectMany_must_work_in_happy_case()
        {
            Func<Script<int, int>> script = () =>
            {
                var phases = new[]
                {
                    ((ICollection<int>)new[] {2}, (ICollection<int>)new int[0]),
                    ((ICollection<int>)new[] {1}, (ICollection<int>)new[] {1, 1}),
                    ((ICollection<int>)new[] {3}, (ICollection<int>)new[] {3}),
                    ((ICollection<int>)new[] {6}, (ICollection<int>)new[] {6, 6, 6})
                };
                return Script.Create(phases);
            };

            RandomTestRange(Sys).ForEach(_ =>
            {
                RunScript(script(), Materializer.Settings, flow => flow.StatefulSelectMany<int,int,int, NotUsed>(() =>
                {
                    int? prev = null;
                    return (x =>
                    {
                        if (prev.HasValue)
                        {
                            var result = Enumerable.Range(1, prev.Value).Select(__ => x);
                            prev = x;
                            return result;
                        }

                        prev = x;
                        return new List<int>();
                    });
                }));
            });
        }

        [Fact]
        public void A_StatefulSelectMany_must_be_able_to_restart()
        {
            var probe = Source.From(new[] {2, 1, 3, 4, 1}).StatefulSelectMany<int, int, NotUsed>(() =>
            {
                int? prev = null;

                return (x =>
                {
                    if (x%3 == 0)
                        throw Ex;

                    if (prev.HasValue)
                    {
                        var result = Enumerable.Range(1, prev.Value).Select(__ => x);
                        prev = x;
                        return result;
                    }

                    prev = x;
                    return new List<int>();
                });
            })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.RestartingDecider))
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(2).ExpectNext(1, 1);
            probe.Request(4).ExpectNext(1, 1, 1, 1);
            probe.ExpectComplete();
        }

        [Fact]
        public void A_StatefulSelectMany_must_be_able_to_resume()
        {
            var probe = Source.From(new[] { 2, 1, 3, 4, 1 }).StatefulSelectMany<int, int, NotUsed>(() =>
            {
                int? prev = null;

                return (x =>
                {
                    if (x % 3 == 0)
                        throw Ex;

                    if (prev.HasValue)
                    {
                        var result = Enumerable.Range(1, prev.Value).Select(__ => x);
                        prev = x;
                        return result;
                    }

                    prev = x;
                    return new List<int>();
                });
            })
                .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(2).ExpectNext(1, 1);
            probe.RequestNext(4);
            probe.Request(4).ExpectNext(1, 1, 1, 1);
            probe.ExpectComplete();
        }
    }
}
