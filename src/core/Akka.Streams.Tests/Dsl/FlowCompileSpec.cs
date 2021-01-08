//-----------------------------------------------------------------------
// <copyright file="FlowCompileSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.CSharp.RuntimeBinder;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowCompileSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowCompileSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private Source<int, NotUsed> IntSeq => Source.From(new [] {1,2,3});
        private Source<string, NotUsed> StringSeq => Source.From(new[] { "a", "b", "c" });

        [Fact]
        public void Flow_should_not_run()
        {
            dynamic open = Flow.Create<int>();
            Action compiler = () => open.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();
        }

        [Fact]
        public void Flow_should_accept_Enumerable() => IntSeq.Via(Flow.Create<int>());

        [Fact]
        public void Flow_should_accept_Future() => Source.FromTask(Task.FromResult(3)).Via(Flow.Create<int>());

        [Fact]
        public void Flow_should_append_Flow()
        {
            var open1 = Flow.Create<int>().Select(x => x.ToString());
            var open2 = Flow.Create<string>().Select(x => x.GetHashCode());
            dynamic open3 = open1.Via(open2);
            Action compiler = () => open3.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();

            dynamic closedSource = IntSeq.Via(open3);
            compiler = () => closedSource.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();

            dynamic closedSink = open3.To(Sink.AsPublisher<int>(false));
            compiler = () => closedSink.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();

            closedSource.To(Sink.AsPublisher<int>(false)).Run(Materializer);
            IntSeq.To(closedSink).Run(Materializer);
        }

        [Fact]
        public void Flow_should_append_Sink()
        {
            var open = Flow.Create<int>().Select(x => x.ToString());
            var closedSink = Flow.Create<string>().Select(x => x.GetHashCode()).To(Sink.AsPublisher<int>(false));
            dynamic appended = open.To(closedSink);
            Action compiler = () => appended.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();
            compiler = () => appended.To(Sink.First<int>());
            compiler.ShouldThrow<RuntimeBinderException>();
            IntSeq.To(appended).Run(Materializer);
        }

        [Fact]
        public void Flow_should_append_Source()
        {
            var open = Flow.Create<int>().Select(x => x.ToString());
            var closedSource = StringSeq.Via(Flow.Create<string>().Select(x => x.GetHashCode()));
            dynamic closedSource2 = closedSource.Via(open);
            Action compiler = () => closedSource2.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();
            compiler = () => StringSeq.To(closedSource2);
            compiler.ShouldThrow<RuntimeBinderException>();
            closedSource2.To(Sink.AsPublisher<string>(false)).Run(Materializer);
        }


        private Sink<int, NotUsed> OpenSink
            => Flow.Create<int>().Select(x => x.ToString()).To(Sink.AsPublisher<string>(false));

        [Fact]
        public void Sink_should_accept_Source() => IntSeq.To(OpenSink);

        [Fact]
        public void Sink_should_not_accept_Sink()
        {
            dynamic openSink = OpenSink;
            Action compiler = () => openSink.To(Sink.First<string>());
            compiler.ShouldThrow<RuntimeBinderException>();
        }

        [Fact]
        public void Sink_should_not_run()
        {
            dynamic openSink = OpenSink;
            Action compiler = () => openSink.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();
        }


        private Source<string, NotUsed> OpenSource => Source.From(new[] {1, 2, 3}).Select(x => x.ToString());

        [Fact]
        public void Source_should_accept_Sink() => OpenSource.To(Sink.AsPublisher<string>(false));

        [Fact]
        public void Source_should_not_be_accepted_by_Source()
        {
            dynamic openSource = OpenSource;
            Action compiler = () => openSource.To(IntSeq);
            compiler.ShouldThrow<RuntimeBinderException>();
        }

        [Fact]
        public void Source_should_not_run()
        {
            dynamic openSource = OpenSource;
            Action compiler = () => openSource.Run(Materializer);
            compiler.ShouldThrow<RuntimeBinderException>();
        }


        private IRunnableGraph<IPublisher<string>> Closed
            =>
                Source.From(new[] {1, 2, 3})
                    .Select(x => x.ToString())
                    .ToMaterialized(Sink.AsPublisher<string>(false), Keep.Right);

        [Fact]
        public void RunnableGraph_should_run() => Closed.Run(Materializer);

        [Fact]
        public void RunnableGraph_should_not_be_accepted_by_Source()
        {
            dynamic intSeq = IntSeq;
            Action compiler = () => intSeq.To(Closed);
            compiler.ShouldThrow<RuntimeBinderException>();
        }

        [Fact]
        public void RunnableGraph_should_not_accepted_Sink()
        {
            dynamic closed = Closed;
            Action compiler = () => closed.To(Sink.First<string>());
            compiler.ShouldThrow<RuntimeBinderException>();
        }
    }
}
