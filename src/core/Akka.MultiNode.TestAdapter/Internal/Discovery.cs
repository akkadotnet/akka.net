//-----------------------------------------------------------------------
// <copyright file="Discovery.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit.Sdk;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter.Internal
{
    /// <summary>
    /// Collects <see cref="MultiNodeTestCase"/> instances during test discovery.
    /// Used as a callback target for <see cref="ITestFrameworkDiscoverer.Find"/>.
    /// </summary>
    internal class Discovery
    {
        // There can be multiple fact attributes in a single class, but our convention
        // limits them to 1 fact attribute per test class
        public List<MultiNodeTestCase> TestCases { get; } = new List<MultiNodeTestCase>();

        /// <summary>
        /// Callback for the v3 discovery API. Filters for <see cref="MultiNodeTestCase"/> and skips abstract types.
        /// </summary>
        public ValueTask<bool> OnTestCaseDiscovered(ITestCase testCase)
        {
            if (testCase is MultiNodeTestCase mnTestCase)
            {
                // Skip abstract classes
                if (!mnTestCase.TestMethod.TestClass.Class.IsAbstract)
                {
                    TestCases.Add(mnTestCase);
                }
            }

            return new ValueTask<bool>(true); // continue discovery
        }
    }
}
