using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    public class MessageFormattingExploratory : AkkaSpec
    {
        private readonly ITestOutputHelper _output;

        public MessageFormattingExploratory(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ConsoleMessageSink_exploratory_tests()
        {
            var consoleMessageSink = Sys.ActorOf(Props.Create(() => new TestOutputHelperMessageSinkActor(true, true, _output)));
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

            consoleMessageSink.Tell(new NodeCompletedSpecWithSuccess(1, "ROLE X", "SPEC PASSED"));

            //end the spec
            consoleMessageSink.Tell(new EndSpec());

            //end the test run...
            var sinkReadyToTerminate =
                consoleMessageSink.AskAndWait<MessageSinkActor.SinkCanBeTerminated>(new EndTestRun(),
                    TimeSpan.FromSeconds(10));
            Assert.NotNull(sinkReadyToTerminate);
        }

        private class TestOutputHelperMessageSinkActor : ConsoleMessageSinkActor
        {
            private readonly ITestOutputHelper _output;
            private readonly bool _teamCity;
            public TestOutputHelperMessageSinkActor(bool useTestCoordinator, bool teamCity, ITestOutputHelper output) : base(useTestCoordinator, teamCity)
            {
                _teamCity = teamCity;
                _output = output;
            }

            protected override void WriteSpecMessage(string message)
            {
                string specMessage = $"[RUNNER][{DateTime.UtcNow.ToShortTimeString()}]: {message}";
                _output.WriteLine(specMessage);
            }

            protected override void WriteSpecPass(int nodeIndex, string nodeRole, string message)
            {
                if (_teamCity)
                {
                     _output.WriteLine($"##teamcity[testStdOut name=\'{TeamCityEscape($"[NODE{nodeIndex}:{nodeRole}]")}\' out=\'{TeamCityEscape(message)}\'");
                }
                else
                {
                    _output.WriteLine("[NODE{0}:{1}][{2}]: SPEC PASSED: {3}", nodeIndex, nodeRole, DateTime.UtcNow.ToShortTimeString(), message);
                }
            }

            private static string TeamCityEscape(string input)
            {
                if (string.IsNullOrEmpty(input))
                    return string.Empty;

                return input.Replace("|", "||")
                    .Replace("'", "|'")
                    .Replace("\n", "|n")
                    .Replace("\r", "|r")
                    .Replace(char.ConvertFromUtf32(int.Parse("0086", NumberStyles.HexNumber)), "|x")
                    .Replace(char.ConvertFromUtf32(int.Parse("2028", NumberStyles.HexNumber)), "|l")
                    .Replace(char.ConvertFromUtf32(int.Parse("2029", NumberStyles.HexNumber)), "|p")
                    .Replace("[", "|[")
                    .Replace("]", "|]");
            }
        }
    }
}
