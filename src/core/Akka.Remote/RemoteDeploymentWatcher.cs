//-----------------------------------------------------------------------
// <copyright file="RemoteDeploymentWatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal class RemoteDeploymentWatcher : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {

        private readonly IDictionary<IActorRef, IInternalActorRef> _supervisors =
            new Dictionary<IActorRef, IInternalActorRef>();

        public RemoteDeploymentWatcher()
        {
            Receive<WatchRemote>(w =>
            {
                _supervisors.Add(w.Actor, w.Supervisor);
                Context.Watch(w.Actor);
            });

            Receive<Terminated>(t =>
            {
                IInternalActorRef supervisor;
                if (_supervisors.TryGetValue(t.ActorRef, out supervisor))
                {
                    // send extra DeathWatchNotification to the supervisor so that it will remove the child
                    supervisor.SendSystemMessage(new DeathWatchNotification(t.ActorRef, t.ExistenceConfirmed,
                        t.AddressTerminated));
                    _supervisors.Remove(t.ActorRef);
                }
            });
        }

        internal class WatchRemote
        {
            public WatchRemote(IActorRef actor, IInternalActorRef supervisor)
            {
                Actor = actor;
                Supervisor = supervisor;
            }

            public IActorRef Actor { get; private set; }
            public IInternalActorRef Supervisor { get; private set; }
        }
    }
}
