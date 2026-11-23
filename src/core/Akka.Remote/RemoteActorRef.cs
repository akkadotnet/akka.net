//-----------------------------------------------------------------------
// <copyright file="RemoteActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Annotations;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util.Internal.Collections;

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

        // Null for a ref built from an inbound sender literal until something reads Path.
        private ActorPath _path;

        private readonly Address _address;

        // The serialized path as UTF-8, written as the recipient literal of every send to this ref:
        // the bytes an inbound sender arrived with, or formatted once on first send.
        private byte[] _pathUtf8;

        /// <summary>
        /// The parent
        /// </summary>
        private readonly IInternalActorRef _parent;
        /// <summary>
        /// The props
        /// </summary>
        private readonly Props _props;

        /// <summary>
        /// INTERNAL API.
        ///
        /// Cached outbound send-routing decision for Akka.Remote.Artery TCP remoting (Pekko's
        /// <c>cachedSendQueueIndex</c>, <c>RemoteActorRef.scala</c>): computed once per ref, on its
        /// first ordinary send, by <c>Akka.Remote.Artery.ArteryRemoting.EnqueueOutbound</c> and
        /// reused for every subsequent send to this same ref -- avoiding repeating the
        /// large-message-destination pattern match / lane hash on every single message. Encodes
        /// either "route to the large-message stream" or an ordinary-outbound lane index; see
        /// <c>ArteryRemoting</c>'s private <c>UnresolvedSendRoute</c>/<c>LargeMessageRoute</c>
        /// constants for the exact encoding.
        ///
        /// <para>
        /// Sentinel <c>-1</c> means "not yet resolved". Meaningless -- and never read -- for
        /// classic DotNetty remoting, which has no lanes/large-message-stream concept. Deliberately
        /// a plain (non-volatile) field, matching Pekko's own <c>cachedSendQueueIndex</c>: the
        /// computed value is a pure, idempotent function of this ref's (immutable) path and the
        /// transport's (fixed for the process's lifetime) settings, so a benign race where two
        /// threads compute and write the same value concurrently is harmless -- no synchronization
        /// needed.
        /// </para>
        /// </summary>
        internal int CachedSendQueueIndex = -1;

        /// <summary>
        /// INTERNAL API. The id an Artery envelope carries for this ref as its recipient, when it
        /// differs from the path's uid; 0 means use the path's uid.
        /// </summary>
        internal long WireId;

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
            _address = path.Address;
            _parent = parent;
            _props = props;
            _deploy = deploy;

            if (path.Address.HasLocalScope)
                throw new ArgumentException($"Unexpected local address in RemoteActorRef [{this}]");
        }

        /// <summary>
        /// INTERNAL API. A ref to the sender of an inbound Artery message, built without decoding or
        /// parsing its path: <paramref name="pathUtf8"/> is the sender path literal as the origin
        /// system wrote it and <paramref name="address"/> is that origin's address. <see cref="Path"/>
        /// is decoded and parsed from the bytes the first time it is read.
        /// </summary>
        internal RemoteActorRef(RemoteTransport remote, Address localAddressToUse, Address address, byte[] pathUtf8, long wireId)
        {
            Remote = remote;
            LocalAddressToUse = localAddressToUse;
            _address = address;
            _pathUtf8 = pathUtf8;
            WireId = wireId;
            _parent = ActorRefs.Nobody;
            _props = Props.None;
            _deploy = Deploy.None;
        }

        /// <summary>INTERNAL API. The address of the system this ref points to, read without forcing <see cref="Path"/>.</summary>
        internal Address RemoteAddress => _address;

        /// <summary>INTERNAL API. The uid of <see cref="Path"/>, read without forcing it.</summary>
        internal long PathUid => _path?.Uid ?? (WireId > 0 ? WireId : 0);

        /// <summary>INTERNAL API. The serialized path as UTF-8, formatted at most once.</summary>
        internal byte[] SerializedPathUtf8 =>
            _pathUtf8 ??= System.Text.Encoding.UTF8.GetBytes(_path.ToSerializationFormatWithAddress(_address));

        private ActorPath MaterializePath()
        {
            var text = System.Text.Encoding.UTF8.GetString(_pathUtf8);
            if (!ActorPath.TryParse(text, out var path))
                throw new InvalidOperationException($"Unparseable remote sender path [{text}]");
            return _path = path;
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
#pragma warning disable CS0809
        public override bool IsTerminated => false;
#pragma warning restore CS0809

        /// <summary>
        /// Gets the child.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns>ActorRef.</returns>
        public override IActorRef GetChild(IReadOnlyList<string> name)
        {
            switch (name.FirstOrDefault())
            {
                case null:
                    return this;
                case "..":
                    return Parent.GetChild(name.NoCopySlice(1));
                default:
                    return new RemoteActorRef(Remote, LocalAddressToUse, Path / name, ActorRefs.Nobody, Props.None, Deploy.None);
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

        
        public override bool IsLocal => false;

        /// <inheritdoc cref="IActorRef"/>
        public override ActorPath Path => _path ?? MaterializePath();

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
        /// <exception cref="InvalidMessageException">The message is <c>null</c>.</exception>
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
        /// <returns><c>true</c> if the watch is intercepted for this remote actor; otherwise, <c>false</c>.</returns>
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

