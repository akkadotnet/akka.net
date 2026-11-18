//-----------------------------------------------------------------------
// <copyright file="TestKitAssertionsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Contains <see cref="ITestKitAssertions"/>.
    /// </summary>
    public class TestKitAssertionsProvider : IExtension
    {
        private readonly ITestKitAssertions _assertions;

        /// <summary>
        /// Creates a provider for the specified test assertion implementation.
        /// </summary>
        /// <param name="assertions">The assertion implementation supplied by the test framework.</param>
        public TestKitAssertionsProvider(ITestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        /// <summary>
        /// Gets the test framework assertion implementation.
        /// </summary>
        public ITestKitAssertions Assertions { get { return _assertions; } }
    }
}
