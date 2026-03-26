//-----------------------------------------------------------------------
// <copyright file="NodeTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Xunit.Abstractions;
using Xunit.Sdk;
using LongLivedMarshalByRefObject = Xunit.LongLivedMarshalByRefObject;

namespace Akka.MultiNode.TestAdapter.Internal
{
    public class NodeTest : LongLivedMarshalByRefObject, ITest
    {
        public NodeTest(MultiNodeTestCase testCase, int node, string role)
        {
            TestCase = testCase;
            Node = node;
            Role = role;
        }

        public int Node { get; }
        public string Role { get; }
        public virtual string DisplayName => $"Node {Node} [{Role}]";
        public MultiNodeTestCase TestCase { get; }
        ITestCase ITest.TestCase => TestCase;
        public string Name => $"{TestCase.DisplayName}_node{Node}[{Role}]";
    }

    internal class ErrorTest : NodeTest
    {
        public ErrorTest(MultiNodeTestCase testCase) : base(testCase, 0, "")
        {
        }

        public override string DisplayName => "ERRORED";
    }
}

