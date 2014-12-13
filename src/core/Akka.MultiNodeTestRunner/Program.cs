using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Akka.MultiNodeTestRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            var assemblyName = args[0];

            using (var controller = new XunitFrontController(assemblyName))
            {
                using (var discovery = new Discovery())
                {
                    controller.Find(false, discovery, new TestFrameworkOptions());
                    discovery.Finished.WaitOne();

                    foreach (var test in discovery.Tests)
                    {
                        Console.WriteLine("Starting test {0}", test.Value.First().MethodName);

                        var processes = new List<Process>();
                        
                        foreach (var nodeTest in test.Value)
                        {
                            //Loop through each test, work out number of nodes to run on and kick off process
                            var process = new Process();
                            processes.Add(process);
                            process.StartInfo.UseShellExecute = false;
                            process.StartInfo.FileName = "Akka.NodeTestRunner.exe";
                            process.StartInfo.Arguments = String.Format(@"-Dmultinode.test-assembly=""{0}"" -Dmultinode.test-class=""{1}"" -Dmultinode.test-method=""{2}"" -Dmultinode.max-nodes={3} -Dmultinode.server-host=""{4}"" -Dmultinode.host=""{5}"" -Dmultinode.index={6}",
                                assemblyName, nodeTest.TypeName, nodeTest.MethodName, test.Value.Count, "localhost", "localhost" , nodeTest.Node - 1);
                            var nodeIndex = nodeTest.Node;
                            process.OutputDataReceived +=
                                (sender, line) => Console.WriteLine("[Node{0}]{1}", nodeIndex, line.Data);
                            process.Start();
                            Console.WriteLine("Started node {0} on pid {1}", nodeTest.Node, process.Id);
                        }

                        foreach (var process in processes)
                        {
                            process.WaitForExit();
                            process.Close();
                        }
                    }
                }
            }
            Console.WriteLine("Complete");
            Console.ReadLine();
        }
    }
}
