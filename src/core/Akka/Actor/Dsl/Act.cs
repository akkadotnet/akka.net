using System;

namespace Akka.Actor.Dsl
{
    public interface IActorDsl
    {
        Action<Exception, IActorContext> OnPostRestart { get; set; }
        Action<Exception, object, IActorContext> OnPreRestart { get; set; }
        Action<IActorContext> OnPostStop { get; set; }
        Action<IActorContext> OnPreStart { get; set; }
        SupervisorStrategy Strategy { get; set; }

        void Receive<T>(Action<T, IActorContext> handler);
        void Receive<T>(Predicate<T> shouldHandle, Action<T, IActorContext> handler);
        void Receive<T>(Action<T, IActorContext> handler, Predicate<T> shouldHandle);
        void ReceiveAny(Action<object, IActorContext> handler);

        void DefaultPreRestart(Exception reason, object message);
        void DefaultPostRestart(Exception reason);
        void DefaultPreStart();
        void DefaultPostStop();

        /// <summary>
        /// Changes the actor's behavior and replaces the current handler with the specified handler.
        /// </summary>
        void Become(Action<object, IActorContext> handler);

        /// <summary>
        /// Changes the actor's behavior and replaces the current handler with the specified handler without discarding the current.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="UnbecomeStacked"/>.</remarks>
        /// </summary>
        void BecomeStacked(Action<object, IActorContext> handler);

        /// <summary>
        /// Changes the actor's behavior and replaces the current handler with the previous one on the behavior stack.
        /// <remarks>In order to store an actor on the behavior stack, a call to <see cref="BecomeStacked"/> must have been made
        /// prior to this call</remarks>
        /// </summary>
        void UnbecomeStacked();

        IActorRef ActorOf(Action<IActorDsl> config, string name = null);
    }

    public sealed class Act : ReceiveActor, IActorDsl
    {
        public Action<Exception, IActorContext> OnPostRestart { get; set; }
        public Action<Exception, object, IActorContext> OnPreRestart { get; set; }
        public Action<IActorContext> OnPostStop { get; set; }
        public Action<IActorContext> OnPreStart { get; set; }
        public SupervisorStrategy Strategy { get; set; }

        public Act(Action<IActorDsl> config)
        {
            config(this);
        }

        public Act(Action<IActorDsl, IActorContext> config)
        {
            config(this, Context);
        }

        public void Receive<T>(Action<T, IActorContext> handler)
        {
            Receive<T>(msg => handler(msg, Context));
        }

        public void Receive<T>(Action<T, IActorContext> handler, Predicate<T> shouldHandle)
        {
            Receive(msg => handler(msg, Context), shouldHandle);
        }
        public void Receive<T>(Predicate<T> shouldHandle, Action<T, IActorContext> handler)
        {
            Receive(shouldHandle, msg => handler(msg, Context));
        }

        public void ReceiveAny(Action<object, IActorContext> handler)
        {
            ReceiveAny(msg => handler(msg, Context));
        }

        public void DefaultPreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);
        }

        public void DefaultPostRestart(Exception reason)
        {
            base.PostRestart(reason);
        }

        public void DefaultPreStart()
        {
            base.PreStart();
        }

        public void DefaultPostStop()
        {
            base.PostStop();
        }

        public void Become(Action<object, IActorContext> handler)
        {
            Become(msg => handler(msg, Context));
        }

        public void BecomeStacked(Action<object, IActorContext> handler)
        {
            BecomeStacked(msg => handler(msg, Context));
        }

        void IActorDsl.UnbecomeStacked()
        {
            base.UnbecomeStacked();
        }

        public IActorRef ActorOf(Action<IActorDsl> config, string name = null)
        {
            var props = Props.Create(() => new Act(config));
            return Context.ActorOf(props, name);
        }

        protected override void PreRestart(Exception reason, object message)
        {
            if (OnPreRestart != null)
            {
                OnPreRestart(reason, message, Context);
            }
            else
            {
                base.PreRestart(reason, message);
            }
        }

        protected override void PostRestart(Exception reason)
        {
            if (OnPostRestart != null)
            {
                OnPostRestart(reason, Context);
            }
            else
            {
                base.PostRestart(reason);
            }
        }

        protected override void PostStop()
        {
            if (OnPostStop != null)
            {
                OnPostStop(Context);
            }
            else
            {
                base.PostStop();
            }
        }

        protected override void PreStart()
        {
            if (OnPreStart != null)
            {
                OnPreStart(Context);
            }
            else
            {
                base.PreStart();
            }
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return Strategy ?? base.SupervisorStrategy();
        }
    }

    public static class ActExtensions
    {
        public static IActorRef ActorOf(this ActorRefFactory factory, Action<IActorDsl> config, string name = null)
        {
            return factory.ActorOf(Props.Create(() => new Act(config)), name);
        }

        public static IActorRef ActorOf(this ActorRefFactory factory, Action<IActorDsl, IActorContext> config, string name = null)
        {
            return factory.ActorOf(Props.Create(() => new Act(config)), name);
        }
    }
}