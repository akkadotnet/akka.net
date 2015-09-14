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