using System;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /**
     * A `Source` is a set of stream processing steps that has one open output. It can comprise
     * any number of internal sources and transformations that are wired together, or it can be
     * an “atomic” source, e.g. from a collection or a file. Materialization turns a Source into
     * a Reactive Streams `Publisher` (at least conceptually).
     */
    public sealed class Source<TOut, TMat> : FlowOps<TOut, TMat>, IGraph<SourceShape<TOut>, TMat>
    {
        private readonly IModule _module;

        public Source(IModule module)
        {
            _module = module;
        }

        public SourceShape<TOut> Shape { get { return (SourceShape<TOut>) _module.Shape; } }
        public IModule Module { get { return _module; } }

        /**
         * Connect this [[akka.stream.scaladsl.Source]] to a [[akka.stream.scaladsl.Sink]],
         * concatenating the processing steps of both.
         */
        public IRunnableGraph<TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> graph)
        {
            return ToMat(graph, Keep.Left<TMat, TMat2, TMat>);
        }

        /**
         * Connect this [[akka.stream.scaladsl.Source]] to a [[akka.stream.scaladsl.Sink]],
         * concatenating the processing steps of both.
         */
        private IRunnableGraph<TMat> ToMat<TMat2, TMat3>(IGraph<SinkShape<TOut>, TMat2> sink, Func<TMat, TMat2, TMat3> combine)
        {
            var sinkCopy = sink.Module.CarbonCopy();
            throw new NotImplementedException();
        }

        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.WithAttributes(Attributes attributes)
        {
            return (Source<TOut, TMat>)WithAttributes(attributes);
        }

        public IGraph<SourceShape<TOut>, TMat> Named(string name)
        {
            return (Source<TOut, TMat>)WithAttributes(Attributes.CreateName(name));
        }

        /**
         * Nests the current Source and returns a Source with the given Attributes
         * @param attr the attributes to add
         * @return a new Source with the added attributes
         */
        public override IFlow<TOut, TMat> WithAttributes(Attributes attributes)
        {
            return new Source<TOut, TMat>(Module.WithAttributes(attributes).Nest());
        }

        public override IFlow<T, TMat3> ViaMat<T, TMat2, TMat3>(IGraph<FlowShape<TOut, T>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            if (flow.Module is Identity<TOut, T, TMat>) return (Source<T, TMat3>)this;
            else
            {
                var flowCopy = flow.Module.CarbonCopy();
                return new Source<T, TMat3>(Module
                    .Fuse(flowCopy, Shape.Outlets[0], flowCopy.Shape.Inlets[0], combine)
                    .ReplaceShape(new SourceShape<T>(flowCopy.Shape.Outlets[0] as Outlet<T>)));
            }
        }

        protected override IFlow<T, TMat> AndThen<T>(StageModule<T, TOut, TMat> op)
        {
            // No need to copy here, op is a fresh instance
            return new Source<T, TMat>(Module
                .Fuse(op, Shape.Outlets[0], op.InPorts.First())
                .ReplaceShape(new SourceShape<T>(op.OutPorts.First() as Outlet<T>)));
        }

        protected override IFlow<T, TMat> AndThenMat<T>(MaterializingStageFactory<T, TOut, TMat> op)
        {
            return new Source<T, TMat>(Module
                .Fuse<T, T, T>(op, Shape.Outlets[0], op.InPorts.First(), Keep.Right<T,T,T>)
                .ReplaceShape(new SourceShape<T>(op.OutPorts.First() as Outlet<T>)));
        }

        public TMat RunWith<T>(Sink<T, TMat> localSink, IMaterializer subFusingMaterializer)
        {
            throw new NotImplementedException();
        }
    }

    public static class Source
    {
        private static readonly Func<object, object> _id = o => o;

        private static TOut Id<TIn, TOut>(TIn i)
        {
            return (TOut) _id(i);
        }

        private static Source<TOut, TMat> Create<TOut, TMat>(SourceModule<TOut, TMat> module)
        {
            return new Source<TOut, TMat>(module);
        }

        private static SourceShape<T> Shape<T>(string name)
        {
            return new SourceShape<T>(new Outlet<T>(name + ".out"));
        }

        /**
         * Helper to create [[Source]] from `Publisher`.
         *
         * Construct a transformation starting with given publisher. The transformation steps
         * are executed by a series of [[org.reactivestreams.Processor]] instances
         * that mediate the flow of elements downstream and the propagation of
         * back-pressure upstream.
         */
        public static Source<TOut, object> Create<TOut>(IPublisher<TOut> publisher)
        {
            return new Source<TOut, object>(new PublisherSource<TOut>(publisher, DefaultAttributes.PublisherSource, Shape<TOut>("PublisherSource")));
        }

        public static Source<T, TMat> FromGraph<T, TMat>(IGraph<SourceShape<T>, TMat> source)
        {
            throw new NotImplementedException();
        }

        public static Source<T, TMat> Empty<T, TMat>()
        {
            throw new NotImplementedException();
        }
    }
}