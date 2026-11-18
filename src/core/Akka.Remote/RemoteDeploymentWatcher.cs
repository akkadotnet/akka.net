//-----------------------------------------------------------------------
// <copyright file="RemoteDeploymentWatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;

namespace Akka.Remote
{
    /// <summary>
    /// Responsible for cleaning up child references of remote deployed actors when remote node
    /// goes down (crash, network failure), i.e. triggered by Akka.Actor.Terminated.AddressTerminated
    /// </summary>
    internal sealed class RemoteDeploymentWatcher : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {

        private readonly IDictionary<IActorRef, IInternalActorRef> _supervisors =
            new Dictionary<IActorRef, IInternalActorRef>();

        /// <summary>
        /// Creates a watcher that removes remote-deployed child references after their actors terminate.
        /// </summary>
        public RemoteDeploymentWatcher()
        {
            Receive<WatchRemote>(w =>
            {
                _supervisors.Add(w.Actor, w.Supervisor);
                Context.Watch(w.Actor);
            });

            Receive<Terminated>(t =>
            {
                if (_supervisors.TryGetValue(t.ActorRef, out var supervisor))
                {
                    // send extra DeathWatchNotification to the supervisor so that it will remove the child
                    supervisor.SendSystemMessage(new DeathWatchNotification(t.ActorRef, t.ExistenceConfirmed,
                        t.AddressTerminated));
                    _supervisors.Remove(t.ActorRef);
                }
            });
        }

        /// <summary>
        /// Message requesting that a remote-deployed actor be watched on behalf of its supervisor.
        /// </summary>
        internal sealed class WatchRemote
        {
            /// <summary>
            /// Creates a watch request for a remote-deployed actor and its supervisor.
            /// </summary>
            /// <param name="actor">The remote-deployed actor to watch.</param>
            /// <param name="supervisor">The internal actor reference that supervises the remote actor.</param>
            public WatchRemote(IActorRef actor, IInternalActorRef supervisor)
            {
                Actor = actor;
                Supervisor = supervisor;
            }

            /// <summary>
            /// Gets the remote-deployed actor being watched.
            /// </summary>
            public IActorRef Actor { get; private set; }
            /// <summary>
            /// Gets the supervisor whose child reference should be cleaned up when the actor terminates.
            /// </summary>
            public IInternalActorRef Supervisor { get; private set; }
        }
    }
}
