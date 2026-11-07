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
    public ReceiveActorHandlers()
    {
        TypedHandlers = new List<ITypeHandler>();
        HandleAny = null;
    }

    private List<ITypeHandler> TypedHandlers { get; }

    private Action<object>? HandleAny { get; set; }

    // Message text matches v1.5.71's MatchBuilder.EnsureCanAdd() exactly. An always-handling object
    // catch-all is routed into HandleAny by ReceiveActor/PersistentActor, so this one check covers it too.
    private void CanAddMoreHandlers()
    {
        if (HandleAny != null)
        {
            throw new InvalidOperationException("A handler that catches all messages has been added. No handler can be added after that.");
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
    /// <remarks>
    /// A typed/generic handler never blocks later registrations, even for <see cref="object"/> with no
    /// predicate - an always-handling catch-all for <see cref="object"/> must go through
    /// <see cref="AddReceiveAnyHandler"/> instead, which <see cref="CanAddMoreHandlers"/> does guard.
    /// </remarks>
    public void AddGenericReceiveHandler<T>(Predicate<T>? shouldHandlePredicate, Func<T, bool> handler)
    {
        CanAddMoreHandlers();

        TypedHandlers.Add(CreateTypeHandler(shouldHandlePredicate, handler));
    }

    /// <param name="messageType">The message type the handler is registered for.</param>
    /// <param name="shouldHandlePredicate">An optional predicate. When <c>null</c>, the message is unconditionally passed to <paramref name="handler"/>.</param>
    /// <param name="handler">The handler to invoke. Its <c>bool</c> result indicates whether it handled the message.</param>
    /// <remarks>
    /// A typed/generic handler never blocks later registrations, even for <see cref="object"/> with no
    /// predicate - an always-handling catch-all for <see cref="object"/> must go through
    /// <see cref="AddReceiveAnyHandler"/> instead, which <see cref="CanAddMoreHandlers"/> does guard.
    /// </remarks>
    public void AddTypedReceiveHandler(Type messageType, Predicate<object>? shouldHandlePredicate, Func<object, bool> handler)
    {
        CanAddMoreHandlers();

        TypedHandlers.Add(CreateTypeHandler(messageType, shouldHandlePredicate, handler));
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