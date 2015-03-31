using System;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// This is the empty container which is installed after the last child has
    /// terminated while stopping; it is necessary to distinguish from the normal
    /// empty state while calling handleChildTerminated() for the last time.
    /// </summary>
    public class TerminatedChildrenContainer : EmptyChildrenContainer
    {
        private static readonly ChildrenContainer _instance = new TerminatedChildrenContainer();

        private TerminatedChildrenContainer()
        {
            //Intentionally left blank
        }
        public new static ChildrenContainer Instance { get { return _instance; } }

        public override ChildrenContainer Add(string name, ChildRestartStats stats)
        {
            return this;
        }

        public override ChildrenContainer Reserve(string name)
        {
            throw new InvalidOperationException("Cannot reserve actor name '" + name + "': already terminated");
        }

        public override bool IsTerminating { get { return true; } }

        public override bool IsNormal { get { return false; } }

        public override string ToString()
        {
            return "Terminated";
        }
    }
}