using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Remote
{
    /// <summary>
    /// Marker interface for Actors that are deployed in a remote scope
    /// </summary>
// ReSharper disable once InconsistentNaming
    internal interface RemoteRef : ActorRefScope { }

    /// <summary>
    /// Class RemoteActorRef.
    /// </summary>
    public class RemoteActorRef : InternalActorRef, RemoteRef
    {
        /// <summary>
        /// The deploy
        /// </summary>
        private readonly Deploy _deploy;

        private readonly ActorPath _path;

        /// <summary>
        /// The parent
        /// </summary>
        private readonly InternalActorRef _parent;
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
        internal RemoteActorRef(RemoteTransport remote, Address localAddressToUse, ActorPath path, InternalActorRef parent,
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
        public override InternalActorRef Parent
        {
            get { return _parent; }
        }

        /// <summary>
        /// Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        public override ActorRefProvider Provider
        {
            get { return Remote.Provider; }
        }

        /// <summary>
        /// Gets the child.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        /// <exception cref="System.NotImplementedException"></exception>
        public override ActorRef GetChild(IEnumerable<string> name)
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
            SendSystemMessage(new Suspend());
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
        private void SendSystemMessage(SystemMessage message)
        {
            Remote.Send(message, null, this);
            Provider.AfterSendSystemMessage(message);
        }

        /// <summary>
        /// Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        protected override void TellInternal(object message, ActorRef sender)
        {
            Remote.Send(message, sender, this);
        }

        /// <summary>
        /// Starts this instance.
        /// </summary>
        public void Start()
        {
            if (_props != null && _deploy != null)
                Remote.Provider.UseActorOnNode(this, _props, _deploy, _parent);
        }
    }
}