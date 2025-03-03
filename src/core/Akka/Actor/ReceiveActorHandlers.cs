// -----------------------------------------------------------------------
//  <copyright file="ReceiveActorHandlers.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Actor;
#nullable enable
internal class ReceiveActorHandlers
{
    private bool _hadObjectHandlerWithNoPredicate;

    public ReceiveActorHandlers()
    {
        TypedHandlers = new Dictionary<Type, ITypeHandler>();
        HandleAny = null;
    }

    private Dictionary<Type, ITypeHandler> TypedHandlers { get; }

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
    
    public void AddGenericReceiveHandler<T>(Predicate<T>? shouldHandlePredicate, Func<T, bool> handler)
    {
        CanAddMoreHandlers();

        var genericType = typeof(T);
        if (!TypedHandlers.TryGetValue(genericType, out var typeHandlerInterface))
        {
            typeHandlerInterface = new TypeHandler<T>();
            TypedHandlers[genericType] = typeHandlerInterface;
        }

        var typedHandler = (TypeHandler<T>)typeHandlerInterface;

        var predicateHandler = new PredicateHandler<T>(shouldHandlePredicate, handler);
        typedHandler.Handlers.Add(predicateHandler);
    }

    public void AddTypedReceiveHandler(Type messageType, Predicate<object>? shouldHandlePredicate, Func<object, bool> handler)
    {
        CanAddMoreHandlers();
        if (!TypedHandlers.TryGetValue(messageType, out var typeHandlerInterface))
        {
            typeHandlerInterface = new TypeHandler<object>();
            TypedHandlers[messageType] = typeHandlerInterface;
        }

        var typedHandler = (TypeHandler<object>)typeHandlerInterface;

        // Have to use object here as dont have the generic type information
        var predicateHandler = new PredicateHandler<object>(shouldHandlePredicate, handler);
        typedHandler.Handlers.Add(predicateHandler);

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

    public bool TryHandle(object message)
    {
        var messageType = message.GetType();
        foreach (var kvp in TypedHandlers)
        {
            // This is covering object types as well. There might be an ordering issue here
            // but this should probably be resolved with the logic around how handlers are ordered.
            if (kvp.Key.IsAssignableFrom(messageType))
            {
                if (kvp.Value.TryHandle(message))
                {
                    return true;
                }
            }
        }

        if (HandleAny != null)
        {
            HandleAny(message);
            return true;
        }

        return false;
    }
}

internal interface ITypeHandler
{
    bool TryHandle(object message);
}

internal class TypeHandler<T> : ITypeHandler
{
    public TypeHandler()
    {
        Handlers = new List<PredicateHandler<T>>();
    }

    public List<PredicateHandler<T>> Handlers { get; }

    public bool TryHandle(object message)
    {
        var typedMessage = (T)message;
        foreach (var predicateHandler in Handlers)
        {
            if (predicateHandler.TryHandle(typedMessage))
            {
                return true;
            }
        }

        return false;
    }
}

internal class PredicateHandler<T>
{
    public PredicateHandler(Predicate<T>? predicate, Func<T, bool> handler)
    {
        Predicate = predicate;
        Handler = handler;
    }

    public Predicate<T>? Predicate { get; }
    public Func<T, bool> Handler { get; }

    public bool TryHandle(T typedMessage)
    {
        if (Predicate == null || Predicate(typedMessage))
        {
            return Handler(typedMessage);
        }

        return false;
    }
}