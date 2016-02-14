using System;
using System.Linq;
using System.Reactive.Streams;
using System.Security.Policy;
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
    public sealed class Flow<TIn, TOut, TMat> : IFlow<TOut, TMat>, IGraph<FlowShape<TIn, TOut>, TMat>
    {
        internal Flow(IModule module)
        {
            Module = module;
        }

        public FlowShape<TIn, TOut> Shape { get { return (FlowShape<TIn, TOut>)Module.Shape; } }
        public IModule Module { get; }

        private bool IsIdentity { get { return Module == Identity<TIn>.Instance.Module; } }

        public Flow<TIn, T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
        {
            return ViaMaterialized(flow, Keep.Left<TMat, TMat2, TMat>);
        }

        IFlow<T2, TMat> IFlow<TOut, TMat>.Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
        {
            return ViaMaterialized(flow, Keep.Left<TMat, TMat2, TMat>);
        }

        /// <summary>
        /// Change the attributes of this <see cref="Flow{TIn,TOut,TMat}"/> to the given ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        public IGraph<FlowShape<TIn, TOut>, TMat> WithAttributes(Attributes attributes)
        {
            if (Module is EmptyModule) return this;
            else return new Flow<TIn, TOut, TMat>(Module.WithAttributes(attributes).Nest());
        }

        public IGraph<FlowShape<TIn, TOut>, TMat> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }

        /// <summary>
        /// Transform the materialized value of this Flow, leaving all other properties as they were.
        /// </summary>
        public Flow<TIn, TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
        {
            return new Flow<TIn, TOut, TMat2>(Module.TransformMaterializedValue(mapFunc));
        }

        public Flow<TIn, TOut2, TMat3> ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            if (IsIdentity)
            {
                return Flow.FromGraph(flow as IGraph<FlowShape<TIn, TOut2>, TMat2>)
                    .MapMaterializedValue(mat2 => combine(default(TMat), mat2));
            }
            else
            {
                var copy = flow.Module.CarbonCopy();
                return new Flow<TIn, TOut2, TMat3>(Module
                    .Fuse(copy, Shape.Outlet, copy.Shape.Inlets.First(), combine)
                    .ReplaceShape(new FlowShape<TIn, TOut2>(Shape.Inlet, (Outlet<TOut2>)copy.Shape.Outlets.First())));
            }
        }

        IFlow<TOut2, TMat3> IFlow<TOut, TMat>.ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            return ViaMaterialized(flow, combine);
        }

        /// <summary>
        /// Connect this <see cref="Flow{TIn,TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>, concatenating the processing steps of both.
        /// The materialized value of the combined <see cref="Sink{TIn,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the given Sink’s value), use
        /// <see cref="ToMaterialized{TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        public Sink<TIn, TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink)
        {
            return ToMaterialized(sink, Keep.Left<TMat, TMat2, TMat>);
        }

        /// <summary>
        /// Connect this <see cref="Flow{TIn,TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>, concatenating the processing steps of both.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// Sink into the materialized value of the resulting Sink.
        /// </summary>
        public Sink<TIn, TMat3> ToMaterialized<TMat2, TMat3>(IGraph<SinkShape<TOut>, TMat2> sink, Func<TMat, TMat2, TMat3> combine)
        {
            if (IsIdentity)
            {
                return Sink.FromGraph(sink as IGraph<SinkShape<TIn>, TMat2>)
                    .MapMaterializedValue(mat2 => combine(default(TMat), mat2));
            }
            else
            {
                var copy = sink.Module.CarbonCopy();
                return new Sink<TIn, TMat3>(Module
                    .Fuse(copy, Shape.Outlet, copy.Shape.Inlets.First(), combine)
                    .ReplaceShape(new SinkShape<TIn>(Shape.Inlet)));
            }
        }

        /// <summary>
        /// Join this <see cref="Flow{TIn,TOut,TMat}"/> to another <see cref="Flow{TOut,TIn,TMat2}"/>, by cross connecting the inputs and outputs,
        /// creating a <see cref="IRunnableGraph{TMat}"/>.
        /// The materialized value of the combined <see cref="Flow{TIn,TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other Flow’s value), use
        /// <see cref="JoinMaterialized{TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        public IRunnableGraph<TMat> Join<TMat2>(IGraph<FlowShape<TOut, TIn>, TMat2> flow)
        {   
            return JoinMaterialized(flow, Keep.Left<TMat, TMat2, TMat>);
        }

        public Flow<TIn2, TOut2, TMat> Join<TIn2, TOut2, TMat2>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi)
        {
            return JoinMaterialized(bidi, Keep.Left<TMat, TMat2, TMat>);
        }

        public Flow<TIn2, TOut2, TMatRes> JoinMaterialized<TIn2, TOut2, TMat2, TMatRes>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi, Func<TMat, TMat2, TMatRes> combine)
        {
            var copy = bidi.Module.CarbonCopy();
            var ins = copy.Shape.Inlets.ToArray();
            var outs = copy.Shape.Outlets.ToArray();

            return new Flow<TIn2, TOut2, TMatRes>(Module.Compose(copy, combine)
                .Wire(Shape.Outlet, ins[0])
                .Wire(outs[1], Shape.Inlet)
                .ReplaceShape(new FlowShape<TIn2, TOut2>(Inlet.Create<TIn2>(ins[1]), Outlet.Create<TOut2>(outs[0]))));
        }

        /// <summary>
        /// Join this [[Flow]] to another [[Flow]], by cross connecting the inputs and outputs, creating a [[RunnableGraph]]
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// Flow into the materialized value of the resulting Flow.
        /// </summary>
        public IRunnableGraph<TMat3> JoinMaterialized<TMat2, TMat3>(IGraph<FlowShape<TOut, TIn>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = flow.Module.CarbonCopy();
            return new RunnableGraph<TMat3>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlet, copy.Shape.Inlets.First())
                .Wire(copy.Shape.Outlets.First(), Shape.Inlet));
        }

        /// <summary>
        /// Connect the <see cref="Source{TOut,TMat1}"/> to this <see cref="Flow{TIn,TOut,TMat}"/> and then connect it to the <see cref="Sink{TIn,TMat2}"/> and run it. 
        /// The returned tuple contains the materialized values of the <paramref name="source"/> and <paramref name="sink"/>, e.g. the <see cref="ISubscriber{T}"/> 
        /// of a of a <see cref="Source{TOut,TMat}.Subscriber"/> and <see cref="IPublisher{T}"/> of a <see cref="Publisher"/>.
        /// </summary>
        public Tuple<TMat1, TMat2> RunWith<TMat1, TMat2>(IGraph<SourceShape<TIn>, TMat1> source, IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            return Source.FromGraph(source).Via(this).ToMaterialized(sink, Keep.Both<TMat1, TMat2>).Run(materializer);
        }

        /// <summary>
        /// Converts this Flow to a <see cref="IRunnableGraph{TMat}"/> that materializes to a Reactive Streams <see cref="IProcessor{T1,T2}"/>
        /// which implements the operations encapsulated by this Flow. Every materialization results in a new Processor
        /// instance, i.e. the returned <see cref="IRunnableGraph{TMat}"/> is reusable.
        /// </summary>
        /// <returns>A <see cref="IRunnableGraph{TMat}"/> that materializes to a <see cref="IProcessor{T1,T2}"/> when Run() is called on it.</returns>
        public IRunnableGraph<IProcessor<TIn, TOut>> ToProcessor()
        {
            var result = Source.AsSubscriber<TIn>()
                .Via(this)
                .ToMaterialized(Sink.AsPublisher<TOut>(false), Keep.Both)
                .MapMaterializedValue(t => new FlowProcessor<TIn, TOut>(t.Item1, t.Item2) as IProcessor<TIn, TOut>);

            return result;
        }
    }

    /// <summary>
    /// A <see cref="Flow"/> is a set of stream processing steps that has one open input and one open output.
    /// </summary>
    public static class Flow
    {
        public static Flow<T, T, Unit> Identity<T>()
        {
            return new Flow<T, T, Unit>(GraphStages.Identity<T>().Module);
        }

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
            return Identity<T>();
        }

        /// <summary>
        /// A graph with the shape of a flow logically is a flow, this method makes it so also in type.
        /// </summary>
        public static Flow<TIn, TOut, TMat> FromGraph<TIn, TOut, TMat>(IGraph<FlowShape<TIn, TOut>, TMat> graph)
        {
            return graph as Flow<TIn, TOut, TMat> ?? new Flow<TIn, TOut, TMat>(graph.Module);
        }
    }

    internal sealed class FlowProcessor<TIn, TOut> : IProcessor<TIn, TOut>
    {
        private readonly ISubscriber<TIn> _subscriber;
        private readonly IPublisher<TOut> _publisher;

        public FlowProcessor(ISubscriber<TIn> subscriber, IPublisher<TOut> publisher)
        {
            _subscriber = subscriber;
            _publisher = publisher;
        }

        public void OnSubscribe(ISubscription subscription)
        {
            _subscriber.OnSubscribe(subscription);
        }

        public void OnError(Exception cause)
        {
            _subscriber.OnError(cause);
        }

        public void OnComplete()
        {
            _subscriber.OnComplete();
        }

        public void OnNext(TIn element)
        {
            _subscriber.OnNext(element);
        }

        public void Subscribe(ISubscriber<TOut> subscriber)
        {
            _publisher.Subscribe(subscriber);
        }

        void ISubscriber.OnNext(object element)
        {
            _subscriber.OnNext(element);
        }

        void IPublisher.Subscribe(ISubscriber subscriber)
        {
            _publisher.Subscribe(subscriber);
        }
    }

    /// <summary>
    /// Operations offered by Sources and Flows with a free output side: the DSL flows left-to-right only.
    /// </summary>
    public interface IFlow<T, out TMat>
    {
        /// <summary>
        /// Transform this <see cref="Flow{TIn,TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="Flow{TIn,TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other Flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        IFlow<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<T, T2>, TMat2> flow);

        #region FlowOpsMat methods

        /// <summary>
        /// Transform this <see cref="IFlow{T,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<T, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine);

        #endregion
    }
}