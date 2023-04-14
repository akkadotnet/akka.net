//-----------------------------------------------------------------------
// <copyright file="WindowsFactAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;

namespace Akka.TestKit.Xunit2.Attributes
{
    /// <summary>
    /// <para>
    /// This custom XUnit Fact attribute will skip unit tests if the run-time environment is not windows
    /// </para>
    /// <para>
    /// Note that the original <see cref="Skip"/> property takes precedence over this attribute,
    /// any unit tests with <see cref="WindowsFactAttribute"/> with its <see cref="Skip"/> property
    /// set will always be skipped, regardless of the environment variable content.
    /// </para>
    /// </summary>
    public class WindowsFactAttribute : FactAttribute
    {
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
                
                var platform = Environment.OSVersion.Platform;
                var notWindows = platform == PlatformID.MacOSX 
                                 || platform == PlatformID.Unix 
                                 || platform == PlatformID.Xbox;
                return notWindows ? SkipUnix ?? "Skipped under Unix platforms" : null;
            }
            set => _skip = value;
        }

        /// <summary>
        /// The reason why this unit test is being skipped by the <see cref="WindowsFactAttribute"/>.
        /// Note that the original <see cref="Skip"/> property takes precedence over this message. 
        /// </summary>
        public string SkipUnix { get; set; }
    }
}

