using System;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A <see cref="Sink{TIn,TMat}"/> is a set of stream processing steps that has one open input and an attached output.
    /// Can be used as a <see cref="ISubscriber{T}"/>
    /// </summary>
    public sealed class Sink<TIn, TMat> : IGraph<SinkShape<TIn>, TMat>
    {
        public Sink(IModule module)
        {
            Module = module;
        }

        public SinkShape<TIn> Shape { get { return (SinkShape<TIn>)Module.Shape; } }
        public IModule Module { get; }

        /// <summary>
        /// Connect this <see cref="Sink{TIn,TMat}"/> to a <see cref="Source{T,TMat}"/> and run it. The returned value is the materialized value
        /// of the <see cref="Source{T,TMat}"/>, e.g. the <see cref="ISubscriber{T}"/>.
        /// </summary>
        public TMat2 RunWith<TMat2>(IGraph<SourceShape<TIn>, TMat2> source, IMaterializer materializer)
        {
            return Source.FromGraph(source).To(this).Run(materializer);
        }

        public Sink<TIn, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> fn)
        {
            return new Sink<TIn, TMat2>(Module.TransformMaterializedValue(fn));
        }

        public IGraph<SinkShape<TIn>, TMat> WithAttributes(Attributes attributes)
        {
            return new Sink<TIn, TMat>(Module.WithAttributes(attributes).Nest());
        }

        public IGraph<SinkShape<TIn>, TMat> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }

    public static class Sink
    {
        private static SinkShape<T> Shape<T>(string name)
        {
            return new SinkShape<T>(new Inlet<T>(name + ".in"));
        }

        /// <summary>
        /// A graph with the shape of a sink logically is a sink, this method makes
        /// it so also in type.
        /// </summary> 
        public static Sink<TIn, TMat> Wrap<TIn, TMat>(IGraph<SinkShape<TIn>, TMat> graph)
        {
            return graph is Sink<TIn, TMat>
                ? graph as Sink<TIn, TMat>
                : new Sink<TIn, TMat>(graph.Module);
        }

        /// <summary>
        /// Helper to create <see cref="Sink{TIn, TMat}"/> from <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        public static Sink<TIn, object> Create<TIn>(ISubscriber<TIn> subscriber)
        {
            return new Sink<TIn, object>(new SubscriberSink<TIn>(subscriber, DefaultAttributes.SubscriberSink, Shape<TIn>("SubscriberSink")));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the first value received.
        /// If the stream completes before signaling at least a single element, the Task will be failed with a <see cref="NoSuchElementException"/>.
        /// If the stream signals an error errors before signaling at least a single element, the Task will be failed with the streams exception.
        /// </summary>
        public static Sink<TIn, Task<TIn>> First<TIn>()
        {
            return FromGraph(new HeadOrDefault<TIn>(throwOnDefault: true).WithAttributes(DefaultAttributes.HeadSink));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the first value received.
        /// If the stream completes before signaling at least a single element, the Task will return default value.
        /// If the stream signals an error errors before signaling at least a single element, the Task will be failed with the streams exception.
        /// </summary>
        public static Sink<TIn, Task<TIn>> FirstOrDefault<TIn>()
        {
            return FromGraph(new HeadOrDefault<TIn>().WithAttributes(DefaultAttributes.HeadOrDefaultSink));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the last value received.
        /// If the stream completes before signaling at least a single element, the Task will be failed with a <see cref="NoSuchElementException"/>.
        /// If the stream signals an error errors before signaling at least a single element, the Task will be failed with the streams exception.
        /// </summary>
        public static Sink<TIn, Task<TIn>> Last<TIn>()
        {
            return FromGraph(new LastOrDefault<TIn>(throwOnDefault: true).WithAttributes(DefaultAttributes.LastSink));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the last value received.
        /// If the stream completes before signaling at least a single element, the Task will be return a default value.
        /// If the stream signals an error errors before signaling at least a single element, the Task will be failed with the streams exception.
        /// </summary>
        public static Sink<TIn, Task<TIn>> LastOrDefault<TIn>()
        {
            return FromGraph(new LastOrDefault<TIn>().WithAttributes(DefaultAttributes.LastOrDefaultSink));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="IPublisher{TIn}"/>.
        /// that can handle one <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        public static Sink<TIn, IPublisher<TIn>> Publisher<TIn>()
        {
            return new Sink<TIn, IPublisher<TIn>>(new PublisherSink<TIn>(DefaultAttributes.PublisherSink, Shape<TIn>("PublisherSink")));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into <see cref="IPublisher{TIn}"/>
        /// that can handle more than one <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        public static Sink<TIn, IPublisher<TIn>> FanoutPublisher<TIn>(int initBufferSize, int maxBufferSize)
        {
            return new Sink<TIn, IPublisher<TIn>>(new FanoutPublisherSink<TIn>(DefaultAttributes.FanoutPublisherSink, Shape<TIn>("FanoutPublisherSink")));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that will consume the stream and discard the elements.
        /// </summary>
        public static Sink<TIn, Task> Ignore<TIn>()
        {
            return new Sink<TIn, Task>(new SinkholeSink<TIn>(Shape<TIn>("BlackholeSink"), DefaultAttributes.IgnoreSink)); ;
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that will invoke the given <paramref name="action"/> for each received element. 
        /// The sink is materialized into a <see cref="Task"/> will be completed with success when reaching the
        /// normal end of the stream, or completed with a failure if there is a failure signaled in
        /// the stream..
        /// </summary>
        public static Sink<TIn, Task> ForEach<TIn>(Action<TIn> action)
        {
            var forEach = Flow.Create<TIn>().Map(input =>
            {
                action(input);
                return Unit.Instance;
            }).ToMaterialized(Ignore<Unit>(), Keep.Right<Unit, Task, Task>).Named("foreachSink");

            return FromGraph(forEach);
        }

        /// <summary>
        /// Combine several sinks with fun-out strategy like <see cref="Broadcast{TIn}"/> or <see cref="Balance{TIn}"/> and returns <see cref="Sink{TIn,TMat}"/>.
        /// </summary>
        public static Sink<TIn, Unit> Combine<TIn, TOut>(Func<int, IGraph<UniformFanOutShape<TIn, TOut>, object>> strategy, Sink<TOut, object> first, Sink<TOut, object> second, params Sink<TOut, object>[] rest)
        {
            return FromGraph(GraphDsl.Create(builder =>
            {
                var d = builder.Add(strategy(rest.Length + 2));
                //TODO implement as far as GraphDsl is ported
                //d.Out(0) ~> first
                //d.Out(1) ~> second

                var index = 2;
                foreach (var sink in rest)
                {
                    //TODO implement as far as GraphDsl is ported
                    //d.Out(index++) ~> sink
                }

                return new SinkShape<TIn>(d.In);
            }));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that will invoke the given <paramref name="action"/> 
        /// to each of the elements as they pass in. The sink is materialized into a <see cref="Task"/>.
        /// 
        /// If the action throws an exception and the supervision decision is
        /// <see cref="Directive.Stop"/> the <see cref="Task"/> will be completed with failure.
        /// 
        /// If the action throws an exception and the supervision decision is
        /// <see cref="Directive.Resume"/> or <see cref="Directive.Restart"/> the
        /// element is dropped and the stream continues. 
        /// 
        ///  <para/>
        /// See also <seealso cref="MapAsyncUnordered{TIn,TOut}"/> 
        /// </summary>
        public static Sink<TIn, Task> ForEachParallel<TIn>(int parallelism, Action<TIn> action, MessageDispatcher dispatcher = null)
        {
            return Flow.Create<TIn>()
#pragma warning disable 1998
                .MapAsyncUnordered(parallelism, async input =>
#pragma warning restore 1998
                {
                    action(input);
                    return Unit.Instance;
                })
                .ToMaterialized(Ignore<Unit>(), Keep.Right<Unit, Task, Task>);
        }

        /// <summary>
        /// A <see cref="Sink{TIn, Task}"/> that will invoke the given <paramref name="aggregate"/> function for every received element, 
        /// giving it its previous output (or the given <paramref name="init"/> value) and the element as input.
        /// The returned <see cref="Task"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with the streams exception
        /// if there is a failure signaled in the stream.
        /// </summary>
        public static Sink<TIn, Task<TOut>> Fold<TIn, TOut>(TOut init, Func<TOut, TIn, TOut> aggregate)
        {
            var fold = Flow.Create<TIn>()
                .Fold(init, aggregate)
                .ToMaterialized(First<TOut>(), Keep.Right<Unit, Task<TOut>, Task<TOut>>)
                .Named("FoldSink");

            return FromGraph(fold);
        }

        /// <summary>
        /// A <see cref="Sink{TIn, Unit}"/> that when the flow is completed, either through a failure or normal
        /// completion, apply the provided function with <paramref name="success"/> or <paramref name="failure"/>.
        /// </summary>
        public static Sink<TIn, Unit> OnComplete<TIn>(Action success, Action<Exception> failure)
        {
            var onCompleted = Flow.Create<TIn>()
                .Transform(() => new OnCompleted<TIn, Unit>(success, failure))
                .To(Ignore<Unit>())
                .Named("OnCompleteSink");

            return FromGraph(onCompleted);
        }


        ///<summary>
        /// Sends the elements of the stream to the given <see cref="IActorRef"/>.
        /// If the target actor terminates the stream will be canceled.
        /// When the stream is completed successfully the given <paramref name="onCompleteMessage"/>
        /// will be sent to the destination actor.
        /// When the stream is completed with failure a <see cref="Status.Failure"/>
        /// message will be sent to the destination actor.
        ///
        /// It will request at most <see cref="ActorMaterializerSettings.MaxInputBufferSize"/> number of elements from
        /// upstream, but there is no back-pressure signal from the destination actor,
        /// i.e. if the actor is not consuming the messages fast enough the mailbox
        /// of the actor will grow. For potentially slow consumer actors it is recommended
        /// to use a bounded mailbox with zero <see cref="BoundedMessageQueue.PushTimeOut"/> or use a rate
        /// limiting stage in front of this <see cref="Sink{TIn, TMat}"/>.
        ///</summary>
        public static Sink<TIn, object> ActorRef<TIn>(IActorRef actorRef, object onCompleteMessage)
        {
            return new Sink<TIn, object>(new ActorRefSink<TIn>(actorRef, onCompleteMessage, DefaultAttributes.ActorRefSink, Shape<TIn>("ActorRefSink")));
        }

        ///<summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that is materialized to an <see cref="IActorRef"/> which points to an Actor
        /// created according to the passed in <see cref="Props"/>. Actor created by the <paramref name="props"/> should
        /// be <see cref="ActorSubscriberSink{TIn}"/>.
        ///</summary>
        public static Sink<TIn, IActorRef> ActorSubscriber<TIn>(Props props)
        {
            return new Sink<TIn, IActorRef>(new ActorSubscriberSink<TIn>(props, DefaultAttributes.ActorSubscriberSink, Shape<TIn>("ActorSubscriberSink")));
        }

        ///<summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that is materialized as an <see cref="ISinkQueue{TIn}"/>.
        /// <see cref="ISinkQueue{TIn}.PullAsync"/> method is pulling element from the stream and returns <see cref="Task{TIn}"/>.
        /// <see cref="Task"/> completes when element is available.
        /// 
        /// <see cref="Sink{TIn,TMat}"/> will request at most <paramref name="bufferSize"/> number of elements from
        /// upstream and then stop back pressure.
        ///</summary>
        /// <param name="bufferSize">The size of the buffer in element count</param>
        /// <param name="timeout">Timeout for <see cref="ISinkQueue{T}.PullAsync"/></param>
        public static Sink<TIn, ISinkQueue<TIn>> Queue<TIn>(int bufferSize, TimeSpan? timeout = null)
        {
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0");
            return FromGraph(new QueueSink<TIn>().WithAttributes(DefaultAttributes.QueueSink));
        }

        /// <summary>
        /// A graph with the shape of a sink logically is a sink, this method makes
        /// it so also in type.
        /// </summary>
        public static Sink<TIn, TMat> FromGraph<TIn, TMat>(IGraph<SinkShape<TIn>, TMat> graph)
        {
            return graph is Sink<TIn, TMat>
                ? graph as Sink<TIn, TMat>
                : new Sink<TIn, TMat>(graph.Module);
        }

        /// <summary>
        /// Helper to create <see cref="Sink{TIn,TMat}"/> from <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        public static Sink<TIn, Unit> FromSubscriber<TIn>(ISubscriber<TIn> subscriber)
        {
            return new Sink<TIn, Unit>(new SubscriberSink<TIn>(subscriber, DefaultAttributes.SubscriberSink, Shape<TIn>("SubscriberSink")));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that immediately cancels its upstream after materialization.
        /// </summary>
        public static Sink<TIn, Unit> Cancelled<TIn>()
        {
            return new Sink<TIn, Unit>(new CancelSink<TIn>(DefaultAttributes.CancelledSink, Shape<TIn>("CancelledSink")));
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="IPublisher{TIn}"/>.
        /// If <paramref name="fanout"/> is true, the materialized <see cref="IPublisher{TIn}"/> will support multiple <see cref="ISubscriber{TIn}"/>`s and
        /// the size of the <see cref="ActorMaterializerSettings.MaxInputBufferSize"/> configured for this stage becomes the maximum number of elements that
        /// the fastest <see cref="ISubscriber{T}"/> can be ahead of the slowest one before slowing
        /// the processing down due to back pressure.
        /// 
        /// If <paramref name="fanout"/> is false then the materialized <see cref="IPublisher{TIn}"/> will only support a single <see cref="ISubscriber{TIn}"/> and
        /// reject any additional <see cref="ISubscriber{TIn}"/>`s.
        /// </summary>
        public static Sink<TIn, IPublisher<TIn>> AsPublisher<TIn>(bool fanout)
        {
            SinkModule<TIn, IPublisher<TIn>> publisherSink;
            if (fanout)
                publisherSink = new FanoutPublisherSink<TIn>(DefaultAttributes.FanoutPublisherSink, Shape<TIn>("FanoutPublisherSink"));
            else
                publisherSink = new PublisherSink<TIn>(DefaultAttributes.PublisherSink, Shape<TIn>("PublisherSink"));

            return new Sink<TIn, IPublisher<TIn>>(publisherSink);
        }
    }
}