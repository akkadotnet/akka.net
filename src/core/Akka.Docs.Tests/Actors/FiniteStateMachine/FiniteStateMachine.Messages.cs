﻿// -----------------------------------------------------------------------
//  <copyright file="FiniteStateMachine.Messages.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;

namespace DocsExamples.Actor.FiniteStateMachine;

#region FSMEvents

// received events
public class SetTarget
{
    public SetTarget(IActorRef @ref)
    {
        Ref = @ref;
    }

    public IActorRef Ref { get; }
}

public class Queue
{
    public Queue(object obj)
    {
        Obj = obj;
    }

    public object Obj { get; }
}

public class Flush
{
}

// send events
public class Batch
{
    public Batch(ImmutableList<object> obj)
    {
        Obj = obj;
    }

    public ImmutableList<object> Obj { get; }
}

#endregion

#region FSMData

// states
public enum State
{
    Idle,
    Active
}

// data
public interface IData
{
}

public class Uninitialized : IData
{
    public static Uninitialized Instance = new();

    private Uninitialized()
    {
    }
}

public class Todo : IData
{
    public Todo(IActorRef target, ImmutableList<object> queue)
    {
        Target = target;
        Queue = queue;
    }

    public IActorRef Target { get; }

    public ImmutableList<object> Queue { get; }

    public Todo Copy(ImmutableList<object> queue)
    {
        return new Todo(Target, queue);
    }
}

#endregion