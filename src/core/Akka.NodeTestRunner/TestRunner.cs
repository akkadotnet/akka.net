// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
// --------------------------------------------------------------------------------------------------------------------
namespace Akka.NodeTestRunner
{
    using System;
    using System.IO;

    using Akka.Remote.TestKit;

    using Xunit;

    public class TestRunner : MarshalByRefObject, ITestRunner
    {
        public bool NoErrors { get; private set; }

        public void InitializeWithTestArgs(string[] testArgs)
        {
            TestSettings.Initialize(testArgs);
        }

        public void Run()
        {

            var nodeIndex = TestSettings.GetInt32("multinode.index");
            var assemblyFileName = TestSettings.GetProperty("multinode.test-assembly");
            var typeName = TestSettings.GetProperty("multinode.test-class");
            var displayName = TestSettings.GetProperty("multinode.test-method");

            /* need to pass in just the assembly name to Discovery, not the full path
             * i.e. "Akka.Cluster.Tests.MultiNode.dll"
             * not "bin/Release/Akka.Cluster.Tests.MultiNode.dll" - this will cause
             * the Discovery class to actually not find any individual specs to run
             */
            var assemblyName = Path.GetFileName(assemblyFileName);
            Console.WriteLine("Running specs for {0} [{1}]", assemblyName, assemblyFileName);

            using (var controller = new XunitFrontController(AppDomainSupport.IfAvailable, assemblyFileName))
            using (var discovery = new Discovery(assemblyName, typeName))
            using (var sink = new Sink(nodeIndex))
            {
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
                    this.NoErrors = false;
                    return;
                }
                catch (Exception ex)
                {
                    var specFail = new SpecFail(nodeIndex, displayName);
                    specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                    specFail.FailureMessages.Add(ex.Message);
                    specFail.FailureStackTraces.Add(ex.StackTrace);
                    Console.WriteLine(specFail);
                    this.NoErrors = false;
                    return;
                }
                sink.Finished.WaitOne();
                this.NoErrors = sink.Passed;
            }
        }
    }
}