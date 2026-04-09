// -----------------------------------------------------------------------
// <copyright file="MultiNodeFactAttribute.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using Xunit.v3;

namespace Akka.MultiNode.TestAdapter
{
    [XunitTestCaseDiscoverer(typeof(MultiNodeFactDiscoverer))]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MultiNodeFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1) : Attribute, IFactAttribute
    {
        /// <inheritdoc />
        public string? DisplayName { get; set; }

        /// <inheritdoc />
        public bool Explicit { get; set; }

        /// <inheritdoc />
        public string? Skip { get; set; }

        /// <inheritdoc />
        public Type[]? SkipExceptions { get; set; }

        /// <inheritdoc />
        public Type? SkipType { get; set; }

        /// <inheritdoc />
        public string? SkipUnless { get; set; }

        /// <inheritdoc />
        public string? SkipWhen { get; set; }

        /// <inheritdoc />
        public string? SourceFilePath { get; } = sourceFilePath;

        /// <inheritdoc />
        public int? SourceLineNumber { get; } = sourceLineNumber < 1 ? null : sourceLineNumber;

        /// <inheritdoc />
        public int Timeout { get; set; }
    }
}
