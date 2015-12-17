//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.MultiNodeTestRunner.Shared.Logging;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.NodeTestRunner
{
    class Program
    {
        protected static ActorSystem TestActorSystem;
        protected static IActorRef UdpLogger;
        protected static ITestRunnerLogger Logger;

        static void Main(string[] args)
        {
            var nodeIndex = CommandLine.GetInt32("multinode.index");
            var assemblyFileName = CommandLine.GetProperty("multinode.test-assembly");
            var typeName = CommandLine.GetProperty("multinode.test-class");
            var testName = CommandLine.GetProperty("multinode.test-method");
            var displayName = testName;

            var listenAddress = IPAddress.Parse(CommandLine.GetProperty("multinode.listen-address"));
            var listenPort = CommandLine.GetInt32("multinode.listen-port");
            var listenEndpoint = new IPEndPoint(listenAddress, listenPort);

            UdpLogger = TestActorSystem.ActorOf(Props.Create(() => new UdpLogger(listenEndpoint, true)));
            Logger = new ActorRunnerLogger(UdpLogger, nodeIndex);

            // Redirect standard out to our UDP logger
            Console.SetOut(new TestRunnerStringWriter(Logger));

            Thread.Sleep(TimeSpan.FromSeconds(10));

            using (var controller = new XunitFrontController(assemblyFileName))
            {
                /* need to pass in just the assembly name to Discovery, not the full path
                 * i.e. "Akka.Cluster.Tests.MultiNode.dll"
                 * not "bin/Release/Akka.Cluster.Tests.MultiNode.dll" - this will cause
                 * the Discovery class to actually not find any indivudal specs to run
                 */
                var assemblyName = Path.GetFileName(assemblyFileName);
                Logger.WriteLine("Running specs for {0} [{1}]", assemblyName, assemblyFileName);
                using (var discovery = new Discovery(assemblyName, typeName))
                {
                    using (var sink = new Sink(nodeIndex, Logger))
                    {
                        Thread.Sleep(10000);
                        try
                        {
                            controller.Find(true, discovery, TestFrameworkOptions.ForDiscovery());
                            discovery.Finished.WaitOne();
                            controller.RunTests(discovery.TestCases, sink, TestFrameworkOptions.ForExecution());
                        }
                        catch (AggregateException ex)
                        {
                           
                            var failureMessages = new List<string>();
                            var failureStackTraces = new List<string>();
                            var failureExceptionTypes = new List<string>();
                            failureExceptionTypes.Add(ex.GetType().ToString());
                            failureMessages.Add(ex.Message);
                            failureStackTraces.Add(ex.StackTrace);
                            foreach (var innerEx in ex.Flatten().InnerExceptions)
                            {
                                failureExceptionTypes.Add(innerEx.GetType().ToString());
                                failureMessages.Add(innerEx.Message);
                                failureStackTraces.Add(innerEx.StackTrace);
                            }
                            var specFail = new SpecFail(nodeIndex, displayName, failureMessages, failureStackTraces, failureExceptionTypes);
                            Logger.Write(specFail);
                            WaitForLogs().Wait();
                            Environment.Exit(1); //signal failure
                        }
                        catch (Exception ex)
                        {
                            var failureMessages = new List<string>();
                            var failureStackTraces = new List<string>();
                            var failureExceptionTypes = new List<string>();
                            failureExceptionTypes.Add(ex.GetType().ToString());
                            failureMessages.Add(ex.Message);
                            failureStackTraces.Add(ex.StackTrace);
                            var specFail = new SpecFail(nodeIndex, displayName, failureMessages, failureStackTraces, failureExceptionTypes);
                            Logger.Write(specFail);
                            WaitForLogs().Wait();
                            Environment.Exit(1); //signal failure
                        }
                        sink.Finished.WaitOne();
                        WaitForLogs().Wait();
                        Environment.Exit(sink.Passed ? 0 : 1);
                    }
                }
            }
        }

        protected static async Task WaitForLogs()
        {
            var runnerShutdown = await UdpLogger.GracefulStop(TimeSpan.FromSeconds(3));
            TestActorSystem.Shutdown();
            await TestActorSystem.TerminationTask;
        }
    }
}

