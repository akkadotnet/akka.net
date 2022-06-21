//-----------------------------------------------------------------------
// <copyright file="LocalFactAttribute.cs" company="Akka.NET Project">
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
    /// "XUNIT_SKIP_LOCAL_FACT" exists and is set to the string "true"
    /// </para>
    /// <para>
    /// Note that the original <see cref="Skip"/> property takes precedence over this attribute,
    /// any unit tests with <see cref="LocalFactAttribute"/> with its <see cref="Skip"/> property
    /// set will always be skipped, regardless of the environment variable content.
    /// </para>
    /// </summary>
    public class LocalFactAttribute: FactAttribute
    {
        private const string EnvironmentVariableName = "XUNIT_SKIP_LOCAL_FACT";

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
        /// The reason why this unit test is being skipped by the <see cref="LocalFactAttribute"/>.
        /// Note that the original <see cref="FactAttribute.Skip"/> property takes precedence over this message. 
        /// </summary>
        public string SkipLocal { get; set; }
    }
}

