using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DocsExamples.Actor.FiniteStateMachine
{
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

        public Object Obj { get; }
    }

    public class Flush { }

    // send events
    public class Batch
    {
        public Batch(ImmutableList<object> obj)
        {
            Obj = obj;
        }

        public ImmutableList<object> Obj { get; }
    }

    // states
    public enum State
    {
        Idle,
        Active
    }

    // data
    public interface IData { }

    public class Uninitialized : IData
    {
        public static Uninitialized Instance = new Uninitialized();

        private Uninitialized() { }
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
}
