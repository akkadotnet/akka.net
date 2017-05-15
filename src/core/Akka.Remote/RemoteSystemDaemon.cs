﻿//-----------------------------------------------------------------------
// <copyright file="RemoteSystemDaemon.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IDaemonMsg { }

    /// <summary>
    ///  INTERNAL API
    /// </summary>
    internal class DaemonMsgCreate : IDaemonMsg
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DaemonMsgCreate" /> class.
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="deploy">The deploy.</param>
        /// <param name="path">The path.</param>
        /// <param name="supervisor">The supervisor.</param>
        public DaemonMsgCreate(Props props, Deploy deploy, string path, IActorRef supervisor)
        {
            Props = props;
            Deploy = deploy;
            Path = path;
            Supervisor = supervisor;
        }

        /// <summary>
        ///     Gets the props.
        /// </summary>
        /// <value>The props.</value>
        public Props Props { get; private set; }

        /// <summary>
        ///     Gets the deploy.
        /// </summary>
        /// <value>The deploy.</value>
        public Deploy Deploy { get; private set; }

        /// <summary>
        ///     Gets the path.
        /// </summary>
        /// <value>The path.</value>
        public string Path { get; private set; }

        /// <summary>
        ///     Gets the supervisor.
        /// </summary>
        /// <value>The supervisor.</value>
        public IActorRef Supervisor { get; private set; }
    }

    /// <summary>
    ///  INTERNAL API
    /// 
    /// Internal system "daemon" actor for remote internal communication.
    /// 
    /// It acts as the brain of the remote that responds to system remote messages and executes actions accordingly.
    /// </summary>
    internal class RemoteSystemDaemon : VirtualPathContainer
    {
        private readonly ActorSystemImpl _system;
        private readonly Switch _terminating = new Switch(false);
        private readonly ConcurrentDictionary<IActorRef, IImmutableSet<IActorRef>> _parent2Children = new ConcurrentDictionary<IActorRef, IImmutableSet<IActorRef>>();
        private readonly IActorRef _terminator;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RemoteSystemDaemon" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="path">The path.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="terminator">TBD</param>
        /// <param name="log">TBD</param>
        public RemoteSystemDaemon(ActorSystemImpl system, ActorPath path, IInternalActorRef parent,IActorRef terminator, ILoggingAdapter log)
            : base(system.Provider, path, parent, log)
        {
            _terminator = terminator;
            _system = system;
            AddressTerminatedTopic.Get(system).Subscribe(this);
        }

       
        private void TerminationHookDoneWhenNoChildren()
        {
            _terminating.WhileOn(() =>
            {
                if (!HasChildren)
                {
                    _terminator.Tell(TerminationHookDone.Instance, this);
                }
            });
        }

        /// <summary>
        ///     Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
            //note: RemoteDaemon does not handle ActorSelection messages - those are handled directly by the RemoteActorRefProvider.
            if (message is IDaemonMsg)
            {
                Log.Debug("Received command [{0}] to RemoteSystemDaemon on [{1}]", message, Path.Address);
                if (message is DaemonMsgCreate) HandleDaemonMsgCreate((DaemonMsgCreate)message);
            }

            //Remote ActorSystem on another process / machine has died. 
            //Need to clean up any references to remote deployments here.
            else if (message is AddressTerminated)
            {
                var addressTerminated = (AddressTerminated)message;
                //stop any remote actors that belong to this address
                ForEachChild(@ref =>
                {
                    if (@ref.Parent.Path.Address == addressTerminated.Address) _system.Stop(@ref);
                });
            }
            else if (message is Identify)
            {
                var identify = message as Identify;
                sender.Tell(new ActorIdentity(identify.MessageId, this));
            }
            else if (message is TerminationHook)
            {
                _terminating.SwitchOn(() =>
                {
                    TerminationHookDoneWhenNoChildren();
                    ForEachChild(c => _system.Stop(c));
                });
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            if (message is DeathWatchNotification)
            {
                var deathWatchNotification = message as DeathWatchNotification;
                var child = deathWatchNotification.Actor as ActorRefWithCell;
                if (child != null)
                {
                    if (child.IsLocal)
                    {

                        _terminating.Locked(() =>
                        {
                            var name = child.Path.Elements.Drop(1).Join("/");
                            RemoveChild(name, child);
                            var parent = child.Parent;
                            if (RemoveChildParentNeedsUnwatch(parent, child))
                            {
                                parent.SendSystemMessage(new Unwatch(parent, this));
                            }
                            TerminationHookDoneWhenNoChildren();

                        });
                    }
                }
                else
                {
                    var parent = deathWatchNotification.Actor;
                    var parentWithScope = parent as IActorRefScope;
                    if (parentWithScope != null && !parentWithScope.IsLocal)
                    {
                        _terminating.Locked(() =>
                        {
                            IImmutableSet<IActorRef> children;
                            if (_parent2Children.TryRemove(parent, out children))
                            {
                                foreach (var c in children)
                                {
                                    _system.Stop(c);
                                    var name = c.Path.Elements.Drop(1).Join("/");
                                    RemoveChild(name, c);
                                }
                                TerminationHookDoneWhenNoChildren();
                            }
                        });
                    }
                }
            }
            else
            {
                base.SendSystemMessage(message);
            }
        }

        /// <summary>
        ///     Handles the daemon MSG create.
        /// </summary>
        /// <param name="message">The message.</param>
        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
            var supervisor = (IInternalActorRef) message.Supervisor;
            var parent = supervisor;
            Props props = message.Props;
            ActorPath childPath;
            if(ActorPath.TryParse(message.Path, out childPath))
            {
                IEnumerable<string> subPath = childPath.ElementsWithUid.Drop(1); //drop the /remote
                ActorPath p = Path/subPath;
                var s = subPath.Join("/");
                var i = s.IndexOf("#", StringComparison.Ordinal);
                var childName = i < 0 ? s : s.Substring(0, i); // extract the name without the UID
                var localProps = props; //.WithDeploy(new Deploy(Scope.Local));

                bool isTerminating = !_terminating.WhileOff(() =>
                {
                    IInternalActorRef actor = _system.Provider.ActorOf(_system, localProps, supervisor, p, false,
                    message.Deploy, true, false);
                   
                    AddChild(childName, actor);
                    actor.SendSystemMessage(new Watch(actor, this));
                    actor.Start();
                    if (AddChildParentNeedsWatch(parent, actor))
                    {
                        //TODO: figure out why current transport is not set when this message is sent
                        parent.SendSystemMessage(new Watch(parent, this));
                    }
                });
                if (isTerminating)
                {
                    Log.Error("Skipping [{0}] to RemoteSystemDaemon on [{1}] while terminating", message, p.Address);
                }
                
            }
            else
            {
                Log.Debug("remote path does not match path from message [{0}]", message);
            }
        }

        /// <summary>
        ///     Find the longest matching path which we know about and return that <see cref="IActorRef"/>
        ///     (or ask that <see cref="IActorRef"/> to continue searching if elements are left).
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            var elements = name.ToArray();
            var path = elements.Join("/");
            var n = 0;
            while (true)
            {
                var nameAndUid = ActorCell.SplitNameAndUid(path);
                var child = GetChild(nameAndUid.Name);
                if (child == null)
                {
                    var last = path.LastIndexOf("/", StringComparison.Ordinal);
                    if (last == -1)
                        return Nobody.Instance;
                    path = path.Substring(0, last);
                    n++;
                    continue;
                }
                if (nameAndUid.Uid != ActorCell.UndefinedUid && nameAndUid.Uid != child.Path.Uid)
                    return Nobody.Instance;

                return n == 0 ? child : child.GetChild(elements.TakeRight(n));
            }
        }


        private IInternalActorRef GetChild(string name)
        {
            var nameAndUid = ActorCell.SplitNameAndUid(name);
            IInternalActorRef child;
            if (TryGetChild(nameAndUid.Name, out child))
            {
                if (nameAndUid.Uid != ActorCell.UndefinedUid && nameAndUid.Uid != child.Path.Uid)
                {
                    return ActorRefs.Nobody;
                }
            }
            return child;
        }


        private bool AddChildParentNeedsWatch(IActorRef parent, IActorRef child)
        {
            const bool weDontHaveTailRecursion = true;
            while (weDontHaveTailRecursion)
            {
                if (_parent2Children.TryAdd(parent, ImmutableHashSet<IActorRef>.Empty.Add(child)))
                    return true; //child was successfully added

                if (_parent2Children.TryGetValue(parent, out var children))
                    if (_parent2Children.TryUpdate(parent, children.Add(child), children))
                        return false; //child successfully added
            }
        }

        private bool RemoveChildParentNeedsUnwatch(IActorRef parent, IActorRef child)
        {
            const bool weDontHaveTailRecursion = true;
            while (weDontHaveTailRecursion)
            {
                if (!_parent2Children.TryGetValue(parent, out var children)) 
                    return false; //parent is missing, so child does not need to be removed

                if (_parent2Children.TryUpdate(parent, children.Remove(child), children))
                    return true; //child was removed
            }
        }
    }
}

