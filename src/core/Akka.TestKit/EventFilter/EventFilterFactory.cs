//-----------------------------------------------------------------------
// <copyright file="EventFilterFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit.Internal;
using Akka.TestKit.Internal.StringMatcher;

#nullable enable
namespace Akka.TestKit;

/// <summary>
/// Creates event filters for assertions and temporary muting of expected log events during tests.
/// </summary>
public partial class EventFilterFactory
{
    private readonly IReadOnlyList<EventFilterBase>? _filters;
    private readonly TestKitBase _testkit;
    private readonly ActorSystem _system;

    /// <summary>
    /// Creates a factory that applies event filters using the test kit's actor system.
    /// </summary>
    /// <param name="testkit">The test kit that owns the actor system and executes filter assertions.</param>
    public EventFilterFactory(TestKitBase testkit)
    {
        _testkit = testkit;
        _system = _testkit.Sys;
    }
        
    /// <summary>
    /// Creates a factory that applies event filters using the specified actor system.
    /// </summary>
    /// <param name="testkit">The test kit that executes filter assertions.</param>
    /// <param name="system">The actor system whose event stream receives the filters.</param>
    public EventFilterFactory(TestKitBase testkit, ActorSystem system)
    {
        _testkit = testkit;
        _system = system;
    }

    /// <summary>
    /// Creates a factory that appends new filters to the supplied filter chain.
    /// </summary>
    /// <param name="testkit">The test kit that executes filter assertions.</param>
    /// <param name="actorSystem">The actor system whose event stream receives the filters.</param>
    /// <param name="filters">The filters already in the chain.</param>
    public EventFilterFactory(TestKitBase testkit, ActorSystem actorSystem, IReadOnlyList<EventFilterBase> filters)
        : this(testkit, actorSystem)
    {
        _filters = filters;
    }

    /// <summary>
    /// Create a filter for <see cref="Akka.Event.Error"/> events. Events must match the specified pattern to be filtered.
    /// <example>
    /// Exception&lt;MyException&gt;(pattern: new Regex("weird.*message"), source: obj) // filter on pattern and source
    /// </example>
    /// <remarks>Please note that filtering on the <paramref name="source"/> being
    /// <c>null</c> does NOT work (passing <c>null</c> disables the source filter).
    /// </remarks>
    /// </summary>
    /// <typeparam name="TException">The type of the exception.</typeparam>
    /// <param name="pattern">The event must match the pattern to be filtered.</param>
    /// <param name="source">>Optional. The event source.</param>
    /// <returns>The new filter</returns>
    public IEventFilterApplier Exception<TException>(Regex pattern, string? source = null) where TException : Exception
    {
        return Exception(typeof(TException), pattern, source);
    }


    /// <summary>
    /// Create a filter for <see cref="Akka.Event.Error"/> events. Events must match the specified pattern to be filtered.
    /// <example>
    /// Exception&lt;MyException&gt;(pattern: new Regex("weird.*message"), source: obj) // filter on pattern and source
    /// </example>
    /// <remarks>Please note that filtering on the <paramref name="source"/> being
    /// <c>null</c> does NOT work (passing <c>null</c> disables the source filter).
    /// </remarks>
    /// </summary>
    /// <param name="exceptionType">The type of the exception. It must be a <see cref="System.Exception"/>.</param>
    /// <param name="pattern">The event must match the pattern to be filtered.</param>
    /// <param name="source">>Optional. The event source.</param>
    /// <param name="checkInnerExceptions">Optional. When set to <c>true</c> not only the top level exception is matched, but inner exceptions are also checked until one matches. Default: <c>false</c></param>
    /// <returns>The new filter</returns>
    public IEventFilterApplier Exception(Type exceptionType, Regex pattern, string? source = null, bool checkInnerExceptions=false)
    {
        var sourceMatcher = source == null ? null : new EqualsStringAndPathMatcher(source);
        return Exception(exceptionType, new RegexMatcher(pattern), sourceMatcher, checkInnerExceptions);
    }


    /// <summary>
    /// Create a filter for <see cref="Akka.Event.Error"/> events.
    /// <para><paramref name="message" /> takes priority over <paramref name="start" />.
    /// If <paramref name="message" />!=<c>null</c> the event must match it to be filtered.
    /// If <paramref name="start" />!=<c>null</c> and <paramref name="message" /> has not been specified,
    /// the event must start with the given string to be filtered.
    /// </para>
    /// <example>
    /// Exception&lt;MyException&gt;()                                         // filter only on exception type
    /// Exception&lt;MyException&gt;("message")                                // filter on exactly matching message
    /// Exception&lt;MyException&gt;(source: obj)                              // filter on event source
    /// Exception&lt;MyException&gt;(start: "Expected")                        // filter on start of message
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
    public IEventFilterApplier Exception<TException>(string? message = null, string? start = null, string? contains = null, string? source = null) where TException : Exception
    {
        return Exception(typeof(TException), message, start, contains, source);
    }

    /// <summary>
    /// Create a filter for <see cref="Akka.Event.Error"/> events.
    /// <para><paramref name="message" /> takes priority over <paramref name="start" />.
    /// If <paramref name="message" />!=<c>null</c> the event must match it to be filtered.
    /// If <paramref name="start" />!=<c>null</c> and <paramref name="message" /> has not been specified,
    /// the event must start with the given string to be filtered.
    /// </para>
    /// <example>
    /// Exception(typeof(MyException))                                     // filter only on exception type
    /// Exception(typeof(MyException), "message")                          // filter on exactly matching message
    /// Exception(typeof(MyException), source: obj)                        // filter on event source
    /// Exception(typeof(MyException), start: "Expected")                  // filter on start of message
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
    /// <param name="checkInnerExceptions">Optional. When set to <c>true</c> not only the top level exception is matched, but inner exceptions are also checked until one matches. Default: <c>false</c></param>
    /// <returns>The new filter</returns>
    public IEventFilterApplier Exception(Type exceptionType, string? message = null, string? start = null, string? contains = null, string? source = null, bool checkInnerExceptions=false)
    {
        var messageMatcher = CreateMessageMatcher(message, start, contains);
        var sourceMatcher = source == null ? null : new EqualsStringAndPathMatcher(source);
        return Exception(exceptionType, messageMatcher, sourceMatcher, checkInnerExceptions);
    }

    private IEventFilterApplier Exception(Type exceptionType, IStringMatcher messageMatcher, IStringMatcher? sourceMatcher, bool checkInnerExceptions)
    {
        var filter = new ErrorFilter(exceptionType, messageMatcher, sourceMatcher, checkInnerExceptions);
        return CreateApplier(filter, _system);
    }

    /// <summary>
    /// Create a custom event filter. The filter will affect those events for
    /// which the <paramref name="predicate"/> function returns <c>true</c>.
    /// </summary>
    /// <param name="predicate">This function must return <c>true</c> for events that should be filtered.</param>
    /// <returns>An applier that can assert on or mute matching custom events.</returns>
    public IEventFilterApplier Custom(Predicate<LogEvent> predicate)
    {
        var filter = new CustomEventFilter(predicate);
        return CreateApplier(filter, _system);
    }


    /// <summary>
    /// Create a custom event filter. The filter will affect those events for
    /// which the <paramref name="predicate"/> function returns <c>true</c>.
    /// </summary>
    /// <typeparam name="TLogEvent">The log-event subtype accepted by the predicate.</typeparam>
    /// <param name="predicate">This function must return <c>true</c> for events that should be filtered.</param>
    /// <returns>An applier that can assert on or mute matching events of type <typeparamref name="TLogEvent"/>.</returns>
    public IEventFilterApplier Custom<TLogEvent>(Predicate<TLogEvent> predicate) where TLogEvent : LogEvent
    {
        var filter = new CustomEventFilter(logEvent => logEvent is TLogEvent @event && predicate(@event));
        return CreateApplier(filter, _system);
    }

    /// <summary>
    /// Creates an event filter given the specified <paramref name="logLevel"/>.
    /// This is the same as calling <see cref="Debug(string,string,string,string)"/>, <see cref="Info(string,string,string,string)"/>
    /// <see cref="Warning(string,string,string,string)"/> or <see cref="Error(string,string,string,string)"/>
    /// directly.
    /// </summary>
    /// <param name="logLevel">The log level used to match events being filtered.</param>
    /// <param name="message">Optional. If specified the event must match it exactly to be filtered.</param>
    /// <param name="start">Optional. If specified (and <paramref name="message"/> is not specified), the event must start with the string to be filtered.</param>
    /// <param name="contains">Optional. If specified (and neither <paramref name="message"/> nor <paramref name="start"/> are specified), the event must contain the string to be filtered.</param>
    /// <param name="source">Optional. The event source.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// This exception is thrown when the given <paramref name="logLevel"/> is unknown.
    /// </exception>
    /// <returns>An event filter that matches on the given <paramref name="logLevel"/>.</returns>
    public IEventFilterApplier ForLogLevel(LogLevel logLevel, string? message = null, string? start = null, string? contains = null, string? source = null)
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
                throw new ArgumentOutOfRangeException(nameof(logLevel), $"Unknown {typeof(LogLevel).Name} value: {logLevel}");
        }
    }

    /// <summary>
    /// Creates a filter given the specified <paramref name="logLevel"/>.
    /// This is the same as calling <see cref="Debug(Regex,string)"/>, <see cref="Info(Regex,string)"/>
    /// <see cref="Warning(Regex,string)"/> or <see cref="Error(Regex,string)"/>
    /// directly.
    /// </summary>
    /// <param name="logLevel">The log level used to match events being filtered.</param>
    /// <param name="pattern">The event must match the pattern to be filtered.</param>
    /// <param name="source">>Optional. The event source.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// This exception is thrown when the given <paramref name="logLevel"/> is unknown.
    /// </exception>
    /// <returns>An event filter that matches the given <paramref name="logLevel"/> with the supplied <paramref name="pattern"/>.</returns>
    public IEventFilterApplier ForLogLevel(LogLevel logLevel, Regex pattern, string? source = null)
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
                throw new ArgumentOutOfRangeException(nameof(logLevel), $"Unknown {typeof(LogLevel).Name} value: {logLevel}");
        }
    }

    /// <summary>
    /// Creates a filter that catches dead letters
    /// </summary>
    /// <returns>An applier that matches all dead-letter warning events.</returns>
    public IEventFilterApplier DeadLetter()
    {
        var filter = new DeadLettersFilter(null, null);
        return CreateApplier(filter, _system);
    }

    /// <summary>
    /// Creates a filter that catches dead letters of the specified type and, optionally from the specified source.
    /// </summary>
    /// <typeparam name="TMessage">The type of dead-letter message to match.</typeparam>
    /// <param name="source">An optional log source to match.</param>
    /// <returns>An applier that matches dead letters whose wrapped message is a <typeparamref name="TMessage"/>.</returns>
    public IEventFilterApplier DeadLetter<TMessage>(string? source = null)
    {
        return DeadLetter(deadLetter => deadLetter.Message is TMessage, source);
    }

    /// <summary>
    /// Creates a filter that catches dead letters of the specified type and matches the predicate, and optionally from the specified source.
    /// </summary>
    /// <typeparam name="TMessage">The type of dead-letter message to test.</typeparam>
    /// <param name="isMatch">A predicate that must accept the wrapped message for the dead letter to match.</param>
    /// <param name="source">An optional log source to match.</param>
    /// <returns>An applier that matches dead letters whose wrapped message has the requested type and passes the predicate.</returns>
    public IEventFilterApplier DeadLetter<TMessage>(Func<TMessage, bool> isMatch, string? source = null)
    {
        return DeadLetter(deadLetter => deadLetter.Message is TMessage message && isMatch(message), source);
    }

    /// <summary>
    /// Creates a filter that catches dead letters of the specified type and, optionally from the specified source.
    /// </summary>
    /// <param name="type">The type that the wrapped message must be assignable to.</param>
    /// <param name="source">An optional log source to match.</param>
    /// <returns>An applier that matches dead letters whose wrapped message is an instance of <paramref name="type"/>.</returns>
    public IEventFilterApplier DeadLetter(Type type, string? source = null)
    {
        return DeadLetter(deadLetter => type.IsInstanceOfType(deadLetter.Message), source);
    }

    /// <summary>
    /// Creates a filter that catches dead letters of the specified type and matches the predicate, and optionally from the specified source.
    /// </summary>
    /// <param name="type">The type that the wrapped message must be assignable to.</param>
    /// <param name="isMatch">An additional predicate that must accept the wrapped message.</param>
    /// <param name="source">An optional log source to match.</param>
    /// <returns>An applier that matches dead letters whose wrapped message has the requested type and passes the predicate.</returns>
    public IEventFilterApplier DeadLetter(Type type, Func<object, bool> isMatch, string? source = null)
    {
        return DeadLetter(deadLetter => type.IsInstanceOfType(deadLetter.Message) && isMatch(deadLetter.Message), source);
    }

    private IEventFilterApplier DeadLetter(Predicate<DeadLetter> isMatch, string? source = null)
    {
        var sourceMatcher = source == null ? null : new EqualsStringAndPathMatcher(source);
        var filter = new DeadLettersFilter(null, sourceMatcher, isMatch);
        return CreateApplier(filter, _system);
    }

    /// <summary>
    /// Creates the message matcher selected by the exact, prefix, and substring filter arguments.
    /// </summary>
    /// <param name="message">An exact message to match, or null to use another matcher.</param>
    /// <param name="start">A required message prefix, or null if no prefix is specified.</param>
    /// <param name="contains">A required message substring, or null if no substring is specified.</param>
    /// <returns>The exact matcher if <paramref name="message"/> is specified; otherwise the prefix, substring, or match-all matcher.</returns>
    protected static IStringMatcher CreateMessageMatcher(string? message, string? start, string? contains)
    {
        if(message != null) return new EqualsString(message);
        if(start != null) return new StartsWithString(start);
        if(contains != null) return new ContainsString(contains);
        return MatchesAll.Instance;
    }

    /// <summary>
    /// Combines the current filter chain with a new filter and creates its assertion applier.
    /// </summary>
    /// <param name="filter">The filter to append to the current chain.</param>
    /// <param name="system">The actor system whose event stream the applier will use.</param>
    /// <returns>An applier for the combined filter chain.</returns>
    protected IEventFilterApplier CreateApplier(EventFilterBase filter, ActorSystem system)
    {
        EventFilterBase[] allFilters;   //This will contain _filters + filter
        if(_filters == null || _filters.Count == 0)
        {
            allFilters = [filter];
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
        return new InternalEventFilterApplier(_testkit, system, allFilters);
    }
}
