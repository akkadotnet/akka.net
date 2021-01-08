//-----------------------------------------------------------------------
// <copyright file="TestRunCoordinatorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    public class TestRunCoordinatorSpec : AkkaSpec
    {
        [Fact]
        public void TestRunCoordinator_should_start_and_route_messages_to_SpecRunCoordinator()
        {
            var testRunCoordinator = Sys.ActorOf(Props.Create<TestRunCoordinator>());
            var nodeIndexes = Enumerable.Range(1, 4).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var beginSpec = new BeginNewSpec(nodeTests.First().TypeName, nodeTests.First().MethodName, nodeTests);

            //begin a new spec
            testRunCoordinator.Tell(beginSpec);

            // create some messages for each node, the test runner, and some result messages
            // just like a real MultiNodeSpec
            var allMessages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 300);
            var runnerMessages = NodeMessageHelpers.GenerateTestRunnerMessageSequence(20);
            allMessages.UnionWith(runnerMessages);

            foreach(var message in allMessages)
                testRunCoordinator.Tell(message);

            //end the spec
            testRunCoordinator.Tell(new EndTestRun(), TestActor);
            var testRunData = ExpectMsg<TestRunTree>();

            Assert.Single(testRunData.Specs);

            var specMessages = new SortedSet<MultiNodeMessage>();
            foreach (var spec in testRunData.Specs)
            {
                specMessages.UnionWith(spec.RunnerMessages);
                foreach(var fact in spec.NodeFacts)
                    specMessages.UnionWith(fact.Value.EventStream);
            }

            Assert.True(allMessages.SetEquals(specMessages));
               
        }

        [Fact]
        public void TestRunCoordinator_should_publish_FactData_to_Subscribers_when_Specs_complete()
        {
            var testRunCoordinator = Sys.ActorOf(Props.Create<TestRunCoordinator>());
            var nodeIndexes = Enumerable.Range(1, 4).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var beginSpec = new BeginNewSpec(nodeTests.First().TypeName, nodeTests.First().MethodName, nodeTests);

            //register the TestActor as a subscriber for FactData announcements
            testRunCoordinator.Tell(new TestRunCoordinator.SubscribeFactCompletionMessages(TestActor));

            //begin a new spec
            testRunCoordinator.Tell(beginSpec);

            // create some messages for each node, the test runner, and some result messages
            // just like a real MultiNodeSpec
            var allMessages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 300);
            var runnerMessages = NodeMessageHelpers.GenerateTestRunnerMessageSequence(20);
            var passMessages = NodeMessageHelpers.GenerateResultMessage(nodeIndexes, true);
            allMessages.UnionWith(runnerMessages);
            allMessages.UnionWith(passMessages);

            foreach (var message in allMessages)
                testRunCoordinator.Tell(message);

            //end the spec
            testRunCoordinator.Tell(new EndSpec());

            var factData = ExpectMsg<FactData>();
            Assert.True(factData.Passed.Value, "Spec should have passed");
            Assert.True(factData.NodeFacts.All(x => x.Value.Passed.Value), "All individual nodes should have reported test pass");
        }

    }
}

