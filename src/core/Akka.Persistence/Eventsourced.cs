using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence
{
    public interface IEventsourced
    {
        bool ReceiveRecover(object message);
        bool ReceiveCommand(object message);
        void Persist<TEvent>(TEvent evt, Action<TEvent> handler);
        void Persist<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler);
        Task PersistAsync<TEvent>(TEvent evt, Action<TEvent> handler);
        Task PersistAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler);
        void Defer<TEvent>(TEvent evt, Action<TEvent> handler);
        void Defer<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler);
        void UnstashAll();
    }

    public interface IPendingHandlerInvocation
    {
        object Event { get; }
        Action<object> Handler { get; }
    }

    public struct StashingHandlerInvocation : IPendingHandlerInvocation
    {
        public StashingHandlerInvocation(object evt, Action<object> handler)
            : this()
        {
            Event = evt;
            Handler = handler;
        }

        public object Event { get; private set; }
        public Action<object> Handler { get; private set; }
    }

    public struct AsyncHandlerInvocation : IPendingHandlerInvocation
    {
        public AsyncHandlerInvocation(object evt, Action<object> handler)
            : this()
        {
            Event = evt;
            Handler = handler;
        }

        public object Event { get; private set; }
        public Action<object> Handler { get; private set; }
    }

    public abstract class PersistentActorBase : Recovery, IEventsourced
    {
        protected PersistentActorBase()
        {
        }

        protected PersistentActorBase(string name)
        {
        }

        protected override bool Receive(object message)
        {
            throw new System.NotImplementedException();
        }

        internal IState CurrentState { get; set; }
        internal IGuaranteedDeliverer Deliverer { get; private set; }

        public abstract bool ReceiveRecover(object message);

        public abstract bool ReceiveCommand(object message);

        public void Persist<TEvent>(TEvent evt, Action<TEvent> handler)
        {
            throw new NotImplementedException();
        }

        public void Persist<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            throw new NotImplementedException();
        }

        public Task PersistAsync<TEvent>(TEvent evt, Action<TEvent> handler)
        {
            throw new NotImplementedException();
        }

        public Task PersistAsync<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            throw new NotImplementedException();
        }

        public void Defer<TEvent>(TEvent evt, Action<TEvent> handler)
        {
            throw new NotImplementedException();
        }

        public void Defer<TEvent>(IEnumerable<TEvent> events, Action<TEvent> handler)
        {
            throw new NotImplementedException();
        }

        public void UnstashAll()
        {
            throw new NotImplementedException();
        }
    }
}