//-----------------------------------------------------------------------
// <copyright file="JsonPersistentTestRunStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Persistence;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests.Persistence
{
    public class JsonPersistentTestRunStoreSpec : AkkaSpec
    {
        [Fact]
        public void Should_save_TestRunTree_as_JSON()
        {
            var testRunStore = new JsonPersistentTestRunStore();
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

            foreach (var message in allMessages)
                testRunCoordinator.Tell(message);

            //end the spec
            testRunCoordinator.Tell(new EndTestRun(), TestActor);
            var testRunData = ExpectMsg<TestRunTree>();

            //save the test run
            var file = Path.GetTempFileName();
            testRunStore.SaveTestRun(file, testRunData).ShouldBeTrue("Should have been able to save test run");
        }

        [Fact]
        public void Should_load_saved_JSON_TestRunTree()
        {
            var testRunStore = new JsonPersistentTestRunStore();
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
            testRunStore.SaveTestRun(file, testRunData).ShouldBeTrue("Should have been able to save test run");

            //retrieve the test run from file
            var retrievedFile = testRunStore.FetchTestRun(file);
            Assert.NotNull(retrievedFile);
            Assert.True(testRunData.Equals(retrievedFile));
        }
    }
}

