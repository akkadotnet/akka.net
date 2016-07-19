//-----------------------------------------------------------------------
// <copyright file="ActorMaterializer.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    public abstract class ActorMaterializer : IMaterializer, IDisposable
    {
        public static Config DefaultConfig()
            => ConfigurationFactory.FromResource<ActorMaterializer>("Akka.Streams.reference.conf");

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

        internal static ActorMaterializer Downcast(IMaterializer materializer)
        {
            //FIXME this method is going to cause trouble for other Materializer implementations
            if (materializer is ActorMaterializer)
                return (ActorMaterializer)materializer;

            throw new ArgumentException($"Expected {typeof(ActorMaterializer)} but got {materializer.GetType()}");
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
        
        public abstract ActorMaterializerSettings Settings { get; }

        /// <summary>
        /// Indicates if the materializer has been shut down.
        /// </summary>
        public abstract bool IsShutdown { get; }

        public abstract MessageDispatcher ExecutionContext { get; }

        public abstract ActorSystem System { get; }

        public abstract ILoggingAdapter Logger { get; }

        public abstract IActorRef Supervisor { get; }

        public abstract IMaterializer WithNamePrefix(string namePrefix);

        public abstract TMat Materialize<TMat>(IGraph<ClosedShape, TMat> runnable);

        public abstract ICancelable ScheduleOnce(TimeSpan delay, Action action);

        public abstract ICancelable ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);

        public abstract ActorMaterializerSettings EffectiveSettings(Attributes attributes);

        /// <summary>
        /// Shuts down this materializer and all the stages that have been materialized through this materializer. After
        /// having shut down, this materializer cannot be used again. Any attempt to materialize stages after having
        /// shut down will result in an <see cref="IllegalStateException"/> being thrown at materialization time.
        /// </summary>
        public abstract void Shutdown();

        protected internal abstract IActorRef ActorOf(MaterializationContext context, Props props);

        public void Dispose() => Shutdown();
    }

    /// <summary>
    /// This exception signals that an actor implementing a Reactive Streams Subscriber, Publisher or Processor
    /// has been terminated without being notified by an onError, onComplete or cancel signal. This usually happens
    /// when an ActorSystem is shut down while stream processing actors are still running.
    /// </summary>
    [Serializable]
    public class AbruptTerminationException : Exception
    {
        public readonly IActorRef Actor;

        public AbruptTerminationException(IActorRef actor)
            : base($"Processor actor [{actor}] terminated abruptly")
        {
            Actor = actor;
        }

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
        public MaterializationException(string message, Exception innerException) : base(message, innerException) { }

        protected MaterializationException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// This class describes the configurable properties of the <see cref="ActorMaterializer"/>. 
    /// Please refer to the withX methods for descriptions of the individual settings.
    /// </summary>
    public sealed class ActorMaterializerSettings
    {
        public static ActorMaterializerSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.stream.materializer");
            if(config == null)
                throw new ArgumentException("Couldn't build an actor materializer settings. `akka.stream.materializer` config path is not defined.");

            return Create(config);
        }

        private static ActorMaterializerSettings Create(Config config)
        {
            return new ActorMaterializerSettings(
                initialInputBufferSize: config.GetInt("initial-input-buffer-size"),
                maxInputBufferSize: config.GetInt("max-input-buffer-size"),
                dispatcher: config.GetString("dispatcher"),
                supervisionDecider: Deciders.StoppingDecider,
                subscriptionTimeoutSettings: StreamSubscriptionTimeoutSettings.Create(config),
                isDebugLogging: config.GetBoolean("debug-logging"),
                outputBurstLimit: config.GetInt("output-burst-limit"),
                isFuzzingMode: config.GetBoolean("debug.fuzzing-mode"),
                isAutoFusing: config.GetBoolean("auto-fusing"),
                maxFixedBufferSize: config.GetInt("max-fixed-buffer-size"),
                syncProcessingLimit: config.GetInt("sync-processing-limit"));
        }

        private const int DefaultlMaxFixedbufferSize = 1000;
        public readonly int InitialInputBufferSize;
        public readonly int MaxInputBufferSize;
        public readonly string Dispatcher;
        public readonly Decider SupervisionDecider;
        public readonly StreamSubscriptionTimeoutSettings SubscriptionTimeoutSettings;
        public readonly bool IsDebugLogging;
        public readonly int OutputBurstLimit;
        public readonly bool IsFuzzingMode;
        public readonly bool IsAutoFusing;
        public readonly int MaxFixedBufferSize;
        public readonly int SyncProcessingLimit;

        public ActorMaterializerSettings(int initialInputBufferSize, int maxInputBufferSize, string dispatcher, Decider supervisionDecider, StreamSubscriptionTimeoutSettings subscriptionTimeoutSettings, bool isDebugLogging, int outputBurstLimit, bool isFuzzingMode, bool isAutoFusing, int maxFixedBufferSize, int syncProcessingLimit = DefaultlMaxFixedbufferSize)
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

        }

        public ActorMaterializerSettings WithInputBuffer(int initialSize, int maxSize)
        {
            return new ActorMaterializerSettings(initialSize, maxSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithDispatcher(string dispatcher)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }
        
        public ActorMaterializerSettings WithSupervisionStrategy(Decider decider)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, decider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithDebugLogging(bool isEnabled)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, isEnabled, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithFuzzingMode(bool isFuzzingMode)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, isFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithAutoFusing(bool isAutoFusing)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, isAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithMaxFixedBufferSize(int maxFixedBufferSize)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, maxFixedBufferSize, SyncProcessingLimit);
        }

        public ActorMaterializerSettings WithSyncProcessingLimit(int limit)
        {
            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, SubscriptionTimeoutSettings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, limit);
        }

        public ActorMaterializerSettings WithSubscriptionTimeoutSettings(StreamSubscriptionTimeoutSettings settings)
        {
            if (Equals(settings, SubscriptionTimeoutSettings))
                return this;

            return new ActorMaterializerSettings(InitialInputBufferSize, MaxInputBufferSize, Dispatcher, SupervisionDecider, settings, IsDebugLogging, OutputBurstLimit, IsFuzzingMode, IsAutoFusing, MaxFixedBufferSize, SyncProcessingLimit);
        }
    }

    /// <summary>
    /// Leaked publishers and subscribers are cleaned up when they are not used within a given deadline, configured by <see cref="StreamSubscriptionTimeoutSettings"/>.
    /// </summary>
    public sealed class StreamSubscriptionTimeoutSettings : IEquatable<StreamSubscriptionTimeoutSettings>
    {
        public static StreamSubscriptionTimeoutSettings Create(Config config)
        {
            var c = config.GetConfig("subscription-timeout");
            var configMode = c.GetString("mode").ToLowerInvariant();
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
                timeout: c.GetTimeSpan("timeout"));
        }

        public readonly StreamSubscriptionTimeoutTerminationMode Mode;

        public readonly TimeSpan Timeout;

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
            if (obj is StreamSubscriptionTimeoutSettings)
                return Equals((StreamSubscriptionTimeoutSettings) obj);

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
        public static ActorMaterializer Materializer(this IActorRefFactory context, ActorMaterializerSettings settings = null, string namePrefix = null)
            => ActorMaterializer.Create(context, settings, namePrefix);
    }
}