using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Akka.MultiNodeTestRunner.Shared;
using Xunit.Abstractions;

namespace Akka.MultiNodeTestRunner
{
    class Discovery : IMessageSink, IDisposable
    {
        public Dictionary<string, List<NodeTest>> Tests { get; set; }

        public Discovery()
        {
            Tests = new Dictionary<string, List<NodeTest>>();
            Finished = new ManualResetEvent(false);
        }

        public ManualResetEvent Finished { get; private set; }

        public IMessageSink NextSink { get; private set; }

        public bool OnMessage(IMessageSinkMessage message)
        {
            var testCaseDiscoveryMessage = message as ITestCaseDiscoveryMessage;
            if (testCaseDiscoveryMessage != null)
            {
                //TODO: Improve this
                if(Regex.IsMatch(testCaseDiscoveryMessage.TestClass.Class.Name, @"\d+$"))
                {
                    var details = GetTestDetails(testCaseDiscoveryMessage);
                    List<NodeTest> tests;
                    if (Tests.TryGetValue(details.TestName, out tests))
                    {
                        tests.Add(details);
                    }
                    else
                    {
                        tests = new List<NodeTest>(new[] { details });
                    }
                    Tests[details.TestName] = tests;                    
                }
            }

            if (message is IDiscoveryCompleteMessage)
                Finished.Set();

            return true;
        }

        private NodeTest GetTestDetails(ITestCaseDiscoveryMessage nodeTest)
        {
            var matches = Regex.Match(nodeTest.TestClass.Class.Name, "(.+)([0-9]+)");

            return new NodeTest
            {
                Node = Convert.ToInt32(matches.Groups[2].Value),
                TestName = matches.Groups[1].Value,
                TypeName = nodeTest.TestClass.Class.Name,
                MethodName = nodeTest.TestCase.TestMethod.Method.Name
            };
        }

        public void Dispose()
        {
            Finished.Dispose();
        }
    }
}