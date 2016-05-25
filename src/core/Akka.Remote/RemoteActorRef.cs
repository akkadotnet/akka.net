//-----------------------------------------------------------------------
// <copyright file="RemoteActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// Marker interface for Actors that are deployed in a remote scope
    /// </summary>
// ReSharper disable once InconsistentNaming
    internal interface IRemoteRef : IActorRefScope { }

    /// <summary>
    /// Class RemoteActorRef.
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
        internal RemoteActorRef(RemoteTransport remote, Address localAddressToUse, ActorPath path, IInternalActorRef parent,
            Props props, Deploy deploy)
        {
            Remote = remote;
            LocalAddressToUse = localAddressToUse;
            _path = path;
            _parent = parent;
            _props = props;
            _deploy = deploy;
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
        public override IInternalActorRef Parent
        {
            get { return _parent; }
        }

        /// <summary>
        /// Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        public override IActorRefProvider Provider
        {
            get { return Remote.Provider; }
        }

        public override bool IsTerminated { get { return false; } }

        /// <summary>
        /// Gets the child.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public override IActorRef GetChild(IEnumerable<string> name)
        {
            throw new NotImplementedException();
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
            SendSystemMessage(Terminate.Instance);
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
            SendSystemMessage(Akka.Dispatch.SysMsg.Suspend.Instance);
        }

        public override bool IsLocal
        {
            get { return false; }
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        /// Sends the system message.
        /// </summary>
        /// <param name="message">The message.</param>
        private void SendSystemMessage(ISystemMessage message)
        {
            Remote.Send(message, null, this);
            Remote.Provider.AfterSendSystemMessage(message);
        }

        /// <summary>
        /// Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        protected override void TellInternal(object message, IActorRef sender)
        {
            Remote.Send(message, sender, this);
            var systemMessage = message as ISystemMessage;
            if (systemMessage != null) Remote.Provider.AfterSendSystemMessage(systemMessage);
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

    public class LazyActorRef : IActorRef
    {
        private readonly Lazy<IActorRef> _lazyActorRef;

        public LazyActorRef(RemoteActorRefProvider provider,Address address, string path)
        {
            _lazyActorRef = new Lazy<IActorRef>(() => provider.ResolveActorRefWithLocalAddress(path,address));
        }

        public void Tell(object message, IActorRef sender)
        {
            _lazyActorRef.Value.Tell(message, sender);
        }

        public bool Equals(IActorRef other)
        {
            return _lazyActorRef.Value.Equals(other);
        }

        public int CompareTo(IActorRef other)
        {
            return _lazyActorRef.Value.CompareTo(other);
        }

        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return _lazyActorRef.Value.ToSurrogate(system);
        }

        public int CompareTo(object obj)
        {
            return _lazyActorRef.Value.CompareTo(obj);
        }

        public ActorPath Path => _lazyActorRef.Value.Path;
    }
}

