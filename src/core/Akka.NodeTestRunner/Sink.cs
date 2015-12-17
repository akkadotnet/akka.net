//-----------------------------------------------------------------------
// <copyright file="Sink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Akka.MultiNodeTestRunner.Shared.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Akka.NodeTestRunner
{
    class Sink : MarshalByRefObject, IMessageSink, IDisposable
    {
        public bool Passed { get; private set; }
        public ManualResetEvent Finished { get; private set; }
        readonly int _nodeIndex;
        private readonly ITestRunnerLogger _logger;

        public Sink(int nodeIndex, ITestRunnerLogger logger)
        {
            _nodeIndex = nodeIndex;
            _logger = logger;
            Finished = new ManualResetEvent(false);
        }

        public bool OnMessage(IMessageSinkMessage message)
        {
            var resultMessage = message as ITestResultMessage;
            if (resultMessage != null)
            {
                _logger.WriteLine(resultMessage.Output);
            }
            var testPassed = message as ITestPassed;
            if (testPassed != null)
            {
                //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                var specPass = new SpecPass(_nodeIndex + 1, testPassed.TestCase.DisplayName);
                _logger.Write(specPass);
                Passed = true;
                return true;
            }
            var testFailed = message as ITestFailed;
            if (testFailed != null)
            {
                //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                var failureMessages = new List<string>();
                var failureStackTraces = new List<string>();
                var failureExceptionTypes = new List<string>();
                foreach (var failedMessage in testFailed.Messages) failureMessages.Add(failedMessage);
                foreach (var stackTrace in testFailed.StackTraces) failureStackTraces.Add(stackTrace);
                foreach (var exceptionType in testFailed.ExceptionTypes) failureExceptionTypes.Add(exceptionType);
                var specFail = new SpecFail(_nodeIndex + 1, testFailed.TestCase.DisplayName, failureMessages, failureStackTraces, failureExceptionTypes);

                _logger.Write(specFail);
                return true;
            }
            var errorMessage = message as ErrorMessage;
            if (errorMessage != null)
            {
                var failureMessages = new List<string>();
                var failureStackTraces = new List<string>();
                var failureExceptionTypes = new List<string>();
                foreach (var failedMessage in errorMessage.Messages) failureMessages.Add(failedMessage);
                foreach (var stackTrace in errorMessage.StackTraces) failureStackTraces.Add(stackTrace);
                foreach (var exceptionType in errorMessage.ExceptionTypes) failureExceptionTypes.Add(exceptionType);
                var specFail = new SpecFail(_nodeIndex + 1, "ERRORED", failureMessages, failureStackTraces, failureExceptionTypes);
                _logger.Write(specFail);
            }
            if (message is ITestAssemblyFinished)
            {
                Finished.Set();
            }

            return true;
        }

        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}

