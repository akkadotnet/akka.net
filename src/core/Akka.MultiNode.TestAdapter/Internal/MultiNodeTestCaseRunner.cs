//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.IO;
using Akka.MultiNode.TestAdapter.Internal.Persistence;
using Akka.MultiNode.TestAdapter.Internal.Sinks;
using Akka.MultiNode.TestAdapter.Internal.TrxReporter;
using Akka.MultiNode.TestAdapter.Configuration;
using Akka.MultiNode.TestAdapter.Helpers;
using Xunit.Sdk;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Internal
{
    /// <summary>
    /// Entry point for the MultiNodeTestRunner
    /// </summary>
    internal class MultiNodeTestCaseRunner
    {
        // Fixed TCP buffer size
        public const int TcpBufferSize = 10240;

        private ActorSystem TestRunSystem { get; set; }
        private IActorRef SinkCoordinator { get; set; }
        private MultiNodeTestRunnerOptions Options { get; }

        private readonly MultiNodeTestCase _testCase;
        private readonly string _displayName;
        private readonly string _skipReason;
        private readonly IMessageBus _messageBus;
        private readonly ExceptionAggregator _aggregator;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly TestMessageIds _ids;

        private Type TestClass { get; }
        private MethodInfo TestMethod { get; }

        public MultiNodeTestCaseRunner(
            MultiNodeTestCase testCase,
            string displayName,
            string skipReason,
            IMessageBus messageBus,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource)
        {
            _testCase = testCase;
            _displayName = displayName;
            _skipReason = skipReason;
            _messageBus = messageBus;
            _aggregator = aggregator;
            _cancellationTokenSource = cancellationTokenSource;
            _ids = TestMessageIds.From(testCase);

            TestClass = testCase.TestMethod.TestClass.Class;
            TestMethod = testCase.TestMethod.Method;

            var assembly = TestClass.Assembly;
            var attr = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            var frameworkParts = attr.FrameworkName.Split(',');
            var versionParts = frameworkParts[1].Split('=');
            var platformName = (frameworkParts[0].Replace(".", "") + versionParts[1].Replace("v", "").Replace(".", "_")).ToLowerInvariant();
            Options = OptionsReader.Load(testCase.AssemblyPath);
            Options.Platform = platformName;

            if (Options.ListenPort == 0)
                Options.ListenPort = SocketUtil.TemporaryTcpAddress(Options.ListenIpAddress).Port;
        }

        public async ValueTask<RunSummary> RunAsync()
        {
            // Shortcut the spec if it is skipped
            if (!string.IsNullOrEmpty(_skipReason))
            {
                foreach (var test in _testCase.Nodes)
                {
                    _messageBus.QueueMessage(CreateTestStarting(test));
                    _messageBus.QueueMessage(CreateTestSkipped(test, _skipReason));
                }

                return new RunSummary
                {
                    Total = _testCase.Nodes.Count,
                    Skipped = _testCase.Nodes.Count
                };
            }

            // Shortcut the spec if it already failed
            if (_aggregator.HasExceptions)
            {
                var exception = _aggregator.ToException();
                foreach (var test in _testCase.Nodes)
                {
                    _messageBus.QueueMessage(CreateTestStarting(test));
                    _messageBus.QueueMessage(CreateTestFailed(test, 0, "Test failed before being executed", exception));
                }

                return new RunSummary
                {
                    Total = _testCase.Nodes.Count,
                    Failed = _testCase.Nodes.Count
                };
            }

            // Run the actual spec
            var config = ConfigurationFactory.ParseString($@"
akka.io.tcp {{
    buffer-pool = ""akka.io.tcp.disabled-buffer-pool""
    disabled-buffer-pool.buffer-size = {TcpBufferSize}
}}
");
            TestRunSystem = ActorSystem.Create("TestRunnerLogging", config);

            var sinks = new List<MessageSink>
            {
                new DiagnosticMessageSink()
            };
            if(Options.UseBuiltInTrxReporter)
                sinks.Add(new TrxMessageSink(_displayName, Options));

            SinkCoordinator = TestRunSystem.ActorOf(Props.Create(()
                => new SinkCoordinator(sinks)), "sinkCoordinator");

            await SinkCoordinator.Ask<SinkCoordinator.Ready>(Sinks.SinkCoordinator.Ready.Instance);

            var tcpLogger = TestRunSystem.ActorOf(Props.Create(() => new TcpLoggingServer(SinkCoordinator)), "TcpLogger");
            var listenEndpoint = new IPEndPoint(IPAddress.Parse(Options.ListenAddress), Options.ListenPort);
            TestRunSystem.Tcp().Tell(new Tcp.Bind(tcpLogger, listenEndpoint), sender: tcpLogger);

            StartNewSpec();
            PublishRunnerMessage($"Starting test {_testCase.TestCaseDisplayName}");

            var timelineCollector = TestRunSystem.ActorOf(Props.Create(() => new TimelineLogCollectorActor(Options.AppendLogOutput)));

            var tasks = new List<Task<RunSummary>>();
            var serverPort = SocketUtil.TemporaryTcpAddress("localhost").Port;
            foreach (var nodeTest in _testCase.Nodes)
            {
                //Loop through each test, work out number of nodes to run on and kick off process
                var args = new []
                    {
                        $@"-Dmultinode.test-class=""{nodeTest.TestCase.TypeName}""",
                        $@"-Dmultinode.test-method=""{nodeTest.TestCase.MethodName}""",
                        $@"-Dmultinode.max-nodes={_testCase.Nodes.Count}",
                        $@"-Dmultinode.server-host=""{"localhost"}""",
                        $@"-Dmultinode.server-port={serverPort}",
                        $@"-Dmultinode.host=""{"localhost"}""",
                        $@"-Dmultinode.index={nodeTest.Node - 1}",
                        $@"-Dmultinode.role=""{nodeTest.Role}""",
                        $@"-Dmultinode.listen-address={Options.ListenAddress}",
                        $@"-Dmultinode.listen-port={Options.ListenPort}",
                        $@"-Dmultinode.test-assembly=""{_testCase.AssemblyPath}"""
                    };

                // Start process for node
                var runner = new MultiNodeTestRunner(
                    nodeTest, _messageBus, args, _skipReason, _aggregator, SinkCoordinator,
                    timelineCollector, Options, _cancellationTokenSource);

                tasks.Add(runner.RunAsync());
            }

            var summary = new RunSummary();
            // Wait for all nodes to finish and collect results
            while (tasks.Count > 0)
            {
                var finished = await Task.WhenAny(tasks);
                tasks.Remove(finished);
                summary.Aggregate(finished.Result);
            }

            try
            {
                // Limit TCP logger unbind to 10 seconds, abort the test if failed.
                await tcpLogger.Ask<TcpLoggingServer.ListenerStopped>(
                    new TcpLoggingServer.StopListener(),
                    TimeSpan.FromSeconds(10));
            }
            catch
            {
                _cancellationTokenSource.Cancel();
            }

            // Save timelined logs to file system
            await DumpAggregatedSpecLogs(summary, timelineCollector);

            await FinishSpec(timelineCollector);

            SinkCoordinator.Tell(new SinkCoordinator.CloseAllSinks());

            // Block until all Sinks have been terminated.
            var cts2 = new CancellationTokenSource();
            try
            {
                // Limit test ActorSystem shutdown to 5 seconds, abort the test if failed
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cts2.Token);
                var shutdownTask = TestRunSystem.WhenTerminated;
                var task = await Task.WhenAny(timeoutTask, shutdownTask);
                if(task != timeoutTask)
                    cts2.Cancel();
                else
                    _cancellationTokenSource.Cancel();
            }
            finally
            {
                cts2.Dispose();
            }

            return summary;
        }

        #region Message factory methods

        internal TestStarting CreateTestStarting(NodeTest test)
        {
            return new TestStarting
            {
                AssemblyUniqueID = _ids.AssemblyUniqueID,
                TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                TestClassUniqueID = _ids.TestClassUniqueID,
                TestMethodUniqueID = _ids.TestMethodUniqueID,
                TestCaseUniqueID = _ids.TestCaseUniqueID,
                TestUniqueID = test.UniqueID,
                TestDisplayName = test.DisplayName,
                Explicit = false,
                StartTime = DateTimeOffset.UtcNow,
                Timeout = 0,
                Traits = new Dictionary<string, IReadOnlyCollection<string>>()
            };
        }

        internal TestSkipped CreateTestSkipped(NodeTest test, string reason)
        {
            return new TestSkipped
            {
                AssemblyUniqueID = _ids.AssemblyUniqueID,
                TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                TestClassUniqueID = _ids.TestClassUniqueID,
                TestMethodUniqueID = _ids.TestMethodUniqueID,
                TestCaseUniqueID = _ids.TestCaseUniqueID,
                TestUniqueID = test.UniqueID,
                Reason = reason,
                ExecutionTime = 0m,
                FinishTime = DateTimeOffset.UtcNow,
                Output = "",
                Warnings = null
            };
        }

        internal TestFailed CreateTestFailed(NodeTest test, decimal executionTime, string output, Exception exception)
        {
            return new TestFailed
            {
                AssemblyUniqueID = _ids.AssemblyUniqueID,
                TestCollectionUniqueID = _ids.TestCollectionUniqueID,
                TestClassUniqueID = _ids.TestClassUniqueID,
                TestMethodUniqueID = _ids.TestMethodUniqueID,
                TestCaseUniqueID = _ids.TestCaseUniqueID,
                TestUniqueID = test.UniqueID,
                Cause = FailureCause.Exception,
                ExceptionTypes = new[] { exception.GetType().FullName },
                Messages = new[] { exception.Message },
                StackTraces = new[] { exception.StackTrace },
                ExceptionParentIndices = new[] { -1 },
                ExecutionTime = executionTime,
                FinishTime = DateTimeOffset.UtcNow,
                Output = output,
                Warnings = null
            };
        }

        #endregion

        private async Task DumpAggregatedSpecLogs(RunSummary summary, IActorRef timelineCollector)
        {
            var dumpFolder = Path.GetFullPath(Path.Combine(Options.OutputDirectory, _testCase.TestCaseDisplayName));
            var dumpPath = Path.Combine(dumpFolder, "aggregated.txt");

            Directory.CreateDirectory(dumpFolder);
            if (!Options.AppendLogOutput && File.Exists(dumpPath))
                File.Delete(dumpPath);

            var logLines = await timelineCollector.Ask<string[]>(new TimelineLogCollectorActor.GetLog());

            // Dump aggregated timeline to file for this test
            File.AppendAllLines(dumpPath, logLines);

            if (summary.Failed > 0)
            {
                var failedSpecFolder = Path.GetFullPath(Path.Combine(Options.OutputDirectory, Options.FailedSpecsDirectory));
                var failedSpecPath = Path.Combine(failedSpecFolder, $"{_testCase.TestCaseDisplayName}.txt");

                Directory.CreateDirectory(failedSpecFolder);
                if(!Options.AppendLogOutput && File.Exists(failedSpecPath))
                    File.Delete(failedSpecPath);

                File.AppendAllLines(failedSpecPath, logLines);
            }
        }

        private void StartNewSpec()
        {
            SinkCoordinator.Tell(_testCase);
        }

        private async Task FinishSpec(IActorRef timelineCollector)
        {
            var log = await timelineCollector.Ask<SpecLog>(new TimelineLogCollectorActor.GetSpecLog(), TimeSpan.FromMinutes(1));
            SinkCoordinator.Tell(new EndSpec(_testCase, log));
        }

        private void PublishRunnerMessage(string message)
        {
            SinkCoordinator.Tell(new SinkCoordinator.RunnerMessage(message));
        }
    }
}
