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
using Xunit.v3;

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
            _ids = TestMessageIds.From(test.TestCase);
        }

        private readonly MultiNodeTestRunnerOptions _options;
        private readonly IActorRef _sinkCoordinator;
        private readonly IActorRef _timelineCollector;
        private readonly string _skipReason;
        private readonly ExceptionAggregator _aggregator;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly string[] _remoteArguments;
        private readonly IMessageBus _messageBus;
        private readonly NodeTest _test;
        private readonly TestMessageIds _ids;

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

            _messageBus.QueueMessage(new TestStarting
            {
                AssemblyUniqueID = _ids.AssemblyUniqueID,
                TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                TestClassUniqueID = _ids.TestClassUniqueID,
                TestMethodUniqueID = _ids.TestMethodUniqueID,
                TestCaseUniqueID = _ids.TestCaseUniqueID,
                TestUniqueID = _test.UniqueID,
                TestDisplayName = _test.DisplayName,
                Explicit = false,
                StartTime = DateTimeOffset.UtcNow,
                Timeout = 0,
                Traits = new Dictionary<string, IReadOnlyCollection<string>>()
            });

            var aggregator = _aggregator.Clone();
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

            var exception = aggregator.ToException();
            if (exception == null)
            {
                switch (returnCode)
                {
                    case 0:
                        _messageBus.QueueMessage(new TestPassed
                        {
                            AssemblyUniqueID = _ids.AssemblyUniqueID,
                            TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                            TestClassUniqueID = _ids.TestClassUniqueID,
                            TestMethodUniqueID = _ids.TestMethodUniqueID,
                            TestCaseUniqueID = _ids.TestCaseUniqueID,
                            TestUniqueID = _test.UniqueID,
                            ExecutionTime = summary.Time,
                            FinishTime = DateTimeOffset.UtcNow,
                            Output = Output,
                            Warnings = null
                        });
                        break;
                    default:
                        summary.Failed++;

                        _messageBus.QueueMessage(new TestFailed
                        {
                            AssemblyUniqueID = _ids.AssemblyUniqueID,
                            TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                            TestClassUniqueID = _ids.TestClassUniqueID,
                            TestMethodUniqueID = _ids.TestMethodUniqueID,
                            TestCaseUniqueID = _ids.TestCaseUniqueID,
                            TestUniqueID = _test.UniqueID,
                            Cause = FailureCause.Assertion,
                            ExceptionTypes = _exceptionType.Count > 0
                                ? _exceptionType.ToArray()
                                : new[] { "Akka.MultiNode.TestAdapter.Internal.TestFailedException" },
                            Messages = _exceptionMessage.Count > 0
                                ? _exceptionMessage.ToArray()
                                : new[] { $"Node {_test.Node} [{_test.Role}] failed with exit code {returnCode}" },
                            StackTraces = _exceptionStacktrace.Count > 0
                                ? _exceptionStacktrace.ToArray()
                                : new[] { "" },
                            ExceptionParentIndices = Enumerable.Range(0, Math.Max(_exceptionType.Count, 1))
                                .Select(_ => -1).ToArray(),
                            ExecutionTime = summary.Time,
                            FinishTime = DateTimeOffset.UtcNow,
                            Output = Output,
                            Warnings = null
                        });
                        break;
                }
            }
            else
            {
                _messageBus.QueueMessage(new TestFailed
                {
                    AssemblyUniqueID = _ids.AssemblyUniqueID,
                    TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                    TestClassUniqueID = _ids.TestClassUniqueID,
                    TestMethodUniqueID = _ids.TestMethodUniqueID,
                    TestCaseUniqueID = _ids.TestCaseUniqueID,
                    TestUniqueID = _test.UniqueID,
                    Cause = FailureCause.Exception,
                    ExceptionTypes = new[] { exception.GetType().FullName },
                    Messages = new[] { exception.Message },
                    StackTraces = new[] { exception.StackTrace },
                    ExceptionParentIndices = new[] { -1 },
                    ExecutionTime = summary.Time,
                    FinishTime = DateTimeOffset.UtcNow,
                    Output = Output,
                    Warnings = null
                });
                summary.Failed++;
            }

            var specFolder = Directory.CreateDirectory(Path.Combine(_options.OutputDirectory, TestCase.TestCaseDisplayName));
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

            _messageBus.QueueMessage(new TestFinished
            {
                AssemblyUniqueID = _ids.AssemblyUniqueID,
                TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                TestClassUniqueID = _ids.TestClassUniqueID,
                TestMethodUniqueID = _ids.TestMethodUniqueID,
                TestCaseUniqueID = _ids.TestCaseUniqueID,
                TestUniqueID = _test.UniqueID,
                ExecutionTime = summary.Time,
                FinishTime = DateTimeOffset.UtcNow,
                Output = Output,
                Warnings = null,
                Attachments = new Dictionary<string, TestAttachment>()
            });

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
            var nodeInfo = new TimelineLogCollectorActor.NodeInfo(_test.Node, _test.Role, _options.Platform, TestCase.TestCaseDisplayName);

            void OutputHandler(object sender, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs?.Data != null)
                {
                    var data = eventArgs.Data;
                    _outputBuilder.AppendLine(data);
                    _messageBus.QueueMessage(new TestOutput
                    {
                        AssemblyUniqueID = _ids.AssemblyUniqueID,
                        TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                        TestClassUniqueID = _ids.TestClassUniqueID,
                        TestMethodUniqueID = _ids.TestMethodUniqueID,
                        TestCaseUniqueID = _ids.TestCaseUniqueID,
                        TestUniqueID = _test.UniqueID,
                        Output = data + Environment.NewLine
                    });
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
