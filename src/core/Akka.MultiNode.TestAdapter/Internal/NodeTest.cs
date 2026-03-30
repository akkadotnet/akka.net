//-----------------------------------------------------------------------
// <copyright file="NodeTest.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit.Sdk;

namespace Akka.MultiNode.TestAdapter.Internal
{
    public class NodeTest
    {
        public NodeTest(MultiNodeTestCase testCase, int node, string role)
        {
            TestCase = testCase;
            Node = node;
            Role = role;

            try
            {
                UniqueID = UniqueIDGenerator.ForTest(testCase.UniqueID, node - 1);
            }
            catch
            {
                // MockMultiNodeTestCase in tests may not have a UniqueID
                UniqueID = Guid.NewGuid().ToString("N");
            }
        }

        public int Node { get; }
        public string Role { get; }
        public virtual string DisplayName => $"Node {Node} [{Role}]";
        public MultiNodeTestCase TestCase { get; }
        public string UniqueID { get; }

        public string Name
        {
            get
            {
                try
                {
                    return $"{TestCase.TestCaseDisplayName}_node{Node}[{Role}]";
                }
                catch
                {
                    return $"node{Node}[{Role}]";
                }
            }
        }
    }

    internal class ErrorTest : NodeTest
    {
        public ErrorTest(MultiNodeTestCase testCase) : base(testCase, 0, "")
        {
        }

        public override string DisplayName => "ERRORED";
    }
}
