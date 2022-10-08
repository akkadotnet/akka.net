﻿//-----------------------------------------------------------------------
// <copyright file="LocalTheoryAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;
using Xunit.Sdk;

namespace Akka.TestKit.Xunit2.Attributes
{
    /// <summary>
    /// <para>
    /// This custom XUnit Fact attribute will skip unit tests if the environment variable
    /// "XUNIT_SKIP_LOCAL_THEORY" exists and is set to the string "true"
    /// </para>
    /// <para>
    /// Note that the original <see cref="Skip"/> property takes precedence over this attribute,
    /// any unit tests with <see cref="LocalTheoryAttribute"/> with its <see cref="Skip"/> property
    /// set will always be skipped, regardless of the environment variable content.
    /// </para>
    /// </summary>
    [XunitTestCaseDiscoverer("Xunit.Sdk.TheoryDiscoverer", "xunit.execution.{Platform}")]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class LocalTheoryAttribute : TheoryAttribute
    {
        private const string EnvironmentVariableName = "XUNIT_SKIP_LOCAL_THEORY";

        private string _skip;

        /// <summary>
        /// Marks the test so that it will not be run, and gets or sets the skip reason
        /// </summary>
        public override string Skip
        {
            get
            {
                if (_skip != null)
                    return _skip;
                
                var skipLocal = Environment.GetEnvironmentVariable(EnvironmentVariableName)?
                    .ToLowerInvariant();
                return skipLocal is "true" ? SkipLocal ?? "Local facts are being skipped" : null;
            }
            set => _skip = value;
        }
        
        /// <summary>
        /// The reason why this unit test is being skipped by the <see cref="LocalTheoryAttribute"/>.
        /// Note that the original <see cref="Skip"/> property takes precedence over this message. 
        /// </summary>
        public string SkipLocal { get; set; }

    }
}
