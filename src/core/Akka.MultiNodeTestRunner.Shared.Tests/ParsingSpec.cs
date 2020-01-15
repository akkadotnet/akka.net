//-----------------------------------------------------------------------
// <copyright file="ParsingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.TestKit;
using Xunit;

namespace Akka.MultiNodeTestRunner.Shared.Tests
{
    /// <summary>
    /// Used to test the <see cref="MessageSink"/>'s ability to parse 
    /// </summary>
    public class ParsingSpec : AkkaSpec
    {
        public ParsingSpec()
            : base(ConfigurationFactory.ParseString(@"
        akka {
                loglevel = DEBUG
                stdout-loglevel = DEBUG
            }
            "))
        {

        }

        #region Actor definitions

        public class LoggingActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Context.GetLogger().Debug("Received message {0}", message);
            }
        }

        #endregion

        [Fact]
        public void MessageSink_should_parse_Node_log_message_fragment_correctly()
        {
           //format the a log fragment as would be recorded by the test runner
            var message = "this is some message";
            var foundMessageStr = "[NODE1]" + message;
            LogMessageFragmentForNode nodeMessage;
            MessageSink.TryParseLogMessage(foundMessageStr, out nodeMessage).ShouldBeTrue("should have been able to parse log message");

            Assert.Equal(1, nodeMessage.NodeIndex);
            Assert.Equal(message, nodeMessage.Message);
        }

        [Fact]
        public void MessageSink_should_parse_Runner_log_message_correctly()
        {
            var loggingActor = Sys.ActorOf<LoggingActor>();
            Sys.EventStream.Subscribe(TestActor, typeof(Debug));
            loggingActor.Tell("LOG ME... but like the test runner this time!");

            //capture the logged message
            var foundMessage = ExpectMsg<Debug>();

            //format the string as it would appear when reported by multinode test runner
            var foundMessageStr = foundMessage.ToString();
            LogMessageForTestRunner runnerMessage;
            MessageSink.TryParseLogMessage(foundMessageStr, out runnerMessage).ShouldBeTrue("should have been able to parse log message");

            Assert.Equal(foundMessage.LogLevel(), runnerMessage.Level);
            Assert.Equal(foundMessage.LogSource, runnerMessage.LogSource);
        }

        [Fact]
        public void MessageSink_should_parse_Node_SpecPass_message_correctly()
        {
            var specPass = new SpecPass(1, "super_role_1", GetType().GetTypeInfo().Assembly.GetName().Name);
            NodeCompletedSpecWithSuccess nodeCompletedSpecWithSuccess;
            MessageSink.TryParseSuccessMessage(specPass.ToString(), out nodeCompletedSpecWithSuccess)
                .ShouldBeTrue("should have been able to parse node success message");

            Assert.Equal(specPass.NodeIndex, nodeCompletedSpecWithSuccess.NodeIndex);
            Assert.Equal(specPass.NodeRole, nodeCompletedSpecWithSuccess.NodeRole);
        }

        [Fact]
        public void MessageSink_should_parse_Node_SpecFail_message_correctly()
        {
            var specFail = new SpecFail(1, "super_role_1", GetType().GetTypeInfo().Assembly.GetName().Name);
            NodeCompletedSpecWithFail nodeCompletedSpecWithFail;
            MessageSink.TryParseFailureMessage(specFail.ToString(), out nodeCompletedSpecWithFail)
                .ShouldBeTrue("should have been able to parse node failure message");

            Assert.Equal(specFail.NodeIndex, nodeCompletedSpecWithFail.NodeIndex);
            Assert.Equal(specFail.NodeRole, nodeCompletedSpecWithFail.NodeRole);
        }

        [Fact]
        public void MessageSink_should_be_able_to_infer_message_type()
        {
            var specPass = new SpecPass(1, "super_role_1", GetType().GetTypeInfo().Assembly.GetName().Name);
            var specFail = new SpecFail(1, "super_role_1", GetType().GetTypeInfo().Assembly.GetName().Name);

            var loggingActor = Sys.ActorOf<LoggingActor>();
            Sys.EventStream.Subscribe(TestActor, typeof(Debug));
            loggingActor.Tell("LOG ME!");

            //capture the logged message
            var foundMessage = ExpectMsg<Debug>();

            //format the string as it would appear when reported by multinode test runner
            var nodeMessageFragment = "[NODE1:super_role_1]      Only part of a message!";
            var runnerMessageStr = foundMessage.ToString();
            
            MessageSink.DetermineMessageType(runnerMessageStr).ShouldBe(MessageSink.MultiNodeTestRunnerMessageType.RunnerLogMessage);
            MessageSink.DetermineMessageType(specPass.ToString()).ShouldBe(MessageSink.MultiNodeTestRunnerMessageType.NodePassMessage);
            MessageSink.DetermineMessageType(specFail.ToString()).ShouldBe(MessageSink.MultiNodeTestRunnerMessageType.NodeFailMessage);
            MessageSink.DetermineMessageType("[Node2][FAIL-EXCEPTION] Type: Xunit.Sdk.TrueException").ShouldBe(MessageSink.MultiNodeTestRunnerMessageType.NodeFailureException);
            MessageSink.DetermineMessageType(nodeMessageFragment).ShouldBe(MessageSink.MultiNodeTestRunnerMessageType.NodeLogFragment);
            MessageSink.DetermineMessageType("foo!").ShouldBe(MessageSink.MultiNodeTestRunnerMessageType.Unknown);
        }
    }
}

