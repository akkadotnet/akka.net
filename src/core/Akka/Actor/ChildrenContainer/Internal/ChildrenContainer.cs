﻿//-----------------------------------------------------------------------
// <copyright file="ChildrenContainer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Actor.Internal
{
    public interface IChildrenContainer
    {
        IChildrenContainer Add(string name, ChildRestartStats stats);
        IChildrenContainer Remove(IActorRef child);
        bool TryGetByName(string name, out IChildStats stats);
        bool TryGetByRef(IActorRef actor, out ChildRestartStats stats);
        IReadOnlyList<IInternalActorRef> Children { get; }
        IReadOnlyList<ChildRestartStats> Stats { get; }
        IChildrenContainer ShallDie(IActorRef actor);
        IChildrenContainer Reserve(string name);
        IChildrenContainer Unreserve(string name);
        bool IsTerminating { get; }
        bool IsNormal { get; }
        bool Contains(IActorRef actor);
    }
}

