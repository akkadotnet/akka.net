// -----------------------------------------------------------------------
//  <copyright file="ReceiveActorHandlers.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Akka.Actor;
#nullable enable
internal sealed class ReceiveActorHandlers
{
    private bool _hadObjectHandlerWithNoPredicate;

    public ReceiveActorHandlers()
    {
        TypedHandlers = new List<ITypeHandler>();
        HandleAny = null;
    }

    private List<ITypeHandler> TypedHandlers { get; }

    private Action<object>? HandleAny { get; set; }

    private void CanAddMoreHandlers()
    {
        if (_hadObjectHandlerWithNoPredicate)
        {
            throw new InvalidOperationException("A handler for object with no predicate has already been added. No more handlers can be added as they would be ignored.");
        }

        if (HandleAny != null)
        {
            throw new InvalidOperationException("A handler that catches all messages has been added. No more handlers can be added as they would be ignored.");
        }
    }
    
    private static ITypeHandler CreateTypeHandler<T>(Predicate<T>? shouldHandlePredicate, Func<T, bool> handler)
    {
        if (shouldHandlePredicate == null)
        {
            return new TypeHandler<T>(handler);
        }

        return new PredicateHandler<T>(shouldHandlePredicate, handler);
    }
    
    private static ITypeHandler CreateTypeHandler(Type t, Predicate<object>? shouldHandlePredicate, Func<object, bool> handler)
    {
        if (shouldHandlePredicate == null)
        {
            return new WeaklyTypedHandler(t, handler);
        }

        return new WeaklyTypedPredicateHandler(t, shouldHandlePredicate, handler);
    }
    
    /// <param name="shouldHandlePredicate">An optional predicate. When <c>null</c>, the message is unconditionally passed to <paramref name="handler"/>.</param>
    /// <param name="handler">The handler to invoke. Its <c>bool</c> result indicates whether it handled the message.</param>
    /// <param name="alwaysHandles">
    /// <c>true</c> when <paramref name="handler"/> is guaranteed to always return <c>true</c> (e.g. it wraps an
    /// <see cref="Action{T}"/>-based <c>Receive</c> overload); <c>false</c> when <paramref name="handler"/> is a
    /// genuine <c>Func&lt;T, bool&gt;</c> that may decline (return <c>false</c>) for a given message, in which case
    /// later handlers remain reachable. This only matters when <typeparamref name="T"/> is <see cref="object"/>
    /// and no predicate was supplied - see <see cref="AddTypedReceiveHandler"/> for the rationale.
    /// </param>
    public void AddGenericReceiveHandler<T>(Predicate<T>? shouldHandlePredicate, Func<T, bool> handler, bool alwaysHandles = true)
    {
        CanAddMoreHandlers();

        TypedHandlers.Add(CreateTypeHandler(shouldHandlePredicate, handler));

        // Mirrors AddTypedReceiveHandler below: only an "always handles" (e.g. Action<T>-based) registration
        // for T=object with no predicate should prevent later handlers from being registered. A genuine
        // Func<T,bool> handler may decline (return false), so later handlers remain reachable - this matches
        // the pre-#7557 MatchBuilder-based implementation (v1.5.71 and earlier), where only MatchAny-style
        // (always-handling) registrations entered the "no more handlers" state.
        if (alwaysHandles &&
            typeof(T) == typeof(object) &&
            shouldHandlePredicate == null)
        {
            _hadObjectHandlerWithNoPredicate = true;
        }
    }


    /// <param name="messageType">The message type the handler is registered for.</param>
    /// <param name="shouldHandlePredicate">An optional predicate. When <c>null</c>, the message is unconditionally passed to <paramref name="handler"/>.</param>
    /// <param name="handler">The handler to invoke. Its <c>bool</c> result indicates whether it handled the message.</param>
    /// <param name="alwaysHandles">
    /// <c>true</c> when <paramref name="handler"/> is guaranteed to always return <c>true</c> (e.g. it wraps an
    /// <see cref="Action{T}"/>-based <c>Receive</c> overload); <c>false</c> when <paramref name="handler"/> is a
    /// genuine <c>Func&lt;object, bool&gt;</c> that may decline (return <c>false</c>) for a given message.
    /// </param>
    public void AddTypedReceiveHandler(Type messageType, Predicate<object>? shouldHandlePredicate, Func<object, bool> handler, bool alwaysHandles = true)
    {
        CanAddMoreHandlers();

        TypedHandlers.Add(CreateTypeHandler(messageType, shouldHandlePredicate, handler));

        // If the message type is object with no predicate, only an "always handles" registration (i.e. one
        // that wraps an Action<object>-based Receive overload, which always returns true) should block later
        // registrations. A Func<object,bool> handler may decline (return false) for a given message, leaving
        // it to fall through to later handlers - this matches the behavior of the pre-#7557 MatchBuilder-based
        // implementation (v1.5.71 and earlier): Match(Type, Action<TItem>, ...) entered the "no more handlers"
        // state for object/no-predicate, but Match(Type, Func<TItem,bool>) never did.
        if (alwaysHandles &&
            messageType == typeof(object) &&
            shouldHandlePredicate == null)
        {
            _hadObjectHandlerWithNoPredicate = true;
        }
    }

    public void AddReceiveAnyHandler(Action<object> handler)
    {
        CanAddMoreHandlers();

        HandleAny = handler;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryHandle(object message)
    {
        var messageType = message.GetType();
        foreach (var handler in TypedHandlers)
        {
            if (!handler.TargetType.IsAssignableFrom(messageType)) continue;
            if (handler.TryHandle(message))
            {
                return true;
            }
        }

        if (HandleAny == null) return false;
        HandleAny(message);
        return true;

    }
}

internal interface ITypeHandler
{
    Type TargetType { get; }
    
    bool TryHandle(object message);
}

internal sealed class WeaklyTypedPredicateHandler : ITypeHandler
{
    public WeaklyTypedPredicateHandler(Type t, Predicate<object> predicate, Func<object, bool> handler)
    {
        Predicate = predicate;
        Handler = handler;
        TargetType = t;
    }

    public Predicate<object> Predicate { get; }
    public Func<object, bool> Handler { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryHandle(object message)
    {
        return Predicate(message) && Handler(message);
    }

    public Type TargetType { get; }
}

internal sealed class WeaklyTypedHandler : ITypeHandler
{
    public WeaklyTypedHandler(Type t, Func<object, bool> handler)
    {
        Handler = handler;
        TargetType = t;
    }

    public Type TargetType { get; }
    
    public Func<object, bool> Handler { get;  }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryHandle(object message)
    {
        return Handler(message);
    }
}

internal sealed class TypeHandler<T> : ITypeHandler
{
    
    public TypeHandler(Func<T, bool> handler)
    {
        Handler = handler;
        TargetType = typeof(T);
    }

    public Type TargetType { get; }
    
    public Func<T, bool> Handler { get;  }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryHandle(object message)
    {
        var typedMessage = (T)message;
        return Handler(typedMessage);
    }
}

internal sealed class PredicateHandler<T> : ITypeHandler
{
    public PredicateHandler(Predicate<T> predicate, Func<T, bool> handler)
    {
        Predicate = predicate;
        Handler = handler;
        TargetType = typeof(T);
    }

    public Predicate<T> Predicate { get; }
    public Func<T, bool> Handler { get; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryHandle(object typedMessage)
    {
        var message = (T)typedMessage;
        return Predicate(message) && Handler(message);
    }

    public Type TargetType { get; }
}