//-----------------------------------------------------------------------
// <copyright file="Sink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Xunit;
using Xunit.Abstractions;
using IMessageSink = Xunit.Abstractions.IMessageSink;

namespace Akka.NodeTestRunner
{
#if CORECLR
    class Sink : IMessageSink, IDisposable
#else
    class Sink : MarshalByRefObject, IMessageSink, IDisposable
#endif
    {
        public bool Passed { get; private set; }
        public ManualResetEvent Finished { get; private set; }
        private readonly int _nodeIndex;
        private readonly string _nodeRole;

        private readonly IActorRef _logger;

        public Sink(int nodeIndex, string nodeRole, IActorRef logger) 
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

            switch (message)
            {
                case ITestPassed testPassed:
                {
                    //the MultiNodeTestRunner uses 1-based indexing, which is why we have to add 1 to the index.
                    var specPass = new SpecPass(_nodeIndex + 1, _nodeRole, testPassed.TestCase.DisplayName);
                    _logger.Tell(specPass.ToString());
                    Console.WriteLine(specPass.ToString()); //so the message also shows up in the individual per-node build log
                    Passed = true;
                    return true;
                }
                case ITestFailed testFailed:
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
                case ErrorMessage errorMessage:
                {
                    var specFail = new SpecFail(_nodeIndex + 1, _nodeRole, "ERRORED");
                    foreach (var failedMessage in errorMessage.Messages) specFail.FailureMessages.Add(failedMessage);
                    foreach (var stackTrace in errorMessage.StackTraces) specFail.FailureStackTraces.Add(stackTrace);
                    foreach (var exceptionType in errorMessage.ExceptionTypes) specFail.FailureExceptionTypes.Add(exceptionType);
                    _logger.Tell(specFail.ToString());
                    Console.WriteLine(specFail.ToString());
                    break;
                }
                case ITestAssemblyFinished _:
                    Finished.Set();
                    break;
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

