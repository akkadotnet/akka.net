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
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.NodeTestRunner;
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
        /// C:\> Akka.MultiNodeTestRunner.exe [assembly name] [-Dmultinode.enable-filesink=on]
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
        ///         i.e. "Akka.Cluster.Tests.dll"
        ///              "C:\akka.net\src\Akka.Cluster.Tests\bin\Debug\Akka.Cluster.Tests.dll"
        ///     </description>
        /// </item>
        /// <item>
        ///     <term>-Dmultinode.enable-filesink</term>
        ///     <description>Having this flag set means that the contents of this test run will be saved in the
        ///                 current working directory as a .JSON file.
        ///     </description>
        /// </item>
        /// </list>
        /// </summary>
        private static void Main(string[] args)
        {
            RunTests(args);
        }

        private static void RunTests(string[] args)
        {
            TestRunSystem = ActorSystem.Create("TestRunnerLogging");
            SinkCoordinator = TestRunSystem.ActorOf(Props.Create<SinkCoordinator>(), "sinkCoordinator");

            var assemblyName = args[0];
            var specName = CommandLine.GetProperty("multinode.test-spec");
            //var specName = "ConvergenceWithAccrualFailureDetectorMultiNode";
            EnableAllSinks(assemblyName);

            using (var controller = new Xunit2(new NullSourceInformationProvider(), assemblyName))
            {
                using (var discovery = new Discovery())
                {
                    controller.Find(false, discovery, TestFrameworkOptions.ForDiscovery());
                    discovery.Finished.WaitOne();

                    var cases = discovery.TestCases.Reverse();

                    if (!string.IsNullOrWhiteSpace(specName))
                    {
                        cases = cases.Where(@case => @case.Key.Contains(specName));
                    }

                    foreach (var test in cases.ToList())
                    {
                        PublishRunnerMessage(string.Format("Starting test {0}", test.Value.First().MethodName));

                        var processes = new List<Task>();

                        StartNewSpec(test.Value);

                        var nodes = test.Value.Count;

                        foreach (var nodeTest in test.Value)
                        {
                            var closureTest = nodeTest;

                            var task = Task.Run(() =>
                            {
                                var writer = new StringWriter();

                                PublishRunnerMessage(string.Format("Started node {0}", nodeTest.Node));

                                var settings = new NodeRunnerSettings
                                {
                                    AssemblyName = assemblyName,
                                    Host = "localhost",
                                    ServerHost = "localhost",
                                    TestClass = closureTest.TypeName,
                                    TestMethod = closureTest.MethodName,
                                    MaxNodes = nodes,
                                    NodeIndex = closureTest.Node - 1
                                };
                                var exitCode = new NodeRunner().Run(settings, writer);

                                if (exitCode == 0)
                                {
                                    ReportSpecPassFromExitCode(closureTest.Node, closureTest.TestName);
                                }

                                writer
                                    .GetStringBuilder()
                                    .ToString()
                                    .Split(new[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries)
                                    .ToList()
                                    .ForEach(line =>
                                    {
                                        if (!line.StartsWith("[NODE", true, CultureInfo.InvariantCulture))
                                        {
                                            line = "[NODE" + (nodeTest.Node) + "]" + line;
                                        }
                                        PublishToAllSinks(line);
                                    });
                            });
                            processes.Add(task);
                        }

                        var ranSuccessfully = Task.WaitAll(processes.ToArray(), TimeSpan.FromMinutes(2));
                        if (ranSuccessfully)
                        {

                            PublishRunnerMessage("Waiting 3 seconds for all messages from all processes to be collected.");
                            Thread.Sleep(TimeSpan.FromSeconds(3));
                            FinishSpec();
                        }
                        else
                        {
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
            }
        }

        static void EnableAllSinks(string assemblyName)
        {
            var fileSystemSink = CommandLine.GetProperty("multinode.enable-filesink");
            if (!string.IsNullOrEmpty(fileSystemSink))
                SinkCoordinator.Tell(new SinkCoordinator.EnableSink(new FileSystemMessageSink(assemblyName)));
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

