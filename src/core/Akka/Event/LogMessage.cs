//-----------------------------------------------------------------------
// <copyright file="LogMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// </remarks>
    public abstract class LogMessage
    {
        protected readonly ILogMessageFormatter Formatter;

        /// <summary>
        /// Gets the format string of this log message.
        /// </summary>
        public string Format { get; private set; }

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

