//-----------------------------------------------------------------------
// <copyright file="RemoteActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Remote
{
    /// <summary>
    /// Marker interface for Actors that are deployed in a remote scope
    /// </summary>
// ReSharper disable once InconsistentNaming
    [InternalApi]
    public interface IRemoteRef : IActorRefScope { }

    /// <summary>
    /// RemoteActorRef - used to provide a local handle to an actor
    /// running in a remote process.
    /// </summary>
    public class RemoteActorRef : InternalActorRefBase, IRemoteRef
    {
        /// <summary>
        /// The deploy
        /// </summary>
        private readonly Deploy _deploy;

        private readonly ActorPath _path;

        /// <summary>
        /// The parent
        /// </summary>
        private readonly IInternalActorRef _parent;
        /// <summary>
        /// The props
        /// </summary>
        private readonly Props _props;

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteActorRef"/> class.
        /// </summary>
        /// <param name="remote">The remote.</param>
        /// <param name="localAddressToUse">The local address to use.</param>
        /// <param name="path">The path.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="props">The props.</param>
        /// <param name="deploy">The deploy.</param>
        public RemoteActorRef(RemoteTransport remote, Address localAddressToUse, ActorPath path, IInternalActorRef parent,
            Props props, Deploy deploy)
        {
            Remote = remote;
            LocalAddressToUse = localAddressToUse;
            _path = path;
            _parent = parent;
            _props = props;
            _deploy = deploy;

            if (path.Address.HasLocalScope)
                throw new ArgumentException($"Unexpected local address in RemoteActorRef [{this}]");
        }

        /// <summary>
        /// Gets the local address to use.
        /// </summary>
        /// <value>The local address to use.</value>
        public Address LocalAddressToUse { get; private set; }

        /// <summary>
        /// Gets the remote.
        /// </summary>
        /// <value>The remote.</value>
        internal RemoteTransport Remote { get; private set; }

        /// <summary>
        /// Gets the parent.
        /// </summary>
        /// <value>The parent.</value>
        public override IInternalActorRef Parent => _parent;

        /// <summary>
        /// Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        public override IActorRefProvider Provider => Remote.Provider;

        private IRemoteActorRefProvider RemoteProvider => Provider as IRemoteActorRefProvider;

        /// <summary>
        /// Obsolete. Use <see cref="Watch"/> or <see cref="ReceiveActor.Receive{T}(Action{T}, Predicate{T})">Receive&lt;<see cref="Akka.Actor.Terminated"/>&gt;</see>
        /// </summary>
        [Obsolete("Use Context.Watch and Receive<Terminated> [1.1.0]")]
        public override bool IsTerminated => false;


        /// <summary>
        /// Gets the child.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        /// <exception cref="System.NotImplementedException">TBD</exception>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            var items = name.ToList();
            switch (items.FirstOrDefault())
            {
                case null:
                    return this;
                case "..":
                    return Parent.GetChild(items.Skip(1));
                default:
                    return new RemoteActorRef(Remote, LocalAddressToUse, Path / items, ActorRefs.Nobody, Props.None, Deploy.None);
            }
        }

        /// <summary>
        /// Resumes the specified caused by failure.
        /// </summary>
        /// <param name="causedByFailure">The caused by failure.</param>
        public override void Resume(Exception causedByFailure = null)
        {
            SendSystemMessage(new Resume(causedByFailure));
        }

        /// <summary>
        /// Stops this instance.
        /// </summary>
        public override void Stop()
        {
            SendSystemMessage(new Terminate());
        }

        /// <summary>
        /// Restarts the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        public override void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        /// <summary>
        /// Suspends this instance.
        /// </summary>
        public override void Suspend()
        {
            SendSystemMessage(new Akka.Dispatch.SysMsg.Suspend());
        }

        /// <inheritdoc cref="IInternalActorRef"/>
        public override bool IsLocal => false;

        /// <inheritdoc cref="IActorRef"/>
        public override ActorPath Path => _path;

        private void HandleException(Exception ex)
        {
            Remote.System.EventStream.Publish(new Error(ex, Path.ToString(), GetType(), "swallowing exception during message send"));
        }

        /// <summary>
        /// Sends the system message.
        /// </summary>
        /// <param name="message">The message.</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            try
            {
                //send to remote, unless watch message is intercepted by the remoteWatcher
                if (message is Watch watch && IsWatchIntercepted(watch.Watchee, watch.Watcher))
                {
                    RemoteProvider.RemoteWatcher.Tell(new RemoteWatcher.WatchRemote(watch.Watchee, watch.Watcher));
                }
                else
                {
                    if (message is Unwatch unwatch && IsWatchIntercepted(unwatch.Watchee, unwatch.Watcher))
                    {
                        RemoteProvider.RemoteWatcher.Tell(new RemoteWatcher.UnwatchRemote(unwatch.Watchee,
                            unwatch.Watcher));
                    }
                    else
                    {
                        Remote.Send(message, null, this);
                    }
                }
            }
            catch (Exception ex)
            {
                HandleException(ex);
            }
        }

        /// <summary>
        /// Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <exception cref="InvalidMessageException">TBD</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if(message == null) throw new InvalidMessageException("Message is null.");
            try { Remote.Send(message, sender, this);}catch(Exception ex) {  HandleException(ex);}
        }

        /// <summary>
        /// Determine if a <see cref="Watch"/>/<see cref="Unwatch"/> message must be handled by the <see cref="RemoteWatcher"/>
        /// actor, or sent to this <see cref="RemoteActorRef"/>.
        /// </summary>
        /// <param name="watchee">The actor being watched.</param>
        /// <param name="watcher">The actor watching.</param>
        /// <returns>TBD</returns>
        public bool IsWatchIntercepted(IActorRef watchee, IActorRef watcher)
        {
            return !watcher.Equals(RemoteProvider.RemoteWatcher) && watchee.Equals(this);
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public override void Start()
        {
            if (_props != null && _deploy != null)
                Remote.Provider.UseActorOnNode(this, _props, _deploy, _parent);
        }
    }
}

