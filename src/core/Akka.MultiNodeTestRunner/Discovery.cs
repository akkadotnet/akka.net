//-----------------------------------------------------------------------
// <copyright file="Discovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Akka.MultiNodeTestRunner.Shared;
using Xunit;
using Xunit.Abstractions;

namespace Akka.MultiNodeTestRunner
{
    [Serializable]
    public class Discovery : TestMessageVisitor<IDiscoveryCompleteMessage>
    {
        public Dictionary<string, List<NodeTest>> TestCases { get; private set; }

        public Discovery()
        {
            TestCases = new Dictionary<string, List<NodeTest>>();
        }

        protected override bool Visit(ITestCaseDiscoveryMessage discovery)
        {
            if (!Regex.IsMatch(discovery.TestClass.Class.Name, @"\d+$")) 
                return true;

            var details = GetTestDetails(discovery);
            List<NodeTest> tests;
            if (TestCases.TryGetValue(details.TestName, out tests))
            {
                tests.Add(details);
            }
            else
            {
                tests = new List<NodeTest>(new[] { details });
            }
            TestCases[details.TestName] = tests;
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
    }
}

