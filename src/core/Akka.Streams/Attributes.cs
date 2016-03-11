using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Event;
using Akka.Streams.Supervision;

namespace Akka.Streams
{

    /// <summary>
    /// Holds attributes which can be used to alter [[akka.stream.scaladsl.Flow]] / [[akka.stream.javadsl.Flow]]
    /// or [[akka.stream.scaladsl.FlowGraph]] / [[akka.stream.javadsl.FlowGraph]] materialization.
    /// 
    /// Note that more attributes for the [[BaseActorFlowMaterializer]] are defined in [[ActorOperationAttributes]].
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
            public static readonly int Off = -1;

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

            public override string ToString() => $"AsyncBoundary";
        }

        public static readonly Attributes None = new Attributes();

        private readonly IAttribute[] _attributes;
        public IEnumerable<IAttribute> AttributeList { get { return _attributes; } }

        public Attributes(params IAttribute[] attributes)
        {
            _attributes = attributes ?? new IAttribute[0];
        }

        public TAttr GetAttribute<TAttr>(TAttr defaultIfNotFound) where TAttr : IAttribute
        {
            var result = _attributes.FirstOrDefault(attr => attr is TAttr);
            return result != null ? (TAttr)result : defaultIfNotFound;
        }

        public Attributes And(Attributes other)
        {
            if (_attributes.Length == 0) return other;
            if (!other.AttributeList.Any()) return this;
            return new Attributes(AttributeList.Union(other.AttributeList).ToArray());
        }

        internal LogLevels GetLogLevels()
        {
            return GetAttribute<LogLevels>(null);
        }

        internal string GetNameLifted()
        {
            if (_attributes.Length == 0) return null;

            var sb = new StringBuilder();
            foreach (var attribute in _attributes)
            {
                var n = attribute as Name;
                if (n != null)
                {
                    if (sb.Length != 0) sb.Append('-');
                    sb.Append(n.Value);
                }
            }

            return sb.Length == 0 ? null : sb.ToString();
        }

        internal string GetNameOrDefault(string defaultIfNotFound = "unknown-operation")
        {
            var n = GetAttribute<Name>(null);
            return n != null ? n.Value : defaultIfNotFound;
        }

        internal string GetName()
        {
            var n = GetAttribute<Name>(null);
            return n != null ? n.Value : null;
        }

        /**
         * Specifies the name of the operation.
         * If the name is null or empty the name is ignored, i.e. [[#none]] is returned.
         */
        public static Attributes CreateName(string name)
        {
            return string.IsNullOrEmpty(name) ? None : new Attributes(new Name(name));
        }

        /**
         * Specifies the initial and maximum size of the input buffer.
         */
        public static Attributes CreateInputBuffer(int initial, int max)
        {
            return new Attributes(new InputBuffer(initial, max));
        }

        /**
         * Configures `log()` stage log-levels to be used when logging.
         * Logging a certain operation can be completely disabled by using [[LogLevels.Off]].
         *
         * See [[OperationAttributes.createLogLevels]] for Java API
         */
        public static Attributes CreateLogLevels(LogLevel onElement, LogLevel onFinish, LogLevel onError)
        {
            return new Attributes(new LogLevels(onElement, onFinish, onError));
        }

        public bool Contains<TAttr>() where TAttr : IAttribute
        {
            return _attributes.Any(attr => attr is TAttr);
        }

        public override string ToString() => $"Attributes({string.Join(", ", _attributes as IEnumerable<IAttribute>)})";
    }

    /// <summary>
    /// Attributes for the [[BaseActorFlowMaterializer]]. Note that more attributes defined in [[OperationAttributes]].
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

            public override string ToString() => $"SupervisionStrategy";
        }

        /**
         * Specifies the name of the dispatcher.
         */
        public static Attributes CreateDispatcher(string dispatcherName)
        {
           return new Attributes(new Dispatcher(dispatcherName)); 
        }

        public static Attributes CreateSupervisionStrategy(Decider decider)
        {
            return new Attributes(new SupervisionStrategy(decider));
        }
    }
}