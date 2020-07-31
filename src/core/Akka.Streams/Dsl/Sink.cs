//-----------------------------------------------------------------------
// <copyright file="Sink.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.MessageQueues;
using Akka.Pattern;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Reactive.Streams;

// ReSharper disable UnusedMember.Global

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A <see cref="Sink{TIn,TMat}"/> is a set of stream processing steps that has one open input.
    /// Can be used as a <see cref="ISubscriber{T}"/>
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    public sealed class Sink<TIn, TMat> : IGraph<SinkShape<TIn>, TMat>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        public Sink(IModule module)
        {
            Module = module;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public SinkShape<TIn> Shape => (SinkShape<TIn>)Module.Shape;

        /// <summary>
        /// TBD
        /// </summary>
        public IModule Module { get; }

        /// <summary>
        /// Transform this <see cref="Sink"/> by applying a function to each *incoming* upstream element before
        /// it is passed to the <see cref="Sink"/>
        /// 
        /// Backpressures when original <see cref="Sink"/> backpressures
        /// 
        /// Cancels when original <see cref="Sink"/> backpressures
        /// </summary>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <param name="function">TBD</param>
        /// <returns>TBD</returns>
        public Sink<TIn2, TMat> ContraMap<TIn2>(Func<TIn2, TIn> function)
            => Flow.FromFunction(function).ToMaterialized(this, Keep.Right);

        /// <summary>
        /// Connect this <see cref="Sink{TIn,TMat}"/> to a <see cref="Source{T,TMat}"/> and run it. The returned value is the materialized value
        /// of the <see cref="Source{T,TMat}"/>, e.g. the <see cref="ISubscriber{T}"/>.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="source">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public TMat2 RunWith<TMat2>(IGraph<SourceShape<TIn>, TMat2> source, IMaterializer materializer)
            => Source.FromGraph(source).To(this).Run(materializer);

        /// <summary>
        /// Transform only the materialized value of this Sink, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="fn">TBD</param>
        /// <returns>TBD</returns>
        public Sink<TIn, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> fn)
            => new Sink<TIn, TMat2>(Module.TransformMaterializedValue(fn));

        /// <summary>
        /// Materializes this Sink immediately.
        /// 
        /// Useful for when you need a materialized value of a Sink when handing it out to someone to materialize it for you.
        /// </summary>
        /// <param name="materializer">The materializer.</param>
        /// <returns>A tuple containing the (1) materialized value and (2) a new <see cref="Sink"/>
        ///  that can be used to consume elements from the newly materialized <see cref="Sink"/>.</returns>
        public (TMat, Sink<TIn, NotUsed>) PreMaterialize(IMaterializer materializer)
        {
            var sub = Source.AsSubscriber<TIn>().ToMaterialized(this, Keep.Both).Run(materializer);
            return (sub.Item2, Sink.FromSubscriber(sub.Item1));
        }

        /// <summary>
        /// Change the attributes of this <see cref="IGraph{TShape}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<SinkShape<TIn>, TMat> IGraph<SinkShape<TIn>, TMat>.WithAttributes(Attributes attributes)
            => WithAttributes(attributes);

        /// <summary>
        /// Change the attributes of this <see cref="Sink{TIn,TMat}"/> to the given ones
        /// and seal the list of attributes. This means that further calls will not be able
        /// to remove these attributes, but instead add new ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public Sink<TIn, TMat> WithAttributes(Attributes attributes)
            => new Sink<TIn, TMat>(Module.WithAttributes(attributes));

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<SinkShape<TIn>, TMat> IGraph<SinkShape<TIn>, TMat>.AddAttributes(Attributes attributes)
            => AddAttributes(attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="Sink{TIn,TMat}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public Sink<TIn, TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// Add a name attribute to this Sink.
        /// </summary>
        IGraph<SinkShape<TIn>, TMat> IGraph<SinkShape<TIn>, TMat>.Named(string name) => Named(name);

        /// <summary>
        /// Add a name attribute to this Sink.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public Sink<TIn, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// Put an asynchronous boundary around this Sink.
        /// </summary>
        IGraph<SinkShape<TIn>, TMat> IGraph<SinkShape<TIn>, TMat>.Async() => Async();

        /// <summary>
        /// Put an asynchronous boundary around this Sink.
        /// </summary>
        /// <returns>TBD</returns>
        public Sink<TIn, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Sink({Shape}, {Module})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Sink
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static SinkShape<T> Shape<T>(string name) => new SinkShape<T>(new Inlet<T>(name + ".in"));

        /// <summary>
        /// A graph with the shape of a sink logically is a sink, this method makes
        /// it so also in type.
        /// </summary> 
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, TMat> Wrap<TIn, TMat>(IGraph<SinkShape<TIn>, TMat> graph)
            => graph is Sink<TIn, TMat>
                ? (Sink<TIn, TMat>)graph
                : new Sink<TIn, TMat>(graph.Module);

        /// <summary>
        /// Helper to create <see cref="Sink{TIn, TMat}"/> from <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="subscriber">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, object> Create<TIn>(ISubscriber<TIn> subscriber)
            => new Sink<TIn, object>(new SubscriberSink<TIn>(subscriber, DefaultAttributes.SubscriberSink, Shape<TIn>("SubscriberSink")));

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the first value received.
        /// If the stream completes before signaling at least a single element, the Task will be failed with a <see cref="NoSuchElementException"/>.
        /// If the stream signals an error before signaling at least a single element, the Task will be failed with the streams exception.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <exception cref="InvalidOperationException">TBD</exception>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TIn>> First<TIn>()
            => FromGraph(new FirstOrDefault<TIn>(throwOnDefault: true))
                .WithAttributes(DefaultAttributes.FirstOrDefaultSink)
                .MapMaterializedValue(e =>
                {
                    if (!e.IsFaulted && e.IsCompleted && e.Result == null)
                        throw new InvalidOperationException("Sink.First materialized on an empty stream");

                    return e;
                });

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the first value received.
        /// If the stream completes before signaling at least a single element, the Task will return default value.
        /// If the stream signals an error errors before signaling at least a single element, the Task will be failed with the streams exception.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TIn>> FirstOrDefault<TIn>()
            => FromGraph(new FirstOrDefault<TIn>()).WithAttributes(DefaultAttributes.FirstOrDefaultSink);

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the last value received.
        /// If the stream completes before signaling at least a single element, the Task will be failed with a <see cref="NoSuchElementException"/>.
        /// If the stream signals an error, the Task will be failed with the stream's exception.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TIn>> Last<TIn>()
            => FromGraph(new LastOrDefault<TIn>(throwOnDefault: true)).WithAttributes(DefaultAttributes.LastOrDefaultSink);


        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="Task{TIn}"/> of the last value received.
        /// If the stream completes before signaling at least a single element, the Task will be return a default value.
        /// If the stream signals an error, the Task will be failed with the stream's exception.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TIn>> LastOrDefault<TIn>()
            => FromGraph(new LastOrDefault<TIn>()).WithAttributes(DefaultAttributes.LastOrDefaultSink);

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that keeps on collecting incoming elements until upstream terminates.
        /// As upstream may be unbounded, `Flow.Create{T}().Take` or the stricter `Flow.Create{T}().Limit` (and their variants)
        /// may be used to ensure boundedness.
        /// Materializes into a <see cref="Task"/> of <see cref="Seq{TIn}"/> containing all the collected elements.
        /// `Seq` is limited to <see cref="int.MaxValue"/> elements, this Sink will cancel the stream
        /// after having received that many elements.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<IImmutableList<TIn>>> Seq<TIn>() => FromGraph(new SeqStage<TIn>());

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="IPublisher{TIn}"/>.
        /// that can handle one <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, IPublisher<TIn>> Publisher<TIn>()
            => new Sink<TIn, IPublisher<TIn>>(new PublisherSink<TIn>(DefaultAttributes.PublisherSink, Shape<TIn>("PublisherSink")));

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into <see cref="IPublisher{TIn}"/>
        /// that can handle more than one <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, IPublisher<TIn>> FanoutPublisher<TIn>()
            => new Sink<TIn, IPublisher<TIn>>(new FanoutPublisherSink<TIn, ResizableMultiReaderRingBuffer<TIn>>(DefaultAttributes.FanoutPublisherSink, Shape<TIn>("FanoutPublisherSink")));

        internal static Sink<TIn, IPublisher<TIn>> DistinctRetainingFanOutPublisher<TIn>(Action onTerminated = null)
            => new Sink<TIn, IPublisher<TIn>>(new FanoutPublisherSink<TIn, DistinctRetainingMultiReaderBuffer<TIn>>(DefaultAttributes.FanoutPublisherSink, Shape<TIn>("DistinctRetainingFanOutPublisherSink"), onTerminated));

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that will consume the stream and discard the elements.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task> Ignore<TIn>() => FromGraph(new IgnoreSink<TIn>());

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that will invoke the given <paramref name="action"/> for each received element. 
        /// The sink is materialized into a <see cref="Task"/> will be completed with success when reaching the
        /// normal end of the stream, or completed with a failure if there is a failure signaled in
        /// the stream..
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task> ForEach<TIn>(Action<TIn> action) => Flow.Create<TIn>()
            .Select(input =>
            {
                action(input);
                return NotUsed.Instance;
            }).ToMaterialized(Ignore<NotUsed>(), Keep.Right).Named("foreachSink");

        /// <summary>
        /// Combine several sinks with fan-out strategy like <see cref="Broadcast{TIn}"/> or <see cref="Balance{TIn}"/> and returns <see cref="Sink{TIn,TMat}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="strategy">TBD</param>
        /// <param name="first">TBD</param>
        /// <param name="second">TBD</param>
        /// <param name="rest">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, NotUsed> Combine<TIn, TOut, TMat>(Func<int, IGraph<UniformFanOutShape<TIn, TOut>, TMat>> strategy, Sink<TOut, NotUsed> first, Sink<TOut, NotUsed> second, params Sink<TOut, NotUsed>[] rest)
            => FromGraph(GraphDsl.Create(builder =>
            {
                var d = builder.Add(strategy(rest.Length + 2));

                builder.From(d.Out(0)).To(first);
                builder.From(d.Out(1)).To(second);

                var index = 2;
                foreach (var sink in rest)
                    builder.From(d.Out(index++)).To(sink);

                return new SinkShape<TIn>(d.In);
            }));

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
        /// See also <seealso cref="SelectAsyncUnordered{TIn,TOut}"/> 
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="parallelism">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task> ForEachParallel<TIn>(int parallelism, Action<TIn> action) => Flow.Create<TIn>()
            .SelectAsyncUnordered(parallelism, input => Task.Run(() =>
            {
                action(input);
                return NotUsed.Instance;
            })).ToMaterialized(Ignore<NotUsed>(), Keep.Right);

        /// <summary>
        /// A <see cref="Sink{TIn, Task}"/> that will invoke the given <paramref name="aggregate"/> function for every received element, 
        /// giving it its previous output (or the given <paramref name="zero"/> value) and the element as input.
        /// The returned <see cref="Task"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with the streams exception
        /// if there is a failure signaled in the stream.
        /// <seealso cref="AggregateAsync{TIn,TOut}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TOut>> Aggregate<TIn, TOut>(TOut zero, Func<TOut, TIn, TOut> aggregate)
            => Flow.Create<TIn>()
                .Aggregate(zero, aggregate)
                .ToMaterialized(First<TOut>(), Keep.Right)
                .Named("AggregateSink");

        /// <summary>
        /// A <see cref="Sink{TIn, Task}"/> that will invoke the given asynchronous function for every received element,
        /// giving it its previous output (or the given <paramref name="zero"/> value) and the element as input.
        /// The returned <see cref="Task"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with "Failure"
        /// if there is a failure signaled in the stream.
        /// 
        /// <seealso cref="Aggregate{TIn,TOut}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TOut>> AggregateAsync<TIn, TOut>(TOut zero, Func<TOut, TIn, Task<TOut>> aggregate)
            => Flow.Create<TIn, Task<TOut>>()
                .AggregateAsync(zero, aggregate)
                .ToMaterialized(First<TOut>(), Keep.Right)
                .Named("AggregateAsyncSink");

        /// <summary>
        /// <para>
        /// A <see cref="Sink{TIn,Task}"/> that will invoke the given <paramref name="reduce"/> for every received element, giving it its previous
        /// output (from the second element) and the element as input.
        /// The returned <see cref="Task{TIn}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with `Failure`
        /// if there is a failure signaled in the stream. 
        /// </para>
        /// <para>
        /// If the stream is empty (i.e. completes before signaling any elements),
        /// the sum stage will fail its downstream with a <see cref="NoSuchElementException"/>,
        /// which is semantically in-line with that standard library collections do in such situations.
        /// </para>
        /// <para>
        /// Adheres to the <see cref="ActorAttributes.SupervisionStrategy"/> attribute.
        /// </para>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="reduce">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TIn>> Sum<TIn>(Func<TIn, TIn, TIn> reduce) => Flow.Create<TIn>()
            .Sum(reduce)
            .ToMaterialized(First<TIn>(), Keep.Right)
            .Named("SumSink");

        /// <summary>
        /// A <see cref="Sink{TIn, NotUsed}"/> that when the flow is completed, either through a failure or normal
        /// completion, apply the provided function with <paramref name="success"/> or <paramref name="failure"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="success">TBD</param>
        /// <param name="failure">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, NotUsed> OnComplete<TIn>(Action success, Action<Exception> failure)
            => Flow.Create<TIn>()
                .Via(new OnCompleted<TIn>(success, failure))
                .To(Ignore<NotUsed>())
                .Named("OnCompleteSink");


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
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="actorRef">TBD</param>
        /// <param name="onCompleteMessage">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, NotUsed> ActorRef<TIn>(IActorRef actorRef, object onCompleteMessage)
            => new Sink<TIn, NotUsed>(new ActorRefSink<TIn>(actorRef, onCompleteMessage, DefaultAttributes.ActorRefSink, Shape<TIn>("ActorRefSink")));

        /// <summary>
        /// Sends the elements of the stream to the given <see cref="IActorRef"/> that sends back back-pressure signal.
        /// First element is always <paramref name="onInitMessage"/>, then stream is waiting for acknowledgement message
        /// <paramref name="ackMessage"/> from the given actor which means that it is ready to process
        /// elements.It also requires <paramref name="ackMessage"/> message after each stream element
        /// to make backpressure work.
        ///
        /// If the target actor terminates the stream will be canceled.
        /// When the stream is completed successfully the given <paramref name="onCompleteMessage"/>
        /// will be sent to the destination actor.
        /// When the stream is completed with failure - result of <paramref name="onFailureMessage"/>
        /// function will be sent to the destination actor.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="actorRef">TBD</param>
        /// <param name="onInitMessage">TBD</param>
        /// <param name="ackMessage">TBD</param>
        /// <param name="onCompleteMessage">TBD</param>
        /// <param name="onFailureMessage">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, NotUsed> ActorRefWithAck<TIn>(IActorRef actorRef, object onInitMessage, object ackMessage,
            object onCompleteMessage, Func<Exception, object> onFailureMessage = null)
        {
            onFailureMessage = onFailureMessage ?? (ex => new Status.Failure(ex));

            return
                FromGraph(new ActorRefBackpressureSinkStage<TIn>(actorRef, onInitMessage, ackMessage,
                    onCompleteMessage, onFailureMessage));
        }

        ///<summary>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that is materialized to an <see cref="IActorRef"/> which points to an Actor
        /// created according to the passed in <see cref="Props"/>. Actor created by the <paramref name="props"/> should
        /// be <see cref="ActorSubscriberSink{TIn}"/>.
        ///</summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="props">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, IActorRef> ActorSubscriber<TIn>(Props props)
            => new Sink<TIn, IActorRef>(new ActorSubscriberSink<TIn>(props, DefaultAttributes.ActorSubscriberSink, Shape<TIn>("ActorSubscriberSink")));

        ///<summary>
        /// <para>
        /// Creates a <see cref="Sink{TIn,TMat}"/> that is materialized as an <see cref="ISinkQueue{TIn}"/>.
        /// <see cref="ISinkQueue{TIn}.PullAsync"/> method is pulling element from the stream and returns <see cref="Task{Option}"/>.
        /// <see cref="Task"/> completes when element is available.
        /// </para>
        /// <para>
        /// Before calling the pull method a second time you need to wait until previous future completes.
        /// Pull returns failed future with <see cref="IllegalStateException"/> if previous future has not yet completed.
        /// </para>
        /// <para>
        /// <see cref="Sink{TIn,TMat}"/> will request at most number of elements equal to size of inputBuffer from
        /// upstream and then stop back pressure. You can configure size of input by using WithAttributes method.
        /// </para>
        /// <para>
        /// For stream completion you need to pull all elements from <see cref="ISinkQueue{T}"/> including last None
        /// as completion marker.
        /// </para>
        ///</summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, ISinkQueue<TIn>> Queue<TIn>() => FromGraph(new QueueSink<TIn>());

        /// <summary>
        /// <para>
        /// Creates a real <see cref="Sink{TIn,TMat}"/> upon receiving the first element. Internal <see cref="Sink{TIn,TMat}"/> will not be created if there are no elements,
        /// because of completion or error.
        /// </para>
        /// <para>
        /// If <paramref name="sinkFactory"/> throws an exception and the supervision decision is <see cref="Supervision.Directive.Stop"/> 
        /// the <see cref="Task"/> will be completed with failure. For all other supervision options it will try to create sink with next element.
        /// </para>
        /// <paramref name="fallback"/> will be executed when there was no elements and completed is received from upstream.
        /// <para>
        /// Adheres to the <see cref="ActorAttributes.SupervisionStrategy"/> attribute.
        /// </para>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="sinkFactory">TBD</param>
        /// <param name="fallback">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, Task<TMat>> LazySink<TIn, TMat>(Func<TIn, Task<Sink<TIn, TMat>>> sinkFactory,
            Func<TMat> fallback) => FromGraph(new LazySink<TIn, TMat>(sinkFactory, fallback));

        /// <summary>
        /// A graph with the shape of a sink logically is a sink, this method makes
        /// it so also in type.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, TMat> FromGraph<TIn, TMat>(IGraph<SinkShape<TIn>, TMat> graph)
            => graph is Sink<TIn, TMat>
                ? (Sink<TIn, TMat>)graph
                : new Sink<TIn, TMat>(graph.Module);

        /// <summary>
        /// Helper to create <see cref="Sink{TIn,TMat}"/> from <see cref="ISubscriber{TIn}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="subscriber">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, NotUsed> FromSubscriber<TIn>(ISubscriber<TIn> subscriber)
            => new Sink<TIn, NotUsed>(new SubscriberSink<TIn>(subscriber, DefaultAttributes.SubscriberSink, Shape<TIn>("SubscriberSink")));

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that immediately cancels its upstream after materialization.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Sink<TIn, NotUsed> Cancelled<TIn>()
            => new Sink<TIn, NotUsed>(new CancelSink<TIn>(DefaultAttributes.CancelledSink, Shape<TIn>("CancelledSink")));

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
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="fanout">TBD</param>
        /// <returns>TBD</returns>
        public static Sink<TIn, IPublisher<TIn>> AsPublisher<TIn>(bool fanout)
        {
            SinkModule<TIn, IPublisher<TIn>> publisherSink;
            if (fanout)
                publisherSink = new FanoutPublisherSink<TIn, ResizableMultiReaderRingBuffer<TIn>>(DefaultAttributes.FanoutPublisherSink, Shape<TIn>("FanoutPublisherSink"));
            else
                publisherSink = new PublisherSink<TIn>(DefaultAttributes.PublisherSink, Shape<TIn>("PublisherSink"));

            return new Sink<TIn, IPublisher<TIn>>(publisherSink);
        }

        /// <summary>
        /// A <see cref="Sink{TIn,TMat}"/> that materializes into a <see cref="IObservable{T}"/>. It supports multiple subscribers.
        /// Since observables have no notion of backpressure, it will push incoming elements as fast as possible, potentially risking
        /// process overrun.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Sink<T, IObservable<T>> AsObservable<T>() => FromGraph(new ObservableSinkStage<T>());
    }
}
