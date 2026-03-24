//-----------------------------------------------------------------------
// <copyright file="JsonPersistentTestRunStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Persistence;
using Akka.MultiNode.TestAdapter.Internal.Reporting;
using Akka.MultiNode.TestAdapter.Internal.Sinks;
using FluentAssertions;
using Xunit;

namespace Akka.MultiNode.TestAdapter.Tests.Internal.Persistence
{
    public class JsonPersistentTestRunStoreSpec : TestKit.Xunit2.TestKit
    {
        [Fact]
        public void Should_save_TestRunTree_as_JSON()
        {
            var testRunStore = new JsonPersistentTestRunStore();
            var testRunCoordinator = Sys.ActorOf(Props.Create<TestRunCoordinator>());
            var nodeIndexes = Enumerable.Range(1, 4).ToArray();

            var beginSpec = new BeginNewSpec(NodeMessageHelpers.BuildNodeTests(nodeIndexes));

            //begin a new spec
            testRunCoordinator.Tell(beginSpec);

            // create some messages for each node, the test runner, and some result messages
            // just like a real MultiNodeSpec
            var allMessages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 300);
            var runnerMessages = NodeMessageHelpers.GenerateTestRunnerMessageSequence(20);
            allMessages.UnionWith(runnerMessages);

            foreach (var message in allMessages)
                testRunCoordinator.Tell(message);

            //end the spec
            testRunCoordinator.Tell(new EndTestRun(), TestActor);
            var testRunData = ExpectMsg<TestRunTree>();

            //save the test run
            var file = Path.GetTempFileName();
            testRunStore.SaveTestRun(file, testRunData).Should().BeTrue("Should have been able to save test run");
        }

        [Fact]
        public void Should_load_saved_JSON_TestRunTree()
        {
            var testRunStore = new JsonPersistentTestRunStore();
            var testRunCoordinator = Sys.ActorOf(Props.Create<TestRunCoordinator>());
            var nodeIndexes = Enumerable.Range(1, 4).ToArray();

            var beginSpec = new BeginNewSpec(NodeMessageHelpers.BuildNodeTests(nodeIndexes));

            //begin a new spec
            testRunCoordinator.Tell(beginSpec);

            // create some messages for each node, the test runner, and some result messages
            // just like a real MultiNodeSpec
            var allMessages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 300);
            var runnerMessages = NodeMessageHelpers.GenerateTestRunnerMessageSequence(20);
            var successMessages = NodeMessageHelpers.GenerateResultMessage(nodeIndexes, true);
            var messageFragments = NodeMessageHelpers.GenerateMessageFragmentSequence(nodeIndexes, 100);
            allMessages.UnionWith(runnerMessages);
            allMessages.UnionWith(successMessages);
            allMessages.UnionWith(messageFragments);

            foreach (var message in allMessages)
                testRunCoordinator.Tell(message);

            //end the spec
            testRunCoordinator.Tell(new EndTestRun(), TestActor);
            var testRunData = ExpectMsg<TestRunTree>();

            //save the test run
            var file = Path.GetTempFileName();
            testRunStore.SaveTestRun(file, testRunData).Should().BeTrue("Should have been able to save test run");

            //retrieve the test run from file
            var retrievedFile = testRunStore.FetchTestRun(file);
            Assert.NotNull(retrievedFile);
            Assert.True(testRunData.Equals(retrievedFile));
        }
    }
}

