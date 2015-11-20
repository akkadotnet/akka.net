//-----------------------------------------------------------------------
// <copyright file="RemoteDeploymentWatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Util.Internal.Collections;

namespace Akka.Remote
{
    /// <summary>
    /// Responsible for cleaning up child references of remote deployed actors when remote node
    /// goes down (crash, network failure), i.e. triggered by Akka.Actor.Terminated.AddressTerminated
    /// </summary>
    internal class RemoteDeploymentWatcher : ActorBase, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {

        private readonly IImmutableMap<IActorRef, IInternalActorRef> _supervisors =
            ImmutableTreeMap<IActorRef, IInternalActorRef>.Empty;
        protected override bool Receive(object message)
        {
            if (message == null)
            {
                return false;
            }
            return message.Match().With<WatchRemote>(w =>
            {
                _supervisors.Add(w.Actor, w.Supervisor);
                Context.Watch(w.Actor);
            }).With<Terminated>(t =>
            {
                IInternalActorRef supervisor;
                if (_supervisors.TryGet(t.ActorRef, out supervisor))
                {
                    supervisor.SendSystemMessage(new DeathWatchNotification(t.ActorRef, t.ExistenceConfirmed, t.AddressTerminated), supervisor);
                    _supervisors.Remove(t.ActorRef);
                }
            }).WasHandled;
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
