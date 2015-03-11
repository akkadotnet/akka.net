using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util;

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
        protected ActorRef MessageSinkActorRef;

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

        internal void RequestExitCode(ActorRef sender)
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
                TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously).Unwrap();
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
            @"\[(\w){4}(?<node>[0-9]{1,2})\]\[(?<status>(PASS|FAIL))\]{1}\s(?<test>.*)";
        protected static readonly Regex NodePassStatusRegex = new Regex(NodePassStatusRegexString);

        private const string NodePassed = "PASS";

        private const string NodeFailed = "FAIL";

        private const string NodeFailureReasonRegexString =
            @"\[(\w){4}(?<node>[0-9]{1,2})\]\[(?<status>(FAIL-EXCEPTION))\]{1}\s(?<message>.*)";
        protected static readonly Regex NodeFailureReasonRegex = new Regex(NodeFailureReasonRegexString);

        /*
         * Regular expressions - go big or go home. [Aaronontheweb]
         */
        private const string NodeLogMessageRegexString = @"\[(\w){4}(?<node>[0-9]{1,2})\]\[(?<level>(\w)*)\]\[(?<time>\d{1,4}[- /.]\d{1,4}[- /.]\d{1,4}\s\d{1,2}:\d{1,2}:\d{1,2}(\s(AM|PM)){0,1})\](?<thread>\[(\w|\s)*\])\[(?<logsource>(\[|\w|:|/|\(|\)|\]|\.|-|\$|%|\+|\^|@)*)\]\s(?<message>(\w|\s|:|<|\.|\+|>|,|\[|/|-|]|%|\$|\+|\^|@|\(|\))*)";
        protected static readonly Regex NodeLogMessageRegex = new Regex(NodeLogMessageRegexString);

        private const string RunnerLogMessageRegexString = @"\[(?<level>(\w)*)\]\[(?<time>\d{1,4}[- /.]\d{1,4}[- /.]\d{1,4}\s\d{1,2}:\d{1,2}:\d{1,2}(\s(AM|PM)){0,1})\](?<thread>\[(\w|\s)*\])\[(?<logsource>(\[|\w|:|/|\(|\)|\]|\.|-|\$|%|\+|\^|@)*)\]\s(?<message>(\w|\s|:|<|\.|\+|>|,|\[|/|-|]|%|\$|\+|\^|@)*)";
        protected static readonly Regex RunnerLogMessageRegex = new Regex(RunnerLogMessageRegexString);

        private const string NodeLogFragmentRegexString = @"\[(\w){4}(?<node>[0-9]{1,2})\](?<message>(.)*)";
        protected static readonly Regex NodeLogFragmentRegex = new Regex(NodeLogFragmentRegexString);

        public static MultiNodeTestRunnerMessageType DetermineMessageType(string messageStr)
        {
            var matchLog = NodeLogMessageRegex.Match(messageStr);
            if(matchLog.Success) return MultiNodeTestRunnerMessageType.NodeLogMessage;

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

        public static bool TryParseLogMessage(string messageStr, out LogMessageForNode logMessage)
        {
            var matchLog = NodeLogMessageRegex.Match(messageStr);
            if (!matchLog.Success)
            {
                logMessage = null;
                return false;
            }
            
            LogLevel logLevel;
            Enum.TryParse(matchLog.Groups["level"].Value, true, out logLevel);

            var logSource = matchLog.Groups["logsource"].Value;
            var message = matchLog.Groups["message"].Value;
            var nodeIndex = Int32.Parse(matchLog.Groups["node"].Value);
            logMessage = new LogMessageForNode(nodeIndex, message, logLevel, DateTime.UtcNow, logSource);

            return true;
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
            logMessage = new LogMessageFragmentForNode(nodeIndex, message, DateTime.UtcNow);

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
            message = new NodeCompletedSpecWithSuccess(nodeIndex, passMessage);

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
            message = new NodeCompletedSpecWithFail(nodeIndex, passMessage);

            return true;
        }

        public static bool TryParseFailureExceptionMessage(string messageStr, out NodeCompletedSpecWithFail message)
        {
            var matchStatus = NodeFailureReasonRegex.Match(messageStr);
            message = null;
            if (!matchStatus.Success) return false;

            var nodeIndex = Int32.Parse(matchStatus.Groups["node"].Value);
            var failureMessage = matchStatus.Groups["message"].Value;
            message = new NodeCompletedSpecWithFail(nodeIndex, failureMessage);

            return true;
        }

        #endregion

        #region Message Handling

        public void BeginTest(string className, string methodName, IList<NodeTest> nodes)
        {
            //begin the next spec
            MessageSinkActorRef.Tell(new BeginNewSpec(className, methodName, nodes));
        }

        public void EndTest()
        {
            //end the current spec
            MessageSinkActorRef.Tell(new EndSpec());
        }

        public void Success(int nodeIndex)
        {
            Success(nodeIndex, NoMessage);
        }

        public void Success(int nodeIndex, string message)
        {
            MessageSinkActorRef.Tell(new NodeCompletedSpecWithSuccess(nodeIndex, message ?? NoMessage));
        }

        public void Fail(int nodeIndex)
        {
            Fail(nodeIndex, NoMessage);
        }

        public void Fail(int nodeIndex, string message)
        {
            MessageSinkActorRef.Tell(new NodeCompletedSpecWithFail(nodeIndex, message ?? NoMessage));
        }

        public void Log(int nodeIndex, string message, string logSource, LogLevel level)
        {
            MessageSinkActorRef.Tell(new LogMessageForNode(nodeIndex, message, level, DateTime.UtcNow, logSource));
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

            if (messageType == MultiNodeTestRunnerMessageType.NodeLogMessage)
            {
                LogMessageForNode log;
                Guard.Assert(TryParseLogMessage(messageStr, out log), "could not parse log message: " + messageStr);
                MessageSinkActorRef.Tell(log);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.RunnerLogMessage)
            {
                LogMessageForTestRunner runnerLog;
                Guard.Assert(TryParseLogMessage(messageStr, out runnerLog), "could not parse test runner log message: " + messageStr);
                MessageSinkActorRef.Tell(runnerLog);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.NodePassMessage)
            {
                NodeCompletedSpecWithSuccess nodePass;
                Guard.Assert(TryParseSuccessMessage(messageStr, out nodePass), "could not parse node spec pass message: " + messageStr);
                MessageSinkActorRef.Tell(nodePass);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.NodeFailMessage)
            {
                NodeCompletedSpecWithFail nodeFail;
                Guard.Assert(TryParseFailureMessage(messageStr, out nodeFail), "could not parse node spec fail message: " + messageStr);
                MessageSinkActorRef.Tell(nodeFail);
            }
            else if (messageType == MultiNodeTestRunnerMessageType.NodeFailureException)
            {
                NodeCompletedSpecWithFail nodeFail;
                Guard.Assert(TryParseFailureExceptionMessage(messageStr, out nodeFail), "could not parse node spec failure + EXCEPTION message: " + messageStr);
                MessageSinkActorRef.Tell(nodeFail);
            }
        }

        protected abstract void HandleUnknownMessageType(string message);

        #endregion
    }
}