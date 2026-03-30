// -----------------------------------------------------------------------
// <copyright file="MultiNodeTestRunner.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNode.TestAdapter.Configuration;
using Akka.MultiNode.TestAdapter.Internal.Sinks;
using Akka.MultiNode.TestAdapter.NodeRunner;
using Xunit.Sdk;
using TestFailed = Xunit.Sdk.TestFailed;
using TestFinished = Xunit.Sdk.TestFinished;
using TestPassed = Xunit.Sdk.TestPassed;
using TestResultMessage = Xunit.Sdk.TestResultMessage;
using TestSkipped = Xunit.Sdk.TestSkipped;
using TestStarting = Xunit.Sdk.TestStarting;

namespace Akka.MultiNode.TestAdapter.Internal
{
    internal class MultiNodeTestRunner
    {
        public MultiNodeTestRunner(
            NodeTest test,
            IMessageBus messageBus,
            string[] remoteArguments,
            string skipReason,
            ExceptionAggregator aggregator,
            IActorRef sinkCoordinator,
            IActorRef timelineCollector,
            MultiNodeTestRunnerOptions options,
            CancellationTokenSource cancellationTokenSource) 
        {
            _test = test;
            _messageBus = messageBus;
            _remoteArguments = remoteArguments;
            _aggregator = aggregator;
            _sinkCoordinator = sinkCoordinator;
            _timelineCollector = timelineCollector;
            _options = options;
            _cancellationTokenSource = cancellationTokenSource;
            _skipReason = skipReason;
        }

        private readonly MultiNodeTestRunnerOptions _options;
        private readonly IActorRef _sinkCoordinator;
        private readonly IActorRef _timelineCollector;

        private readonly string _skipReason;
        
        /// <summary>
        /// Gets or sets the exception aggregator used to run code and collect exceptions.
        /// </summary>
        private readonly ExceptionAggregator _aggregator;

        /// <summary>
        /// Gets or sets the task cancellation token source, used to cancel the test run.
        /// </summary>
        private readonly CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// Gets or sets the constructor arguments used to construct the test class.
        /// </summary>
        private readonly string[] _remoteArguments;

        /// <summary>
        /// Gets or sets the display name of the invoked test.
        /// </summary>
        private string DisplayName => _test.DisplayName;

        /// <summary>
        /// Gets or sets the message bus to report run status to.
        /// </summary>
        private readonly IMessageBus _messageBus;

        /// <summary>
        /// Gets or sets the test to be run.
        /// </summary>
        private readonly NodeTest _test;

        /// <summary>
        /// Gets the test case to be run.
        /// </summary>
        private MultiNodeTestCase TestCase => _test.TestCase;

        private readonly StringBuilder _outputBuilder = new StringBuilder();
        private string Output => _outputBuilder.ToString();

        private readonly List<string> _exceptionType = new List<string>();
        private readonly List<string> _exceptionMessage = new List<string>();
        private readonly List<string> _exceptionStacktrace = new List<string>();
        
        /// <summary>
        /// Runs the test.
        /// </summary>
        /// <returns>Returns summary information about the test that was run.</returns>
        public async Task<RunSummary> RunAsync()
        {
            var summary = new RunSummary { Total = 1 };

            _messageBus.QueueMessage(new TestStarting(_test));
            var aggregator = new ExceptionAggregator(_aggregator);
            var returnCode = -1;

            if (!aggregator.HasExceptions)
            {
                await aggregator.RunAsync(async () =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        returnCode = await RunNode();
                    }
                    finally
                    {
                        stopwatch.Stop();
                        summary.Time = (decimal)stopwatch.Elapsed.TotalSeconds;
                    }
                });
            }

            TestResultMessage testResult;
            var exception = aggregator.ToException();
            if (exception == null)
            {
                switch (returnCode)
                {
                    case 0:
                        testResult = new TestPassed(_test, summary.Time, Output);
                        break;
                    default:
                        summary.Failed++;
                        
                        testResult = new TestFailed(
                            test: _test,
                            executionTime: summary.Time,
                            output: Output, 
                            exceptionTypes: _exceptionType.ToArray(),
                            messages: _exceptionMessage.ToArray(),
                            stackTraces: _exceptionStacktrace.ToArray(),
                            exceptionParentIndices: Enumerable.Range(0, _exceptionType.Count).ToArray());
                        break;
                }
            }
            else
            {
                testResult = new TestFailed(_test, summary.Time, Output, exception);
                summary.Failed++;
            }

            _messageBus.QueueMessage(testResult);
            var specFolder = Directory.CreateDirectory(Path.Combine(_options.OutputDirectory, TestCase.DisplayName));
            var logFilePath = Path.GetFullPath(Path.Combine(specFolder.FullName, $"node{_test.Node}__{_test.Role}__{_options.Platform}.txt"));
            bool dumpSuccess;
            do
            {
                try
                {
                    if(!_options.AppendLogOutput && File.Exists(logFilePath))
                        File.Delete(logFilePath);
                    
                    File.AppendAllText(logFilePath, Output);
                    dumpSuccess = true;
                }
                catch
                {
                    dumpSuccess = false;
                }
            } while (!dumpSuccess);

            _messageBus.QueueMessage(new TestFinished(_test, summary.Time, Output));
            
            return summary;
        }

        private void ExtractExceptionData(string data)
        {
            if (data.Contains("[FAIL-EXCEPTION]"))
            {
                var index = data.IndexOf("[FAIL-EXCEPTION] Type: ", StringComparison.OrdinalIgnoreCase);
                if(index != -1)
                {
                    _exceptionType.Add(data.Substring(index + 23)); 
                    return;
                } 
                
                index = data.IndexOf("[FAIL-EXCEPTION] Message: ", StringComparison.OrdinalIgnoreCase);
                if(index != -1)
                {
                    _exceptionMessage.Add(data.Substring(index + 26));
                    return;
                } 
                        
                index = data.IndexOf("[FAIL-EXCEPTION] StackTrace: ", StringComparison.OrdinalIgnoreCase); 
                if(index != -1)
                {
                    _exceptionStacktrace.Add(data.Substring(index + 29));
                } 
            }
        }

        private async Task<int> RunNode()
        {
            var nodeInfo = new TimelineLogCollectorActor.NodeInfo(_test.Node, _test.Role, _options.Platform, TestCase.DisplayName);
            
            void OutputHandler(object sender, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs?.Data != null)
                {
                    var data = eventArgs.Data;
                    _outputBuilder.AppendLine(data);
                    _messageBus.QueueMessage(new TestOutput(_test, data + Environment.NewLine));
                    _timelineCollector.Tell(new TimelineLogCollectorActor.LogMessage(nodeInfo, data));

                    ExtractExceptionData(data);
                }
            }

            var exitCode = -1;
            var (process, task) = RemoteHost.RemoteHost.RunProcessAsync(new Executor().Execute, _remoteArguments, opt =>
            {
                opt.OnExit = p =>
                {
                    exitCode = p.ExitCode;
                    if (p.ExitCode == 0)
                    {
                        _sinkCoordinator.Tell(new NodeCompletedSpecWithSuccess(_test.Node, _test.Role, _test.DisplayName + " passed."));
                    }
                    else
                    {
                        _sinkCoordinator.Tell(new NodeCompletedSpecWithFail(_test.Node, _test.Role, _test.DisplayName + " passed."));
                    }
                };
                opt.OutputDataReceived = OutputHandler;
                opt.ErrorDataReceived = OutputHandler;
            }, _cancellationTokenSource.Token);
            
            _sinkCoordinator.Tell(new SinkCoordinator.RunnerMessage($"Started node {_test.Node} : {_test.Role} on pid {process.Id}"));
            
            await task;
            return exitCode;
        }
    }
}