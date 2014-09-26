using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Akka.Event;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;

namespace Akka.TestKit
{
    public partial class EventFilterFactory
    {
        private readonly IReadOnlyList<EventFilterBase> _filters;
        private readonly TestKitBase _testkit;

        public EventFilterFactory(TestKitBase testkit)
        {
            _testkit = testkit;
        }

        public EventFilterFactory(TestKitBase testkit, IReadOnlyList<EventFilterBase> filters)
        {
            _testkit = testkit;
            _filters = filters;
        }

        /// <summary>
        /// Create a filter for <see cref="Akka.Event.Error"/> events. Events must match the specified pattern to be filtered.
        /// <example>
        /// Error&lt;MyException&gt;(pattern: new Regex("weird.*message"), source: obj) // filter on pattern and source
        /// </example>
        /// <remarks>Please note that filtering on the <paramref name="source"/> being
        /// <c>null</c> does NOT work (passing <c>null</c> disables the source filter).
        /// </remarks>
        /// </summary>
        /// <typeparam name="TException">The type of the exception.</typeparam>
        /// <param name="pattern">The event must match the pattern to be filtered.</param>
        /// <param name="source">>Optional. The event source.</param>
        /// <returns>The new filter</returns>
        public EventFilterApplier Exception<TException>(Regex pattern, string source = null) where TException : Exception
        {
            return Exception(typeof(TException), pattern, source);
        }


        /// <summary>
        /// Create a filter for Error events. Events must match the specified pattern to be filtered.
        /// <example>
        /// Error&lt;MyException&gt;(pattern: new Regex("weird.*message"), source: obj) // filter on pattern and source
        /// </example>
        /// <remarks>Please note that filtering on the <paramref name="source"/> being
        /// <c>null</c> does NOT work (passing <c>null</c> disables the source filter).
        /// </remarks>
        /// </summary>
        /// <param name="exceptionType">The type of the exception. It must be a <see cref="System.Exception"/>.</param>
        /// <param name="pattern">The event must match the pattern to be filtered.</param>
        /// <param name="source">>Optional. The event source.</param>
        /// <returns>The new filter</returns>
        public EventFilterApplier Exception(Type exceptionType, Regex pattern, string source = null)
        {
            var sourceMatcher = source == null ? null : new EqualsString(source);
            return Exception(exceptionType, new RegexMatcher(pattern), sourceMatcher);
        }


        /// <summary>
        /// Create a filter for Error events.
        /// <para><paramref name="message" /> takes priority over <paramref name="start" />.
        /// If <paramref name="message" />!=<c>null</c> the event must match it to be filtered.
        /// If <paramref name="start" />!=<c>null</c> and <paramref name="message" /> has not been specified,
        /// the event must start with the given string to be filtered.
        /// </para><example>
        /// Error&lt;MyException&gt;()                                         // filter only on exception type
        /// Error&lt;MyException&gt;("message")                                // filter on exactly matching message
        /// Error&lt;MyException&gt;(source: obj)                              // filter on event source
        /// Error&lt;MyException&gt;(start: "Expected")                        // filter on start of message
        /// </example>
        /// <remarks>Please note that filtering on the <paramref name="source"/> being
        /// <c>null</c> does NOT work (passing <c>null</c> disables the source filter).
        /// </remarks>
        /// </summary>
        /// <typeparam name="TException">The type of the exception.</typeparam>
        /// <param name="message">Optional. If specified the event must match it exactly to be filtered.</param>
        /// <param name="contains">Optional. If specified (and neither <paramref name="message"/> nor <paramref name="start"/> are specified), the event must contain the string to be filtered.</param>
        /// <param name="start">>Optional. If specified (and <paramref name="message"/> is not specified, the event must start with the string to be filtered.</param>
        /// <param name="source">>Optional. The event source.</param>
        /// <returns>The new filter</returns>
        public EventFilterApplier Exception<TException>(string message = null, string start = null, string contains = null, string source = null) where TException : Exception
        {
            return Exception(typeof(TException), message, start, contains, source);
        }

        /// <summary>
        /// Create a filter for Error events.
        /// <para><paramref name="message" /> takes priority over <paramref name="start" />.
        /// If <paramref name="message" />!=<c>null</c> the event must match it to be filtered.
        /// If <paramref name="start" />!=<c>null</c> and <paramref name="message" /> has not been specified,
        /// the event must start with the given string to be filtered.
        /// </para><example>
        /// Error(typeof(MyException))                                     // filter only on exception type
        /// Error(typeof(MyException), "message")                          // filter on exactly matching message
        /// Error(typeof(MyException), source: obj)                        // filter on event source
        /// Error(typeof(MyException), start: "Expected")                  // filter on start of message
        /// </example>
        /// <remarks>Please note that filtering on the <paramref name="source"/> being
        /// <c>null</c> does NOT work (passing <c>null</c> disables the source filter).
        /// </remarks>
        /// </summary>
        /// <param name="exceptionType">The type of the exception. It must be a <see cref="System.Exception"/>.</param>
        /// <param name="message">Optional. If specified the event must match it exactly to be filtered.</param>
        /// <param name="contains">Optional. If specified (and neither <paramref name="message"/> nor <paramref name="start"/> are specified), the event must contain the string to be filtered.</param>
        /// <param name="start">>Optional. If specified (and <paramref name="message"/> is not specified, the event must start with the string to be filtered.</param>
        /// <param name="source">>Optional. The event source.</param>
        /// <returns>The new filter</returns>
        public EventFilterApplier Exception(Type exceptionType, string message = null, string start = null, string contains = null, string source = null)
        {
            var messageMathcer = CreateMessageMatcher(message, start, contains);
            var sourceMatcher = source == null ? null : new EqualsString(source);
            return Exception(exceptionType, messageMathcer, sourceMatcher);
        }

        /// <summary>
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        private EventFilterApplier Exception(Type exceptionType, IStringMatcher messageMatcher, IStringMatcher sourceMatcher)
        {
            var filter = new ErrorFilter(exceptionType, messageMatcher, sourceMatcher);
            return CreateApplier(filter);
        }

        /// <summary>
        /// Create a custom event filter. The filter will affect those events for
        ///  which the predicate function returns <c>true</c>.
        /// </summary>
        /// <param name="predicate">This function must return <c>true</c> for events that should be filtered.</param>
        /// <returns></returns>
        public EventFilterApplier Custom(Predicate<LogEvent> predicate)
        {
            var filter = new CustomEventFilter(predicate);
            return CreateApplier(filter);
        }


        /// <summary>
        /// Create a custom event filter. The filter will affect those events for
        ///  which the predicate function returns <c>true</c>.
        /// </summary>
        /// <param name="predicate">This function must return <c>true</c> for events that should be filtered.</param>
        /// <returns></returns>
        public EventFilterApplier Custom<TLogEvent>(Predicate<TLogEvent> predicate) where TLogEvent : LogEvent
        {
            var filter = new CustomEventFilter(logEvent => logEvent is TLogEvent && predicate((TLogEvent)logEvent));
            return CreateApplier(filter);
        }

        /// <summary>
        /// Creates a event filter given the specified <paramref name="logLevel"/>.
        /// This is the same as calling <see cref="Debug(string,string,string,string)"/>, <see cref="Info(string,string,string,string)"/>
        ///  <see cref="Warning(string,string,string,string)"/> or <see cref="Error(string,string,string,string)"/>
        /// directly.
        /// </summary>
        public EventFilterApplier ForLogLevel(LogLevel logLevel, string message = null, string start = null, string contains = null, string source = null)
        {
            switch(logLevel)
            {
                case LogLevel.DebugLevel:
                    return Debug(message, start, contains, source);
                case LogLevel.InfoLevel:
                    return Info(message, start, contains, source);
                case LogLevel.WarningLevel:
                    return Warning(message, start, contains, source);
                case LogLevel.ErrorLevel:
                    return Error(message, start, contains, source);
                default:
                    throw new ArgumentOutOfRangeException("logLevel", string.Format("Unknown {1} value: {0}", logLevel, typeof(LogLevel).Name));
            }
        }
        /// <summary>
        /// Creates a filter given the specified <paramref name="logLevel"/>.
        /// This is the same as calling <see cref="Debug(Regex,string)"/>, <see cref="Info(Regex,string)"/>
        ///  <see cref="Warning(Regex,string)"/> or <see cref="Error(Regex,string)"/>
        /// directly.
        /// </summary>
        public EventFilterApplier ForLogLevel(LogLevel logLevel, Regex pattern, string source = null)
        {
            switch(logLevel)
            {
                case LogLevel.DebugLevel:
                    return Debug(pattern, source);
                case LogLevel.InfoLevel:
                    return Info(pattern, source);
                case LogLevel.WarningLevel:
                    return Warning(pattern, source);
                case LogLevel.ErrorLevel:
                    return Error(pattern, source);
                default:
                    throw new ArgumentOutOfRangeException("logLevel", string.Format("Unknown {1} value: {0}", logLevel, typeof(LogLevel).Name));
            }
        }

        /// <summary>
        /// Creates a filter that catches dead letters
        /// </summary>
        /// <returns></returns>
        public EventFilterApplier DeadLetter()
        {
            var filter = new DeadLettersFilter(null, null);
            return CreateApplier(filter);
        }

        /// <summary>
        /// Creates a filter that catches dead letters of the specified type and, optionally from the specified source.
        /// </summary>
        /// <returns></returns>
        public EventFilterApplier DeadLetter<TMessage>(string source = null)
        {
            return DeadLetter(deadLetter => deadLetter.Message is TMessage, source);
        }

        /// <summary>
        /// Creates a filter that catches dead letters of the specified type and, optionally from the specified source.
        /// </summary>
        /// <returns></returns>
        public EventFilterApplier DeadLetter(Type type, string source = null)
        {
            return DeadLetter(deadLetter => deadLetter.Message.GetType().IsInstanceOfType(type), source);
        }

        private EventFilterApplier DeadLetter(Predicate<DeadLetter> isMatch, string source = null)
        {
            var sourceMatcher = source == null ? null : new EqualsString(source);
            var filter = new DeadLettersFilter(null, sourceMatcher, isMatch);
            return CreateApplier(filter);
        }

        protected static IStringMatcher CreateMessageMatcher(string message, string start, string contains)
        {
            if(message != null) return new EqualsString(message);
            if(start != null) return new StartsWithString(start);
            if(contains != null) return new ContainsString(contains);
            return MatchesAll.Instance;
        }

        protected EventFilterApplier CreateApplier(EventFilterBase filter)
        {
            EventFilterBase[] allFilters;   //This will contain _filters + filter
            if(_filters == null || _filters.Count == 0)
            {
                allFilters = new[] { filter };
            }
            else
            {
                var existingFiltersCount = _filters.Count;
                allFilters = new EventFilterBase[existingFiltersCount + 1];
                //Copy everything from _filters to allFilter and put filter at the end
                for(var i = 0; i < existingFiltersCount; i++)
                {
                    allFilters[i] = _filters[i];
                }
                allFilters[existingFiltersCount] = filter;
            }
            return new InternalEventFilterApplier(_testkit, allFilters);
        }

    }
}