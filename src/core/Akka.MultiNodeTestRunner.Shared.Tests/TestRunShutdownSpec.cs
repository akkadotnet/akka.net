//-----------------------------------------------------------------------
// <copyright file="TestRunShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    /// <summary>
    /// Used to validate that we can get final reporting on shutdown
    /// </summary>
    public class TestRunShutdownSpec : AkkaSpec
    {
        [Fact]
        public void TestCoordinatorEnabledMessageSink_should_receive_TestRunTree_when_EndTestRun_is_received()
        {
            var consoleMessageSink = Sys.ActorOf(Props.Create(() => new ConsoleMessageSinkActor(true)));
            var nodeIndexes = Enumerable.Range(1, 4).ToArray();
            var nodeTests = NodeMessageHelpers.BuildNodeTests(nodeIndexes);

            var beginSpec = new BeginNewSpec(nodeTests.First().TypeName, nodeTests.First().MethodName, nodeTests);
            consoleMessageSink.Tell(beginSpec);

            // create some messages for each node, the test runner, and some result messages
            // just like a real MultiNodeSpec
            var allMessages = NodeMessageHelpers.GenerateMessageSequence(nodeIndexes, 300);
            var runnerMessages = NodeMessageHelpers.GenerateTestRunnerMessageSequence(20);
            var passMessages = NodeMessageHelpers.GenerateResultMessage(nodeIndexes, true);
            allMessages.UnionWith(runnerMessages);
            allMessages.UnionWith(passMessages);

            foreach (var message in allMessages)
                consoleMessageSink.Tell(message);

            //end the spec
            consoleMessageSink.Tell(new EndSpec());

            //end the test run...
            var sinkReadyToTerminate =
                consoleMessageSink.AskAndWait<MessageSinkActor.SinkCanBeTerminated>(new EndTestRun(),
                    TimeSpan.FromSeconds(10));
            Assert.NotNull(sinkReadyToTerminate);

        }
    }
}

