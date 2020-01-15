//-----------------------------------------------------------------------
// <copyright file="TestSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.TestKit;

namespace Akka.Streams.TestKit
{
    public static class TestSource
    {
        /// <summary>
        /// A Source that materializes to a <see cref="TestPublisher.Probe{T}"/>.
        /// </summary>
        public static Source<T, TestPublisher.Probe<T>> SourceProbe<T>(this TestKitBase testKit)
        {
            return new Source<T, TestPublisher.Probe<T>>(new StreamTestKit.ProbeSource<T>(testKit, Attributes.None, new SourceShape<T>(new Outlet<T>("ProbeSource.out"))));
        }
    }
}
