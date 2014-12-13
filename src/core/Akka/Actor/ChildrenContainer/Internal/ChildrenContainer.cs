using System.Collections.Generic;

namespace Akka.Actor.Internal
{
    // ReSharper disable once InconsistentNaming
    public interface ChildrenContainer
    {
        ChildrenContainer Add(string name, ChildRestartStats stats);
        ChildrenContainer Remove(ActorRef child);
        bool TryGetByName(string name, out ChildStats stats);
        bool TryGetByRef(ActorRef actor, out ChildRestartStats stats);
        IReadOnlyList<InternalActorRef> Children { get; }
        IReadOnlyList<ChildRestartStats> Stats { get; }
        ChildrenContainer ShallDie(ActorRef actor);
        ChildrenContainer Reserve(string name);
        ChildrenContainer Unreserve(string name);
        bool IsTerminating { get; }
        bool IsNormal { get; }
        bool Contains(ActorRef actor);
    }
}