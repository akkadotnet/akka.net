//-----------------------------------------------------------------------
// <copyright file="TestKitAssertionsProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="assertions">TBD</param>
        public TestKitAssertionsProvider(ITestKitAssertions assertions)
        {
            _assertions = assertions;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public ITestKitAssertions Assertions { get { return _assertions; } }
    }
}
