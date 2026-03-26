//-----------------------------------------------------------------------
// <copyright file="TestRunShutdownSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Sinks;
using Xunit;

namespace Akka.MultiNode.TestAdapter.Tests.Internal
{
    /// <summary>
    /// Used to validate that we can get final reporting on shutdown
    /// </summary>
    public class TestRunShutdownSpec : TestKit.Xunit2.TestKit
    {
        [Fact]
        public void TestCoordinatorEnabledMessageSink_should_receive_TestRunTree_when_EndTestRun_is_received()
        {
            var consoleMessageSink = Sys.ActorOf(Props.Create(() => new ConsoleMessageSinkActor(true)));
            var nodeIndexes = Enumerable.Range(1, 4).ToArray();

            var beginSpec = new BeginNewSpec(NodeMessageHelpers.BuildNodeTests(nodeIndexes));
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

    public static class AskExtensions
    {
        public static TAnswer AskAndWait<TAnswer>(this ICanTell self, object message, TimeSpan timeout)
        {
            var task = self.Ask<TAnswer>(message, timeout);
            task.Wait();
            return task.Result;
        }
    }
}

