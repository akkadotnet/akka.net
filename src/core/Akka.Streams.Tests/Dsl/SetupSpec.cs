//-----------------------------------------------------------------------
// <copyright file="SeqSinkSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class SetupSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SetupSpec(ITestOutputHelper helper)
            : base(helper) => Materializer = ActorMaterializer.Create(Sys);

        [Fact]
        public void SourceSetup_should_expose_materializer()
        {
            var source = Source.Setup((mat, _) => Source.Single(mat.IsShutdown));
            source.RunWith(Sink.First<bool>(), Materializer).Result.Should().BeFalse();
        }

        [Fact]
        public void SourceSetup_should_expose_attributes()
        {
            var source = Source.Setup((_, attr) => Source.Single(attr.AttributeList));
            source.RunWith(Sink.First<IEnumerable<Attributes.IAttribute>>(), Materializer).Result.Should().NotBeEmpty();
        }

        [Fact]
        public void SourceSetup_should_propagate_materialized_value()
        {
            var source = Source.Setup((_, _) => Source.Maybe<NotUsed>());

            var (completion, element) = source.ToMaterialized(Sink.First<NotUsed>(), Keep.Both).Run(Materializer);
            completion.Result.TrySetResult(NotUsed.Instance);
            element.Result.ShouldBe(NotUsed.Instance);
        }

        [Fact]
        public void SourceSetup_should_propagate_attributes()
        {
            var source = Source.Setup((_, attr) => Source.Single(attr.GetNameLifted)).Named("my-name");
            source.RunWith(Sink.First<Func<string>>(), Materializer).Result.Invoke().ShouldBe("setup-my-name");
        }

        [Fact]
        public void SourceSetup_should_propagate_attributes_when_nested()
        {
            var source = Source.Setup((_, _) => Source.Setup((_, attr) => Source.Single(attr.GetNameLifted))).Named("my-name");
            source.RunWith(Sink.First<Func<string>>(), Materializer).Result.Invoke().ShouldBe("setup-my-name-setup");
        }

        [Fact]
        public void SourceSetup_should_handle_factory_failure()
        {
            var error = new ApplicationException("boom");
            var source = Source.Setup<NotUsed, NotUsed>((_, _) => throw error);

            var (materialized, completion) = source.ToMaterialized(Sink.First<NotUsed>(), Keep.Both).Run(Materializer);

            Assert.Throws<AggregateException>(() => materialized.Result).InnerException?.Should().BeOfType<ApplicationException>();
            Assert.Throws<AggregateException>(() => completion.Result).InnerException?.Should().BeOfType<ApplicationException>();
        }

        [Fact]
        public void SourceSetup_should_handle_materialization_failure()
        {
            var error = new ApplicationException("boom");
            var source = Source.Setup((_, _) => Source.Empty<NotUsed>().MapMaterializedValue<NotUsed>(_ => throw error));

            var (materialized, completion) = source.ToMaterialized(Sink.First<NotUsed>(), Keep.Both).Run(Materializer);

            Assert.Throws<AggregateException>(() => materialized.Result).InnerException?.Should().BeOfType<ApplicationException>();
            Assert.Throws<AggregateException>(() => completion.Result).InnerException?.Should().BeOfType<ApplicationException>();
        }

        [Fact]
        public void FlowSetup_should_expose_materializer()
        {
            var flow = Flow.Setup((mat, _) => Flow.FromSinkAndSource(
                Sink.Ignore<object>().MapMaterializedValue(_ => NotUsed.Instance),
                Source.Single(mat.IsShutdown)));

            Source.Empty<object>().Via(flow).RunWith(Sink.First<bool>(), Materializer).Result.Should().BeFalse();
        }

        [Fact]
        public void FlowSetup_should_expose_attributes()
        {
            var flow = Flow.Setup((_, attr) => Flow.FromSinkAndSource(
                Sink.Ignore<object>().MapMaterializedValue(_ => NotUsed.Instance),
                Source.Single(attr.AttributeList)));

            Source.Empty<object>().Via(flow).RunWith(Sink.First<IEnumerable<Attributes.IAttribute>>(), Materializer).Result.Should().NotBeEmpty();
        }

        [Fact]
        public void FlowSetup_should_propagate_materialized_value()
        {
            var flow = Flow.Setup((_, _) => Flow.FromSinkAndSource(
                Sink.Ignore<object>().MapMaterializedValue(_ => NotUsed.Instance),
                Source.Maybe<NotUsed>(), Keep.Right));

            var (completion, element) = Source.Empty<object>()
                .ViaMaterialized(flow, Keep.Right)
                .ToMaterialized(Sink.First<NotUsed>(), Keep.Both).Run(Materializer);

            completion.Result.TrySetResult(NotUsed.Instance);
            element.Result.ShouldBe(NotUsed.Instance);
        }

        [Fact]
        public void FlowSetup_should_propagate_attributes()
        {
            var flow = Flow.Setup((_, attr) => Flow.FromSinkAndSource(
                Sink.Ignore<object>().MapMaterializedValue(_ => NotUsed.Instance),
                Source.Single(attr.GetNameLifted))).Named("my-name");

            Source.Empty<object>().Via(flow).RunWith(Sink.First<Func<string>>(), Materializer).Result.Invoke().ShouldBe("setup-my-name");
        }

        [Fact]
        public void FlowSetup_should_propagate_attributes_when_nested()
        {
            var flow = Flow.Setup((_, _) => Flow.Setup((_, attr) => Flow.FromSinkAndSource(
                Sink.Ignore<object>().MapMaterializedValue(_ => NotUsed.Instance),
                Source.Single(attr.GetNameLifted)))).Named("my-name");

            Source.Empty<object>().Via(flow).RunWith(Sink.First<Func<string>>(), Materializer).Result.Invoke().ShouldBe("setup-my-name-setup");
        }

        [Fact]
        public void FlowSetup_should_handle_factory_failure()
        {
            var error = new ApplicationException("boom");
            var flow = Flow.Setup<NotUsed, NotUsed, NotUsed>((_, _) => throw error);

            var (materialized, completion) = Source.Empty<NotUsed>()
                .ViaMaterialized(flow, Keep.Right)
                .ToMaterialized(Sink.First<NotUsed>(), Keep.Both)
                .Run(Materializer);

            Assert.Throws<AggregateException>(() => materialized.Result).InnerException?.Should().BeOfType<ApplicationException>();
            Assert.Throws<AggregateException>(() => completion.Result).InnerException?.Should().BeOfType<ApplicationException>();
        }

        [Fact]
        public void FlowSetup_should_handle_materialization_failure()
        {
            var error = new ApplicationException("boom");
            var flow = Flow.Setup((_, _) => Flow.Create<NotUsed>().MapMaterializedValue<NotUsed>(_ => throw error));

            var (materialized, completion) = Source.Empty<NotUsed>()
                .ViaMaterialized(flow, Keep.Right)
                .ToMaterialized(Sink.First<NotUsed>(), Keep.Both)
                .Run(Materializer);

            Assert.Throws<AggregateException>(() => materialized.Result).InnerException?.Should().BeOfType<ApplicationException>();
            Assert.Throws<AggregateException>(() => completion.Result).InnerException?.Should().BeOfType<ApplicationException>();
        }
    }
}
