//-----------------------------------------------------------------------
// <copyright file="NodeMessageHelpers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.Util;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    /// <summary>
    /// Helper class for creating <see cref="MultiNodeMessage"/>
    /// </summary>
    public static class NodeMessageHelpers
    {
        public static IList<NodeTest> BuildNodeTests(IEnumerable<int> nodeIndicies)
        {
            var methodName = Faker.Generators.Strings.GenerateAlphaNumericString();
            var className = Faker.Generators.Strings.GenerateAlphaNumericString();
            var testName = Faker.Generators.Strings.GenerateAlphaNumericString();

            return nodeIndicies.Select(i => new NodeTest() {MethodName = methodName, Node = i, TestName = testName, TypeName = className}).ToList();
        }

        /// <summary>
        /// Meta-function for generating a distribution of messages across multiple nodes
        /// </summary>
        private static SortedSet<MultiNodeMessage> GenerateMessageDistributionForNodes(IEnumerable<int> nodeIndices,
            int count, Func<int, int, SortedSet<MultiNodeMessage>> messageGenerator)
        {
            var nodes = nodeIndices.ToList();
            var messages = new SortedSet<MultiNodeMessage>();

            //special case for 1:1 distribution
            if (nodes.Count == count)
            {
                foreach (var node in nodes)
                {
                    messages.UnionWith(messageGenerator(node, node));
                }
                return messages;
            }

            // Key = nodeIndex, Value = # of allocated messages
            var messageDistribution = new Dictionary<int, int>();
            foreach (var node in nodes)
            {
                messageDistribution[node] = 0;
            }

            var remainingMessages = count;
            var nodeIterator = nodes.GetContinuousEnumerator();

            while (remainingMessages > 0)
            {
                nodeIterator.MoveNext();
                var node = nodeIterator.Current;
                var added = Faker.Generators.Numbers.Int(1, Math.Max(1, remainingMessages / 2));

                //Don't go over the message count
                if (added > remainingMessages)
                    added = remainingMessages;

                messageDistribution[node] += added;
                remainingMessages -= added;
            }

            //generate the assigned sequence for each node
            foreach (var node in messageDistribution)
                messages.UnionWith(messageGenerator(node.Key, node.Value));

            return messages;
        }

        public static SortedSet<MultiNodeMessage> GenerateMessageSequence(IEnumerable<int> nodeIndices, int count)
        {
            return GenerateMessageDistributionForNodes(nodeIndices, count, GenerateMessageSequence);
        }

        public static SortedSet<MultiNodeMessage> GenerateMessageSequence(int nodeIndex, int count)
        {
            var messages = new SortedSet<MultiNodeMessage>();
            var startTime = DateTime.UtcNow;
            foreach (var i in Enumerable.Range(0, count))
            {
                messages.Add(new MultiNodeLogMessage(Faker.Generators.DateTimes.GetTimeStamp(startTime, startTime + TimeSpan.FromSeconds(20)), String.Format("Message {0}", i), nodeIndex,
                    "/foo", LogLevel.InfoLevel));
            }
            return messages;
        }

        public static SortedSet<MultiNodeMessage> GenerateMessageFragmentSequence(IEnumerable<int> nodeIndices, int count)
        {
            return GenerateMessageDistributionForNodes(nodeIndices, count, GenerateMessageFragmentSequence);
        }

        public static SortedSet<MultiNodeMessage> GenerateMessageFragmentSequence(int nodeIndex, int count)
        {
            var messages = new SortedSet<MultiNodeMessage>();
            var startTime = DateTime.UtcNow;
            foreach (var i in Enumerable.Range(0, count))
            {
                messages.Add(new MultiNodeLogMessageFragment(Faker.Generators.DateTimes.GetTimeStamp(startTime, startTime + TimeSpan.FromSeconds(20)), String.Format("Message {0}", i), nodeIndex));
            }
            return messages;
        }

        public static SortedSet<MultiNodeMessage> GenerateTestRunnerMessageSequence(int count)
        {
            var messages = new SortedSet<MultiNodeMessage>();
            var startTime = DateTime.UtcNow;
            foreach (var i in Enumerable.Range(0, count))
            {
                messages.Add(new MultiNodeTestRunnerMessage(Faker.Generators.DateTimes.GetTimeStamp(startTime, startTime + TimeSpan.FromSeconds(20)), String.Format("Message {0}", i),
                    "/foo", LogLevel.InfoLevel));
            }
            return messages;
        }

        public static SortedSet<MultiNodeMessage> GenerateResultMessage(IEnumerable<int> nodeIndices, bool pass)
        {
            var messages = new SortedSet<MultiNodeMessage>();
            var enumerable = nodeIndices as int[] ?? nodeIndices.ToArray();
            return GenerateMessageDistributionForNodes(enumerable, enumerable.Count(),
                (i, i1) => GenerateResultMessage(i, pass));
        }

        public static SortedSet<MultiNodeMessage> GenerateResultMessage(int nodeIndex, bool pass)
        {
            var messages = new SortedSet<MultiNodeMessage>();
            var startTime = DateTime.UtcNow;
            messages.Add(
                new MultiNodeResultMessage(
                    Faker.Generators.DateTimes.GetTimeStamp(startTime, startTime + TimeSpan.FromSeconds(30)),
                    String.Format("Test passed? {0}", pass), nodeIndex, pass));
            return messages;
        }
    }
}

