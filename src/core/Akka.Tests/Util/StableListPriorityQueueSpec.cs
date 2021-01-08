//-----------------------------------------------------------------------
// <copyright file="StableListPriorityQueueSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Util;
using FsCheck;
using FsCheck.Xunit;

namespace Akka.Tests.Util
{
    public class StableListPriorityQueueSpec
    {
        public static int Priority(object obj)
        {
            switch (obj)
            {
                case int i:
                    return i;
                case string str:
                    return str.Length;
                default:
                    return 1;
            }
        }

        [Property(MaxTest = 1000)]
        public Property StableListPriorityQueue_must_be_stable(NonEmptyString[] values)
        {
            var sortedValues = values
                .Select(x => x.Item)
                .GroupBy(x => x.Length)
                .OrderBy(x => x.Key)
                .SelectMany(x => x).ToList();
            var pq = new StableListPriorityQueue(10, Priority);

            foreach (var i in values.Select(x => x.Item)) pq.Enqueue(new Envelope(i, ActorRefs.NoSender));

            var isConsistent = pq.IsConsistent().ToProperty().Label("Expected queue to be consistent, but was not.");

            var queueValues = new List<string>();
            while (pq.Count() > 0) queueValues.Add((string)pq.Dequeue().Message);

            var sequenceEqual = queueValues.SequenceEqual(sortedValues).ToProperty().Label(
                $"Expected output to be [{string.Join(",", sortedValues)}] but was [{string.Join(",", queueValues)}]");

            return sequenceEqual.And(isConsistent);
        }
    }
}
