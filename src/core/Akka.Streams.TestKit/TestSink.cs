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