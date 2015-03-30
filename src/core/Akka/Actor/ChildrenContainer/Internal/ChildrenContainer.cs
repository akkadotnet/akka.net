using System.Collections.Generic;

namespace Akka.Actor.Internal
{
    // ReSharper disable once InconsistentNaming
    public interface ChildrenContainer
    {
        ChildrenContainer Add(string name, ChildRestartStats stats);
        ChildrenContainer Remove(IActorRef child);
        bool TryGetByName(string name, out ChildStats stats);
        bool TryGetByRef(IActorRef actor, out ChildRestartStats stats);
        IReadOnlyList<IInternalActorRef> Children { get; }
        IReadOnlyList<ChildRestartStats> Stats { get; }
        ChildrenContainer ShallDie(IActorRef actor);
        ChildrenContainer Reserve(string name);
        ChildrenContainer Unreserve(string name);
        bool IsTerminating { get; }
        bool IsNormal { get; }
        bool Contains(IActorRef actor);
    }
}