using System;
using System.Reactive.Streams;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A <see cref="Flow{TIn,TOut,TMat}"/> is a set of stream processing steps that has one open input and one open output.
    /// </summary>
    /// <typeparam name="TIn">Type of the flow input.</typeparam>
    /// <typeparam name="TOut">Type of the flow output.</typeparam>
    /// <typeparam name="TMat">Type of value, flow graph may materialize to.</typeparam>
    public sealed class Flow<TIn, TOut, TMat> : FlowOps<TOut, TMat>, IGraph<FlowShape<TIn, TOut>, TMat>
    {
        private readonly IModule _module;

        public FlowShape<TIn, TOut> Shape { get { return (FlowShape<TIn, TOut>)_module.Shape; } }
        public IModule Module { get { return _module; } }

        public Flow(IModule module)
        {
            _module = module;
        }

        private bool IsIdentity { get { return Module is Identity<TIn, TOut, TMat>; } }

        public override Flow<TIn, T, TMat3> ViaMat<T, TMat2, TMat3>(IGraph<FlowShape<TOut, T>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            if (this.IsIdentity)
            {
                var flowInstance = (Flow<TIn, T, TMat2>)flow;
                return flowInstance.MapMaterializedValue<TMat3>(combine);
            }
            else
            {
                var flowCopy = flow.Module.CarbonCopy();
                return new Flow<TIn, T, TMat3>(Module
                    .Fuse(flowCopy, Shape.Outlets[0], flowCopy.Shape.Inlets[0], combine)
                    .ReplaceShape(new SourceShape<T>(flowCopy.Shape.Outlets[0] as Outlet<T>)));
            }
        }

        /**
         * Connect this [[Flow]] to a [[Sink]], concatenating the processing steps of both.
         * {{{
         *     +----------------------------+
         *     | Resulting Sink             |
         *     |                            |
         *     |  +------+        +------+  |
         *     |  |      |        |      |  |
         * In ~~> | flow | ~Out~> | sink |  |
         *     |  |      |        |      |  |
         *     |  +------+        +------+  |
         *     +----------------------------+
         * }}}
         * The materialized value of the combined [[Sink]] will be the materialized
         * value of the current flow (ignoring the given Sink’s value), use
         * [[Flow#toMat[Mat2* toMat]] if a different strategy is needed.
         */
        public Sink<TIn, TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink)
        {
            return ToMat(sink, Keep.Left<TMat, TMat2, TMat>);
        }

        /**
         * Connect this [[Flow]] to a [[Sink]], concatenating the processing steps of both.
         * {{{
         *     +----------------------------+
         *     | Resulting Sink             |
         *     |                            |
         *     |  +------+        +------+  |
         *     |  |      |        |      |  |
         * In ~~> | flow | ~Out~> | sink |  |
         *     |  |      |        |      |  |
         *     |  +------+        +------+  |
         *     +----------------------------+
         * }}}
         * The `combine` function is used to compose the materialized values of this flow and that
         * Sink into the materialized value of the resulting Sink.
         */
        public Sink<TIn, TMat3> ToMat<TMat2, TMat3>(IGraph<SinkShape<TOut>, TMat2> sink, Func<TMat, TMat2, TMat3> combine)
        {
            if (IsIdentity) return ((Sink<TIn, TMat2>)sink).MapMaterializedValue<TMat3>(combine);
            else
            {
                var sinkCopy = sink.Module.CarbonCopy();
                return new Sink<TIn, TMat3>(Module
                    .Fuse(sinkCopy, Shape.Outlets[0], sinkCopy.Shape.Inlets[0], combine)
                    .ReplaceShape(new SinkShape<TIn>(Shape.Inlets[0] as Inlet<TIn>)));
            }
        }

        /**
         * Transform the materialized value of this Flow, leaving all other properties as they were.
         */
        public Flow<TIn, TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> fn)
        {
            return new Flow<TIn, TOut, TMat2>(Module.TransformMaterializedValue(fn));
        }

        /**
         * Join this [[Flow]] to another [[Flow]], by cross connecting the inputs and outputs, creating a [[RunnableGraph]].
         * {{{
         * +------+        +-------+
         * |      | ~Out~> |       |
         * | this |        | other |
         * |      | <~In~  |       |
         * +------+        +-------+
         * }}}
         * The materialized value of the combined [[Flow]] will be the materialized
         * value of the current flow (ignoring the other Flow’s value), use
         * [[Flow#joinMat[Mat2* joinMat]] if a different strategy is needed.
         */
        public IRunnableGraph<TMat> Join<TMat2>(IGraph<FlowShape<TOut, TIn>, TMat2> flow)
        {
            return JoinMat<TMat2>(flow, Keep.Left<TMat, TMat2, TMat>);
        }

        /**
         * Join this [[Flow]] to another [[Flow]], by cross connecting the inputs and outputs, creating a [[RunnableGraph]]
         * {{{
         * +------+        +-------+
         * |      | ~Out~> |       |
         * | this |        | other |
         * |      | <~In~  |       |
         * +------+        +-------+
         * }}}
         * The `combine` function is used to compose the materialized values of this flow and that
         * Flow into the materialized value of the resulting Flow.
         */
        public IRunnableGraph<TMat> JoinMat<TMat2, TMat3>(IGraph<FlowShape<TOut, TIn>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            var flowCopy = flow.Module.CarbonCopy();
            return new RunnableGraph<TMat>(Module
                .Compose(flowCopy, combine)
                .Wire(Shape.Outlets[0], flowCopy.Shape.Inlets[0])
                .Wire(flowCopy.Shape.Outlets[0], Shape.Inlets[0]));
        }

        /**
         * Join this [[Flow]] to a [[BidiFlow]] to close off the “top” of the protocol stack:
         * {{{
         * +---------------------------+
         * | Resulting Flow            |
         * |                           |
         * | +------+        +------+  |
         * | |      | ~Out~> |      | ~~> O2
         * | | flow |        | bidi |  |
         * | |      | <~In~  |      | <~~ I2
         * | +------+        +------+  |
         * +---------------------------+
         * }}}
         * The materialized value of the combined [[Flow]] will be the materialized
         * value of the current flow (ignoring the [[BidiFlow]]’s value), use
         * [[Flow#joinMat[I2* joinMat]] if a different strategy is needed.
         */
        public Flow<TIn2, TOut2, TMat> Join<TIn2, TOut2, TMat2>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi)
        {
            return JoinMat(bidi, Keep.Left<TMat, TMat2, TMat>);
        }

        /**
         * Join this [[Flow]] to a [[BidiFlow]] to close off the “top” of the protocol stack:
         * {{{
         * +---------------------------+
         * | Resulting Flow            |
         * |                           |
         * | +------+        +------+  |
         * | |      | ~Out~> |      | ~~> O2
         * | | flow |        | bidi |  |
         * | |      | <~In~  |      | <~~ I2
         * | +------+        +------+  |
         * +---------------------------+
         * }}}
         * The `combine` function is used to compose the materialized values of this flow and that
         * [[BidiFlow]] into the materialized value of the resulting [[Flow]].
         */
        public Flow<TIn2, TOut2, TMat3> JoinMat<TIn2, TOut2, TMat2, TMat3>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = bidi.Module.CarbonCopy();
            var ins = copy.Shape.Inlets;
            var outs = copy.Shape.Outlets;

            return new Flow<TIn2, TOut2, TMat3>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlets[0], ins[0])
                .Wire(outs[1], Shape.Inlets[0])
                .ReplaceShape(new FlowShape<TIn2, TOut2>(ins[1] as Inlet<TIn2>, outs[0] as Outlet<TOut2>)));
        }

        /**
         * Concatenate the given [[Source]] to this [[Flow]], meaning that once this
         * Flow’s input is exhausted and all result elements have been generated,
         * the Source’s elements will be produced. Note that the Source is materialized
         * together with this Flow and just kept from producing elements by asserting
         * back-pressure until its time comes.
         *
         * The resulting Flow’s materialized value is a Tuple2 containing both materialized
         * values (of this Flow and that Source).
         */
        public Flow<TIn, TOut2, Tuple<TMat, TMat2>> Concat<TOut2, TMat2>(IGraph<SourceShape<TOut2>, TMat2> source) where TOut2 : TOut
        {
            return ConcatMat(source, Keep.Both);
        }

        /**
         * Concatenate the given [[Source]] to this [[Flow]], meaning that once this
         * Flow’s input is exhausted and all result elements have been generated,
         * the Source’s elements will be produced. Note that the Source is materialized
         * together with this Flow and just kept from producing elements by asserting
         * back-pressure until its time comes.
         */
        public Flow<TIn, TOut2, TMat3> ConcatMat<TOut2, TMat2, TMat3>(IGraph<SourceShape<TOut2>, TMat2> source, Func<TMat, TMat2, TMat3> both)
            where TOut2 : TOut
        {
            throw new NotImplementedException();
        }

        /**
         * Connect the `Source` to this `Flow` and then connect it to the `Sink` and run it. The returned tuple contains
         * the materialized values of the `Source` and `Sink`, e.g. the `Subscriber` of a of a [[Source#subscriber]] and
         * and `Publisher` of a [[Sink#publisher]].
         */
        public Tuple<TMat2, TMat3> RunWith<TMat2, TMat3>(IGraph<SourceShape<TIn>, TMat2> source, IGraph<SinkShape<TOut>, TMat3> sink, IMaterializer materializer)
        {
            return Source.Wrap(source).Via(this).ToMat(sink, Keep.Both).Run();
        }

        /**
         * Converts this Flow to a [[RunnableGraph]] that materializes to a Reactive Streams [[org.reactivestreams.Processor]]
         * which implements the operations encapsulated by this Flow. Every materialization results in a new Processor
         * instance, i.e. the returned [[RunnableGraph]] is reusable.
         *
         * @return A [[RunnableGraph]] that materializes to a Processor when run() is called on it.
         */
        public IRunnableGraph<IProcessor<TIn, TOut>> ToProcessor()
        {
            throw new NotImplementedException();
        }

        public override IFlow<T, TMat> AndThen<T>(StageModule<T, TOut, TMat> op)
        {
            throw new NotImplementedException();
        }

        public override IFlow<T, TMat> AndThenMat<T>(MaterializingStageFactory<T, TOut, TMat> op)
        {
            throw new NotImplementedException();
        }
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.WithAttributes(Attributes attributes)
        {
            return (Flow<TIn, TOut, TMat>) WithAttributes(attributes);
        }

        public IGraph<FlowShape<TIn, TOut>, TMat> Named(string name)
        {
            return (Flow<TIn, TOut, TMat>)WithAttributes(Attributes.CreateName(name));
        }

        /**
         * Change the attributes of this [[Flow]] to the given ones. Note that this
         * operation has no effect on an empty Flow (because the attributes apply
         * only to the contained processing stages).
         */
        public override IFlow<TOut, TMat> WithAttributes(Attributes attributes)
        {
            return this is EmptyModule ? this : new Flow<TIn, TOut, TMat>(Module.WithAttributes(attributes).Nest());
        }
    }

    /// <summary>
    /// A <see cref="Flow"/> is a set of stream processing steps that has one open input and one open output.
    /// </summary>
    public static class Flow
    {
        /// <summary>
        /// Creates flow from the Reactive Streams <see cref="IProcessor{T1,T2}"/>.
        /// </summary>
        public static Flow<TIn, TOut, Unit> FromProcessor<TIn, TOut>(Func<IProcessor<TIn, TOut>> factory)
        {
            return FromProcessorMaterialized(() => Tuple.Create(factory(), Unit.Instance));
        }

        /// <summary>
        /// Creates a Flow from a Reactive Streams <see cref="IProcessor{T1,T2}"/> and returns a materialized value.
        /// </summary>
        public static Flow<TIn, TOut, TMat> FromProcessorMaterialized<TIn, TOut, TMat>(Func<Tuple<IProcessor<TIn, TOut>, TMat>> factory)
        {
            throw new NotImplementedException();   
        }

        /// <summary>
        /// Helper to create a <see cref="Flow{TIn,TOut,TMat}"/> without a <see cref="Source"/> or <see cref="Sink"/>.
        /// </summary>
        public static Flow<T, T, Unit> Create<T>()
        {
            throw new NotImplementedException();
        }
    }
}