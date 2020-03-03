//-----------------------------------------------------------------------
// <copyright file="TestSink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Dsl;
using Akka.TestKit;

namespace Akka.Streams.TestKit
{
    public static class TestSink
    {
        /// <summary>
        /// A Sink that materialized to a <see cref="TestSubscriber.Probe{T}"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="testKit"></param>
        /// <returns></returns>
        public static Sink<T, TestSubscriber.Probe<T>> SinkProbe<T>(this TestKitBase testKit)
        {
            return new Sink<T, TestSubscriber.Probe<T>>(new StreamTestKit.ProbeSink<T>(testKit, Attributes.None, new SinkShape<T>(new Inlet<T>("ProbeSink.in"))));
        }
    }
}
