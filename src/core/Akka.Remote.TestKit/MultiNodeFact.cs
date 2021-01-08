//-----------------------------------------------------------------------
// <copyright file="MultiNodeFact.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;

namespace Akka.Remote.TestKit
{
    public class MultiNodeFactAttribute : FactAttribute
    {
        /// <summary>
        /// Set by MultiNodeTestRunner when running multi-node tests
        /// </summary>
        public const string MultiNodeTestEnvironmentName = "__AKKA_MULTI_NODE_ENVIRONMENT";
        
        public static Lazy<bool> ExecutedByMultiNodeRunner =
            new Lazy<bool>(() =>
            {
                var args = Environment.GetCommandLineArgs();
                if (args.Length == 0) return false;
                var firstArg = args[0];
                return firstArg.Contains("Akka.MultiNodeTestRunner") 
                    || firstArg.Contains("Akka.NodeTestRunner")
                    || Environment.GetEnvironmentVariable(MultiNodeTestEnvironmentName) != null;
            });

        public override string Skip
        {
            get
            {
                return ExecutedByMultiNodeRunner.Value
                    ? base.Skip
                    : "Must be executed by multi-node test runner";
            }
            set { base.Skip = value; }
        }
    }
}

