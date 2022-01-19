//-----------------------------------------------------------------------
// <copyright file="MultiNodeFact.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;

namespace Akka.Remote.TestKit
{
    [Obsolete(
        "This attribute and the Akka.MultiNodeTestRunner NuGet package is being deprecated in favor of the new " +
        "Akka.MultiNode.TestAdapter NuGet package. To migrate your multi node test to the new package, please read " +
        "https://github.com/akkadotnet/akka.net/discussions/5482")]
    public class MultiNodeFactAttribute : FactAttribute
    {
        /// <summary>
        /// Set by MultiNodeTestRunner when running multi-node tests
        /// </summary>
        public const string MultiNodeTestEnvironmentName = "__AKKA_MULTI_NODE_ENVIRONMENT";

        private bool? _executedByMultiNodeRunner;

        public override string Skip
        {
            get
            {
                if (_executedByMultiNodeRunner == null)
                {
                    CommandLine.Initialize(Environment.GetCommandLineArgs());
                    var cmd = CommandLine.GetPropertyOrDefault("multinode.test-runner", null);
                    var env = Environment.GetEnvironmentVariable(MultiNodeTestEnvironmentName); 
                    _executedByMultiNodeRunner = env != null || cmd == "multinode";
                }
                
                return _executedByMultiNodeRunner != null && _executedByMultiNodeRunner.Value
                    ? base.Skip
                    : "Must be executed by multi-node test runner";
            }
            set { base.Skip = value; }
        }
    }
}

