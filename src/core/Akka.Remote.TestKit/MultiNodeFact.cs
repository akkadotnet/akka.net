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
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MultiNodeFactAttribute : Attribute
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

        private string _skip;
        /// <summary>
        /// Marks the test so that it will not be run, and gets or sets the skip reason
        /// </summary>
        public virtual string Skip
        {
            get
            {
                return ExecutedByMultiNodeRunner.Value
                    ? _skip
                    : "Must be executed by multi-node test runner";
            }
            set { _skip = value; }
        }
        
        /// <summary>
        /// Gets the name of the test to be used when the test is skipped. Defaults to
        /// null, which will cause the fully qualified test name to be used.
        /// </summary>
        public virtual string DisplayName { get; set; }
    }
}

