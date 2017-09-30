//-----------------------------------------------------------------------
// <copyright file="Attributes.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        /// <summary>
        /// TBD
        /// </summary>
        public interface IAttribute { }

        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        public sealed class InputBuffer : IAttribute, IEquatable<InputBuffer>
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
            public bool Equals(AsyncBoundary other) => true;

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is AsyncBoundary;

            /// <inheritdoc/>
            public override string ToString() => "AsyncBoundary";
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
        /// TBD
        /// </summary>
        public IEnumerable<IAttribute> AttributeList => _attributes;

        /// <summary>
        /// Get all attributes of a given type or subtype thereof
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
        public TAttr GetFirstAttribute<TAttr>() where TAttr : class, IAttribute
            => _attributes.FirstOrDefault(attr => attr is TAttr) as TAttr;

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
        /// </summary>
        /// <typeparam name="TAttr">TBD</typeparam>
        /// <param name="attribute">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains<TAttr>(TAttr attribute) where TAttr : IAttribute => _attributes.Contains(attribute);

        /// <summary>
        /// Specifies the name of the operation.
        /// If the name is null or empty the name is ignored, i.e. <see cref="None"/> is returned.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateName(string name)
            => string.IsNullOrEmpty(name) ? None : new Attributes(new Name(name));

        /// <summary>
        /// Specifies the initial and maximum size of the input buffer.
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
        /// <see cref="LogLevel.DebugLevel"/> for <paramref name="onElement"/> and <paramref name="onFinish"/>, and <see cref="LogLevel.ErrorLevel"/> for <paramref name="onError"/>.
        ///</summary>
        /// <param name="onElement">TBD</param>
        /// <param name="onFinish">TBD</param>
        /// <param name="onError">TBD</param>
        /// <returns>TBD</returns>
        public static Attributes CreateLogLevels(LogLevel onElement = LogLevel.DebugLevel,
            LogLevel onFinish = LogLevel.DebugLevel, LogLevel onError = LogLevel.ErrorLevel)
            => new Attributes(new LogLevels(onElement, onFinish, onError));

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
    /// Attributes for the <see cref="ActorMaterializer"/>. Note that more attributes defined in <see cref="ActorAttributes"/>.
    /// </summary>
    public static class ActorAttributes
    {
        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Dispatcher : Attributes.IAttribute, IEquatable<Dispatcher>
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
        public sealed class SupervisionStrategy : Attributes.IAttribute
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
        /// Specifies the name of the dispatcher.
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
    }
}