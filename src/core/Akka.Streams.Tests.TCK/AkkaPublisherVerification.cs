//-----------------------------------------------------------------------
// <copyright file="AkkaPublisherVerification.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;
using Akka.TestKit.TestEvent;
using NUnit.Framework;
using Reactive.Streams;
using Reactive.Streams.TCK;

namespace Akka.Streams.Tests.TCK
{
    [TestFixture]
    abstract class AkkaPublisherVerification<T> : PublisherVerification<T>, IDisposable
    {
        protected AkkaPublisherVerification() : this(false)
        {

        }

        protected AkkaPublisherVerification(bool writeLineDebug)
            : this(
                new TestEnvironment(Timeouts.DefaultTimeoutMillis,
                    TestEnvironment.EnvironmentDefaultNoSignalsTimeoutMilliseconds(), writeLineDebug),
                Timeouts.PublisherShutdownTimeoutMillis,
                AkkaSpec.AkkaSpecConfig.WithFallback(
                    ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf")))
        {
        }

        protected AkkaPublisherVerification(Config config)
            : this(
                new TestEnvironment(Timeouts.DefaultTimeoutMillis,
                    TestEnvironment.EnvironmentDefaultNoSignalsTimeoutMilliseconds(), false),
                Timeouts.PublisherShutdownTimeoutMillis, config)
        {
        }

        protected AkkaPublisherVerification(TestEnvironment environment, long publisherShutdownTimeoutMillis, Config config)
            : base(environment, publisherShutdownTimeoutMillis)
        {
            System = ActorSystem.Create(GetType().Name, config);
            System.EventStream.Publish(new Mute(new ErrorFilter(typeof(Exception), new ContainsString("Test exception"))));

            Materializer = ActorMaterializer.Create(System,
                ActorMaterializerSettings.Create(System).WithInputBuffer(512, 512));
        }

        protected ActorSystem System { get; private set; }

        protected ActorMaterializer Materializer { get; private set; }

        public override bool SkipStochasticTests { get; } = true;

        public void Dispose()
        {
            var shutdowned = System.Terminate().Wait(Timeouts.ShutdownTimeout);
            AfterShutdown();
            if(!shutdowned)
                throw new Exception($"Failed to stop {System.Name} within {Timeouts.ShutdownTimeout}");
        }

        protected virtual void AfterShutdown() { }

        public override IPublisher<T> CreateFailedPublisher()
            => TestPublisher.Error<T>(new Exception("Unable to serve subscribers right now!"));

        protected IEnumerable<int> Enumerate(long elements) => Enumerate(elements > int.MaxValue, elements);

        protected IEnumerable<int> Enumerate(bool useInfinite, long elements)
            => useInfinite
                ? new InfiniteEnumerable()
                : Enumerable.Range(0, (int)elements);

        private sealed class InfiniteEnumerable : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator() => new InfiniteEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class InfiniteEnumerator : IEnumerator<int>
            {
                private int _current;

                public void Dispose()
                {

                }

                public bool MoveNext()
                {
                    _current++;
                    return true;
                }

                public void Reset() => _current = 0;

                public int Current => _current;

                object IEnumerator.Current => Current;
            }
        }
    }
}
