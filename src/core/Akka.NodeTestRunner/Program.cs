//-----------------------------------------------------------------------
// <copyright file="Program.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Remote.TestKit;
using Xunit;

namespace Akka.NodeTestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var nodeIndex = CommandLine.GetInt32("multinode.index");
            var assemblyFileName = CommandLine.GetProperty("multinode.test-assembly");
            var typeName = CommandLine.GetProperty("multinode.test-class");
            var testName = CommandLine.GetProperty("multinode.test-method");
            var displayName = testName;

            var system = ActorSystem.Create("NoteTestRunner-" + nodeIndex);
            var tcpClient = system.ActorOf<RunnerTcpClient>();
            system.Tcp().Tell(new Tcp.Connect(new DnsEndPoint("localhost", 5678)), tcpClient);
            var tcpOut = new TcpConsole(tcpClient);
            Console.SetOut(tcpOut);

            Thread.Sleep(TimeSpan.FromSeconds(10));

            using (var controller = new XunitFrontController(assemblyFileName))
            {
                /* need to pass in just the assembly name to Discovery, not the full path
                 * i.e. "Akka.Cluster.Tests.MultiNode.dll"
                 * not "bin/Release/Akka.Cluster.Tests.MultiNode.dll" - this will cause
                 * the Discovery class to actually not find any indivudal specs to run
                 */
                var assemblyName = Path.GetFileName(assemblyFileName);
                Console.WriteLine("Running specs for {0} [{1}]", assemblyName, assemblyFileName);
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

                            //make sure message is send over the wire
                            Task.Delay(TimeSpan.FromSeconds(10)).Wait();
                            Environment.Exit(1); //signal failure
                        }
                        catch (Exception ex)
                        {
                            var specFail = new SpecFail(nodeIndex, displayName);
                            specFail.FailureExceptionTypes.Add(ex.GetType().ToString());
                            specFail.FailureMessages.Add(ex.Message);
                            specFail.FailureStackTraces.Add(ex.StackTrace);
                            Console.WriteLine(specFail);

                            //make sure message is send over the wire
                            Task.Delay(TimeSpan.FromSeconds(10)).Wait();
                            Environment.Exit(1); //signal failure
                        }
                        sink.Finished.WaitOne();

                        //make sure alls messages are send over the wire
                        Task.Delay(TimeSpan.FromSeconds(10)).Wait();
                        system.Shutdown();
                        system.AwaitTermination();

                        Environment.Exit(sink.Passed ? 0 : 1);
                    }
                }
            }
        }
    }

    class TcpConsole : TextWriter
     {
         private readonly IActorRef _tcpClient;
 
         public TcpConsole(IActorRef tcpClient)
         {
             _tcpClient = tcpClient;
             Encoding = Encoding.UTF8;
         }
 
         public override void Write(string value)
         {
             _tcpClient.Tell(value);
         }
 
         public override void WriteLine(string value)
         {
             _tcpClient.Tell(value);
         }
 
         public override Encoding Encoding { get; }
     }
 
     class RunnerTcpClient : ReceiveActor, IWithUnboundedStash
     {
         public RunnerTcpClient()
         {
             Become(WaitingForConnection);
         }
 
         private void WaitingForConnection()
         {
             Receive<Tcp.Connected>(connected =>
             {
                 Sender.Tell(new Tcp.Register(Self));
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

