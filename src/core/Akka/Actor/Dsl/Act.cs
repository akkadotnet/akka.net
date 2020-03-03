//-----------------------------------------------------------------------
// <copyright file="Act.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Actor.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IActorDsl
    {
        /// <summary>
        /// TBD
        /// </summary>
        Action<Exception, IActorContext> OnPostRestart { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        Action<Exception, object, IActorContext> OnPreRestart { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        Action<IActorContext> OnPostStop { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        Action<IActorContext> OnPreStart { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        SupervisorStrategy Strategy { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        void Receive<T>(Action<T, IActorContext> handler);
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="shouldHandle">TBD</param>
        /// <param name="handler">TBD</param>
        void Receive<T>(Predicate<T> shouldHandle, Action<T, IActorContext> handler);
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        void Receive<T>(Action<T, IActorContext> handler, Predicate<T> shouldHandle);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        void ReceiveAny(Action<object, IActorContext> handler);

        /// <summary>
        /// Registers an async handler for messages of the specified type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">the type of the message</typeparam>
        /// <param name="handler">the message handler invoked on the incoming message</param>
        /// <param name="shouldHandle">when not null, determines whether this handler should handle the message</param>
        void ReceiveAsync<T>(Func<T, IActorContext, Task> handler, Predicate<T> shouldHandle = null);
        /// <summary>
        /// Registers an async handler for messages of the specified type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="shouldHandle">determines whether this handler should handle the message</param>
        /// <param name="handler">the message handler invoked on the incoming message</param>
        void ReceiveAsync<T>(Predicate<T> shouldHandle, Func<T, IActorContext, Task> handler);
        /// <summary>
        /// Registers an asynchronous handler for any incoming message that has not already been handled.
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        void ReceiveAnyAsync(Func<object, IActorContext, Task> handler);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="message">TBD</param>
        void DefaultPreRestart(Exception reason, object message);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        void DefaultPostRestart(Exception reason);
        /// <summary>
        /// TBD
        /// </summary>
        void DefaultPreStart();
        /// <summary>
        /// TBD
        /// </summary>
        void DefaultPostStop();

        /// <summary>
        /// Changes the actor's behavior and replaces the current handler with the specified handler.
        /// </summary>
        /// <param name="handler">TBD</param>
        void Become(Action<object, IActorContext> handler);

        /// <summary>
        /// Changes the actor's behavior and replaces the current handler with the specified handler without discarding the current.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="handler">TBD</param>
        void BecomeStacked(Action<object, IActorContext> handler);

        /// <summary>
        /// Changes the actor's behavior and replaces the current handler with the previous one on the behavior stack.
        /// <remarks>In order to store an actor on the behavior stack, a call to <see cref="BecomeStacked"/> must have been made
        /// prior to this call</remarks>
        /// </summary>
        void UnbecomeStacked();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        IActorRef ActorOf(Action<IActorDsl> config, string name = null);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class Act : ReceiveActor, IActorDsl
    {
        /// <summary>
        /// TBD
        /// </summary>
        public Action<Exception, IActorContext> OnPostRestart { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action<Exception, object, IActorContext> OnPreRestart { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action<IActorContext> OnPostStop { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Action<IActorContext> OnPreStart { get; set; }
        /// <summary>
        /// TBD
        /// </summary>
        public SupervisorStrategy Strategy { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public Act(Action<IActorDsl> config)
        {
            config(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public Act(Action<IActorDsl, IActorContext> config)
        {
            config(this, Context);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        public void Receive<T>(Action<T, IActorContext> handler)
        {
            Receive<T>(msg => handler(msg, Context));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="handler">TBD</param>
        /// <param name="shouldHandle">TBD</param>
        public void Receive<T>(Action<T, IActorContext> handler, Predicate<T> shouldHandle)
        {
            Receive(msg => handler(msg, Context), shouldHandle);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="shouldHandle">TBD</param>
        /// <param name="handler">TBD</param>
        public void Receive<T>(Predicate<T> shouldHandle, Action<T, IActorContext> handler)
        {
            Receive(shouldHandle, msg => handler(msg, Context));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        public void ReceiveAny(Action<object, IActorContext> handler)
        {
            ReceiveAny(msg => handler(msg, Context));
        }

        /// <summary>
        /// Registers an async handler for messages of the specified type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">the type of the message</typeparam>
        /// <param name="handler">the message handler invoked on the incoming message</param>
        /// <param name="shouldHandle">when not null, determines whether this handler should handle the message</param>
        public void ReceiveAsync<T>(Func<T, IActorContext, Task> handler, Predicate<T> shouldHandle = null)
        {
            ReceiveAsync(msg => handler(msg, Context), shouldHandle);
        }
        /// <summary>
        /// Registers an async handler for messages of the specified type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="shouldHandle">determines whether this handler should handle the message</param>
        /// <param name="handler">the message handler invoked on the incoming message</param>
        public void ReceiveAsync<T>(Predicate<T> shouldHandle, Func<T, IActorContext, Task> handler)
        {
            ReceiveAsync(shouldHandle, msg => handler(msg, Context));
        }
        /// <summary>
        /// Registers an asynchronous handler for any incoming message that has not already been handled.
        /// </summary>
        /// <param name="handler">The message handler that is invoked for all</param>
        public void ReceiveAnyAsync(Func<object, IActorContext, Task> handler)
        {
            ReceiveAnyAsync(msg => handler(msg, Context));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="message">TBD</param>
        public void DefaultPreRestart(Exception reason, object message)
        {
            base.PreRestart(reason, message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        public void DefaultPostRestart(Exception reason)
        {
            base.PostRestart(reason);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void DefaultPreStart()
        {
            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void DefaultPostStop()
        {
            base.PostStop();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        public void Become(Action<object, IActorContext> handler)
        {
            Become(msg => handler(msg, Context));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handler">TBD</param>
        public void BecomeStacked(Action<object, IActorContext> handler)
        {
            BecomeStacked(msg => handler(msg, Context));
        }

        void IActorDsl.UnbecomeStacked()
        {
            base.UnbecomeStacked();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IActorRef ActorOf(Action<IActorDsl> config, string name = null)
        {
            var props = Props.Create(() => new Act(config));
            return Context.ActorOf(props, name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="message">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
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

        /// <summary>
        /// TBD
        /// </summary>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return Strategy ?? base.SupervisorStrategy();
        }
    }

    /// <summary>
    /// This class contains extension methods used for working with <see cref="IActorRefFactory"/>.
    /// </summary>
    public static class ActExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="factory">TBD</param>
        /// <param name="config">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef ActorOf(this IActorRefFactory factory, Action<IActorDsl> config, string name = null)
        {
            return factory.ActorOf(Props.Create(() => new Act(config)), name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="factory">TBD</param>
        /// <param name="config">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef ActorOf(this IActorRefFactory factory, Action<IActorDsl, IActorContext> config, string name = null)
        {
            return factory.ActorOf(Props.Create(() => new Act(config)), name);
        }
    }
}

