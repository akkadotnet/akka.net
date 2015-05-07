//-----------------------------------------------------------------------
// <copyright file="RemoteDaemon.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internals;
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
    /// It acts as the brain of the remote that response to system remote messages and executes actions accordingly.
    /// </summary>
    internal class RemoteDaemon : VirtualPathContainer
    {
        private readonly ActorSystemImpl _system;
        private readonly Switch _terminating;
        private readonly IActorRef _terminator;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RemoteDaemon" /> class.
        /// </summary>
        /// <param name="system">The system.</param>
        /// <param name="path">The path.</param>
        /// <param name="parent">The parent.</param>
	    /// <param name="terminator">The remoting terminator.</param>
        /// <param name="log"></param>
        public RemoteDaemon(ActorSystemImpl system, ActorPath path, IInternalActorRef parent, IActorRef terminator, ILoggingAdapter log)
            : base(system.Provider, path, parent, log)
        {
            _system = system;
            _terminating = new Switch(false);
            _terminator = terminator;
            AddressTerminatedTopic.Get(system).Subscribe(this);
        }

       
        /// <summary>
        ///     Called when [receive].
        /// </summary>
        /// <param name="message">The message.</param>
        protected void OnReceive(object message)
        {
            //note: RemoteDaemon does not handle ActorSelection messages - those are handled directly by the RemoteActorRefProvider.
            if (message is IDaemonMsg)
            {
                Log.Debug("Received command [{0}] to RemoteSystemDaemon on [{1}]", message, Path.Address);
                if (message is DaemonMsgCreate) HandleDaemonMsgCreate((DaemonMsgCreate)message);
                return;
            }

            if (message is TerminationHook)
            {
                _terminating.SwitchOn(() =>
                {
                    TerminationHookDoneWhenNoChildren();
                    ForEachChild(c =>
                    {
                        _system.Stop(c);
                    });                    
                });
                return;
            }

            //Remote ActorSystem on another process / machine has died. 
            //Need to clean up any references to remote deployments here.
            if (message is AddressTerminated)
            {
                var addressTerminated = (AddressTerminated) message;
                //stop any remote actors that belong to this address
                ForEachChild(@ref =>
                {
                    if(@ref.Parent.Path.Address == addressTerminated.Address) _system.Stop(@ref);
                });
                return;
            }
        }

        /// <summary>
        ///     Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
            OnReceive(message);
        }

        /// <summary>
        ///     Handles the daemon MSG create.
        /// </summary>
        /// <param name="message">The message.</param>
        private void HandleDaemonMsgCreate(DaemonMsgCreate message)
        {
            var supervisor = (IInternalActorRef) message.Supervisor;
            Props props = message.Props;
            ActorPath childPath;
            if(ActorPath.TryParse(message.Path, out childPath))
            {
                IEnumerable<string> subPath = childPath.Elements.Drop(1); //drop the /remote
                ActorPath path = Path/subPath;
                var localProps = props; //.WithDeploy(new Deploy(Scope.Local));
                IInternalActorRef actor = _system.Provider.ActorOf(_system, localProps, supervisor, path, false,
                    message.Deploy, true, false);
                string childName = subPath.Join("/");
                AddChild(childName, actor);
                actor.Tell(new Watch(actor, this));
                actor.Start();
            }
            else
            {
                Log.Debug("remote path does not match path from message [{0}]", message);
            }
        }

        /// <summary>
        ///     Gets the child.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            string[] parts = name.ToArray();
            //TODO: I have no clue what the scala version does
            if (!parts.Any())
                return this;

            string n = parts.First();
            if (string.IsNullOrEmpty(n))
                return this;

            for (int i = parts.Length; i >= 0; i--)
            {
                string joined = string.Join("/", parts, 0, i);
                IInternalActorRef child;
                if (TryGetChild(joined, out child))
                {
                    //longest match found
                    IEnumerable<string> rest = parts.Skip(i);
                    return child.GetChild(rest);
                }
            }
            return ActorRefs.Nobody;
        }

        public void TerminationHookDoneWhenNoChildren()
        {
            _terminating.WhileOn(() =>
            {
                if (!HasChildren) _terminator.Tell(TerminationHookDone.Instance, this);
            });
        }
    }
}
