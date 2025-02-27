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
    public ReceiveActorHandlers()
    {
        TypedHandlers = new Dictionary<Type, ITypeHandler>();
        HandleAny = null;
    }

    public Dictionary<Type, ITypeHandler> TypedHandlers { get; }

    public Action<object>? HandleAny { get; private set; }

    public void AddGenericReceiveHandler<T>(Predicate<T>? shouldHandle, Func<T, bool> handler)
    {
        if (HandleAny != null)
        {
            throw new InvalidOperationException("A handler that catches all messages has been added. No handler can be added after that.");
        }
        
        if (!TypedHandlers.TryGetValue(typeof(T), out var typeHandlerInterface))
        {
            typeHandlerInterface = new TypeHandler<T>();
            TypedHandlers[typeHandlerInterface.HandlesType] = typeHandlerInterface;
        }

        var typedHandler = (TypeHandler<T>)typeHandlerInterface;

        // If the last item added to the handlers has a predicate, then we can add a handler.
        var handlerCount = typedHandler.Handlers.Count;
        if (handlerCount > 0 && 
            typedHandler.Handlers[handlerCount - 1].Predicate == null)
        {
            throw new InvalidOperationException("A handler with no predicate has already been added for this type. No more handlers can be added.");
        }
        
        var predicateHandler = new PredicateHandler<T>() { Predicate = shouldHandle, Handler = handler };

        typedHandler.Handlers.Add(predicateHandler);
    }

    public void AddTypedReceiveHandler(Type messageType, Predicate<object>? shouldHandle, Func<object, bool> handler)
    {
        if (HandleAny != null)
        {
            throw new InvalidOperationException("A handler that catches all messages has been added. No handler can be added after that.");
        }
        
        if (!TypedHandlers.TryGetValue(messageType, out var typeHandlerInterface))
        {
            typeHandlerInterface = new TypeHandler<object>();
            TypedHandlers[messageType] = typeHandlerInterface;
        }

        var typedHandler = (TypeHandler<object>)typeHandlerInterface;
        
        // If the last item added to the handlers has a predicate, then we can add a handler.
        var handlerCount = typedHandler.Handlers.Count;
        if (handlerCount > 0 && 
            typedHandler.Handlers[handlerCount - 1].Predicate == null)
        {
            throw new InvalidOperationException("A handler with no predicate has already been added for this type. No more handlers can be added.");
        }

        // Have to use object here as dont have the generic type information
        var predicateHandler = new PredicateHandler<object>() { Predicate = shouldHandle, Handler = handler };

        typedHandler.Handlers.Add(predicateHandler);
    }

    // TODO - Should receive any be treated like an object handler?
    public void AddReceiveAnyHandler(Action<object> handler)
    {
        if (HandleAny != null)
        {
            throw new InvalidOperationException(
                "A handler that catches all messages has been added. No handler can be added after that.");
        }

        HandleAny = handler;
    }

    public bool TryHandle(object message)
    {
        var messageType = message.GetType();
        foreach (var (type, typedHandlers) in TypedHandlers)
        {
            // This is covering object types as well. There might be an ordering issue here
            // but this should probably be resolved with the logic around how handlers are ordered.
            if (type.IsAssignableFrom(messageType))
            {
                if (typedHandlers.TryHandle(message))
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
    Type HandlesType { get; }

    bool TryHandle(object message);
}

internal class TypeHandler<T> : ITypeHandler
{
    public TypeHandler()
    {
        HandlesType = typeof(T);
        Handlers = new List<PredicateHandler<T>>();
    }

    public Type HandlesType { get; }

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
    public Predicate<T>? Predicate { get; init; }
    public Func<T, bool> Handler { get; init; }

    public bool TryHandle(T typedMessage)
    {
        if (Predicate == null || Predicate(typedMessage))
        {
            return Handler(typedMessage);
        }

        return false;
    }
}