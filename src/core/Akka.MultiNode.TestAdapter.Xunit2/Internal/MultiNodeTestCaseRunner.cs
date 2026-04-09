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
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter.Internal
{
    /// <summary>
    /// Entry point for the MultiNodeTestRunner
    /// </summary>
    internal class MultiNodeTestCaseRunner : TestCaseRunner<MultiNodeTestCase>
    {
        // Fixed TCP buffer size
        public const int TcpBufferSize = 10240;
        
        private ActorSystem TestRunSystem { get; set; }
        private IActorRef SinkCoordinator { get; set; }
        private MultiNodeTestRunnerOptions Options { get; }
        
        /// <summary>
        /// Gets or sets the display name of the test case
        /// </summary>
        private string DisplayName { get; }

        /// <summary>
        /// Gets or sets the skip reason for the test, if set.
        /// </summary>
        private string SkipReason { get; }

        /// <summary>
        /// Gets or sets the runtime type for the test class that the test method belongs to.
        /// </summary>
        private Type TestClass { get; }

        /// <summary>
        /// Gets of sets the runtime method for the test method that the test case belongs to.
        /// </summary>
        private MethodInfo TestMethod { get; }

        private readonly Xunit.Abstractions.IMessageSink _diagnosticSink;

        public MultiNodeTestCaseRunner(
            MultiNodeTestCase testCase,
            string displayName,
            string skipReason,
            IMessageBus messageBus,
            Xunit.Abstractions.IMessageSink diagnosticSink,
            ExceptionAggregator aggregator,
            CancellationTokenSource cancellationTokenSource) 
            : base(testCase, messageBus, aggregator, cancellationTokenSource)
        {
            _diagnosticSink = diagnosticSink;
            DisplayName = displayName;
            SkipReason = skipReason;

            TestClass = TestCase.TestMethod.TestClass.Class.ToRuntimeType();
            TestMethod = TestCase.Method.ToRuntimeMethod();
            
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

        protected override async Task<RunSummary> RunTestAsync()
        {
            // Shortcut the spec if it is skipped
            if (!string.IsNullOrEmpty(SkipReason))
            {
                foreach (var test in TestCase.Nodes)
                {
                    MessageBus.QueueMessage(new TestStarting(test));
                    MessageBus.QueueMessage(new TestSkipped(test, SkipReason));
                }

                return new RunSummary
                {
                    Total = TestCase.Nodes.Count,
                    Skipped = TestCase.Nodes.Count
                };
            }

            // Shortcut the spec if it already failed
            if (Aggregator.HasExceptions)
            {
                var exception = Aggregator.ToException();
                foreach (var test in TestCase.Nodes)
                {
                    MessageBus.QueueMessage(new TestStarting(test));
                    MessageBus.QueueMessage(new TestFailed(test, 0, "Test failed before being executed", exception));
                }

                return new RunSummary
                {
                    Total = TestCase.Nodes.Count,
                    Failed = TestCase.Nodes.Count
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
                new DiagnosticMessageSink(_diagnosticSink)
            };
            if(Options.UseBuiltInTrxReporter)
                sinks.Add(new TrxMessageSink(DisplayName, Options));
            
            SinkCoordinator = TestRunSystem.ActorOf(Props.Create(()
                => new SinkCoordinator(sinks)), "sinkCoordinator");

            await SinkCoordinator.Ask<SinkCoordinator.Ready>(Sinks.SinkCoordinator.Ready.Instance);
            
            var tcpLogger = TestRunSystem.ActorOf(Props.Create(() => new TcpLoggingServer(SinkCoordinator)), "TcpLogger");
            var listenEndpoint = new IPEndPoint(IPAddress.Parse(Options.ListenAddress), Options.ListenPort);
            TestRunSystem.Tcp().Tell(new Tcp.Bind(tcpLogger, listenEndpoint), sender: tcpLogger);

            StartNewSpec();
            PublishRunnerMessage($"Starting test {TestCase.DisplayName}");

            var timelineCollector = TestRunSystem.ActorOf(Props.Create(() => new TimelineLogCollectorActor(Options.AppendLogOutput)));
            
            var tasks = new List<Task<RunSummary>>();
            var serverPort = SocketUtil.TemporaryTcpAddress("localhost").Port;
            foreach (var nodeTest in TestCase.Nodes)
            {
                //Loop through each test, work out number of nodes to run on and kick off process
                var args = new []
                    {
                        $@"-Dmultinode.test-class=""{nodeTest.TestCase.TypeName}""",
                        $@"-Dmultinode.test-method=""{nodeTest.TestCase.MethodName}""",
                        $@"-Dmultinode.max-nodes={TestCase.Nodes.Count}",
                        $@"-Dmultinode.server-host=""{"localhost"}""",
                        $@"-Dmultinode.server-port={serverPort}",
                        $@"-Dmultinode.host=""{"localhost"}""",
                        $@"-Dmultinode.index={nodeTest.Node - 1}",
                        $@"-Dmultinode.role=""{nodeTest.Role}""",
                        $@"-Dmultinode.listen-address={Options.ListenAddress}",
                        $@"-Dmultinode.listen-port={Options.ListenPort}",
                        $@"-Dmultinode.test-assembly=""{TestCase.AssemblyPath}"""
                    };

                // Start process for node
                var runner = new MultiNodeTestRunner(
                    nodeTest, MessageBus, args, SkipReason, Aggregator, SinkCoordinator,
                    timelineCollector, Options, CancellationTokenSource);
                
                tasks.Add(runner.RunAsync());
            }

            var summary = new RunSummary();
            // Wait for all nodes to finish and collect results
            while (tasks.Count > 0)
            {
                // TODO: might be a bug source if await throws
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
                CancellationTokenSource.Cancel();
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
                    CancellationTokenSource.Cancel();
            }
            finally
            {
                cts2.Dispose();
            }
            
            return summary;
        }

        private async Task DumpAggregatedSpecLogs(RunSummary summary, IActorRef timelineCollector)
        {
            var dumpFolder = Path.GetFullPath(Path.Combine(Options.OutputDirectory, TestCase.DisplayName)); 
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
                var failedSpecPath = Path.Combine(failedSpecFolder, $"{TestCase.DisplayName}.txt");
                
                Directory.CreateDirectory(failedSpecFolder);
                if(!Options.AppendLogOutput && File.Exists(failedSpecPath))
                    File.Delete(failedSpecPath);
                
                File.AppendAllLines(failedSpecPath, logLines);
            }
        }

        private void StartNewSpec()
        {
            SinkCoordinator.Tell(TestCase);
        }

        private async Task FinishSpec(IActorRef timelineCollector)
        {
            var log = await timelineCollector.Ask<SpecLog>(new TimelineLogCollectorActor.GetSpecLog(), TimeSpan.FromMinutes(1));
            SinkCoordinator.Tell(new EndSpec(TestCase, log));
        }

        private void PublishRunnerMessage(string message)
        {
            SinkCoordinator.Tell(new SinkCoordinator.RunnerMessage(message));
        }
    }
}