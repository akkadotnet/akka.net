//-----------------------------------------------------------------------
// <copyright file="SpecRunCoordinatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    public class SpecRunCoordinatorSpec : AkkaSpec
    {
        [Fact]
        public void SpecRunCoordinator_should_log_TestRunner_messages()
        {
            var nodeIndexes = Enumerable.Range(1, 3).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var specRunCoordinator = Sys.ActorOf(Props.Create(() => new SpecRunCoordinator(nodeTests.First()
                .TypeName, nodeTests.First().MethodName, nodeTests)));

            var runnerMessages = NodeMessageHelpers.GenerateTestRunnerMessageSequence(100);
            foreach (var multiNodeMessage in runnerMessages)
            {
                specRunCoordinator.Tell(multiNodeMessage);
            }

            //End the test
            specRunCoordinator.Tell(new EndSpec(), TestActor);
            var factData = ExpectMsg<FactData>();

            Assert.True(factData.RunnerMessages.Any());
            Assert.True(runnerMessages.SetEquals(factData.RunnerMessages));
        }

        [Fact]
        public void SpecRunCoordinator_should_route_messages_correctly_to_child_NodeDataActors()
        {
            var nodeIndexes = Enumerable.Range(1, 3).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var specRunCoordinator = Sys.ActorOf(Props.Create(() => new SpecRunCoordinator(nodeTests.First()
                .TypeName, nodeTests.First().MethodName, nodeTests)));

            var messages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 100);
            foreach (var multiNodeMessage in messages)
            {
                specRunCoordinator.Tell(multiNodeMessage);
            }

            //End the test
            specRunCoordinator.Tell(new EndSpec(), TestActor);
            var factData = ExpectMsg<FactData>();

            // Combine the messages from each individual NodeData back into a unioned set. 
            // Should match what we sent (messages.)
            var combinedTimeline = new SortedSet<MultiNodeMessage>();
            foreach(var nodeData in factData.NodeFacts)
                combinedTimeline.UnionWith(nodeData.Value.EventStream);

            Assert.Equal(nodeIndexes.Length, factData.NodeFacts.Count);
            Assert.True(messages.SetEquals(combinedTimeline));
        }

        [Fact]
        public void SpecRunCoordinator_should_mark_spec_as_passed_if_all_nodes_pass()
        {
            var nodeIndexes = Enumerable.Range(1, 3).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var specRunCoordinator = Sys.ActorOf(Props.Create(() => new SpecRunCoordinator(nodeTests.First()
                .TypeName, nodeTests.First().MethodName, nodeTests)));

            var messages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 100);

            //Add some result (PASS) messages
            messages.UnionWith(NodeMessageHelpers.GenerateResultMessage(nodeIndexes, true));

            foreach (var multiNodeMessage in messages)
            {
                specRunCoordinator.Tell(multiNodeMessage);
            }

            //End the test
            specRunCoordinator.Tell(new EndSpec(), TestActor);
            var factData = ExpectMsg<FactData>();

            Assert.Equal(nodeIndexes.Length, factData.NodeFacts.Count);
            Assert.True(factData.NodeFacts.All(x => x.Value.Passed.Value), "each individual node should have marked their test as passed");
            Assert.True(factData.Passed.Value, "SpecCoordinator should have marked spec as passed");
        }

        [Fact]
        public void SpecRunCoordinator_should_mark_spec_as_failed_if_all_nodes_fail()
        {
            var nodeIndexes = Enumerable.Range(1, 3).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var specRunCoordinator = Sys.ActorOf(Props.Create(() => new SpecRunCoordinator(nodeTests.First()
                .TypeName, nodeTests.First().MethodName, nodeTests)));

            var messages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 100);

            //Add some result (FAIL) messages
            messages.UnionWith(NodeMessageHelpers.GenerateResultMessage(nodeIndexes, false));

            foreach (var multiNodeMessage in messages)
            {
                specRunCoordinator.Tell(multiNodeMessage);
            }

            //End the test
            specRunCoordinator.Tell(new EndSpec(), TestActor);
            var factData = ExpectMsg<FactData>();

            Assert.Equal(nodeIndexes.Length, factData.NodeFacts.Count);
            Assert.True(factData.NodeFacts.All(x => !x.Value.Passed.Value), "each individual node should have marked their test as failed");
            Assert.False(factData.Passed.Value, "SpecCoordinator should have marked spec as failed");
        }

        [Fact]
        public void SpecRunCoordinator_should_mark_spec_as_failed_if_one_node_fails()
        {
            var nodeIndexes = Enumerable.Range(1, 3).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var specRunCoordinator = Sys.ActorOf(Props.Create(() => new SpecRunCoordinator(nodeTests.First()
                .TypeName, nodeTests.First().MethodName, nodeTests)));

            var messages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 100);

            //Add some result (1 FAIL, 2 PASS) messages
            var front = nodeIndexes.First();
            var nodesMinusFront = nodeIndexes.Where(x => x != front);
            messages.UnionWith(NodeMessageHelpers.GenerateResultMessage(nodesMinusFront, true)); //PASS messages
            messages.UnionWith(NodeMessageHelpers.GenerateResultMessage(front, false)); //one FAIL message

            foreach (var multiNodeMessage in messages)
            {
                specRunCoordinator.Tell(multiNodeMessage);
            }

            //End the test
            specRunCoordinator.Tell(new EndSpec(), TestActor);
            var factData = ExpectMsg<FactData>();

            Assert.Equal(nodeIndexes.Length, factData.NodeFacts.Count);
            Assert.True(factData.NodeFacts.Count(x => !x.Value.Passed.Value) == 1, "one node should have marked their test as failed");
            Assert.True(factData.NodeFacts.Count(x => x.Value.Passed.Value) == nodeIndexes.Length - 1, "rest of nodes should have marked their test as passed");
            Assert.False(factData.Passed.Value, "SpecCoordinator should have marked spec as failed");
        }
    }
}

