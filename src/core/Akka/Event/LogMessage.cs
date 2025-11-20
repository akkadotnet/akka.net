//-----------------------------------------------------------------------
// <copyright file="LogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Akka.Annotations;

namespace Akka.Event
{
    /// <summary>
    /// Extension methods for creating <see cref="LogMessage"/> instances.
    /// </summary>
    public static class LogMessageExtensions{
        
    }
    
    /// <summary>
    /// Represents a log message which is composed of a format string and format args.
    /// </summary>
    /// <remarks>
    /// Call ToString to get the formatted output.
    /// Supports semantic logging by extracting property names from message templates.
    /// </remarks>
    public abstract class LogMessage
    {
        protected readonly ILogMessageFormatter Formatter;
        private IReadOnlyList<string>? _propertyNames;
        private IReadOnlyDictionary<string, object>? _properties;

        /// <summary>
        /// Gets the format string of this log message.
        /// </summary>
        public string Format { get; private set; }

        /// <summary>
        /// Gets the property names extracted from the message template.
        /// For positional templates like "{0} and {1}", returns ["0", "1"].
        /// For named templates like "{UserId} logged in", returns ["UserId"].
        /// This property uses lazy initialization and caching for performance.
        /// </summary>
        public IReadOnlyList<string> PropertyNames
        {
            get
            {
                if (_propertyNames == null)
                    _propertyNames = MessageTemplateParser.GetPropertyNames(Format);
                return _propertyNames;
            }
        }

        /// <summary>
        /// Initializes an instance of the LogMessage with the specified formatter, format and args.
        /// </summary>
        /// <param name="formatter">The formatter for the LogMessage.</param>
        /// <param name="format">The string format of the LogMessage.</param>
        public LogMessage(ILogMessageFormatter formatter, string format)
        {
            Formatter = formatter;
            Format = format;
        }

        /// <summary>
        /// Gets a dictionary of property names to their values.
        /// Combines PropertyNames with Parameters() to create name-value pairs.
        /// This method uses lazy initialization and caching for performance.
        /// </summary>
        /// <returns>A read-only dictionary of property names and values</returns>
        public IReadOnlyDictionary<string, object> GetProperties()
        {
            if (_properties == null)
            {
                var names = PropertyNames;
                var parameters = Parameters();

                // Optimize: avoid ToArray() if Parameters() already returns IReadOnlyList
                if (parameters is IReadOnlyList<object> readOnlyList)
                {
                    _properties = CreatePropertyDictionary(names, readOnlyList);
                }
                else if (parameters is object[] array)
                {
                    _properties = CreatePropertyDictionary(names, array);
                }
                else
                {
                    // Fallback: convert to array
                    _properties = CreatePropertyDictionary(names, parameters.ToArray());
                }
            }
            return _properties;
        }

        private static IReadOnlyDictionary<string, object> CreatePropertyDictionary(
            IReadOnlyList<string> names,
            IReadOnlyList<object> values)
        {
            // Handle empty case
            if (names.Count == 0)
                return EmptyDictionary;

            // Handle mismatched counts (more values than names, or vice versa)
            var count = Math.Min(names.Count, values.Count);
            if (count == 0)
                return EmptyDictionary;

            var dict = new Dictionary<string, object>(count);
            for (int i = 0; i < count; i++)
            {
                dict[names[i]] = values[i];
            }

#if NET8_0_OR_GREATER
            // Use FrozenDictionary for optimal read performance on .NET 8+
            return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(dict);
#else
            return dict;
#endif
        }

        private static IReadOnlyDictionary<string, object> CreatePropertyDictionary(
            IReadOnlyList<string> names,
            object[] values)
        {
            // Handle empty case
            if (names.Count == 0)
                return EmptyDictionary;

            // Handle mismatched counts (more values than names, or vice versa)
            var count = Math.Min(names.Count, values.Length);
            if (count == 0)
                return EmptyDictionary;

            var dict = new Dictionary<string, object>(count);
            for (int i = 0; i < count; i++)
            {
                dict[names[i]] = values[i];
            }

#if NET8_0_OR_GREATER
            // Use FrozenDictionary for optimal read performance on .NET 8+
            return System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(dict);
#else
            return dict;
#endif
        }

        private static readonly IReadOnlyDictionary<string, object> EmptyDictionary =
#if NET8_0_OR_GREATER
            System.Collections.Frozen.FrozenDictionary<string, object>.Empty;
#else
            new Dictionary<string, object>();
#endif

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <returns>An unformatted copy of the state string - used for debugging bad logging templates</returns>
        [InternalApi]
        public abstract string Unformatted();

        /// <summary>
        /// INTERNAL API
        /// </summary>
        /// <returns>The unformatted log arguments - used during debugging and by third-party logging libraries</returns>
        [InternalApi]
        public abstract IEnumerable<object> Parameters();
    }

    /// <summary>
    /// Generic version of the argument.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class LogMessage<T> : LogMessage where T:IEnumerable<object>
    {
        public LogMessage(ILogMessageFormatter formatter, string format, T arg) : base(formatter, format)
        {
            Arg = arg;
        }
        
        public T Arg { get; }

        public override string ToString()
        {
            return Formatter.Format(Format, Arg);
        }

        public override string Unformatted()
        {
            return Arg.ToString();
        }

        public override IEnumerable<object> Parameters() => Arg;
    }

    /// <summary>
    /// Works akin to the original <see cref="LogMessage"/> class with an array of objects as the format args.
    /// </summary>
    internal sealed class DefaultLogMessage : LogMessage
    {
        public DefaultLogMessage(ILogMessageFormatter formatter, string format, params object[] args) : base(formatter, format)
        {
            Args = args;
        }
        
        public object[] Args { get; }

        public override string ToString()
        {
            return Formatter.Format(Format, Args);
        }

        public override string Unformatted()
        {
            return string.Join(",", Args);
        }

        public override IEnumerable<object> Parameters()
        {
            return Args;
        }
    }

    internal readonly struct LogValues<T1> : IReadOnlyList<object>
    {
        private readonly T1 _value1;

        public LogValues(T1 value1)
        {
            _value1 = value1;
        }

        public IEnumerator<object> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => 1;

        public object this[int index]
        {
            get
            {
                if(index == 0)
                    return _value1;
                throw new IndexOutOfRangeException(nameof(index));
            }
        }
    }
    
    internal readonly struct LogValues<T1, T2> : IReadOnlyList<object>
    {
        private readonly T1 _value1;
        private readonly T2 _value2;

        public LogValues(T1 value1, T2 value2)
        {
            _value1 = value1;
            _value2 = value2;
        }

        public IEnumerator<object> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => 2;

        public object this[int index]
        {
            get
            {
                return index switch
                {
                    0 => _value1,
                    1 => _value2,
                    _ => throw new IndexOutOfRangeException(nameof(index))
                };
            }
        }
    }
    
    internal readonly struct LogValues<T1, T2, T3> : IReadOnlyList<object>
    {
        private readonly T1 _value1;
        private readonly T2 _value2;
        private readonly T3 _value3;

        public LogValues(T1 value1, T2 value2, T3 value3)
        {
            _value1 = value1;
            _value2 = value2;
            _value3 = value3;
        }

        public IEnumerator<object> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
            yield return this[2];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => 3;

        public object this[int index]
        {
            get
            {
                return index switch
                {
                    0 => _value1,
                    1 => _value2,
                    2 => _value3,
                    _ => throw new IndexOutOfRangeException(nameof(index))
                };
            }
        }
    }
    
    internal readonly struct LogValues<T1, T2, T3, T4> : IReadOnlyList<object>
    {
        private readonly T1 _value1;
        private readonly T2 _value2;
        private readonly T3 _value3;
        private readonly T4 _value4;

        public LogValues(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            _value1 = value1;
            _value2 = value2;
            _value3 = value3;
            _value4 = value4;
        }

        public IEnumerator<object> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
            yield return this[2];
            yield return this[3];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => 4;

        public object this[int index]
        {
            get
            {
                return index switch
                {
                    0 => _value1,
                    1 => _value2,
                    2 => _value3,
                    3 => _value4,
                    _ => throw new IndexOutOfRangeException(nameof(index))
                };
            }
        }
    }
    
    internal readonly struct LogValues<T1, T2, T3, T4, T5> : IReadOnlyList<object>
    {
        private readonly T1 _value1;
        private readonly T2 _value2;
        private readonly T3 _value3;
        private readonly T4 _value4;
        private readonly T5 _value5;

        public LogValues(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            _value1 = value1;
            _value2 = value2;
            _value3 = value3;
            _value4 = value4;
            _value5 = value5;
        }

        public IEnumerator<object> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
            yield return this[2];
            yield return this[3];
            yield return this[4];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => 5;

        public object this[int index]
        {
            get
            {
                return index switch
                {
                    0 => _value1,
                    1 => _value2,
                    2 => _value3,
                    3 => _value4,
                    4 => _value5,
                    _ => throw new IndexOutOfRangeException(nameof(index))
                };
            }
        }
    }
    
    internal readonly struct LogValues<T1, T2, T3, T4, T5, T6> : IReadOnlyList<object>
    {
        private readonly T1 _value1;
        private readonly T2 _value2;
        private readonly T3 _value3;
        private readonly T4 _value4;
        private readonly T5 _value5;
        private readonly T6 _value6;

        public LogValues(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            _value1 = value1;
            _value2 = value2;
            _value3 = value3;
            _value4 = value4;
            _value5 = value5;
            _value6 = value6;
        }

        public IEnumerator<object> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
            yield return this[2];
            yield return this[3];
            yield return this[4];
            yield return this[5];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int Count => 6;

        public object this[int index]
        {
            get
            {
                return index switch
                {
                    0 => _value1,
                    1 => _value2,
                    2 => _value3,
                    3 => _value4,
                    4 => _value5,
                    5 => _value6,
                    _ => throw new IndexOutOfRangeException(nameof(index))
                };
            }
        }
    }
}

