//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared;
using Akka.MultiNodeTestRunner.Shared.Persistence;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.Remote.TestKit;
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
        /// MultiNodeTestRunner takes the following <see cref="args"/>:
        /// 
        /// C:\> Akka.MultiNodeTestRunner.exe [assembly name] [-Dmultinode.enable-filesink=on] [-Dmultinode.output-directory={dir path}]
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
        /// </list>
        /// </summary>
        static void Main(string[] args)
        {
            TestRunSystem = ActorSystem.Create("TestRunnerLogging");
            SinkCoordinator = TestRunSystem.ActorOf(Props.Create<SinkCoordinator>(), "sinkCoordinator");

            var assemblyName = Path.GetFullPath(args[0]);
            EnableAllSinks(assemblyName);
            PublishRunnerMessage(String.Format("Running MultiNodeTests for {0}", assemblyName));

            using (var controller = new XunitFrontController(assemblyName))
            {
                using (var discovery = new Discovery())
                {
                    controller.Find(false, discovery, TestFrameworkOptions.ForDiscovery());
                    discovery.Finished.WaitOne();

                    foreach (var test in discovery.Tests.Reverse())
                    {
                        if (!string.IsNullOrEmpty(test.Value.First().SkipReason))
                        {
                            PublishRunnerMessage(string.Format("Skipping test {0}. Reason - {1}", test.Value.First().MethodName, test.Value.First().SkipReason));
                            continue;
                        }

                        PublishRunnerMessage(string.Format("Starting test {0}", test.Value.First().MethodName));

                        var processes = new List<Process>();

                        StartNewSpec(test.Value);

                        foreach (var nodeTest in test.Value)
                        {
                            //Loop through each test, work out number of nodes to run on and kick off process
                            var process = new Process();
                            processes.Add(process);
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.RedirectStandardOutput = true;
                            process.StartInfo.FileName = "Akka.NodeTestRunner.exe";
                            process.StartInfo.Arguments = String.Format(@"-Dmultinode.test-assembly=""{0}"" -Dmultinode.test-class=""{1}"" -Dmultinode.test-method=""{2}"" -Dmultinode.max-nodes={3} -Dmultinode.server-host=""{4}"" -Dmultinode.host=""{5}"" -Dmultinode.index={6}",
                                assemblyName, nodeTest.TypeName, nodeTest.MethodName, test.Value.Count, "localhost", "localhost", nodeTest.Node - 1);
                            var nodeIndex = nodeTest.Node;
                            process.OutputDataReceived +=
                                (sender, line) =>
                                {
                                    //ignore any trailing whitespace
                                    if (string.IsNullOrEmpty(line.Data) || string.IsNullOrWhiteSpace(line.Data)) return;
                                    string message = line.Data;
                                    if (!message.StartsWith("[NODE", true, CultureInfo.InvariantCulture))
                                    {
                                        message = "[NODE" + nodeIndex + "]" + message;
                                    }
                                    PublishToAllSinks(message);
                                };

                            var closureTest = nodeTest;
                            process.Exited += (sender, eventArgs) =>
                            {
                                if (process.ExitCode == 0)
                                {
                                    ReportSpecPassFromExitCode(nodeIndex, closureTest.TestName);
                                }
                            };
                            process.Start();

                            process.BeginOutputReadLine();
                            PublishRunnerMessage(string.Format("Started node {0} on pid {1}", nodeTest.Node, process.Id));
                        }

                        foreach (var process in processes)
                        {
                            process.WaitForExit();
                            var exitCode = process.ExitCode;
                            process.Close();
                        }

                        PublishRunnerMessage("Waiting 3 seconds for all messages from all processes to be collected.");
                        Thread.Sleep(TimeSpan.FromSeconds(3));
                        FinishSpec();
                    }
                }
            }
            Console.WriteLine("Complete");
            PublishRunnerMessage("Waiting 5 seconds for all messages from all processes to be collected.");
            Thread.Sleep(TimeSpan.FromSeconds(5));
            CloseAllSinks();
            
            //Block until all Sinks have been terminated.
            TestRunSystem.AwaitTermination(TimeSpan.FromMinutes(1));

            //Return the proper exit code
            Environment.Exit(ExitCodeContainer.ExitCode);
        }

        static void EnableAllSinks(string assemblyName)
        {
            var now = DateTime.UtcNow;

            // if multinode.output-directory wasn't specified, the results files will be written
            // to the same directory as the test assembly.
            var outputDirectory = CommandLine.GetProperty("multinode.output-directory");

            Func<MessageSink> createJsonFileSink = () =>
                {
                    var fileName = FileNameGenerator.GenerateFileName(outputDirectory, assemblyName, ".json", now);

                    var jsonStoreProps = Props.Create(() =>
                        new FileSystemMessageSinkActor(new JsonPersistentTestRunStore(), fileName, true));

                    return new FileSystemMessageSink(jsonStoreProps);
                };

            Func<MessageSink> createVisualizerFileSink = () =>
                {
                    var fileName = FileNameGenerator.GenerateFileName(outputDirectory, assemblyName, ".html", now);

                    var visualizerProps = Props.Create(() =>
                        new FileSystemMessageSinkActor(new VisualizerPersistentTestRunStore(), fileName, true));

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

        static void ReportSpecPassFromExitCode(int nodeIndex, string testName)
        {
            SinkCoordinator.Tell(new NodeCompletedSpecWithSuccess(nodeIndex, testName + " passed."));
        }

        static void FinishSpec()
        {
           SinkCoordinator.Tell(new EndSpec());
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
}

