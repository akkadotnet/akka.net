using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Event;
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
        public interface IAttribute { }

        public sealed class Name : IAttribute
        {
            public readonly string Value;

            public Name(string value)
            {
                Value = value;
            }

            public override string ToString() => $"Name({Value})";
        }

        public sealed class InputBuffer : IAttribute
        {
            public readonly int Initial;
            public readonly int Max;

            public InputBuffer(int initial, int max)
            {
                Initial = initial;
                Max = max;
            }

            public override string ToString() => $"InputBuffer(initial={Initial}, max={Max})";
        }

        public sealed class LogLevels : IAttribute
        {
            /// <summary>
            /// Use to disable logging on certain operations when configuring <see cref="LogLevels"/>
            /// </summary>
            public static readonly LogLevel Off = Logging.LogLevelFor("off");

            public readonly LogLevel OnElement;
            public readonly LogLevel OnFinish;
            public readonly LogLevel OnFailure;

            public LogLevels(LogLevel onElement, LogLevel onFinish, LogLevel onFailure)
            {
                OnElement = onElement;
                OnFinish = onFinish;
                OnFailure = onFailure;
            }

            public override string ToString() => $"LogLevel(element={OnElement}, finish={OnFinish}, failure={OnFailure})";
        }
        
        public sealed class AsyncBoundary : IAttribute
        {
            public static readonly AsyncBoundary Instance = new AsyncBoundary();
            private AsyncBoundary() { }

            public override string ToString() => "AsyncBoundary";
        }

        public static readonly Attributes None = new Attributes();

        private readonly IAttribute[] _attributes;

        public Attributes(params IAttribute[] attributes)
        {
            _attributes = attributes ?? new IAttribute[0];
        }

        public IEnumerable<IAttribute> AttributeList => _attributes;

        /// <summary>
        /// Get all attributes of a given type or subtype thereof
        /// </summary>
        public IEnumerable<TAttr> GetAttributeList<TAttr>() where TAttr : IAttribute
            => _attributes.Length == 0 ? Enumerable.Empty<TAttr>() : _attributes.Where(a => a is TAttr).Cast<TAttr>();

        /// <summary>
        /// Get the last (most specific) attribute of a given type or subtype thereof.
        /// If no such attribute exists the default value is returned.
        /// </summary>
        public TAttr GetAttribute<TAttr>(TAttr defaultIfNotFound) where TAttr : class, IAttribute
            => GetAttribute<TAttr>() ?? defaultIfNotFound;

        /// <summary>
        /// Get the first (least specific) attribute of a given type or subtype thereof.
        /// If no such attribute exists the default value is returned.
        /// </summary>
        public TAttr GetFirstAttribute<TAttr>(TAttr defaultIfNotFound) where TAttr : class, IAttribute
            => GetFirstAttribute<TAttr>() ?? defaultIfNotFound;

        /// <summary>
        /// Get the last (most specific) attribute of a given type or subtype thereof.
        /// </summary>
        public TAttr GetAttribute<TAttr>() where TAttr : class, IAttribute
            => _attributes.LastOrDefault(attr => attr is TAttr) as TAttr;

        /// <summary>
        /// Get the first (least specific) attribute of a given type or subtype thereof.
        /// </summary>
        public TAttr GetFirstAttribute<TAttr>() where TAttr : class, IAttribute
            => _attributes.FirstOrDefault(attr => attr is TAttr) as TAttr;

        /// <summary>
        /// Adds given attributes to the end of these attributes.
        /// </summary>
        public Attributes And(Attributes other)
        {
            if (_attributes.Length == 0) return other;
            if (!other.AttributeList.Any()) return this;
            return new Attributes(_attributes.Concat(other.AttributeList).ToArray());
        }

        /// <summary>
        /// Adds given attribute to the end of these attributes.
        /// </summary>
        public Attributes And(IAttribute other) => new Attributes(_attributes.Concat(new[] {other}).ToArray());

        /// <summary>
        /// Extracts Name attributes and concatenates them.
        /// </summary>
        public string GetNameLifted() => GetNameOrDefault(null);

        internal string GetNameOrDefault(string defaultIfNotFound = "unknown-operation")
        {
            if (_attributes.Length == 0)
                return null;

            var sb = new StringBuilder();
            foreach (var attribute in _attributes)
            {
                var n = attribute as Name;
                if (n != null)
                {
                    //FIXME this UrlEncode is a bug IMO, if that format is important then that is how it should be store in Name
                    var encoded = Uri.EscapeDataString(n.Value);

                    if (sb.Length != 0)
                        sb.Append('-');

                    sb.Append(encoded);
                }
            }

            return sb.Length == 0 ? defaultIfNotFound : sb.ToString();
        }

        /// <summary>
        /// Test whether the given attribute is contained within this attributes list.
        /// </summary>
        public bool Contains<TAttr>(TAttr attribute) where TAttr : IAttribute => _attributes.Contains(attribute);

        /// <summary>
        /// Specifies the name of the operation.
        /// If the name is null or empty the name is ignored, i.e. <see cref="None"/> is returned.
        /// </summary>
        public static Attributes CreateName(string name)
            => string.IsNullOrEmpty(name) ? None : new Attributes(new Name(name));

        /// <summary>
        /// Specifies the initial and maximum size of the input buffer.
        /// </summary>
        public static Attributes CreateInputBuffer(int initial, int max)
        {
            return new Attributes(new InputBuffer(initial, max));
        }

        ///<summary>
        /// Configures `log()` stage log-levels to be used when logging.
        /// Logging a certain operation can be completely disabled by using <see cref="LogLevels.Off"/>
        ///
        /// Passing in null as any of the arguments sets the level to its default value, which is:
        /// <see cref="LogLevel.DebugLevel"/> for <paramref name="onElement"/> and <paramref name="onFinish"/>, and <see cref="LogLevel.ErrorLevel"/> for <paramref name="onError"/>.
        ///</summary>
        public static Attributes CreateLogLevels(LogLevel onElement = LogLevel.DebugLevel,
            LogLevel onFinish = LogLevel.DebugLevel, LogLevel onError = LogLevel.ErrorLevel)
            => new Attributes(new LogLevels(onElement, onFinish, onError));

        /// <summary>
        /// Compute a name by concatenating all Name attributes that the given module
        /// has, returning the given default value if none are found.
        /// </summary>
        public static string ExtractName(IModule module, string defaultIfNotFound)
        {
            var copy = module as CopiedModule;

            return copy != null
                ? copy.Attributes.And(copy.CopyOf.Attributes).GetNameOrDefault(defaultIfNotFound)
                : module.Attributes.GetNameOrDefault(defaultIfNotFound);
        }

        public override string ToString() => $"Attributes({string.Join(", ", _attributes as IEnumerable<IAttribute>)})";
    }

    /// <summary>
    /// Attributes for the <see cref="ActorMaterializer"/>. Note that more attributes defined in <see cref="ActorAttributes"/>.
    /// </summary>
    public static class ActorAttributes
    {
        public sealed class Dispatcher : Attributes.IAttribute
        {
            public readonly string Name;

            public Dispatcher(string name)
            {
                Name = name;
            }

            public override string ToString() => $"Dispatcher({Name})";
        }

        public sealed class SupervisionStrategy : Attributes.IAttribute
        {
            public readonly Decider Decider;

            public SupervisionStrategy(Decider decider)
            {
                Decider = decider;
            }

            public override string ToString() => "SupervisionStrategy";
        }

        /// <summary>
        /// Specifies the name of the dispatcher.
        /// </summary>
        public static Attributes CreateDispatcher(string dispatcherName) => new Attributes(new Dispatcher(dispatcherName));

        /// <summary>
        /// Specifies the SupervisionStrategy.
        /// Decides how exceptions from user are to be handled
        /// </summary>
        public static Attributes CreateSupervisionStrategy(Decider strategy)
            => new Attributes(new SupervisionStrategy(strategy));
    }
}