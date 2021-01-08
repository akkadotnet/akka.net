//-----------------------------------------------------------------------
// <copyright file="IndexSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.TestKit;
using Akka.Util;
using Xunit;

namespace Akka.Tests.Util
{
    public class IndexSpec : AkkaSpec
    {
        private Index<string, int> Empty => new Index<string, int>();

        private Index<string, int> IndexWithValues
        {
            get
            {
                var index = Empty;
                index.Put("s1", 1);
                index.Put("s1", 2);
                index.Put("s1", 3);
                index.Put("s2", 1);
                index.Put("s2", 2);
                index.Put("s3", 3);
                return index;
            }
        }

        [Fact]
        public void Index_must_take_and_return_a_value()
        {
            var index = Empty;
            index.Put("s1", 1);
            Assert.True(index.Values.SetEquals(new HashSet<int>(new[] {1})));
        }

        [Fact]
        public void Index_must_take_and_return_several_values()
        {
            var index = Empty;
            index.Put("s1", 1).ShouldBeTrue();
            index.Put("s1", 1).ShouldBeFalse();
            index.Put("s1", 2);
            index.Put("s1", 3);
            index.Put("s2", 4);
            new HashSet<int>(index["s1"]).SetEquals(new HashSet<int>(new[] {1, 2, 3})).ShouldBeTrue();
            new HashSet<int>(index["s2"]).SetEquals(new HashSet<int>(new[] {4})).ShouldBeTrue();
        }

        [Fact]
        public void Index_must_remove_values()
        {
            var index = Empty;
            index.Put("s1", 1);
            index.Put("s1", 2);
            index.Put("s2", 1);
            index.Put("s2", 2);

            // Remove value
            index.Remove("s1", 1).ShouldBeTrue();
            index.Remove("s1", 1).ShouldBeFalse();
            new HashSet<int>(index["s1"]).SetEquals(new HashSet<int>(new[] { 2 })).ShouldBeTrue();

            // Remove key
            new HashSet<int>(index.Remove("s2")).SetEquals(new HashSet<int>(new[] {1, 2})).ShouldBeTrue();
            index.Remove("s2").Any().ShouldBeFalse("Should not be able to remove set a second time");
            index["s2"].Any().ShouldBeFalse("Removed index should not exist");
        }

        [Fact]
        public void Index_must_remove_specified_values()
        {
            var index = Empty;
            index.Put("s1", 1);
            index.Put("s1", 2);
            index.Put("s1", 3);
            index.Put("s2", 1);
            index.Put("s2", 2);
            index.Put("s3", 2);

            index.RemoveValue(1);
            new HashSet<int>(index["s1"]).SetEquals(new HashSet<int>(new[] { 2,3 })).ShouldBeTrue();
            new HashSet<int>(index["s2"]).SetEquals(new HashSet<int>(new[] { 2 })).ShouldBeTrue();
            new HashSet<int>(index["s3"]).SetEquals(new HashSet<int>(new[] { 2 })).ShouldBeTrue();
        }

        [Fact]
        public void Index_must_apply_a_function_for_all_key_value_pairs_and_find_every_value()
        {
            var index = IndexWithValues;
            var valueCount = 0;
            index.ForEach((s, i) =>
            {
                valueCount = valueCount + 1;
                index.FindValue(s, i1 => i1 == i).ShouldBe(i);
            });
            valueCount.ShouldBe(6);
        }

        [Fact]
        public void Index_must_be_cleared()
        {
            var index = IndexWithValues;
            index.IsEmpty.ShouldBeFalse();
            index.Clear();
            index.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void Index_must_be_accessed_in_parallel()
        {
            var index = new Index<int,int>();

            var nrOfTasks = 10000;
            var nrOfKeys = 10;
            var nrOfValues = 10;

            // Fill index
            for(var key = 0; key < nrOfKeys; key++)
                for (var value = 0; value < nrOfValues; value++)
                    index.Put(key, value);

            // Tasks to be executed in parallel
            Action putTask =
                () => index.Put(ThreadLocalRandom.Current.Next(nrOfKeys), ThreadLocalRandom.Current.Next(nrOfValues));

            Action removeTask1 = () => index.Remove(ThreadLocalRandom.Current.Next(nrOfKeys/2), ThreadLocalRandom.Current.Next(nrOfValues));
            Action removeTask2 = () => index.Remove(ThreadLocalRandom.Current.Next(nrOfKeys / 2));
            Action readTask = () =>
            {
                var key = ThreadLocalRandom.Current.Next(nrOfKeys);
                var values = index[key];
                if (key >= nrOfKeys/2)
                {
                    values.Any().ShouldBeTrue();
                }
            };

            Func<Action> randomTask = () =>
            {
                var next = ThreadLocalRandom.Current.Next(4);
                switch (next)
                {
                    case 0:
                        return putTask;
                    case 1:
                        return removeTask1;
                    case 2:
                        return removeTask2;
                    default:
                        return readTask;
                }
            };

            var tasks = Enumerable.Repeat(randomTask(), nrOfTasks).Select(Task.Run);

            Task.WaitAll(tasks.ToArray(), GetTimeoutOrDefault(null));
        }
    }
}

