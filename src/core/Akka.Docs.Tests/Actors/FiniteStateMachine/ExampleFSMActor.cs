using Akka.Actor;
using Akka.Event;
using System;
using System.Collections.Immutable;

namespace DocsExamples.Actor.FiniteStateMachine
{
    public class ExampleFSMActor : FSM<State, IData>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public ExampleFSMActor()
        {
            StartWith(State.Idle, Uninitialized.Instance);

            When(State.Idle, state =>
            {
                if (state.FsmEvent is SetTarget target && state.StateData is Uninitialized)
                {
                    return Stay().Using(new Todo(target.Ref, ImmutableList<object>.Empty));
                }

                return null;
            });

            When(State.Active, state =>
            {
                if ((state.FsmEvent is Flush || state.FsmEvent is StateTimeout) 
                    && state.StateData is Todo t)
                {
                    return GoTo(State.Idle).Using(t.Copy(ImmutableList<object>.Empty));
                }

                return null;
            }, TimeSpan.FromSeconds(1));

            WhenUnhandled(state =>
            {
                if (state.FsmEvent is Queue q && state.StateData is Todo t)
                {
                    return GoTo(State.Active).Using(t.Copy(t.Queue.Add(q.Obj)));
                }
                else
                {
                    _log.Warning("Received unhandled request {0} in state {1}/{2}", state.FsmEvent, StateName, state.StateData);
                    return Stay();
                }
            });

            OnTransition((initialState, nextState) =>
            {
                if (initialState == State.Active && nextState == State.Idle)
                {
                    if (StateData is Todo todo)
                    {
                        todo.Target.Tell(new Batch(todo.Queue));
                    }
                    else
                    {
                        // nothing to do
                    }
                }
            });

            Initialize();
        }
    }
}
