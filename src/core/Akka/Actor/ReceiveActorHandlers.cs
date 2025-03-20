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
    
    public void AddGenericReceiveHandler<T>(Predicate<T>? shouldHandlePredicate, Func<T, bool> handler)
    {
        CanAddMoreHandlers();
        
        TypedHandlers.Add(CreateTypeHandler(shouldHandlePredicate, handler));
    }
    

    public void AddTypedReceiveHandler(Type messageType, Predicate<object>? shouldHandlePredicate, Func<object, bool> handler)
    {
        CanAddMoreHandlers();
        
        TypedHandlers.Add(CreateTypeHandler(messageType, shouldHandlePredicate, handler));

        // If the message type is object, then we need to track that we have added a handler with no predicate.
        if (messageType == typeof(object) && 
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