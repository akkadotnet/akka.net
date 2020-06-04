//-----------------------------------------------------------------------
// <copyright file="Attributes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Supervision;

namespace Akka.Streams
{

    /// <summary>
    /// Holds attributes which can be used to alter <see cref="Flow{TIn,TOut,TMat}"/>
    /// or <see cref="GraphDsl"/> materialization.
    /// 
    /// Note that more attributes for the <see cref="ActorMaterializer"/> are defined in <see cref="ActorAttributes"/>.
    /// </summary>
    public sealed class Attributes
    {
        /// <summary>
        /// TBD
        /// </summary>
        public interface IAttribute { }

        /// <summary>
        /// Attributes that are always present (is defined with default values by the materializer)
        /// 
        /// Not for user extension
        /// </summary>
        public interface IMandatoryAttribute : IAttribute { }

        /// <summary>
        /// Specifies the name of the operation.
        /// 
        /// Use factory method <see cref="CreateName(string)"/> to create instances.
        /// </summary>
        public sealed class Name : IAttribute, IEquatable<Name>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Value;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="value">TBD</param>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="value"/> is undefined.
            /// </exception>
            public Name(string value)
            {
                if (string.IsNullOrEmpty(value)) throw new ArgumentNullException(nameof(value), "Name attribute cannot be empty");
                Value = value;
            }

            /// <inheritdoc/>
            public bool Equals(Name other) => !ReferenceEquals(other, null) && Equals(Value, other.Value);

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is Name && Equals((Name)obj);

            /// <inheritdoc/>
            public override int GetHashCode() => Value.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"Name({Value})";
        }

        /// <summary>
        /// Each asynchronous piece of a materialized stream topology is executed by one Actor
        /// that manages an input buffer for all inlets of its shape. This attribute configures
        /// the initial and maximal input buffer in number of elements for each inlet.
        /// 
        /// Use factory method <see cref="CreateInputBuffer(int, int)"/> to create instances.
        /// </summary>
        public sealed class InputBuffer : IMandatoryAttribute, IEquatable<InputBuffer>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Initial;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Max;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="initial">TBD</param>
            /// <param name="max">TBD</param>
            public InputBuffer(int initial, int max)
            {
                Initial = initial;
                Max = max;
            }

            /// <inheritdoc/>
            public bool Equals(InputBuffer other)
            {
                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;
                return Initial == other.Initial && Max == other.Max;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is InputBuffer && Equals((InputBuffer) obj);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (Initial*397) ^ Max;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"InputBuffer(initial={Initial}, max={Max})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class LogLevels : IAttribute, IEquatable<LogLevels>
        {
            /// <summary>
            /// Use to disable logging on certain operations when configuring <see cref="LogLevels"/>
            /// </summary>
            public static readonly LogLevel Off = Logging.LogLevelFor("off");

            /// <summary>
            /// TBD
            /// </summary>
            public readonly LogLevel OnElement;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly LogLevel OnFinish;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly LogLevel OnFailure;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="onElement">TBD</param>
            /// <param name="onFinish">TBD</param>
            /// <param name="onFailure">TBD</param>
            public LogLevels(LogLevel onElement, LogLevel onFinish, LogLevel onFailure)
            {
                OnElement = onElement;
                OnFinish = onFinish;
                OnFailure = onFailure;
            }

            /// <inheritdoc/>
            public bool Equals(LogLevels other)
            {
                if (ReferenceEquals(other, null))
                    return false;
                if (ReferenceEquals(other, this))
                    return true;

                return OnElement == other.OnElement && OnFinish == other.OnFinish && OnFailure == other.OnFailure;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is LogLevels && Equals((LogLevels) obj);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (int) OnElement;
                    hashCode = (hashCode*397) ^ (int) OnFinish;
                    hashCode = (hashCode*397) ^ (int) OnFailure;
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"LogLevel(element={OnElement}, finish={OnFinish}, failure={OnFailure})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class AsyncBoundary : IAttribute, IEquatable<AsyncBoundary>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly AsyncBoundary Instance = new AsyncBoundary();
            private AsyncBoundary() { }

            /// <inheritdoc/>
            public bool Equals(AsyncBoundary other) => other is AsyncBoundary;

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is AsyncBoundary;

            /// <inheritdoc/>
            public override string ToString() => "AsyncBoundary";
        }

        /// <summary>
        /// Cancellation strategies provide a way to configure the behavior of a stage 
        /// when `cancelStage` is called.
        /// 
        /// It is only relevant for stream components that have more than one output 
        /// and do not define a custom cancellation behavior by overriding `onDownstreamFinish`. 
        /// In those cases, if the first output is cancelled, the default behavior
        /// is to call `cancelStage` which shuts down the stage completely. 
        /// The given strategy will allow customization of how the shutdown procedure should be done precisely.
        /// </summary>
        public sealed class CancellationStrategy:IMandatoryAttribute
        {
            internal CancellationStrategy Default { get; } = new CancellationStrategy(new PropagateFailure());

            public IStrategy Strategy { get; }

            public CancellationStrategy(IStrategy strategy)
            {
                Strategy = strategy;
            }

            public interface IStrategy { }

            /// <summary>
            /// Strategy that treats `cancelStage` the same as `completeStage`, i.e. all inlets are cancelled (propagating the
            /// cancellation cause) and all outlets are regularly completed.
            /// 
            /// This used to be the default behavior before Akka 2.6.
            ///
            /// This behavior can be problematic in stacks of BidiFlows where different layers of the stack are both connected
            /// through inputs and outputs. In this case, an error in a doubly connected component triggers both a cancellation
            /// going upstream and an error going downstream.Since the stack might be connected to those components with inlets and
            /// outlets, a race starts whether the cancellation or the error arrives first.If the error arrives first, that's usually
            /// good because then the error can be propagated both on inlets and outlets.However, if the cancellation arrives first,
            /// the previous default behavior to complete the stage will lead other outputs to be completed regularly.The error
            /// which arrive late at the other hand will just be ignored (that connection will have been cancelled already and also
            /// the paths through which the error could propagates are already shut down).
            /// </summary>
            public class CompleteStage : IStrategy { }

            /// <summary>
            /// Strategy that treats `cancelStage` the same as `failStage`, i.e. all inlets are cancelled (propagating the
            /// cancellation cause) and all outlets are failed propagating the cause from cancellation.
            /// </summary>
            public class FailStage : IStrategy { }

            /// <summary>
            /// Strategy that treats `cancelStage` in different ways depending on the cause that was given to the cancellation.
            /// 
            /// If the cause was a regular, active cancellation (`SubscriptionWithCancelException.NoMoreElementsNeeded`), the stage
            /// receiving this cancellation is completed regularly.
            /// 
            /// If another cause was given, this is treated as an error and the behavior is the same as with `failStage`.
            /// 
            /// This is a good default strategy.
            /// </summary>
            public class PropagateFailure : IStrategy { }

            /// <summary>
            /// Strategy that allows to delay any action when `cancelStage` is invoked.
            /// 
            /// The idea of this strategy is to delay any action on cancellation because it is expected that the stage is completed
            /// through another path in the meantime. The downside is that a stage and a stream may live longer than expected if no
            /// such signal is received and cancellation is invoked later on. In streams with many stages that all apply this strategy,
            /// this strategy might significantly delay the propagation of a cancellation signal because each upstream stage might impose
            /// such a delay. During this time, the stream will be mostly "silent", i.e. it cannot make progress because of backpressure,
            /// but you might still be able observe a long delay at the ultimate source.
            /// </summary>
            public class AfterDelay : IStrategy 
            { 
                public TimeSpan Delay { get; }
                public IStrategy Strategy { get; }

                public AfterDelay(TimeSpan delay, IStrategy strategy)
                {
                    Delay = delay;
                    Strategy = strategy;
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public static readonly Attributes None = new Attributes();

        private readonly IAttribute[] _attributes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        public Attributes(params IAttribute[] attributes)
        {
            _attributes = attributes ?? new IAttribute[0];
        }

        /// <summary>
        /// The list is ordered with the most specific attribute first, least specific last.
        /// 
        /// Note that operators in general should not inspect the whole hierarchy but instead use
        /// <see cref="GetAttribute{TAttr}"/> to get the most specific attribute value.
        /// </summary>
        public IEnumerable<IAttribute> AttributeList => _attributes;

        /// <summary>
        /// Note that this must only be used during traversal building and not during materialization
        /// as it will then always return true because of the defaults from the ActorMaterializerSettings
        /// 
        /// INTERNAL API
        /// </summary>
        internal bool IsAsync
            => _attributes.Count() > 0 && 
                _attributes.Any(
                    attr => attr is AsyncBoundary || 
                    attr is ActorAttributes.Dispatcher);

        /// <summary>
        /// Get all attributes of a given type (or subtypes thereof).
        /// 
        /// Note that operators in general should not inspect the whole hierarchy but instead use
        /// <see cref="GetAttribute{TAttr}"/> to get the most specific attribute value.
        /// 
        /// The list is ordered with the most specific attribute first, least specific last.
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <returns>TBD</returns>
        public IEnumerable<TAttr> GetAttributeList<TAttr>() where TAttr : IAttribute
            => _attributes.Length == 0 ? Enumerable.Empty<TAttr>() : _attributes.Where(a => a is TAttr).Cast<TAttr>();

        /// <summary>
        /// Get the last (most specific) attribute of a given type or subtype thereof.
        /// If no such attribute exists the default value is returned.
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <param name="defaultIfNotFound">TBD</param>
        /// <returns>TBD</returns>
        public TAttr GetAttribute<TAttr>(TAttr defaultIfNotFound) where TAttr : class, IAttribute
            => GetAttribute<TAttr>() ?? defaultIfNotFound;

        /// <summary>
        /// Get the first (least specific) attribute of a given type or subtype thereof.
        /// If no such attribute exists the default value is returned.
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <param name="defaultIfNotFound">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Attributes should always be most specific, use GetAttribute<TAttr>()")]
        public TAttr GetFirstAttribute<TAttr>(TAttr defaultIfNotFound) where TAttr : class, IAttribute
            => GetFirstAttribute<TAttr>() ?? defaultIfNotFound;

        /// <summary>
        /// Get the last (most specific) attribute of a given type or subtype thereof.
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <returns>TBD</returns>
        public TAttr GetAttribute<TAttr>() where TAttr : class, IAttribute
            => _attributes.LastOrDefault(attr => attr is TAttr) as TAttr;

        /// <summary>
        /// Get the first (least specific) attribute of a given type or subtype thereof.
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <returns>TBD</returns>
        [Obsolete("Attributes should always be most specific, use GetAttribute<TAttr>()")]
        public TAttr GetFirstAttribute<TAttr>() where TAttr : class, IAttribute
            => _attributes.FirstOrDefault(attr => attr is TAttr) as TAttr;

        /// <summary>
        /// Get the most specific of one of the mandatory attributes. Mandatory attributes are guaranteed
        /// to always be among the attributes when the attributes are coming from a materialization.
        /// </summary>
        /// <typeparam name="TAttr"></typeparam>
        /// <returns></returns>
        public TAttr GetMandatoryAttribute<TAttr>() where TAttr : class, IMandatoryAttribute
        {
            if (!(_attributes.First(attr => attr is TAttr) is TAttr result))
                throw new IllegalStateException($"Mandatory attribute [{typeof(TAttr)}] not found.");
            return result;
        }

        /// <summary>
        /// Adds given attributes to the end of these attributes.
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public Attributes And(Attributes other)
        {
            if (_attributes.Length == 0)
                return other;
            if (!other.AttributeList.Any())
                return this;
            return new Attributes(_attributes.Concat(other.AttributeList).ToArray());
        }

        /// <summary>
        /// Adds given attribute to the end of these attributes.
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public Attributes And(IAttribute other) => new Attributes(_attributes.Concat(new[] { other }).ToArray());

        /// <summary>
        /// Extracts Name attributes and concatenates them.
        /// </summary>
        /// <returns>TBD</returns>
        public string GetNameLifted() => GetNameOrDefault(null);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="defaultIfNotFound">TBD</param>
        /// <returns>TBD</returns>
        public string GetNameOrDefault(string defaultIfNotFound = "unknown-operation")
        {
            if (_attributes.Length == 0)
                return null;

            var sb = new StringBuilder();
            foreach (var nameAttribute in _attributes.OfType<Name>())
            {
                // FIXME this UrlEncode is a bug IMO, if that format is important then that is how it should be store in Name
                var encoded = Uri.EscapeDataString(nameAttribute.Value);

                if (sb.Length != 0)
                    sb.Append('-');

                sb.Append(encoded);
            }

            return sb.Length == 0 ? defaultIfNotFound : sb.ToString();
        }

        /// <summary>
        /// Test whether the given attribute is contained within this attributes list.
        /// 
        /// Note that operators in general should not inspect the whole hierarchy but instead use
        /// `get` to get the most specific attribute value.
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <param name="attribute">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains<TAttr>(TAttr attribute) where TAttr : IAttribute => _attributes.Contains(attribute);

        /// <summary>
        /// Specifies the name of the operation.
        /// If the name is null or empty the name is ignored, i.e. <see cref="None"/> is returned.
        /// 
        /// When using this method the name is encoded with `Uri.EscapeUriString` because
        /// the name is sometimes used as part of actor name. If that is not desired
        /// the name can be added in it's raw format using `.And(new Attributes(new Name(name)))`.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateName(string name)
            => string.IsNullOrEmpty(name) ? 
                None : 
                new Attributes(new Name(Uri.EscapeUriString(name)));

        /// <summary>
        /// Each asynchronous piece of a materialized stream topology is executed by one Actor
        /// that manages an input buffer for all inlets of its shape. This attribute configures
        /// the initial and maximal input buffer in number of elements for each inlet.
        /// </summary>
        /// <param name="initial">TBD</param>
        /// <param name="max">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateInputBuffer(int initial, int max) => new Attributes(new InputBuffer(initial, max));

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Attributes CreateAsyncBoundary() => new Attributes(AsyncBoundary.Instance);

        ///<summary>
        /// Configures <see cref="FlowOperations.Log{TIn,TOut,TMat}"/> stage log-levels to be used when logging.
        /// Logging a certain operation can be completely disabled by using <see cref="LogLevels.Off"/>
        ///
        /// Passing in null as any of the arguments sets the level to its default value, which is:
        /// <see cref="Akka.Event.LogLevel.DebugLevel"/> for <paramref name="onElement"/> and <paramref name="onFinish"/>, and <see cref="Akka.Event.LogLevel.ErrorLevel"/> for <paramref name="onError"/>.
        ///</summary>
        /// <param name="onElement">TBD</param>
        /// <param name="onFinish">TBD</param>
        /// <param name="onError">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateLogLevels(LogLevel onElement = LogLevel.DebugLevel,
            LogLevel onFinish = LogLevel.DebugLevel, LogLevel onError = LogLevel.ErrorLevel)
            => new Attributes(new LogLevels(onElement, onFinish, onError));

        // TODO: different than scala code, investigate later.
        /// <summary>
        /// Compute a name by concatenating all Name attributes that the given module
        /// has, returning the given default value if none are found.
        /// </summary>
        /// <param name="module">TBD</param>
        /// <param name="defaultIfNotFound">TBD</param>
        /// <returns>TBD</returns>
        public static string ExtractName(IModule module, string defaultIfNotFound)
        {
            var copy = module as CopiedModule;

            return copy != null
                ? copy.Attributes.And(copy.CopyOf.Attributes).GetNameOrDefault(defaultIfNotFound)
                : module.Attributes.GetNameOrDefault(defaultIfNotFound);
        }

        /// <inheritdoc/>
        public override string ToString() => $"Attributes({string.Join(", ", _attributes as IEnumerable<IAttribute>)})";
    }

    /// <summary>
    /// Attributes for the <see cref="ActorMaterializer"/>. Note that more attributes defined in <see cref="Attributes"/>.
    /// </summary>
    public static class ActorAttributes
    {
        /// <summary>
        /// Configures the dispatcher to be used by streams.
        /// 
        /// Use factory method <see cref="CreateDispatcher(string)"/> to create instances.
        /// </summary>
        public sealed class Dispatcher : Attributes.IMandatoryAttribute, IEquatable<Dispatcher>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string Name;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="name">TBD</param>
            public Dispatcher(string name)
            {
                Name = name;
            }

            /// <inheritdoc/>
            public bool Equals(Dispatcher other)
            {
                if (ReferenceEquals(other, null))
                    return false;
                if (ReferenceEquals(other, this))
                    return true;
                return Equals(Name, other.Name);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is Dispatcher && Equals((Dispatcher) obj);

            /// <inheritdoc/>
            public override int GetHashCode() => Name?.GetHashCode() ?? 0;

            /// <inheritdoc/>
            public override string ToString() => $"Dispatcher({Name})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class SupervisionStrategy : Attributes.IMandatoryAttribute
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Decider Decider;

            /// <summary>
            /// Initializes a new instance of the <see cref="SupervisionStrategy"/> class.
            /// </summary>
            /// <param name="decider">TBD</param>
            public SupervisionStrategy(Decider decider)
            {
                Decider = decider;
            }

            /// <inheritdoc/>
            public override string ToString() => "SupervisionStrategy";
        }

        /// <summary>
        /// Enables additional low level troubleshooting logging at DEBUG log level
        /// 
        /// Use factory method `CreateDebugLogging` to create.
        /// </summary>
        public sealed class DebugLogging :
            Attributes.IMandatoryAttribute,
            IEquatable<DebugLogging>
        {
            public bool Enabled { get; }

            public DebugLogging(bool enabled)
            {
                Enabled = enabled;
            }

            /// <inheritdoc/>
            public bool Equals(DebugLogging other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Enabled == other.Enabled;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is DebugLogging attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Enabled.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"DebugLogging(enabled={Enabled})";
        }

        /// <summary>
        /// Defines a timeout for stream subscription and what action to take when that hits.
        /// 
        /// Use factory method `CreateStreamSubscriptionTimeout` to create.
        /// </summary>
        public sealed class StreamSubscriptionTimeout :
            Attributes.IMandatoryAttribute,
            IEquatable<StreamSubscriptionTimeout>
        {
            public TimeSpan Timeout { get; }
            public StreamSubscriptionTimeoutTerminationMode Mode { get; }

            public StreamSubscriptionTimeout(TimeSpan timeout, StreamSubscriptionTimeoutTerminationMode mode)
            {
                if (timeout.Ticks < 0)
                    throw new ArgumentException("Timeout must be finite.", nameof(timeout));
                Timeout = timeout;
                Mode = mode;
            }

            public bool Equals(StreamSubscriptionTimeout other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Timeout.Equals(other.Timeout) && Mode.Equals(other.Mode);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is StreamSubscriptionTimeout attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var initial = Timeout.GetHashCode();
                    return (initial * 397) ^ Mode.GetHashCode();
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"StreamSubscriptionTimeout(timeout={Timeout.TotalMilliseconds}ms, mode={Mode})";
        }

        /// <summary>
        /// Maximum number of elements emitted in batch if downstream signals large demand.
        /// 
        /// Use factory method `CreateOutputBurstLimit` to create.
        /// </summary>
        public sealed class OutputBurstLimit :
            Attributes.IMandatoryAttribute,
            IEquatable<OutputBurstLimit>
        {
            public int Limit { get; }

            public OutputBurstLimit(int limit)
            {
                Limit = limit;
            }

            public bool Equals(OutputBurstLimit other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Limit == other.Limit;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is OutputBurstLimit attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Limit.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"OutputBurstLimit(limit={Limit})";
        }

        /// <summary>
        /// Test utility: fuzzing mode means that GraphStage events are not processed
        /// in FIFO order within a fused subgraph, but randomized.
        /// 
        /// Use factory method `CreateFuzzingMode` to create.
        /// </summary>
        public sealed class FuzzingMode :
            Attributes.IMandatoryAttribute,
            IEquatable<FuzzingMode>
        {
            public bool Enabled { get; }

            public FuzzingMode(bool enabled)
            {
                Enabled = enabled;
            }

            public bool Equals(FuzzingMode other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Enabled == other.Enabled;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is FuzzingMode attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Enabled.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"FuzzingMode(enabled={Enabled})";
        }

        /// <summary>
        /// Configure the maximum buffer size for which a FixedSizeBuffer will be preallocated.
        /// This defaults to a large value because it is usually better to fail early when
        /// system memory is not sufficient to hold the buffer.
        /// 
        /// Use factory method `CreateMaxFixedBufferSize` to create.
        /// </summary>
        public sealed class MaxFixedBufferSize :
            Attributes.IMandatoryAttribute,
            IEquatable<MaxFixedBufferSize>
        {
            public int Size { get; }

            public MaxFixedBufferSize(int size)
            {
                Size = size;
            }

            public bool Equals(MaxFixedBufferSize other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Size == other.Size;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is MaxFixedBufferSize attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Size.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"MaxFixedBufferSize(size={Size})";
        }

        /// <summary>
        /// Limit for number of messages that can be processed synchronously 
        /// in stream to substream communication.
        /// 
        /// Use factory method `CreateSyncProcessingLimit` to create.
        /// </summary>
        public sealed class SyncProcessingLimit :
            Attributes.IMandatoryAttribute,
            IEquatable<SyncProcessingLimit>
        {
            public int Limit { get; }

            public SyncProcessingLimit(int limit)
            {
                Limit = limit;
            }

            public bool Equals(SyncProcessingLimit other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Limit == other.Limit;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is SyncProcessingLimit attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Limit.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"SyncProcessingLimit(limit={Limit})";
        }

        /// <summary>
        /// Specifies the name of the dispatcher. This also adds an async boundary.
        /// </summary>
        /// <param name="dispatcherName">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateDispatcher(string dispatcherName) => new Attributes(new Dispatcher(dispatcherName));

        /// <summary>
        /// Decides how exceptions from user are to be handled
        /// <para>
        /// Stages supporting supervision strategies explicitly document that they do so. If a stage does not document
        /// support for these, it should be assumed it does not support supervision.
        /// </para>
        /// </summary>
        /// <param name="strategy">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateSupervisionStrategy(Decider strategy)
            => new Attributes(new SupervisionStrategy(strategy));

        /// <summary>
        /// Enables additional low level troubleshooting logging at DEBUG log level
        /// </summary>
        /// <param name="enabled"></param>
        /// <returns></returns>
        public static Attributes CreateDebugLogging(bool enabled)
            => new Attributes(new DebugLogging(enabled));

        /// <summary>
        /// Defines a timeout for stream subscription and what action to take when that hits.
        /// </summary>
        /// <param name="timeout"></param>
        /// <param name="mode"></param>
        /// <returns></returns>
        public static Attributes CreateStreamSubscriptionTimeout(
            TimeSpan timeout, 
            StreamSubscriptionTimeoutTerminationMode mode)
            => new Attributes(new StreamSubscriptionTimeout(timeout, mode));

        /// <summary>
        /// Maximum number of elements emitted in batch if downstream signals large demand.
        /// </summary>
        /// <param name="limit"></param>
        /// <returns></returns>
        public static Attributes CreateOutputBurstLimit(int limit)
            => new Attributes(new OutputBurstLimit(limit));

        /// <summary>
        /// Test utility: fuzzing mode means that GraphStage events are not processed
        /// in FIFO order within a fused subgraph, but randomized.
        /// </summary>
        /// <param name="enabled"></param>
        /// <returns></returns>
        public static Attributes CreateFuzzingMode(bool enabled)
            => new Attributes(new FuzzingMode(enabled));

        /// <summary>
        /// Configure the maximum buffer size for which a FixedSizeBuffer will be preallocated.
        /// This defaults to a large value because it is usually better to fail early when
        /// system memory is not sufficient to hold the buffer.
        /// </summary>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Attributes CreateMaxFixedBufferSize(int size)
            => new Attributes(new MaxFixedBufferSize(size));

        /// <summary>
        /// Limit for number of messages that can be processed synchronously in stream to substream communication
        /// </summary>
        /// <param name="limit"></param>
        /// <returns></returns>
        public static Attributes CreateSyncProcessingLimit(int limit)
            => new Attributes(new SyncProcessingLimit(limit));
    }
    
    /// <summary>
    /// Attributes for stream refs (<see cref="ISourceRef{TOut}"/> and <see cref="ISinkRef{TIn}"/>).
    /// Note that more attributes defined in <see cref="Attributes"/> and <see cref="ActorAttributes"/>.
    /// </summary>
    public static class StreamRefAttributes
    {
        /// <summary>
        /// Attributes specific to stream refs.
        /// 
        /// Not for user extension.
        /// </summary>
        public interface IStreamRefAttribute : Attributes.IAttribute { }

        /// <summary>
        /// Specifies the subscription timeout within which the remote side MUST subscribe to the handed out stream reference.
        /// </summary>
        public sealed class SubscriptionTimeout : IStreamRefAttribute, IEquatable<SubscriptionTimeout>
        {
            public TimeSpan Timeout { get; }

            public SubscriptionTimeout(TimeSpan timeout)
            {
                if (timeout.Ticks < 0)
                    throw new ArgumentException("Timeout must be finite.", nameof(timeout));

                Timeout = timeout;
            }

            public bool Equals(SubscriptionTimeout other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Timeout.Equals(other.Timeout);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is SubscriptionTimeout attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Timeout.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"SubscriptionTimeout(timeout={Timeout.TotalMilliseconds}ms)";
        }

        /// <summary>
        /// Specifies the size of the buffer on the receiving side that is eagerly filled even without demand.
        /// </summary>
        public sealed class BufferCapacity : IStreamRefAttribute, IEquatable<BufferCapacity>
        {
            public int Capacity { get; }
            public BufferCapacity (int capacity)
            {
                if (capacity <= 0)
                    throw new ArgumentException("Capacity must be greater than zero", nameof(capacity));
                Capacity = capacity;
            }

            public bool Equals(BufferCapacity other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Capacity == other.Capacity;
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is BufferCapacity attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Capacity.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"BufferCapacity(capacity={Capacity})";
        }

        /// <summary>
        /// If no new elements arrive within this timeout, demand is redelivered.
        /// </summary>
        public sealed class DemandRedeliveryInterval : IStreamRefAttribute, IEquatable<DemandRedeliveryInterval>
        {
            public TimeSpan Timeout { get; }

            public DemandRedeliveryInterval(TimeSpan timeout)
            {
                if (timeout.Ticks < 0)
                    throw new ArgumentException("Timeout must be finite.", nameof(timeout));

                Timeout = timeout;
            }

            public bool Equals(DemandRedeliveryInterval other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Timeout.Equals(other.Timeout);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is DemandRedeliveryInterval attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Timeout.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"DemandRedeliveryInterval(timeout={Timeout.TotalMilliseconds}ms)";
        }

        /// <summary>
        /// The time between the Terminated signal being received and when the local SourceRef determines to fail itself
        /// </summary>
        public sealed class FinalTerminationSignalDeadline : IStreamRefAttribute, IEquatable<FinalTerminationSignalDeadline>
        {
            public TimeSpan Timeout { get; }

            public FinalTerminationSignalDeadline(TimeSpan timeout)
            {
                if (timeout.Ticks < 0)
                    throw new ArgumentException("Timeout must be finite.", nameof(timeout));

                Timeout = timeout;
            }

            public bool Equals(FinalTerminationSignalDeadline other)
            {
                if (other is null) return false;
                if (ReferenceEquals(this, other)) return true;
                return Timeout.Equals(other.Timeout);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is FinalTerminationSignalDeadline attr && Equals(attr);

            /// <inheritdoc/>
            public override int GetHashCode() => Timeout.GetHashCode();

            /// <inheritdoc/>
            public override string ToString() => $"FinalTerminationSignalDeadline(timeout={Timeout.TotalMilliseconds}ms)";
        }

        /// <summary>
        /// Specifies the subscription timeout within which the remote side MUST subscribe to the handed out stream reference.
        /// </summary>
        public static Attributes CreateSubscriptionTimeout(TimeSpan timeout) => new Attributes(new SubscriptionTimeout(timeout));

        /// <summary>
        /// Specifies the size of the buffer on the receiving side that is eagerly filled even without demand.
        /// </summary>
        public static Attributes CreateBufferCapacity(int capacity)
            => new Attributes(new BufferCapacity(capacity));


        /// <summary>
        /// If no new elements arrive within this timeout, demand is redelivered.
        /// </summary>
        public static Attributes CreateDemandRedeliveryInterval(TimeSpan timeout) 
            => new Attributes(new DemandRedeliveryInterval(timeout));

        /// <summary>
        /// The time between the Terminated signal being received and when the local SourceRef determines to fail itself
        /// </summary>
        public static Attributes CreateFinalTerminationSignalDeadline(TimeSpan timeout) 
            => new Attributes(new FinalTerminationSignalDeadline(timeout));
    }
}
