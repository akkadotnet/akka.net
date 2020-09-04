//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
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
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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
        /// <param name="context">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="namePrefix">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="context"/> is not of type <see cref="ActorSystem"/> or <see cref="IActorContext"/>.
        /// </exception>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="context"/> is undefined.
        /// </exception>
        /// <returns>TBD</returns>
        public static ActorMaterializer Create(IActorRefFactory context, ActorMaterializerSettings settings = null, string namePrefix = null)
        {
            var haveShutDown = new AtomicBoolean();
            var system = ActorSystemOf(context);
            system.Settings.InjectTopLevelFallback(DefaultConfig());
            settings = settings ?? ActorMaterializerSettings.Create(system);

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
            if (context is ExtendedActorSystem)
                return (ActorSystem)context;
            if (context is IActorContext)
                return ((IActorContext)context).System;
            if (context == null)
                throw new ArgumentNullException(nameof(context), "IActorRefFactory must be defined");

            throw new ArgumentException($"ActorRefFactory context must be a ActorSystem or ActorContext, got [{context.GetType()}]");
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public abstract ActorMaterializerSettings Settings { get; }

        /// <summary>
        /// Indicates if the materializer has been shut down.
        /// </summary>
        public abstract bool IsShutdown { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract MessageDispatcher ExecutionContext { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract ActorSystem System { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract ILoggingAdapter Logger { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract IActorRef Supervisor { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="namePrefix">TBD</param>
        /// <returns>TBD</returns>
        public abstract IMaterializer WithNamePrefix(string namePrefix);

        /// <inheritdoc />
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable);

        /// <inheritdoc />
        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable, Attributes initialAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="delay">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public abstract ICancelable ScheduleOnce(TimeSpan delay, Action action);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="action">TBD</param>
        /// <returns>TBD</returns>
        public abstract ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public abstract ActorMaterializerSettings EffectiveSettings(Attributes attributes);

        /// <summary>
        /// Shuts down this materializer and all the stages that have been materialized through this materializer. After
        /// having shut down, this materializer cannot be used again. Any attempt to materialize stages after having
        /// shut down will result in an <see cref="IllegalStateException"/> being thrown at materialization time.
        /// </summary>
        public abstract void Shutdown();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="props">TBD</param>
        /// <returns>TBD</returns>
        public abstract IActorRef ActorOf(MaterializationContext context, Props props);

        /// <summary>
        /// Creates a new logging adapter.
        /// </summary>
        /// <param name="logSource">The source that produces the log events.</param>
        /// <returns>The newly created logging adapter.</returns>
        public abstract ILoggingAdapter MakeLogger(object logSource);

        /// <inheritdoc/>
        public void Dispose() => Shutdown();
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class ActorMaterializerHelper
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="materializer">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="materializer"/> is not of type <see cref="ActorMaterializer"/>.
        /// </exception>
        /// <returns>TBD</returns>
        internal static ActorMaterializer Downcast(IMaterializer materializer)
        {
            //FIXME this method is going to cause trouble for other Materializer implementations
            var downcast = materializer as ActorMaterializer;
            if (downcast != null)
                return downcast;

            throw new ArgumentException($"Expected {typeof(ActorMaterializer)} but got {materializer.GetType()}");
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

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AbruptTerminationException" /> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AbruptTerminationException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            Actor = (IActorRef)info.GetValue("Actor", typeof(IActorRef));
        }
#endif
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

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="MaterializationException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected MaterializationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#endif
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
    }


    /// <summary>
    /// This class describes the configurable properties of the <see cref="ActorMaterializer"/>. 
    /// Please refer to the withX methods for descriptions of the individual settings.
    /// </summary>
    public sealed class ActorMaterializerSettings
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
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

        private const int DefaultlMaxFixedbufferSize = 1000;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int InitialInputBufferSize;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int MaxInputBufferSize;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Dispatcher;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Decider SupervisionDecider;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool IsDebugLogging;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int OutputBurstLimit;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool IsFuzzingMode;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool IsAutoFusing;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int MaxFixedBufferSize;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int SyncProcessingLimit;

        /// <summary>
        /// INTERNAL API
        /// </summary>
        public readonly StreamRefSettings StreamRefSettings;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialInputBufferSize">TBD</param>
        /// <param name="maxInputBufferSize">TBD</param>
        /// <param name="dispatcher">TBD</param>
        /// <param name="supervisionDecider">TBD</param>
        /// <param name="subscriptionTimeoutSettings">TBD</param>
        /// <param name="streamRefSettings">TBD</param>
        /// <param name="isDebugLogging">TBD</param>
        /// <param name="outputBurstLimit">TBD</param>
        /// <param name="isFuzzingMode">TBD</param>
        /// <param name="isAutoFusing">TBD</param>
        /// <param name="maxFixedBufferSize">TBD</param>
        /// <param name="syncProcessingLimit">TBD</param>
        public ActorMaterializerSettings(int initialInputBufferSize, int maxInputBufferSize, string dispatcher, Decider supervisionDecider, StreamSubscriptionTimeoutSettings subscriptionTimeoutSettings, StreamRefSettings streamRefSettings, bool isDebugLogging, int outputBurstLimit, bool isFuzzingMode, bool isAutoFusing, int maxFixedBufferSize, int syncProcessingLimit = DefaultlMaxFixedbufferSize)
        {
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialSize">TBD</param>
        /// <param name="maxSize">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithInputBuffer(int initialSize, int maxSize)
        {
            return new ActorMaterializerSettings(initialSize, maxSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="dispatcher">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithDispatcher(string dispatcher)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="decider">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithSupervisionStrategy(Decider decider)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, decider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="isEnabled">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithDebugLogging(bool isEnabled)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, isEnabled, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="isFuzzingMode">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithFuzzingMode(bool isFuzzingMode)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, isFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="isAutoFusing">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithAutoFusing(bool isAutoFusing)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, isAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxFixedBufferSize">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithMaxFixedBufferSize(int maxFixedBufferSize)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, maxFixedBufferSize, SyncProcessingLimit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="limit">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithSyncProcessingLimit(int limit)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, limit);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="settings">TBD</param>
        /// <returns>TBD</returns>
        public ActorMaterializerSettings WithSubscriptionTimeoutSettings(StreamSubscriptionTimeoutSettings settings)
        {
            if (Equals(settings, SubscriptionTimeoutSettings))
                return this;

            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, settings, StreamRefSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithStreamRefSettings(StreamRefSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (ReferenceEquals(settings, this.StreamRefSettings)) return this;
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher,
                SupervisionDecider, SubscriptionTimeoutSettings, settings, IsDebugLogging, OutputBurstLimit,
                IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }
    }

    /// <summary>
    /// Leaked publishers and subscribers are cleaned up when they are not used within a given deadline, configured by <see cref="StreamSubscriptionTimeoutSettings"/>.
    /// </summary>
    public sealed class StreamSubscriptionTimeoutSettings : IEquatable<StreamSubscriptionTimeoutSettings>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public readonly StreamSubscriptionTimeoutTerminationMode Mode;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="mode">TBD</param>
        /// <param name="timeout">TBD</param>
        public StreamSubscriptionTimeoutSettings(StreamSubscriptionTimeoutTerminationMode mode, TimeSpan timeout)
        {
            Mode = mode;
            Timeout = timeout;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
                return false;
            if (ReferenceEquals(obj, this))
                return true;
            if (obj is StreamSubscriptionTimeoutSettings)
                return Equals((StreamSubscriptionTimeoutSettings) obj);

            return false;
        }

        /// <inheritdoc/>
        public bool Equals(StreamSubscriptionTimeoutSettings other)
            => Mode == other.Mode && Timeout.Equals(other.Timeout);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Mode * 397) ^ Timeout.GetHashCode();
            }
        }

        /// <inheritdoc/>
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
    /// TBD
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
        /// <param name="context">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="namePrefix">TBD</param>
        /// <returns>TBD</returns>
        public static ActorMaterializer Materializer(this IActorRefFactory context, ActorMaterializerSettings settings = null, string namePrefix = null)
            => ActorMaterializer.Create(context, settings, namePrefix);
    }
}
