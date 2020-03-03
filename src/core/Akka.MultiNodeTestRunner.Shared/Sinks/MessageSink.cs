//-----------------------------------------------------------------------
// <copyright file="MessageSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.MultiNodeTestRunner.Shared.Reporting;

namespace Akka.MultiNodeTestRunner.Shared.Sinks
{
    /// <summary>
    /// Abstract base class for all <see cref="IMessageSink"/> implementations. Includes some methods
    /// for parsing log messages into structured formats.
    /// </summary>
    public abstract class MessageSink : IMessageSink
    {
        /// <summary>
        /// ActorRef for the actor who coordinates all of reporting for each test run
        /// </summary>
        protected IActorRef MessageSinkActorRef;

        protected readonly Props MessageSinkActorProps;

        protected MessageSink(Props messageSinkActorProps)
        {
            MessageSinkActorProps = messageSinkActorProps;
        }

        #region Flow Control

        public void Open(ActorSystem context)
        {
            //Do nothing
            if(IsClosed || IsOpen) return;

            IsOpen = true;

            //Start the TestCoordinatorActor
            MessageSinkActorRef = context.ActorOf(MessageSinkActorProps);
        }

        public bool IsOpen { get; private set; }
        public bool IsClosed { get; private set; }

        internal void RequestExitCode(IActorRef sender)
        {
            MessageSinkActorRef.Tell(new SinkCoordinator.RequestExitCode(), sender);
        }

        public async Task<bool> Close(ActorSystem context)
        {
            //Test run has already been closed or hasn't started
            if (!IsOpen || IsClosed) return await Task.FromResult(false);

            IsOpen = false;
            IsClosed = true;

            //Signal that the test run has ended
            return await MessageSinkActorRef.Ask<MessageSinkActor.SinkCanBeTerminated>(new EndTestRun())
                .ContinueWith(tr => MessageSinkActorRef.GracefulStop(TimeSpan.FromSeconds(2)), 
                TaskContinuationOptions.ExecuteSynchronously).Unwrap();
        }

        #endregion

        #region Static methods and constants

        /// <summary>
        /// Constant used on calls where no message is proceeded by the caller.
        /// </summary>
        public const string NoMessage = "[no message given.]";

        public enum MultiNodeTestRunnerMessageType
        {
            RunnerLogMessage,
            NodeLogFragment, //for messages that had line breaks (such as stack traces)
            NodeLogMessage,
            NodePassMessage,
            NodeFailMessage,
            NodeFailureException,
            Unknown
        };

        private const string NodePassStatusRegexString =
            @"\[(\w){4}(?<node>[0-9]{1,2})(?<role>:\w+)?\]\[(?<status>(PASS|FAIL))\]{1}\s(?<test>.*)";
        protected static readonly Regex NodePassStatusRegex = new Regex(NodePassStatusRegexString);

        private const string NodePassed = "PASS";

        private const string NodeFailed = "FAIL";

        private const string NodeFailureReasonRegexString =
            @"\[(\w){4}(?<node>[0-9]{1,2})(?<role>:\w+)?\]\[(?<status>(FAIL-EXCEPTION))\]{1}\s(?<message>.*)";
        protected static readonly Regex NodeFailureReasonRegex = new Regex(NodeFailureReasonRegexString);

        /*
         * Regular expressions - go big or go home. [Aaronontheweb]
         */
        private const string RunnerLogMessageRegexString = @"\[(?<level>(\w)*)\]\[(?<time>\d{1,4}[- /.]\d{1,4}[- /.]\d{1,4}\s\d{1,2}:\d{1,2}:\d{1,2}(\s(AM|PM)){0,1})\](?<thread>\[(\w|\s)*\])\[(?<logsource>(\[|\w|:|/|\(|\)|\]|\.|-|\$|%|\+|#|\^|@)*)\]\s(?<message>(\w|\s|:|<|\.|\+|>|,|\[|/|-|]|%|\$|\+|\^|@)*)";
        protected static readonly Regex RunnerLogMessageRegex = new Regex(RunnerLogMessageRegexString);

        private const string NodeLogFragmentRegexString = @"\[(\w){4}(?<node>[0-9]{1,2})(?<role>:\w+)?\](?<message>(.)*)";
        protected static readonly Regex NodeLogFragmentRegex = new Regex(NodeLogFragmentRegexString);

        public static MultiNodeTestRunnerMessageType DetermineMessageType(string messageStr)
        {
            var matchRunnerLog = RunnerLogMessageRegex.Match(messageStr);
            if (matchRunnerLog.Success) return MultiNodeTestRunnerMessageType.RunnerLogMessage;

            var matchStatus = NodePassStatusRegex.Match(messageStr);
            if (matchStatus.Success)
            {
                return matchStatus.Groups["status"].Value.Equals(NodePassed) ? MultiNodeTestRunnerMessageType.NodePassMessage : MultiNodeTestRunnerMessageType.NodeFailMessage;
            }

            var matchFailureReason = NodeFailureReasonRegex.Match(messageStr);
            if(matchFailureReason.Success) return MultiNodeTestRunnerMessageType.NodeFailureException;

            var nodeLogFragmentStatus = NodeLogFragmentRegex.Match(messageStr);
            if(nodeLogFragmentStatus.Success) return MultiNodeTestRunnerMessageType.NodeLogFragment;

            return MultiNodeTestRunnerMessageType.Unknown;
        }

        public static bool TryParseLogMessage(string messageStr, out LogMessageFragmentForNode logMessage)
        {
            var matchLog = NodeLogFragmentRegex.Match(messageStr);
            if (!matchLog.Success)
            {
                logMessage = null;
                return false;
            }

            var message = matchLog.Groups["message"].Value;
            var nodeIndex = Int32.Parse(matchLog.Groups["node"].Value);
            var nodeRoleGroup = matchLog.Groups["role"];
            var nodeRole = nodeRoleGroup.Success ? nodeRoleGroup.Value.Substring(1) : String.Empty;
            logMessage = new LogMessageFragmentForNode(nodeIndex, nodeRole, message, DateTime.UtcNow);

            return true;
        }

        public static bool TryParseLogMessage(string messageStr, out LogMessageForTestRunner logMessage)
        {
            var matchLog = RunnerLogMessageRegex.Match(messageStr);
            if (!matchLog.Success)
            {
                logMessage = null;
                return false;
            }

            LogLevel logLevel;
            Enum.TryParse(matchLog.Groups["level"].Value, true, out logLevel);

            var logSource = matchLog.Groups["logsource"].Value;
            var message = matchLog.Groups["message"].Value;
            logMessage = new LogMessageForTestRunner(message, logLevel, DateTime.UtcNow, logSource);

            return true;
        }

        public static bool TryParseSuccessMessage(string messageStr, out NodeCompletedSpecWithSuccess message)
        {
            var matchStatus = NodePassStatusRegex.Match(messageStr);
            message = null;
            if (!matchStatus.Success) return false;
            if (!matchStatus.Groups["status"].Value.Equals(NodePassed)) return false;

            var nodeIndex = Int32.Parse(matchStatus.Groups["node"].Value);
            var passMessage = matchStatus.Groups["test"].Value + " " + matchStatus.Groups["status"].Value;
            var nodeRoleGroup = matchStatus.Groups["role"];
            var nodeRole = nodeRoleGroup.Success ? nodeRoleGroup.Value.Substring(1) : String.Empty;
            message = new NodeCompletedSpecWithSuccess(nodeIndex, nodeRole, passMessage);

            return true;
        }

        public static bool TryParseFailureMessage(string messageStr, out NodeCompletedSpecWithFail message)
        {
            var matchStatus = NodePassStatusRegex.Match(messageStr);
            message = null;
            if (!matchStatus.Success) return false;
            if (!matchStatus.Groups["status"].Value.Equals(NodeFailed)) return false;

            var nodeIndex = Int32.Parse(matchStatus.Groups["node"].Value);
            var passMessage = matchStatus.Groups["test"].Value + " " + matchStatus.Groups["status"].Value;
            var nodeRoleGroup = matchStatus.Groups["role"];
            var nodeRole = nodeRoleGroup.Success ? nodeRoleGroup.Value.Substring(1) : String.Empty;
            message = new NodeCompletedSpecWithFail(nodeIndex, nodeRole, passMessage);

            return true;
        }

        public static bool TryParseFailureExceptionMessage(string messageStr, out NodeCompletedSpecWithFail message)
        {
            var matchStatus = NodeFailureReasonRegex.Match(messageStr);
            message = null;
            if (!matchStatus.Success) return false;

            var nodeIndex = Int32.Parse(matchStatus.Groups["node"].Value);
            var failureMessage = matchStatus.Groups["message"].Value;
            var nodeRoleGroup = matchStatus.Groups["role"];
            var nodeRole = nodeRoleGroup.Success ? nodeRoleGroup.Value.Substring(1) : String.Empty;
            message = new NodeCompletedSpecWithFail(nodeIndex, nodeRole, failureMessage);

            return true;
        }

        #endregion

        #region Message Handling

        public void BeginTest(string className, string methodName, IList<NodeTest> nodes)
        {
            //begin the next spec
            MessageSinkActorRef.Tell(new BeginNewSpec(className, methodName, nodes));
        }

        public void EndTest(string className, string methodName, SpecLog log)
        {
            //end the current spec
            MessageSinkActorRef.Tell(new EndSpec(className, methodName, log));
        }
        
        public void Success(int nodeIndex, string nodeRole, string message)
        {
            MessageSinkActorRef.Tell(new NodeCompletedSpecWithSuccess(nodeIndex, nodeRole, message ?? NoMessage));
        }

        public void LogRunnerMessage(string message, string logSource, LogLevel level)
        {
            MessageSinkActorRef.Tell(new LogMessageForTestRunner(message, level, DateTime.UtcNow, logSource));
        }

        public void Offer(string messageStr)
        {
            var messageType = DetermineMessageType(messageStr);
            if (messageType == MultiNodeTestRunnerMessageType.Unknown)
            {
                HandleUnknownMessageType(messageStr);
                return;
            }

            if (messageType == MultiNodeTestRunnerMessageType.RunnerLogMessage)
            {
                LogMessageForTestRunner runnerLog;
                if (!TryParseLogMessage(messageStr, out runnerLog)) throw new InvalidOperationException("could not parse test runner log message: " + messageStr);
                MessageSinkActorRef.Tell(runnerLog);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.NodePassMessage)
            {
                NodeCompletedSpecWithSuccess nodePass;
                if (!TryParseSuccessMessage(messageStr, out nodePass)) throw new InvalidOperationException("could not parse node spec pass message: " + messageStr);
                MessageSinkActorRef.Tell(nodePass);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.NodeFailMessage)
            {
                NodeCompletedSpecWithFail nodeFail;
                if (!TryParseFailureMessage(messageStr, out nodeFail)) throw new InvalidOperationException("could not parse node spec fail message: " + messageStr);
                MessageSinkActorRef.Tell(nodeFail);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.NodeFailureException)
            {
                NodeCompletedSpecWithFail nodeFail;
                if (!TryParseFailureExceptionMessage(messageStr, out nodeFail)) throw new InvalidOperationException("could not parse node spec failure + EXCEPTION message: " + messageStr);
                MessageSinkActorRef.Tell(nodeFail);
            }
        }

        protected abstract void HandleUnknownMessageType(string message);

        #endregion
    }
}

