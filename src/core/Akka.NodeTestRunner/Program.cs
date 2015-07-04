//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.NodeTestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var nodeIndex = CommandLine.GetInt32("multinode.index");
            var assemblyName = CommandLine.GetProperty("multinode.test-assembly");
            var typeName = CommandLine.GetProperty("multinode.test-class");
            var testName = CommandLine.GetProperty("multinode.test-method");
            var displayName = testName;

            Thread.Sleep(TimeSpan.FromSeconds(10));

            using (var controller = new XunitFrontController(assemblyName))
            {
                using (var discovery = new Discovery(assemblyName, typeName))
                {
                    using (var sink = new Sink(nodeIndex))
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
                            var specFail = new SpecFail(nodeIndex, displayName);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            foreach (var innerEx in ex.Flatten().InnerExceptions)
                            {
                                specFail.FailureExceptionTypes.Add(innerEx.GetType().ToString());
                                specFail.FailureMessages.Add(innerEx.Message);
                                specFail.FailureStackTraces.Add(innerEx.StackTrace);
                            }
                            Console.WriteLine(specFail);
                            Environment.Exit(1); //signal failure
                        }
                        catch (Exception ex)
                        {
                            var specFail = new SpecFail(nodeIndex, displayName);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            Console.WriteLine(specFail);
                            Environment.Exit(1); //signal failure
                        }
                        sink.Finished.WaitOne();
                        Environment.Exit(sink.Passed ? 0 : 1);
                    }
                }
            }
        }
    }
}

