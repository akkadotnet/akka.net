//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Util;
using Reactive.Streams;
using Decider = Akka.Streams.Supervision.Decider;

namespace Akka.Streams
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class DefaultMaterializerExt : ExtensionIdProvider<DefaultMaterializer>
    {
        public override DefaultMaterializer CreateExtension(ExtendedActorSystem system)
        {
            return new DefaultMaterializer(system);
        }
    }
    
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>
    /// Caches a default materializer instance for each actor system. Prevents the need to create a new materializer
    /// for trivial changes.
    /// </remarks>
    internal sealed class DefaultMaterializer : IExtension
    {
        public ActorMaterializer Materializer { get; }
        
        public DefaultMaterializer(ActorSystem system)
        {
            var haveShutDown = new AtomicBoolean();
            
            // Inject the top-level fallback config for the Materializer once, and only once.
            // This is a performance optimization to avoid having to do this on every materialization.
            system.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            
            var settings = ActorMaterializerSettings.Create(system);
            
            Materializer = new ActorMaterializerImpl(
                system: system,
                settings: settings,
                dispatchers: system.Dispatchers,
                supervisor: system.ActorOf(StreamSupervisor.Props(settings, haveShutDown).WithDispatcher(settings.Dispatcher), StreamSupervisor.NextName()),
                haveShutDown: haveShutDown,
                flowNames: EnumerableActorName.Create("Flow"));
        }
        
        public static DefaultMaterializer Get(ActorSystem system)
        {
            return system.WithExtension<DefaultMaterializer, DefaultMaterializerExt>();
        }
    }

    /// <summary>
    /// A ActorMaterializer takes the list of transformations comprising a
    /// <see cref="IFlow{TOut,TMat}"/> and materializes them in the form of
    /// <see cref="IProcessor{T1,T2}"/> instances. How transformation
    /// steps are split up into asynchronous regions is implementation
    /// dependent.
    /// </summary>
    public abstract class ActorMaterializer : IMaterializer, IMaterializerLoggingProvider, IDisposable
    {
        private static readonly Config DefaultMaterializerConfig = ConfigurationFactory.FromResource<ActorMaterializer>("Akka.Streams.reference.conf");

        /// <summary>
        /// Gets the default configuration for the stream materializer.
        /// </summary>
        /// <returns>The reference configuration for the stream materializer.</returns>
        public static Config DefaultConfig()
            => DefaultMaterializerConfig;

        #region static

        /// <summary>
        /// <para>
        /// Creates a ActorMaterializer which will execute every step of a transformation
        /// pipeline within its own <see cref="ActorBase"/>. The required <see cref="IActorRefFactory"/>
        /// (which can be either an <see cref="ActorSystem"/> or an <see cref="IActorContext"/>)
        /// will be used to create one actor that in turn creates actors for the transformation steps.
        /// </para>
        /// <para>
        /// The materializer's <see cref="ActorMaterializerSettings"/> will be obtained from the
        /// configuration of the <paramref name="context"/>'s underlying <see cref="ActorSystem"/>.
        /// </para>
        /// <para>
        /// The <paramref name="namePrefix"/> is used as the first part of the names of the actors running
        /// the processing steps. The default <paramref name="namePrefix"/> is "flow". The actor names are built up of
        /// `namePrefix-flowNumber-flowStepNumber-stepName`.
        /// </para>
        /// </summary>
        /// <param name="context">The actor system or actor context used to create the materializer's supervisor.</param>
        /// <param name="settings">Settings to use, or <see langword="null"/> to use the actor system's materializer settings.</param>
        /// <param name="namePrefix">The prefix used for names of actors created for stream processing.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="context"/> is not of type <see cref="ActorSystem"/> or <see cref="IActorContext"/>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="context"/> is undefined.
        /// </exception>
        /// <returns>A materializer configured with the supplied settings and name prefix.</returns>
        public static ActorMaterializer Create(IActorRefFactory context, ActorMaterializerSettings settings = null, string namePrefix = null)
        {
            var system = ActorSystemOf(context);
            
            // forces settings to get injected into the ActorSystem the first time we materialize
            var defaultMaterializer = DefaultMaterializer.Get(system).Materializer;

            // optimized paths for non-allocation
            if (context.Equals(system) && settings == null && namePrefix == null)
                return defaultMaterializer;
            if (context.Equals(system) && settings == null && namePrefix != null)
                return (ActorMaterializer)defaultMaterializer.WithNamePrefix(namePrefix);

            // use the default settings if none have been passed in
            settings ??= defaultMaterializer.Settings;
            
            var haveShutDown = new AtomicBoolean();

            return new ActorMaterializerImpl(
                system: system,
                settings: settings,
                dispatchers: system.Dispatchers,
                supervisor: context.ActorOf(StreamSupervisor.Props(settings, haveShutDown).WithDispatcher(settings.Dispatcher), StreamSupervisor.NextName()),
                haveShutDown: haveShutDown,
                flowNames: EnumerableActorName.Create(namePrefix ?? "Flow"));
        }

        private static ActorSystem ActorSystemOf(IActorRefFactory context)
        {
            return context switch
            {
                ExtendedActorSystem system => system,
                IActorContext actorContext => actorContext.System,
                null => throw new ArgumentNullException(nameof(context), "IActorRefFactory must be defined"),
                _ => throw new ArgumentException(
                    $"ActorRefFactory context must be a ActorSystem or ActorContext, got [{context.GetType()}]")
            };
        }

        #endregion

        /// <summary>
        /// The settings used by this materializer.
        /// </summary>
        public abstract ActorMaterializerSettings Settings { get; }

        /// <summary>
        /// Indicates if the materializer has been shut down.
        /// </summary>
        public abstract bool IsShutdown { get; }

        /// <summary>
        /// The dispatcher used for stream execution and callbacks.
        /// </summary>
        public abstract MessageDispatcher ExecutionContext { get; }

        /// <summary>
        /// The actor system that owns this materializer.
        /// </summary>
        public abstract ActorSystem System { get; }

        /// <summary>
        /// The logging adapter used by this materializer.
        /// </summary>
        public abstract ILoggingAdapter Logger { get; }

        /// <summary>
        /// The supervisor actor that manages materialized stream stages.
        /// </summary>
        public abstract IActorRef Supervisor { get; }

        /// <summary>
        /// Returns a materializer that uses the supplied prefix when naming stream processing actors.
        /// </summary>
        /// <param name="namePrefix">The prefix used for names of actors created for stream processing.</param>
        /// <returns>A materializer with the supplied name prefix.</returns>
        public abstract IMaterializer WithNamePrefix(string namePrefix);

        /// <inheritdoc />
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable);

        /// <inheritdoc />
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes);

        /// <summary>
        /// Schedules a single action after the specified delay.
        /// </summary>
        /// <param name="delay">The time to wait before the action runs.</param>
        /// <param name="action">The action to schedule.</param>
        /// <returns>A handle that can be used to cancel the scheduled action.</returns>
        public abstract ICancelable ScheduleOnce(TimeSpan delay, Action action);

        /// <summary>
        /// Schedules an action repeatedly after an initial delay and at the specified interval.
        /// </summary>
        /// <param name="initialDelay">The time to wait before the first invocation.</param>
        /// <param name="interval">The time between subsequent invocations.</param>
        /// <param name="action">The action to schedule repeatedly.</param>
        /// <returns>A handle that can be used to cancel the scheduled action.</returns>
        public abstract ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);

        /// <summary>
        /// Resolves the materializer settings, applying input-buffer, dispatcher, and supervision attributes where supplied.
        /// </summary>
        /// <param name="attributes">The attributes that may override input-buffer, dispatcher, and supervision settings.</param>
        /// <returns>The effective settings after applying those supported attributes.</returns>
        public abstract ActorMaterializerSettings EffectiveSettings(Attributes attributes);

        /// <summary>
        /// Shuts down this materializer and all the stages that have been materialized through this materializer. After
        /// having shut down, this materializer cannot be used again. Any attempt to materialize stages after having
        /// shut down will result in an <see cref="IllegalStateException"/> being thrown at materialization time.
        /// </summary>
        public abstract void Shutdown();

        /// <summary>
        /// Creates an actor using the materializer's actor system and the supplied materialization context.
        /// </summary>
        /// <param name="context">The context for the stage being materialized.</param>
        /// <param name="props">The actor configuration to instantiate.</param>
        /// <returns>The actor reference for the created actor.</returns>
        public abstract IActorRef ActorOf(MaterializationContext context, Props props);

        /// <summary>
        /// Creates a new logging adapter.
        /// </summary>
        /// <param name="logSource">The source that produces the log events.</param>
        /// <returns>The newly created logging adapter.</returns>
        public abstract ILoggingAdapter MakeLogger(object logSource);

       public void Dispose() => Shutdown();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class ActorMaterializerHelper
    {
        /// <summary>
        /// Converts an <see cref="IMaterializer"/> to an <see cref="ActorMaterializer"/>.
        /// </summary>
        /// <param name="materializer">The original materializer.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="materializer"/> is not of type <see cref="ActorMaterializer"/>.
        /// </exception>
        internal static ActorMaterializer Downcast(IMaterializer materializer)
        {
            //FIXME this method is going to cause trouble for other Materializer implementations
            if (materializer is ActorMaterializer downcast)
                return downcast;

            throw new ArgumentException($"Expected {typeof(ActorMaterializer)} but got {materializer.GetType()}");
        }

        /// <summary>
        /// Resolves the stream supervisor's <see cref="ActorCell"/>, so a caller can host a function ref on it.
        /// The supervisor is a <see cref="RepointableActorRef"/> that starts asynchronously; a stream materialized
        /// immediately after the materializer is created can race that startup, leaving the cell as an
        /// <c>UnstartedCell</c>. In that case the supervisor is forced to start before its cell is returned
        /// (mirrors how <c>ActorMaterializerImpl.ActorOf</c> handles the same race for actor-backed sources).
        /// Callers that only run after the interpreter has started (e.g. from <c>PreStart</c>) always take the
        /// fast path, since the supervisor is started by then.
        /// </summary>
        internal static ActorCell GetSupervisorCell(ActorMaterializer materializer)
        {
            var supervisor = materializer.Supervisor;
            switch (supervisor)
            {
                case LocalActorRef r:
                    return r.Cell;
                case RepointableActorRef { IsStarted: true } r:
                    return (ActorCell)r.Underlying;
                case RepointableActorRef r:
                    var timeout = r.Underlying.System.Settings.CreationTimeout;
                    r.Ask<ActorIdentity>(new Identify(null), timeout).Wait();
                    return (ActorCell)r.Underlying;
                default:
                    throw new IllegalStateException(
                        $"Stream supervisor must be a local actor, was [{supervisor.GetType()}]");
            }
        }
    }

    /// <summary>
    /// This exception signals that an actor implementing a Reactive Streams Subscriber, Publisher or Processor
    /// has been terminated without being notified by an onError, onComplete or cancel signal. This usually happens
    /// when an ActorSystem is shut down while stream processing actors are still running.
    /// </summary>
    [Serializable]
    public class AbruptTerminationException : Exception
    {
        /// <summary>
        /// The actor that was terminated without notification.
        /// </summary>
        public readonly IActorRef Actor;

        /// <summary>
        /// Initializes a new instance of the <see cref="AbruptTerminationException" /> class.
        /// </summary>
        /// <param name="actor">The actor that was terminated.</param>
        public AbruptTerminationException(IActorRef actor)
            : base($"Processor actor [{actor}] terminated abruptly")
        {
            Actor = actor;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AbruptTerminationException" /> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AbruptTerminationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Actor = (IActorRef)info.GetValue("Actor", typeof(IActorRef));
        }
    }

    /// <summary>
    /// This exception or subtypes thereof should be used to signal materialization failures.
    /// </summary>
    public class MaterializationException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MaterializationException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public MaterializationException(string message, Exception innerException) : base(message, innerException) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterializationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected MaterializationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Signal that the stage was abruptly terminated, usually seen as a call to <see cref="GraphStageLogic.PostStop"/> without
    /// any of the handler callbacks seeing completion or failure from upstream or cancellation from downstream. This can happen when
    /// the actor running the graph is killed, which happens when the materializer or actor system is terminated.
    /// </summary>
    public sealed class AbruptStageTerminationException : Exception
    {
        public AbruptStageTerminationException(GraphStageLogic logic) 
            : base($"GraphStage {logic} terminated abruptly, caused by for example materializer or actor system termination.")
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AbruptStageTerminationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        public AbruptStageTerminationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }


    /// <summary>
    /// This class describes the configurable properties of the <see cref="ActorMaterializer"/>. 
    /// Please refer to the withX methods for descriptions of the individual settings.
    /// </summary>
    public sealed class ActorMaterializerSettings
    {
        public static ActorMaterializerSettings Create(ActorSystem system)
        {
            // need to make sure the default materializer settings are available
            system.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            var config = system.Settings.Config.GetConfig("akka.stream.materializer");

            // No need to check for Config.IsEmpty because this function expects empty Config.
            if (config == null)
                throw ConfigurationException.NullOrEmptyConfig<ActorMaterializerSettings>("akka.stream.materializer");

            return Create(config);
        }

        // NOTE: Make sure that this class can handle empty Config
        private static ActorMaterializerSettings Create(Config config)
        {
            // No need to check for Config.IsEmpty because this function expects empty Config.
            if (config == null)
                throw ConfigurationException.NullOrEmptyConfig<ActorMaterializerSettings>();

            return new ActorMaterializerSettings(
                initialInputBufferSize: config.GetInt("initial-input-buffer-size", 4),
                maxInputBufferSize: config.GetInt("max-input-buffer-size", 16),
                dispatcher: config.GetString("dispatcher", string.Empty),
                supervisionDecider: Deciders.StoppingDecider,
                subscriptionTimeoutSettings: StreamSubscriptionTimeoutSettings.Create(config),
                isDebugLogging: config.GetBoolean("debug-logging", false),
                outputBurstLimit: config.GetInt("output-burst-limit", 1000),
                isFuzzingMode: config.GetBoolean("debug.fuzzing-mode", false),
                isAutoFusing: config.GetBoolean("auto-fusing", true),
                maxFixedBufferSize: config.GetInt("max-fixed-buffer-size", 1000000000),
                syncProcessingLimit: config.GetInt("sync-processing-limit", 1000),
                streamRefSettings: StreamRefSettings.Create(config.GetConfig("stream-ref")));
        }

        private const int DefaultMaxFixedBufferSize = 1000;
        /// <summary>
        /// The initial number of elements buffered for each inlet by default.
        /// </summary>
        public readonly int InitialInputBufferSize;
        /// <summary>
        /// The maximum number of elements buffered for each inlet by default.
        /// </summary>
        public readonly int MaxInputBufferSize;
        /// <summary>
        /// The dispatcher identifier used for stream execution by default.
        /// </summary>
        public readonly string Dispatcher;
        /// <summary>
        /// The default decider used for exceptions handled by supervision-aware stream stages.
        /// </summary>
        public readonly Decider SupervisionDecider;
        /// <summary>
        /// The settings for timing out unused substream publishers and subscribers.
        /// </summary>
        public readonly StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings;
        /// <summary>
        /// Whether debug logging for dropped elements is enabled.
        /// </summary>
        public readonly bool IsDebugLogging;
        /// <summary>
        /// The maximum number of elements emitted in one batch when downstream demand is large.
        /// </summary>
        public readonly int OutputBurstLimit;
        /// <summary>
        /// Whether graph-stage event processing uses randomized ordering for testing.
        /// </summary>
        public readonly bool IsFuzzingMode;
        /// <summary>
        /// Whether eligible stream operations are automatically fused.
        /// </summary>
        public readonly bool IsAutoFusing;
        /// <summary>
        /// The maximum fixed-size buffer capacity that is preallocated.
        /// </summary>
        public readonly int MaxFixedBufferSize;
        /// <summary>
        /// The maximum number of messages processed synchronously in stream-to-substream communication.
        /// </summary>
        public readonly int SyncProcessingLimit;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        public readonly StreamRefSettings StreamRefSettings;
        
        public ActorMaterializerSettings(
            int initialInputBufferSize, 
            int maxInputBufferSize, 
            string dispatcher, 
            Decider supervisionDecider, 
            StreamSubscriptionTimeoutSettings subscriptionTimeoutSettings, 
            StreamRefSettings streamRefSettings, 
            bool isDebugLogging, 
            int outputBurstLimit, 
            bool isFuzzingMode, 
            bool isAutoFusing, 
            int maxFixedBufferSize,
            int syncProcessingLimit = DefaultMaxFixedBufferSize)
        {
            if(initialInputBufferSize <= 0)
                throw new ArgumentException($"{nameof(initialInputBufferSize)} must be > 0", nameof(initialInputBufferSize));
            if(syncProcessingLimit <= 0)
                throw new ArgumentException($"{nameof(syncProcessingLimit)} must be > 0", nameof(syncProcessingLimit));

            if(maxInputBufferSize <= 0)
                throw new ArgumentException($"{nameof(maxInputBufferSize)} must be > 0", nameof(maxInputBufferSize));
            if((maxInputBufferSize & (maxInputBufferSize - 1)) != 0)
                throw new ArgumentException($"{nameof(maxInputBufferSize)} must be a power of two", nameof(maxInputBufferSize));

            if(initialInputBufferSize > maxInputBufferSize)
                throw new ArgumentException($"initialInputBufferSize({initialInputBufferSize}) must be <= maxInputBufferSize({maxInputBufferSize})");

            InitialInputBufferSize = initialInputBufferSize;
            MaxInputBufferSize = maxInputBufferSize;
            Dispatcher = dispatcher;
            SupervisionDecider = supervisionDecider;
            SubscriptionTimeoutSettings = subscriptionTimeoutSettings;
            IsDebugLogging = isDebugLogging;
            OutputBurstLimit = outputBurstLimit;
            IsFuzzingMode = isFuzzingMode;
            IsAutoFusing = isAutoFusing;
            MaxFixedBufferSize = maxFixedBufferSize;
            SyncProcessingLimit = syncProcessingLimit;
            StreamRefSettings = streamRefSettings;
        }

        private ActorMaterializerSettings Copy(
            int? initialInputBufferSize = null,
            int? maxInputBufferSize = null,
            string dispatcher = null,
            Decider supervisionDecider = null,
            StreamSubscriptionTimeoutSettings subscriptionTimeoutSettings = null,
            StreamRefSettings streamRefSettings = null,
            bool? isDebugLogging = null,
            int? outputBurstLimit = null,
            bool? isFuzzingMode = null,
            bool? isAutoFusing = null,
            int? maxFixedBufferSize = null,
            int? syncProcessingLimit = null)
        {
            return new ActorMaterializerSettings(
                initialInputBufferSize??InitialInputBufferSize,
                maxInputBufferSize??MaxInputBufferSize,
                dispatcher??Dispatcher,
                supervisionDecider??SupervisionDecider,
                subscriptionTimeoutSettings??SubscriptionTimeoutSettings,
                streamRefSettings ?? StreamRefSettings,
                isDebugLogging ?? IsDebugLogging,
                outputBurstLimit??OutputBurstLimit,
                isFuzzingMode??IsFuzzingMode,
                isAutoFusing??IsAutoFusing,
                maxFixedBufferSize??MaxFixedBufferSize,
                syncProcessingLimit??SyncProcessingLimit);
        }

        /// <summary>
        /// Each asynchronous piece of a materialized stream topology is executed by one Actor
        /// that manages an input buffer for all inlets of its shape. This setting configures
        /// the default for initial and maximal input buffer in number of elements for each inlet.
        /// This can be overridden for individual parts of the
        /// stream topology by using <see cref="Attributes.InputBuffer"/>.
        /// </summary>
        /// <param name="initialSize">The initial buffer size in elements for each inlet.</param>
        /// <param name="maxSize">The maximum buffer size in elements for each inlet.</param>
        /// <returns>Settings with the specified default input-buffer sizes.</returns>
        public ActorMaterializerSettings WithInputBuffer(int initialSize, int maxSize)
        {
            if (initialSize == InitialInputBufferSize && maxSize == MaxInputBufferSize)
                return this;
            return Copy(initialInputBufferSize: initialSize, maxInputBufferSize: maxSize);
        }

        /// <summary>
        /// This setting configures the default dispatcher to be used by streams materialized
        /// with the <see cref="ActorMaterializer"/>. This can be overridden for individual parts of the
        /// stream topology by using <see cref="ActorAttributes.Dispatcher"/>.
        /// </summary>
        /// <param name="dispatcher">The dispatcher identifier to use for stream execution.</param>
        /// <returns>Settings with the supplied default dispatcher, or the current dispatcher when <paramref name="dispatcher"/> is <see langword="null"/>.</returns>
        public ActorMaterializerSettings WithDispatcher(string dispatcher)
        {
            if (dispatcher == Dispatcher) return this;
            return Copy(dispatcher: dispatcher);
        }

        /// <summary>
        /// Decides how exceptions from application code are to be handled, unless
        /// overridden for specific flows of the stream operations with
        /// <see cref="ActorAttributes.SupervisionStrategy"/>
        /// </summary>
        /// <param name="decider">The function that selects a supervision directive for an exception.</param>
        /// <returns>Settings with the specified default supervision decider.</returns>
        public ActorMaterializerSettings WithSupervisionStrategy(Decider decider)
        {
            if (decider.Equals(SupervisionDecider)) return this;
            return Copy(supervisionDecider: decider);
        }

        /// <summary>
        /// Enable to log all elements that are dropped due to failures (at DEBUG level).
        /// </summary>
        /// <param name="isEnabled">Whether debug logging for dropped elements is enabled.</param>
        /// <returns>Settings with the requested debug-logging option.</returns>
        public ActorMaterializerSettings WithDebugLogging(bool isEnabled)
        {
            if (IsDebugLogging == isEnabled) return this;
            return Copy(isDebugLogging: isEnabled);
        }

        /// <summary>
        /// Test utility: fuzzing mode means that GraphStage events are not processed
        /// in FIFO order within a fused subgraph, but randomized.
        /// </summary>
        /// <param name="isFuzzingMode">Whether graph-stage event processing uses randomized ordering.</param>
        /// <returns>Settings with the requested fuzzing mode.</returns>
        public ActorMaterializerSettings WithFuzzingMode(bool isFuzzingMode)
        {
            if (IsFuzzingMode == isFuzzingMode) return this;
            return Copy(isFuzzingMode: isFuzzingMode);
        }

        /// <summary>
        /// Maximum number of elements emitted in batch if downstream signals large demand.
        /// </summary>
        /// <param name="limit"></param>
        /// <returns></returns>
        public ActorMaterializerSettings WithOutputBurstLimit(int limit)
        {
            if (limit == OutputBurstLimit) return this;
            return Copy(outputBurstLimit: limit);
        }

        /// <summary>
        /// Sets whether eligible stream operations are automatically fused.
        /// </summary>
        /// <param name="isAutoFusing">Whether automatic fusing is enabled.</param>
        /// <returns>Settings with the requested automatic-fusing option.</returns>
        public ActorMaterializerSettings WithAutoFusing(bool isAutoFusing)
        {
            if (IsAutoFusing == isAutoFusing) return this;
            return Copy(isAutoFusing: isAutoFusing);
        }

        /// <summary>
        /// Configure the maximum buffer size for which a FixedSizeBuffer will be preallocated.
        /// This defaults to a large value because it is usually better to fail early when
        /// system memory is not sufficient to hold the buffer.
        /// </summary>
        /// <param name="maxFixedBufferSize">The maximum fixed-size buffer capacity to preallocate.</param>
        /// <returns>Settings with the specified maximum preallocated buffer size.</returns>
        public ActorMaterializerSettings WithMaxFixedBufferSize(int maxFixedBufferSize)
        {
            if (MaxFixedBufferSize == maxFixedBufferSize) return this;
            return Copy(maxFixedBufferSize: maxFixedBufferSize);
        }

        /// <summary>
        /// Limit for number of messages that can be processed synchronously in stream to substream communication
        /// </summary>
        /// <param name="limit">The maximum number of messages processed synchronously in stream-to-substream communication.</param>
        /// <returns>Settings with the specified synchronous processing limit.</returns>
        public ActorMaterializerSettings WithSyncProcessingLimit(int limit)
        {
            if (SyncProcessingLimit == limit) return this;
            return Copy(syncProcessingLimit: limit);
        }

        /// <summary>
        /// Leaked publishers and subscribers are cleaned up when they are not used within a given
        /// deadline, configured by <see cref="StreamSubscriptionTimeoutSettings"/>.
        /// </summary>
        /// <param name="settings">The subscription-timeout settings to use.</param>
        /// <returns>Settings with the supplied subscription-timeout behavior, or the current setting when <paramref name="settings"/> is <see langword="null"/>.</returns>
        public ActorMaterializerSettings WithSubscriptionTimeoutSettings(StreamSubscriptionTimeoutSettings settings)
        {
            if (Equals(settings, SubscriptionTimeoutSettings))
                return this;
            return Copy(subscriptionTimeoutSettings: settings);
        }

        public ActorMaterializerSettings WithStreamRefSettings(StreamRefSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (ReferenceEquals(settings, this.StreamRefSettings)) return this;
            return Copy(streamRefSettings: settings);
        }

        public override bool Equals(object obj)
        {
            if (!(obj is ActorMaterializerSettings s)) return false;
            return
                s.InitialInputBufferSize == InitialInputBufferSize &&
                s.MaxInputBufferSize == MaxInputBufferSize &&
                s.Dispatcher == Dispatcher &&
                s.SupervisionDecider == SupervisionDecider &&
                s.SubscriptionTimeoutSettings == SubscriptionTimeoutSettings &&
                s.IsDebugLogging == IsDebugLogging &&
                s.OutputBurstLimit == OutputBurstLimit &&
                s.SyncProcessingLimit == SyncProcessingLimit &&
                s.IsFuzzingMode == IsFuzzingMode &&
                s.IsAutoFusing == IsAutoFusing &&
                s.MaxFixedBufferSize == MaxFixedBufferSize &&
                s.StreamRefSettings == StreamRefSettings;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (17 * 23) ^ InitialInputBufferSize;
                hash = (hash * 23) ^ MaxInputBufferSize;
                hash = (hash * 23) ^ Dispatcher.GetHashCode();
                hash = (hash * 23) ^ SupervisionDecider.GetHashCode();
                hash = (hash * 23) ^ SubscriptionTimeoutSettings.GetHashCode();
                hash = (hash * 23) ^ IsDebugLogging.GetHashCode();
                hash = (hash * 23) ^ OutputBurstLimit;
                hash = (hash * 23) ^ SyncProcessingLimit;
                hash = (hash * 23) ^ IsFuzzingMode.GetHashCode();
                hash = (hash * 23) ^ IsAutoFusing.GetHashCode();
                hash = (hash * 23) ^ MaxFixedBufferSize;
                hash = (hash * 23) ^ StreamRefSettings.GetHashCode();
                return hash;
            }
        }

        internal Attributes ToAttributes()
        {
            return new Attributes(new Attributes.IAttribute[]
                {
                    new Attributes.InputBuffer(InitialInputBufferSize, MaxInputBufferSize), 
                    Attributes.CancellationStrategy.Default,
                    new ActorAttributes.Dispatcher(Dispatcher), 
                    new ActorAttributes.SupervisionStrategy(SupervisionDecider), 
                    new ActorAttributes.DebugLogging(IsDebugLogging), 
                    new ActorAttributes.StreamSubscriptionTimeout(SubscriptionTimeoutSettings.Timeout, SubscriptionTimeoutSettings.Mode), 
                    new ActorAttributes.OutputBurstLimit(OutputBurstLimit), 
                    new ActorAttributes.FuzzingMode(IsFuzzingMode), 
                    new ActorAttributes.MaxFixedBufferSize(MaxFixedBufferSize), 
                    new ActorAttributes.SyncProcessingLimit(SyncProcessingLimit), 
                });
        }
    }

    /// <summary>
    /// Leaked publishers and subscribers are cleaned up when they are not used within a given deadline, configured by <see cref="StreamSubscriptionTimeoutSettings"/>.
    /// </summary>
    public sealed class StreamSubscriptionTimeoutSettings : IEquatable<StreamSubscriptionTimeoutSettings>
    {
        /// <summary>
        /// Creates subscription-timeout settings from the materializer configuration.
        /// </summary>
        /// <param name="config">The configuration containing the <c>subscription-timeout</c> section.</param>
        /// <exception cref="ArgumentException">The configured timeout mode is not recognized.</exception>
        /// <returns>The timeout mode and duration read from the configuration.</returns>
        public static StreamSubscriptionTimeoutSettings Create(Config config)
        {
            // No need to check for Config.IsEmpty because this function expects empty Config.
            if (config == null)
                throw ConfigurationException.NullOrEmptyConfig<StreamSubscriptionTimeoutSettings>();

            var c = config.GetConfig("subscription-timeout");
            var configMode = c.GetString("mode", "cancel").ToLowerInvariant();
            StreamSubscriptionTimeoutTerminationMode mode;
            switch (configMode)
            {
                case "no": case "off": case "false": case "noop": mode = StreamSubscriptionTimeoutTerminationMode.NoopTermination; break;
                case "warn": mode = StreamSubscriptionTimeoutTerminationMode.WarnTermination; break;
                case "cancel": mode = StreamSubscriptionTimeoutTerminationMode.CancelTermination; break;
                default: throw new ArgumentException("akka.stream.materializer.subscribtion-timeout.mode was not defined or has invalid value. Valid values are: no, off, false, noop, warn, cancel");
            }
            
            return new StreamSubscriptionTimeoutSettings(
                mode: mode,
                timeout: c.GetTimeSpan("timeout", TimeSpan.FromSeconds(5)));
        }

        /// <summary>
        /// The action taken when a subscription timeout expires.
        /// </summary>
        public readonly StreamSubscriptionTimeoutTerminationMode Mode;

        /// <summary>
        /// The duration after which an unused substream publisher or subscriber is treated as leaked.
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// Creates subscription-timeout settings from a termination mode and timeout duration.
        /// </summary>
        /// <param name="mode">The action taken when the timeout expires.</param>
        /// <param name="timeout">The duration before the timeout expires.</param>
        public StreamSubscriptionTimeoutSettings(StreamSubscriptionTimeoutTerminationMode mode, TimeSpan timeout)
        {
            Mode = mode;
            Timeout = timeout;
        }
               
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
                return false;
            if (ReferenceEquals(obj, this))
                return true;
            if (obj is StreamSubscriptionTimeoutSettings settings)
                return Equals(settings);

            return false;
        }
        
        public bool Equals(StreamSubscriptionTimeoutSettings other)
            => Mode == other.Mode && Timeout.Equals(other.Timeout);

       
        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Mode * 397) ^ Timeout.GetHashCode();
            }
        }

        public override string ToString() => $"StreamSubscriptionTimeoutSettings<{Mode}, {Timeout}>";
    }

    /// <summary>
    /// This mode describes what shall happen when the subscription timeout expires 
    /// for substream Publishers created by operations like <see cref="InternalFlowOperations.PrefixAndTail{T,TMat}"/>.
    /// </summary>
    public enum StreamSubscriptionTimeoutTerminationMode
    {
        /// <summary>
        /// Do not do anything when timeout expires.
        /// </summary>
        NoopTermination,

        /// <summary>
        /// Log a warning when the timeout expires.
        /// </summary>
        WarnTermination,

        /// <summary>
        /// When the timeout expires attach a Subscriber that will immediately cancel its subscription.
        /// </summary>
        CancelTermination
    }

    /// <summary>
    /// Extension methods for creating stream materializers from actor-reference factories.
    /// </summary>
    public static class ActorMaterializerExtensions
    {
        /// <summary>
        /// <para>
        /// Creates a ActorMaterializer which will execute every step of a transformation
        /// pipeline within its own <see cref="ActorBase"/>. The required <see cref="IActorRefFactory"/>
        /// (which can be either an <see cref="ActorSystem"/> or an <see cref="IActorContext"/>)
        /// will be used to create one actor that in turn creates actors for the transformation steps.
        /// </para>
        /// <para>
        /// The materializer's <see cref="ActorMaterializerSettings"/> will be obtained from the
        /// configuration of the <paramref name="context"/>'s underlying <see cref="ActorSystem"/>.
        /// </para>
        /// <para>
        /// The <paramref name="namePrefix"/> is used as the first part of the names of the actors running
        /// the processing steps. The default <paramref name="namePrefix"/> is "flow". The actor names are built up of
        /// namePrefix-flowNumber-flowStepNumber-stepName.
        /// </para>
        /// </summary>
        /// <param name="context">The actor system or actor context used to create the materializer.</param>
        /// <param name="settings">Settings to use, or <see langword="null"/> to use the actor system's materializer settings.</param>
        /// <param name="namePrefix">The prefix used for names of actors created for stream processing.</param>
        /// <returns>A materializer configured with the supplied settings and name prefix.</returns>
        public static ActorMaterializer Materializer(this IActorRefFactory context, ActorMaterializerSettings settings = null, string namePrefix = null)
            => ActorMaterializer.Create(context, settings, namePrefix);
    }
}
