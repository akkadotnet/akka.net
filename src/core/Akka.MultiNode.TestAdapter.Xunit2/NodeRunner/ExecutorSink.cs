//-----------------------------------------------------------------------
// <copyright file="Sink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Internal.Sinks;
using Xunit;
using Xunit.Abstractions;
using IMessageSink = Xunit.Abstractions.IMessageSink;
using LongLivedMarshalByRefObject = Xunit.Sdk.LongLivedMarshalByRefObject;

namespace Akka.MultiNode.TestAdapter.NodeRunner
{
    internal class ExecutorSink : LongLivedMarshalByRefObject, IMessageSink, IDisposable
    {
        public bool Passed { get; private set; }
        public ManualResetEvent Finished { get; private set; }
        private readonly int _nodeIndex;
        private readonly string _nodeRole;

        private readonly IActorRef _logger;

        public ExecutorSink(int nodeIndex, string nodeRole, IActorRef logger) 
        {
            _nodeIndex = nodeIndex;
            _nodeRole = nodeRole;
            Finished = new ManualResetEvent(false);
            _logger = logger;
        }

        public bool OnMessage(IMessageSinkMessage message)
        {
            if (message is ITestResultMessage resultMessage)
            {
                _logger.Tell(resultMessage.Output);
                Console.WriteLine(resultMessage.Output);
            }

            if (message is ITestPassed testPassed)
            {
                //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                var specPass = new SpecPass(_nodeIndex + 1, _nodeRole, testPassed.TestCase.DisplayName);
                _logger.Tell(specPass.ToString());
                Console.WriteLine(specPass.ToString()); //so the message also shows up in the individual per-node build log
                Passed = true;
                return true;
            }

            if (message is ITestFailed testFailed)
            {
                //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                var specFail = new SpecFail(_nodeIndex + 1, _nodeRole, testFailed.TestCase.DisplayName);
                foreach (var failedMessage in testFailed.Messages) specFail.FailureMessages.Add(failedMessage);
                foreach (var stackTrace in testFailed.StackTraces) specFail.FailureStackTraces.Add(stackTrace);
                foreach(var exceptionType in testFailed.ExceptionTypes) specFail.FailureExceptionTypes.Add(exceptionType);
                _logger.Tell(specFail.ToString());
                Console.WriteLine(specFail.ToString());
                return true;
            }

            if (message is ErrorMessage errorMessage)
            {
                var specFail = new SpecFail(_nodeIndex + 1, _nodeRole, "ERRORED");
                foreach (var failedMessage in errorMessage.Messages) specFail.FailureMessages.Add(failedMessage);
                foreach (var stackTrace in errorMessage.StackTraces) specFail.FailureStackTraces.Add(stackTrace);
                foreach (var exceptionType in errorMessage.ExceptionTypes) specFail.FailureExceptionTypes.Add(exceptionType);
                _logger.Tell(specFail.ToString());
                Console.WriteLine(specFail.ToString());
            }
            
            if (message is ITestAssemblyFinished)
            {
                Finished.Set();
            }

            return true;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}

