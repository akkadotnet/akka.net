//-----------------------------------------------------------------------
// <copyright file="FlowSlidingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSlidingSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSlidingSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2,16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private void Check(IEnumerable<(int, int, int)> gen)
        {
            gen.ForEach(t =>
            {
                var len = t.Item1;
                var win = t.Item2;
                var step = t.Item3;

                var af = Source.FromEnumerator(() => Enumerable.Range(0, int.MaxValue).Take(len).GetEnumerator())
                    .Sliding(win, step)
                    .RunAggregate(new List<IEnumerable<int>>(), (ints, e) =>
                    {
                        ints.Add(e);
                        return ints;
                    }, Materializer);

                var input = Enumerable.Range(0, int.MaxValue).Take(len).ToList();
                var cf = Source.FromEnumerator(() => Sliding(input, win, step).GetEnumerator())
                    .RunAggregate(new List<IEnumerable<int>>(), (ints, e) =>
                    {
                        ints.Add(e);
                        return ints;
                    }, Materializer);

                af.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
                cf.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue();
                af.Result.ShouldAllBeEquivalentTo(cf.Result);
            });
        }

        private static List<List<int>> Sliding(List<int> source, int win, int step)
        {
            var result = new List<List<int>>();

            if (source.Count == 0)
                return result;

            if (source.Count <= win)
            {
                result.Add(source);
                return result;
            }

            while (source.Any())
            {
                var window = source.Take(win).ToList();
                result.Add(window);
                if (source.Count <= win)
                    break;
                source = source.Skip(step).ToList();
            }

            return result;
        }

        [Fact]
        public void Sliding_must_behave_just_like_collections_sliding_with_step_lower_than_window()
        {
            this.AssertAllStagesStopped(() =>
            {
                var random = new Random();
                var gen = Enumerable.Range(1, 1000)
                    .Select(_ =>
                    {
                        var win = random.Next(1, 62);
                        return (random.Next(0, 32), win, random.Next(1, win));
                    });

                Check(gen);
            }, Materializer);
        }

        [Fact]
        public void Sliding_must_behave_just_like_collections_sliding_with_step_equals_window()
        {
            this.AssertAllStagesStopped(() =>
            {
                var random = new Random();
                var gen = Enumerable.Range(1, 1000)
                    .Select(_ =>
                    {
                        var win = random.Next(1, 62);
                        return (random.Next(0, 32), win, win);
                    });

                Check(gen);
            }, Materializer);
        }

        [Fact]
        public void Sliding_must_behave_just_like_collections_sliding_with_step_greater_than_window()
        {
            this.AssertAllStagesStopped(() =>
            {
                var random = new Random();
                var gen = Enumerable.Range(1, 1000)
                    .Select(_ =>
                    {
                        var win = random.Next(1, 62);
                        return (random.Next(0, 32), win, random.Next(win + 1, 128));
                    });

                Check(gen);
            }, Materializer);
        }

        [Fact]
        public void Sliding_must_work_with_empty_sources()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.Empty<int>().Sliding(1).RunForeach(ints => TestActor.Tell(ints), Materializer)
                    .ContinueWith(t =>
                    {
                        if (t.IsCompleted && t.Exception == null)
                            TestActor.Tell("done");
                    });
                    

                ExpectMsg("done");
            }, Materializer);
        }
    }
}
