//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.MultiNodeTestRunner.Shared.Sinks;
using Akka.Remote.TestKit;
using Xunit;
#if CORECLR
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyModel;
#endif

namespace Akka.NodeTestRunner
{
    class Program
    {
        /// <summary>
        /// If it takes longer than this value for the <see cref="Sink"/> to get back to us
        /// about a particular test passing or failing, throw loudly.
        /// </summary>
        private static readonly TimeSpan MaxProcessWaitTimeout = TimeSpan.FromMinutes(5);
        private static IActorRef _logger;

        static int Main(string[] args)
        {
            var nodeIndex = CommandLine.GetInt32("multinode.index");
            var nodeRole = CommandLine.GetProperty("multinode.role");
            var assemblyFileName = CommandLine.GetProperty("multinode.test-assembly");
            var typeName = CommandLine.GetProperty("multinode.test-class");
            var testName = CommandLine.GetProperty("multinode.test-method");
            var displayName = testName;

            var listenAddress = IPAddress.Parse(CommandLine.GetProperty("multinode.listen-address"));
            var listenPort = CommandLine.GetInt32("multinode.listen-port");
            var listenEndpoint = new IPEndPoint(listenAddress, listenPort);

            var system = ActorSystem.Create("NoteTestRunner-" + nodeIndex);
            var tcpClient = _logger = system.ActorOf<RunnerTcpClient>();
            system.Tcp().Tell(new Tcp.Connect(listenEndpoint), tcpClient);

#if CORECLR
            // In NetCore, if the assembly file hasn't been touched, 
            // XunitFrontController would fail loading external assemblies and its dependencies.
            AssemblyLoadContext.Default.Resolving += (assemblyLoadContext, assemblyName) => DefaultOnResolving(assemblyLoadContext, assemblyName, assemblyFileName);
            var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyFileName);
            DependencyContext.Load(assembly)
                .CompileLibraries
                .Where(dep => dep.Name.ToLower()
                    .Contains(assembly.FullName.Split(new[] { ',' })[0].ToLower()))
                .Select(dependency => AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(dependency.Name)));
#endif

            Thread.Sleep(TimeSpan.FromSeconds(10));
            using (var controller = new XunitFrontController(AppDomainSupport.IfAvailable, assemblyFileName))
            {
                /* need to pass in just the assembly name to Discovery, not the full path
                 * i.e. "Akka.Cluster.Tests.MultiNode.dll"
                 * not "bin/Release/Akka.Cluster.Tests.MultiNode.dll" - this will cause
                 * the Discovery class to actually not find any individual specs to run
                 */
                var assemblyName = Path.GetFileName(assemblyFileName);
                Console.WriteLine("Running specs for {0} [{1}]", assemblyName, assemblyFileName);
                using (var discovery = new Discovery(assemblyName, typeName))
                {
                    using (var sink = new Sink(nodeIndex, nodeRole, tcpClient))
                    {
                        try
                        {
                            controller.Find(true, discovery, TestFrameworkOptions.ForDiscovery());
                            discovery.Finished.WaitOne();
                            controller.RunTests(discovery.TestCases, sink, TestFrameworkOptions.ForExecution());
                        }
                        catch (AggregateException ex)
                        {
                            var specFail = new SpecFail(nodeIndex, nodeRole, displayName);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            foreach (var innerEx in ex.Flatten().InnerExceptions)
                            {
                                specFail.FailureExceptionTypes.Add(innerEx.GetType().ToString());
                                specFail.FailureMessages.Add(innerEx.Message);
                                specFail.FailureStackTraces.Add(innerEx.StackTrace);
                            }
                            _logger.Tell(specFail.ToString());
                            Console.WriteLine(specFail);

                            //make sure message is send over the wire
                            FlushLogMessages();
                            Environment.Exit(1); //signal failure
                            return 1;
                        }
                        catch (Exception ex)
                        {
                            var specFail = new SpecFail(nodeIndex, nodeRole, displayName);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            var innerEx = ex.InnerException;
                            while (innerEx != null)
                            {
                                specFail.FailureExceptionTypes.Add(innerEx.GetType().ToString());
                                specFail.FailureMessages.Add(innerEx.Message);
                                specFail.FailureStackTraces.Add(innerEx.StackTrace);
                                innerEx = innerEx.InnerException;
                            }
                            _logger.Tell(specFail.ToString());
                            Console.WriteLine(specFail);

                            //make sure message is send over the wire
                            FlushLogMessages();
                            Environment.Exit(1); //signal failure
                            return 1;
                        }

                        var timedOut = false;
                        if (!sink.Finished.WaitOne(MaxProcessWaitTimeout)) //timed out
                        {
                            var line = string.Format("Timed out while waiting for test to complete after {0} ms",
                                MaxProcessWaitTimeout);
                            _logger.Tell(line);
                            Console.WriteLine(line);
                            timedOut = true;
                        }

                        FlushLogMessages();
                        system.Terminate().Wait();

                        Environment.Exit(sink.Passed && !timedOut ? 0 : 1);
                        return sink.Passed ? 0 : 1;
                    }
                }
            }
        }

        private static void FlushLogMessages()
        {
            try
            {
                _logger.GracefulStop(TimeSpan.FromSeconds(2)).Wait();
            }
            catch
            {
                Console.WriteLine("Exception thrown while waiting for TCP transport to flush - not all messages may have been logged.");
            }
        }

#if CORECLR
        private static Assembly DefaultOnResolving(AssemblyLoadContext assemblyLoadContext, AssemblyName assemblyName, string assemblyPath)
        {
            string dllName = assemblyName.Name.Split(new[] { ',' })[0] + ".dll";
            return assemblyLoadContext.LoadFromAssemblyPath(Path.Combine(Path.GetDirectoryName(assemblyPath), dllName));
        }
#endif
    }

    class RunnerTcpClient : ReceiveActor, IWithUnboundedStash
    {
        private IActorRef _connection;
        
        public RunnerTcpClient()
        {
            Become(WaitingForConnection);
        }

        /// <inheritdoc />
        protected override void PostStop()
        {
            // Close connection property to avoid exception logged at TcpConnection actor once this actor is terminated
            try
            {
                _connection.Ask<Tcp.Closed>(Tcp.Close.Instance, TimeSpan.FromSeconds(1)).Wait();
            }
            catch { /* well... at least we have tried */ }
            
            base.PostStop();
        }

        private void WaitingForConnection()
        {
            Receive<Tcp.Connected>(connected =>
            {
                Sender.Tell(new Tcp.Register(Self));
                _connection = Sender;
                Become(Connected(Sender));
            });
            Receive<string>(_ => Stash.Stash());
        }

        private Receive Connected(IActorRef connection)
        {
            Stash.UnstashAll();

            return message =>
            {
                var bytes = ByteString.FromString(message.ToString());
                connection.Tell(Tcp.Write.Create(bytes));

                return true;
            };
        }

        public IStash Stash { get; set; }
    }
}

