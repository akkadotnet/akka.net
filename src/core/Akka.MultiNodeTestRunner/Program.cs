﻿//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.MultiNodeTestRunner.Shared;
using Akka.MultiNodeTestRunner.Shared.Persistence;
using Akka.MultiNodeTestRunner.Shared.Reporting;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.Remote.TestKit;
using JetBrains.TeamCity.ServiceMessages.Write.Special;
using JetBrains.TeamCity.ServiceMessages.Write.Special.Impl;
using Xunit;

namespace Akka.MultiNodeTestRunner
{
    /// <summary>
    /// Entry point for the MultiNodeTestRunner
    /// </summary>
    class Program
    {
        protected static ActorSystem TestRunSystem;

        protected static IActorRef SinkCoordinator;

        /// <summary>
        /// file output directory
        /// </summary>
        protected static string OutputDirectory;

        protected static bool TeamCityFormattingOn;

        /// <summary>
        /// MultiNodeTestRunner takes the following <see cref="args"/>:
        /// 
        /// C:\> Akka.MultiNodeTestRunner.exe [assembly name] [-Dmultinode.enable-filesink=on] [-Dmultinode.output-directory={dir path}] [-Dmultinode.spec={spec name}]
        /// 
        /// <list type="number">
        /// <listheader>
        ///     <term>Argument</term>
        ///     <description>The name and possible value of a given Akka.MultiNodeTestRunner.exe argument.</description>
        /// </listheader>
        /// <item>
        ///     <term>AssemblyName</term>
        ///     <description>
        ///         The full path or name of an assembly containing as least one MultiNodeSpec in the current working directory.
        /// 
        ///         i.e. "Akka.Cluster.Tests.MultiNode.dll"
        ///              "C:\akka.net\src\Akka.Cluster.Tests\bin\Debug\Akka.Cluster.Tests.MultiNode.dll"
        ///     </description>
        /// </item>
        /// <item>
        ///     <term>-Dmultinode.enable-filesink</term>
        ///     <description>Having this flag set means that the contents of this test run will be saved in the
        ///                 current working directory as a .JSON file.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term>-Dmultinode.multinode.output-directory</term>
        ///     <description>Setting this flag means that any persistent multi-node test runner output files
        ///                  will be written to this directory instead of the default, which is the same folder
        ///                  as the test binary.
        ///     </description>
        /// </item>
        /// <item>
        ///     <term>-Dmultinode.listen-address={ip}</term>
        ///     <description>
        ///             Determines the address that this multi-node test runner will use to listen for log messages from
        ///             individual NodeTestRunner.exe processes.
        /// 
        ///             Defaults to 127.0.0.1
        ///     </description>
        /// </item>
        /// <item>
        ///     <term>-Dmultinode.listen-port={port}</term>
        ///     <description>
        ///             Determines the port number that this multi-node test runner will use to listen for log messages from
        ///             individual NodeTestRunner.exe processes.
        /// 
        ///             Defaults to 6577
        ///     </description>
        /// </item>
        /// <item>
        ///     <term>-Dmultinode.spec={spec name}</term>
        ///     <description>
        ///             Setting this flag means that only tests which contains the spec name will be executed
        ///             otherwise all tests will be executed
        ///     </description>
        /// </item>
        /// </list>
        /// </summary>
        static void Main(string[] args)
        {
            OutputDirectory = CommandLine.GetProperty("multinode.output-directory") ?? string.Empty;
            TestRunSystem = ActorSystem.Create("TestRunnerLogging");

            var suiteName = Path.GetFileNameWithoutExtension(Path.GetFullPath(args[0].Trim('"')));
            var teamCityFormattingOn = CommandLine.GetProperty("multinode.teamcity") ?? "false";
            if (!Boolean.TryParse(teamCityFormattingOn, out TeamCityFormattingOn))
                throw new ArgumentException("Invalid argument provided for -Dteamcity");

            SinkCoordinator = TestRunSystem.ActorOf(TeamCityFormattingOn ?
                Props.Create(() => new SinkCoordinator(new[] { new TeamCityMessageSink(str => Console.WriteLine(str), suiteName) })) : // mutes ConsoleMessageSinkActor
                Props.Create<SinkCoordinator>(), "sinkCoordinator");

            var listenAddress = IPAddress.Parse(CommandLine.GetPropertyOrDefault("multinode.listen-address", "127.0.0.1"));
            var listenPort = CommandLine.GetInt32OrDefault("multinode.listen-port", 6577);
            var listenEndpoint = new IPEndPoint(listenAddress, listenPort);
            var specName = CommandLine.GetPropertyOrDefault("multinode.spec", "");

            var tcpLogger = TestRunSystem.ActorOf(Props.Create(() => new TcpLoggingServer(SinkCoordinator)), "TcpLogger");
            TestRunSystem.Tcp().Tell(new Tcp.Bind(tcpLogger, listenEndpoint));

            var assemblyName = Path.GetFullPath(args[0].Trim('"')); //unquote the string first
            EnableAllSinks(assemblyName);
            PublishRunnerMessage(String.Format("Running MultiNodeTests for {0}", assemblyName));

            using (var controller = new XunitFrontController(AppDomainSupport.IfAvailable, assemblyName))
            {
                using (var discovery = new Discovery())
                {
                    controller.Find(false, discovery, TestFrameworkOptions.ForDiscovery());
                    discovery.Finished.WaitOne();

                    foreach (var test in discovery.Tests.Reverse())
                    {
                        if (!string.IsNullOrEmpty(test.Value.First().SkipReason))
                        {
                            PublishRunnerMessage(string.Format("Skipping test {0}. Reason - {1}",
                                test.Value.First().MethodName, test.Value.First().SkipReason));
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(specName) &&
                            CultureInfo.InvariantCulture.CompareInfo.IndexOf(test.Value.First().TestName,
                                specName,
                                CompareOptions.IgnoreCase) < 0)
                        {
                            PublishRunnerMessage($"Skipping [{test.Value.First().MethodName}] (Filtering)");
                            continue;
                        }

                        var processes = new List<Process>();

                        PublishRunnerMessage(string.Format("Starting test {0}",
                            test.Value.First().MethodName));

                        StartNewSpec(test.Value);
                        foreach (var nodeTest in test.Value)
                        {
                            //Loop through each test, work out number of nodes to run on and kick off process
                            var process = new Process();
                            processes.Add(process);
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.RedirectStandardOutput = true;
                            process.StartInfo.FileName = "Akka.NodeTestRunner.exe";
                            process.StartInfo.Arguments =
                                $@"-Dmultinode.test-assembly=""{assemblyName}"" -Dmultinode.test-class=""{
                                        nodeTest.TypeName
                                    }"" -Dmultinode.test-method=""{
                                        nodeTest.MethodName
                                    }"" -Dmultinode.max-nodes={
                                        test.Value.Count
                                    } -Dmultinode.server-host=""{"localhost"}"" -Dmultinode.host=""{
                                        "localhost"
                                    }"" -Dmultinode.index={nodeTest.Node - 1} -Dmultinode.role=""{
                                        nodeTest.Role
                                    }"" -Dmultinode.listen-address={listenAddress} -Dmultinode.listen-port={
                                        listenPort
                                    }";
                            var nodeIndex = nodeTest.Node;
                            var nodeRole = nodeTest.Role;
                            //TODO: might need to do some validation here to avoid the 260 character max path error on Windows
                            var folder =
                                Directory.CreateDirectory(Path.Combine(OutputDirectory,
                                    nodeTest.TestName));
                            var logFilePath =
                                Path.Combine(folder.FullName,
                                    "node" + nodeIndex + "__" + nodeRole + ".txt");
                            var fileActor =
                                TestRunSystem.ActorOf(
                                    Props.Create(() => new FileSystemAppenderActor(logFilePath)));
                            process.OutputDataReceived += (sender, eventArgs) =>
                            {
                                if (eventArgs?.Data != null)
                                {
                                    fileActor.Tell(eventArgs.Data);
                                    if (TeamCityFormattingOn)
                                    {
                                        // teamCityTest.WriteStdOutput(eventArgs.Data); TODO: open flood gates
                                    }
                                }
                            };
                            var closureTest = nodeTest;
                            process.Exited += (sender, eventArgs) =>
                            {
                                if (process.ExitCode == 0)
                                {
                                    ReportSpecPassFromExitCode(nodeIndex, nodeRole,
                                        closureTest.TestName);
                                }
                            };

                            process.Start();
                            process.BeginOutputReadLine();
                            PublishRunnerMessage(string.Format("Started node {0} : {1} on pid {2}",
                                nodeIndex,
                                nodeRole, process.Id));
                        }

                        foreach (var process in processes)
                        {
                            process.WaitForExit();
                            var exitCode = process.ExitCode;
                            process.Close();
                        }

                        PublishRunnerMessage(
                            "Waiting 3 seconds for all messages from all processes to be collected.");
                        Thread.Sleep(TimeSpan.FromSeconds(3));
                        FinishSpec(test.Value);


                    }
                }
            }
            Console.WriteLine("Complete");
            PublishRunnerMessage("Waiting 5 seconds for all messages from all processes to be collected.");
            Thread.Sleep(TimeSpan.FromSeconds(5));
            CloseAllSinks();

            //Block until all Sinks have been terminated.
            TestRunSystem.WhenTerminated.Wait(TimeSpan.FromMinutes(1));

            if (Debugger.IsAttached)
            {
                Console.ReadLine(); //block when debugging
            }

            //Return the proper exit code
            Environment.Exit(ExitCodeContainer.ExitCode);
        }

        static void EnableAllSinks(string assemblyName)
        {
            var now = DateTime.UtcNow;

            // if multinode.output-directory wasn't specified, the results files will be written
            // to the same directory as the test assembly.
            var outputDirectory = OutputDirectory;

            Func<MessageSink> createJsonFileSink = () =>
                {
                    var fileName = FileNameGenerator.GenerateFileName(outputDirectory, assemblyName, ".json", now);

                    var jsonStoreProps = Props.Create(() =>
                        new FileSystemMessageSinkActor(new JsonPersistentTestRunStore(), fileName, !TeamCityFormattingOn, true));

                    return new FileSystemMessageSink(jsonStoreProps);
                };

            Func<MessageSink> createVisualizerFileSink = () =>
                {
                    var fileName = FileNameGenerator.GenerateFileName(outputDirectory, assemblyName, ".html", now);

                    var visualizerProps = Props.Create(() =>
                        new FileSystemMessageSinkActor(new VisualizerPersistentTestRunStore(), fileName, !TeamCityFormattingOn, true));

                    return new FileSystemMessageSink(visualizerProps);
                };

            var fileSystemSink = CommandLine.GetProperty("multinode.enable-filesink");
            if (!string.IsNullOrEmpty(fileSystemSink))
            {
                SinkCoordinator.Tell(new SinkCoordinator.EnableSink(createJsonFileSink()));
                SinkCoordinator.Tell(new SinkCoordinator.EnableSink(createVisualizerFileSink()));
            }
        }

        static void CloseAllSinks()
        {
            SinkCoordinator.Tell(new SinkCoordinator.CloseAllSinks());
        }

        static void StartNewSpec(IList<NodeTest> tests)
        {
            SinkCoordinator.Tell(tests);
        }

        static void ReportSpecPassFromExitCode(int nodeIndex, string nodeRole, string testName)
        {
            SinkCoordinator.Tell(new NodeCompletedSpecWithSuccess(nodeIndex, nodeRole, testName + " passed."));
        }

        static void FinishSpec(IList<NodeTest> tests)
        {
            var spec = tests.First();
            SinkCoordinator.Tell(new EndSpec(spec.TestName, spec.MethodName));
        }

        static void PublishRunnerMessage(string message)
        {
            SinkCoordinator.Tell(new SinkCoordinator.RunnerMessage(message));
        }

        static void PublishToAllSinks(string message)
        {
            SinkCoordinator.Tell(message, ActorRefs.NoSender);
        }
    }

    class TcpLoggingServer : ReceiveActor
    {
        private readonly IActorRef _sinkCoordinator;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public TcpLoggingServer(IActorRef sinkCoordinator)
        {
            _sinkCoordinator = sinkCoordinator;

            Receive<Tcp.Connected>(connected =>
            {
                _log.Info($"Node connected on {Sender}");
                Sender.Tell(new Tcp.Register(Self));
            });

            Receive<Tcp.ConnectionClosed>(
                closed => _log.Info($"Node disconnected on {Sender}{Environment.NewLine}"));

            Receive<Tcp.Received>(received =>
            {
                var message = received.Data.ToString();
                _sinkCoordinator.Tell(message);
            });
        }
    }
}

