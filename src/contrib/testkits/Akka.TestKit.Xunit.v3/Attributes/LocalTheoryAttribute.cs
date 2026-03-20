//-----------------------------------------------------------------------
// <copyright file="LocalTheoryAttribute.cs" company="Akka.NET Project">
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
    /// This custom XUnit Fact attribute will skip unit tests if the environment variable
    /// "XUNIT_SKIP_LOCAL_THEORY" exists and is set to the string "true"
    /// </para>
    /// <para>
    /// Note that the original <see cref="IFactAttribute.Skip"/> property takes precedence over this attribute,
    /// any unit tests with <see cref="LocalTheoryAttribute"/> with its <see cref="IFactAttribute.Skip"/> property
    /// set will always be skipped, regardless of the environment variable content.
    /// </para>
    /// </summary>
    [XunitTestCaseDiscoverer(typeof(TheoryDiscoverer))]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class LocalTheoryAttribute : LocalFactAttribute, ITheoryAttribute
    {
        /// <inheritdoc />
        public bool DisableDiscoveryEnumeration { get; set; }

        /// <inheritdoc />
        public bool SkipTestWithoutData { get; set; }
    }
}
