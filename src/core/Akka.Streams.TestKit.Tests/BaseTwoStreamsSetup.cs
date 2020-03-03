//-----------------------------------------------------------------------
// <copyright file="BaseTwoStreamsSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Reactive.Streams;

namespace Akka.Streams.TestKit.Tests
{
    public abstract class BaseTwoStreamsSetup<TOutputs> : AkkaSpec
    {
        protected readonly ActorMaterializer Materializer;

        protected BaseTwoStreamsSetup(ITestOutputHelper output = null) : base(output)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(initialSize: 2, maxSize: 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        protected Exception TestException()
        {
            return new TestException("test");
        }

        protected virtual TestSubscriber.Probe<TOutputs> Setup(IPublisher<int> p1, IPublisher<int> p2)
        {
            return this.CreateSubscriberProbe<TOutputs>();
        }

        protected IPublisher<T> FailedPublisher<T>()
        {
            return TestPublisher.Error<T>(TestException());
        }

        protected IPublisher<T> CompletedPublisher<T>()
        {
            return TestPublisher.Empty<T>();
        }

        protected IPublisher<T> NonEmptyPublisher<T>(IEnumerable<T> elements)
        {
            return Source.From(elements).RunWith(Sink.AsPublisher<T>(false), Materializer);
        }

        protected IPublisher<T> SoonToFailPublisher<T>()
        {
            return TestPublisher.LazyError<T>(TestException());
        }

        protected IPublisher<T> SoonToCompletePublisher<T>()
        {
            return TestPublisher.LazyEmpty<T>();
        }

        [Fact]
        public void Should_work_with_two_immediately_completed_publishers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(CompletedPublisher<int>(), CompletedPublisher<int>());
                subscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void Should_work_with_two_delayed_completed_publishers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(SoonToCompletePublisher<int>(), SoonToCompletePublisher<int>());
                subscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void Should_work_with_one_immediately_completed_and_one_delayed_completed_publisher()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(CompletedPublisher<int>(), SoonToCompletePublisher<int>());
                subscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void Should_work_with_two_immediately_failed_publishers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(FailedPublisher<int>(), FailedPublisher<int>());
                subscriber.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }

        [Fact]
        public void Should_work_with_two_delayed_failed_publishers()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(SoonToFailPublisher<int>(), SoonToFailPublisher<int>());
                subscriber.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }

        // Warning: The two test cases below are somewhat implementation specific and might fail if the implementation
        // is changed. They are here to be an early warning though.
        [Fact]
        public void Should_work_with_one_immediately_failed_and_one_delayed_failed_publisher_case_1()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(SoonToFailPublisher<int>(), FailedPublisher<int>());
                subscriber.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }

        [Fact]
        public void Should_work_with_one_immediately_failed_and_one_delayed_failed_publisher_case_2()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = Setup(FailedPublisher<int>(), SoonToFailPublisher<int>());
                subscriber.ExpectSubscriptionAndError().Should().Be(TestException());
            }, Materializer);
        }
    }
}
