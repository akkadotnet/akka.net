//-----------------------------------------------------------------------
// <copyright file="WindowsFactAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit.v3;

namespace Akka.TestKit.Xunit.Attributes
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
    public class WindowsFactAttribute : Attribute, IFactAttribute
    {
        private string? _skip;


        /// <inheritdoc />
        public string? DisplayName { get; set; }

        /// <inheritdoc/>
        public bool Explicit { get; set; }

        /// <inheritdoc/>
        public Type[]? SkipExceptions { get; set; }

        /// <inheritdoc/>
        public Type? SkipType { get; set; }

        /// <inheritdoc/>
        public string? SkipUnless { get; set; }

        /// <inheritdoc/>
        public string? SkipWhen { get; set; }

        /// <inheritdoc/>
        public int Timeout { get; set; }
    
        /// <inheritdoc/>
        public string? Skip
        {
            get
            {
                if (_skip != null)
                    return _skip;
                
                var platform = Environment.OSVersion.Platform;
                var notWindows = platform is PlatformID.MacOSX or PlatformID.Unix or PlatformID.Xbox;
                return notWindows ? SkipUnix ?? "Skipped under Unix platforms" : null;
            }
            set => _skip = value;
        }

        /// <summary>
        /// The reason why this unit test is being skipped by the <see cref="WindowsFactAttribute"/>.
        /// Note that the original <see cref="Skip"/> property takes precedence over this message. 
        /// </summary>
        public string? SkipUnix { get; set; }
    }
}

