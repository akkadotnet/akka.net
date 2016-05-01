//-----------------------------------------------------------------------
// <copyright file="RemoteActorRefProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Remote.Configuration;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class RemoteActorRefProvider : IActorRefProvider
    {
        private readonly ILoggingAdapter _log;

        public RemoteActorRefProvider(string systemName, Settings settings, EventStream eventStream)
        {
            settings.InjectTopLevelFallback(RemoteConfigFactory.Default());

            var remoteDeployer = new RemoteDeployer(settings);
            Func<ActorPath, IInternalActorRef> deadLettersFactory = path => new RemoteDeadLetterActorRef(this, path, eventStream);
            _local = new LocalActorRefProvider(systemName, settings, eventStream, remoteDeployer, deadLettersFactory);
            RemoteSettings = new RemoteSettings(settings.Config);
            Deployer = remoteDeployer;
            _log = _local.Log;
        }

        private readonly LocalActorRefProvider _local;
        private volatile Internals _internals;
        private ActorSystemImpl _system;

        private Internals RemoteInternals
        {
            get { return _internals ?? (_internals = CreateInternals()); }
        }

        private Internals CreateInternals()
        {
            var internals =
                new Internals(new Remoting(_system, this), _system.Serialization,
                    new RemoteSystemDaemon(_system, RootPath/"remote", SystemGuardian, _remotingTerminator, _log));
            _local.RegisterExtraName("remote", internals.RemoteDaemon);
            return internals;
        }

        public IInternalActorRef RemoteDaemon { get { return RemoteInternals.RemoteDaemon; } }
        public RemoteTransport Transport { get { return RemoteInternals.Transport; } }

        internal RemoteSettings RemoteSettings { get; private set; }

        /* these are only available after Init() is called */

        public ActorPath RootPath
        {
            get { return _local.RootPath; }
        }


        public IInternalActorRef RootGuardian { get { return _local.RootGuardian; } }
        public LocalActorRef Guardian { get { return _local.Guardian; } }
        public LocalActorRef SystemGuardian { get { return _local.SystemGuardian; } }
        public IInternalActorRef TempContainer { get { return _local.TempContainer; } }
        public IActorRef DeadLetters { get { return _local.DeadLetters; } }
        public Deployer Deployer { get; protected set; }
        public Address DefaultAddress { get { return Transport.DefaultAddress; } }
        public Settings Settings { get { return _local.Settings; } }
        public Task TerminationTask { get { return _local.TerminationTask; } }
        private IInternalActorRef InternalDeadLetters { get { return (IInternalActorRef)_local.DeadLetters; } }

        public ActorPath TempPath()
        {
            return _local.TempPath();
        }

        public void RegisterTempActor(IInternalActorRef actorRef, ActorPath path)
        {
            _local.RegisterTempActor(actorRef, path);
        }

        public void UnregisterTempActor(ActorPath path)
        {
            _local.UnregisterTempActor(path);
        }

        private volatile IActorRef _remotingTerminator;
        private volatile IActorRef _remoteWatcher;
        private volatile IActorRef _remoteDeploymentWatcher;

        public virtual void Init(ActorSystemImpl system)
        {
            _system = system;

            _local.Init(system);

            _remotingTerminator =
                _system.SystemActorOf(
                    RemoteSettings.ConfigureDispatcher(Props.Create(() => new RemotingTerminator(_local.SystemGuardian))),
                    "remoting-terminator");

            _remotingTerminator.Tell(RemoteInternals);

            Transport.Start();
            _remoteWatcher = CreateRemoteWatcher(system);
            _remoteDeploymentWatcher = CreateRemoteDeploymentWatcher(system);
        }

        protected virtual IActorRef CreateRemoteWatcher(ActorSystemImpl system)
        {
            var failureDetector = CreateRemoteWatcherFailureDetector(system);
            return system.SystemActorOf(RemoteSettings.ConfigureDispatcher(
                RemoteWatcher.Props(
                    failureDetector,
                    RemoteSettings.WatchHeartBeatInterval,
                    RemoteSettings.WatchUnreachableReaperInterval,
                    RemoteSettings.WatchHeartbeatExpectedResponseAfter)), "remote-watcher");
        }

        protected virtual IActorRef CreateRemoteDeploymentWatcher(ActorSystemImpl system)
        {
            return system.SystemActorOf(RemoteSettings.ConfigureDispatcher(Props.Create<RemoteDeploymentWatcher>()),
                "remote-deployment-watcher");
        }

        protected DefaultFailureDetectorRegistry<Address> CreateRemoteWatcherFailureDetector(ActorSystem system)
        {
            return new DefaultFailureDetectorRegistry<Address>(() =>
                FailureDetectorLoader.Load(RemoteSettings.WatchFailureDetectorImplementationClass,
                RemoteSettings.WatchFailureDetectorConfig, _system));
        }

        public IInternalActorRef ActorOf(ActorSystemImpl system, Props props, IInternalActorRef supervisor, ActorPath path, bool systemService, Deploy deploy, bool lookupDeploy, bool async)
        {
            if (systemService) return LocalActorOf(system, props, supervisor, path, true, deploy, lookupDeploy, async);

            /*
            * This needs to deal with "mangled" paths, which are created by remote
            * deployment, also in this method. The scheme is the following:
            *
            * Whenever a remote deployment is found, create a path on that remote
            * address below "remote", including the current system’s identification
            * as "sys@host:port" (typically; it will use whatever the remote
            * transport uses). This means that on a path up an actor tree each node
            * change introduces one layer or "remote/scheme/sys@host:port/" within the URI.
            *
            * Example:
            *
            * akka.tcp://sys@home:1234/remote/akka/sys@remote:6667/remote/akka/sys@other:3333/user/a/b/c
            *
            * means that the logical parent originates from "akka.tcp://sys@other:3333" with
            * one child (may be "a" or "b") being deployed on "akka.tcp://sys@remote:6667" and
            * finally either "b" or "c" being created on "akka.tcp://sys@home:1234", where
            * this whole thing actually resides. Thus, the logical path is
            * "/user/a/b/c" and the physical path contains all remote placement
            * information.
            *
            * Deployments are always looked up using the logical path, which is the
            * purpose of the lookupRemotes internal method.
            */

            var elements = path.Elements;
            Deploy configDeploy = null;
            if (lookupDeploy)
            {
                if (elements.Head().Equals("user")) configDeploy = Deployer.Lookup(elements.Drop(1));
                else if (elements.Head().Equals("remote")) configDeploy = LookUpRemotes(elements);
            }

            //merge all of the fallbacks together
            var deployment = new List<Deploy>() { deploy, configDeploy }.Where(x => x != null).Aggregate(Deploy.None, (deploy1, deploy2) => deploy2.WithFallback(deploy1));
            var propsDeploy = new List<Deploy>() { props.Deploy, deployment }.Where(x => x != null)
                .Aggregate(Deploy.None, (deploy1, deploy2) => deploy2.WithFallback(deploy1));

            //match for remote scope
            if (propsDeploy.Scope is RemoteScope)
            {
                var addr = propsDeploy.Scope.AsInstanceOf<RemoteScope>().Address;

                //Even if this actor is in RemoteScope, it might still be a local address
                if (HasAddress(addr))
                {
                    return LocalActorOf(system, props, supervisor, path, false, deployment, false, async);
                }

                //check for correct scope configuration
                if (props.Deploy.Scope is LocalScope)
                {
                    throw new ConfigurationException(
                        string.Format("configuration requested remote deployment for local-only Props at {0}", path));
                }

                try
                {
                    try
                    {
                        // for consistency we check configuration of dispatcher and mailbox locally
                        var dispatcher = _system.Dispatchers.Lookup(props.Dispatcher);
                        var mailboxType = _system.Mailboxes.GetMailboxType(props, dispatcher.Configurator.Config);
                    }
                    catch (Exception ex)
                    {
                        throw new ConfigurationException(
                            string.Format(
                                "Configuration problem while creating {0} with dispatcher [{1}] and mailbox [{2}]", path,
                                props.Dispatcher, props.Mailbox), ex);
                    }
                    var localAddress = Transport.LocalAddressForRemote(addr);
                    var rpath = (new RootActorPath(addr) / "remote" / localAddress.Protocol / localAddress.HostPort() /
                                 path.Elements.ToArray()).
                        WithUid(path.Uid);
                    var remoteRef = new RemoteActorRef(Transport, localAddress, rpath, supervisor, props, deployment);
                    return remoteRef;
                }
                catch (Exception ex)
                {
                    throw new ActorInitializationException(string.Format("Remote deployment failed for [{0}]", path), ex);
                }

            }
            else
            {
                return LocalActorOf(system, props, supervisor, path, false, deployment, false, async);
            }

        }

        /// <summary>
        /// Looks up local overrides for remote deployments
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private Deploy LookUpRemotes(IEnumerable<string> p)
        {
            if (p == null || !p.Any()) return Deploy.None;
            if (p.Head().Equals("remote")) return LookUpRemotes(p.Drop(3));
            if (p.Head().Equals("user")) return Deployer.Lookup(p.Drop(1));
            return Deploy.None;
        }

        private bool HasAddress(Address address)
        {
            return address == _local.RootPath.Address || address == RootPath.Address || Transport.Addresses.Any(a => a == address);
        }

        public IActorRef RootGuardianAt(Address address)
        {
            if (HasAddress(address))
            {
                return RootGuardian;
            }
            return new RemoteActorRef(
                Transport,
                Transport.LocalAddressForRemote(address),
                new RootActorPath(address),
                ActorRefs.Nobody,
                Props.None,
                Deploy.None);
        }

        private IInternalActorRef LocalActorOf(ActorSystemImpl system, Props props, IInternalActorRef supervisor,
            ActorPath path, bool systemService, Deploy deploy, bool lookupDeploy, bool async)
        {
            return _local.ActorOf(system, props, supervisor, path, systemService, deploy, lookupDeploy, async);
        }


        /// <summary>
        /// INTERNAL API.
        /// 
        /// Called in deserialization of incoming remote messages where the correct local address is known.
        /// </summary>
        internal IInternalActorRef ResolveActorRefWithLocalAddress(string path, Address localAddress)
        {
            ActorPath actorPath;
            if (ActorPath.TryParse(path, out actorPath))
            {
                //the actor's local address was already included in the ActorPath
                if (HasAddress(actorPath.Address))
                    return (IInternalActorRef)ResolveActorRef(actorPath);
                return new RemoteActorRef(Transport, localAddress, new RootActorPath(actorPath.Address) / actorPath.Elements, ActorRefs.Nobody, Props.None, Deploy.None);
            }
            _log.Debug("resolve of unknown path [{0}] failed", path);
            return InternalDeadLetters;
        }

        public IActorRef ResolveActorRef(string path)
        {
            if (path == String.Empty)
                return ActorRefs.NoSender;

            ActorPath actorPath;
            if (ActorPath.TryParse(path, out actorPath))
                return ResolveActorRef(actorPath);

            _log.Debug("resolve of unknown path [{0}] failed", path);
            return DeadLetters;
        }


        public IActorRef ResolveActorRef(ActorPath actorPath)
        {
            if (HasAddress(actorPath.Address))
            {
                var elements = actorPath.Elements;
                if (elements.Head() == "remote")
                {
                    if (actorPath.ToStringWithoutAddress() == "/remote")
                    {
                        return RemoteDaemon;
                    }
                    //skip ""/"remote", 
                    var parts = elements.Drop(1);
                    return RemoteDaemon.GetChild(parts);
                }
                if (elements.Head() == "temp")
                {
                    //skip ""/"temp", 
                    var parts = elements.Drop(1);
                    return TempContainer.GetChild(parts);
                }
                //standard
                var rootGuardian = RootGuardian;
                if (actorPath.ToStringWithoutAddress() == "/")
                {
                    return rootGuardian;
                }
                return rootGuardian.GetChild(elements);
            }
            return new RemoteActorRef(Transport,
                Transport.LocalAddressForRemote(actorPath.Address),
                actorPath,
                ActorRefs.Nobody,
                Props.None,
                Deploy.None);
        }

        public Address GetExternalAddressFor(Address address)
        {
            if (HasAddress(address)) { return _local.RootPath.Address; }
            if (!string.IsNullOrEmpty(address.Host) && address.Port.HasValue)
            {
                try
                {
                    return Transport.LocalAddressForRemote(address);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        public void UseActorOnNode(RemoteActorRef actor, Props props, Deploy deploy, IInternalActorRef supervisor)
        {
            _log.Debug("[{0}] Instantiating Remote Actor [{1}]", RootPath, actor.Path);
            IActorRef remoteNode = ResolveActorRef(new RootActorPath(actor.Path.Address) / "remote");
            remoteNode.Tell(new DaemonMsgCreate(props, deploy, actor.Path.ToSerializationFormat(), supervisor));
            _remoteDeploymentWatcher.Tell(new RemoteDeploymentWatcher.WatchRemote(actor, supervisor));
        }

        /// <summary>
        /// Marks a remote system as out of sync and prevents reconnects until the quarantine timeout elapses.
        /// </summary>
        /// <param name="address">Address of the remote system to be quarantined</param>
        /// <param name="uid">UID of the remote system, if the uid is not defined it will not be a strong quarantine but
        /// the current endpoint writer will be stopped (dropping system messages) and the address will be gated
        /// </param>
        public void Quarantine(Address address, int? uid)
        {
            Transport.Quarantine(address, uid);
        }

        /// <summary>
        ///     Afters the send system message.
        /// </summary>
        /// <param name="message">The message.</param>
        public void AfterSendSystemMessage(ISystemMessage message)
        {
            message.Match()
                .With<RemoteWatcher.Rewatch>(rew => _remoteWatcher.Tell(new RemoteWatcher.RewatchRemote(rew.Watchee, rew.Watcher)))
                .With<Watch>(m => _remoteWatcher.Tell(new RemoteWatcher.WatchRemote(m.Watchee, m.Watcher)))
                .With<Unwatch>(m => _remoteWatcher.Tell(new RemoteWatcher.UnwatchRemote(m.Watchee, m.Watcher)));

        }


        #region Internals

        /// <summary>
        /// All of the private internals used by <see cref="RemoteActorRefProvider"/>, namely its transport
        /// registry, remote serializers, and the <see cref="RemoteDaemon"/> instance.
        /// </summary>
        class Internals : INoSerializationVerificationNeeded
        {
            public Internals(RemoteTransport transport, Akka.Serialization.Serialization serialization, IInternalActorRef remoteDaemon)
            {
                Transport = transport;
                Serialization = serialization;
                RemoteDaemon = remoteDaemon;
            }

            public RemoteTransport Transport { get; private set; }

            public Akka.Serialization.Serialization Serialization { get; private set; }

            public IInternalActorRef RemoteDaemon { get; private set; }
        }

        #endregion

        #region RemotingTerminator

        /// <summary>
        /// Describes the FSM states of the <see cref="RemotingTerminator"/>
        /// </summary>
        enum TerminatorState
        {
            Uninitialized,
            Idle,
            WaitDaemonShutdown,
            WaitTransportShutdown,
            Finished
        }

        /// <summary>
        /// Responsible for shutting down the <see cref="RemoteDaemon"/> and all transports
        /// when the <see cref="ActorSystem"/> is being shutdown.
        /// </summary>
        private class RemotingTerminator : FSM<TerminatorState, Internals>
        {
            private readonly IActorRef _systemGuardian;
            private readonly ILoggingAdapter _log;

            public RemotingTerminator(IActorRef systemGuardian)
            {
                _systemGuardian = systemGuardian;
                _log = Context.GetLogger();
                InitFSM();
            }

            private void InitFSM()
            {
                When(TerminatorState.Uninitialized, @event =>
                {
                    var internals = @event.FsmEvent as Internals;
                    if (internals != null)
                    {
                        _systemGuardian.Tell(RegisterTerminationHook.Instance);
                        return GoTo(TerminatorState.Idle).Using(internals);
                    }
                    return null;
                });

                When(TerminatorState.Idle, @event =>
                {
                    if (@event.StateData != null && @event.FsmEvent is TerminationHook)
                    {
                        _log.Info("Shutting down remote daemon.");
                        @event.StateData.RemoteDaemon.Tell(TerminationHook.Instance);
                        return GoTo(TerminatorState.WaitDaemonShutdown);
                    }
                    return null;
                });

                // TODO: state timeout
                When(TerminatorState.WaitDaemonShutdown, @event =>
                {
                    if (@event.StateData != null && @event.FsmEvent is TerminationHookDone)
                    {
                        _log.Info("Remote daemon shut down; proceeding with flushing remote transports.");
                        @event.StateData.Transport.Shutdown()
                            .ContinueWith(t => TransportShutdown.Instance,
                                TaskContinuationOptions.ExecuteSynchronously)
                            .PipeTo(Self);
                        return GoTo(TerminatorState.WaitTransportShutdown);
                    }

                    return null;
                });

                When(TerminatorState.WaitTransportShutdown, @event =>
                {
                    _log.Info("Remoting shut down.");
                    _systemGuardian.Tell(TerminationHookDone.Instance);
                    return Stop();
                });

                StartWith(TerminatorState.Uninitialized, null);
            }

            public sealed class TransportShutdown
            {
                private TransportShutdown() { }
                private static readonly TransportShutdown _instance = new TransportShutdown();
                public static TransportShutdown Instance
                {
                    get
                    {
                        return _instance;
                    }
                }

                public override string ToString()
                {
                    return "<TransportShutdown>";
                }
            }
        }

        #endregion

        private class RemoteDeadLetterActorRef : DeadLetterActorRef
        {
            public RemoteDeadLetterActorRef(IActorRefProvider provider, ActorPath actorPath, EventStream eventStream)
                : base(provider, actorPath, eventStream)
            {
            }

            protected override void TellInternal(object message, IActorRef sender)
            {
                var send = message as EndpointManager.Send;
                var deadLetter = message as DeadLetter;
                if (send != null)
                {
                    if (send.Seq == null)
                    {
                        base.TellInternal(send.Message, send.SenderOption ?? ActorRefs.NoSender);
                    }
                }
                else if (deadLetter?.Message is EndpointManager.Send)
                {
                    var deadSend = (EndpointManager.Send) deadLetter.Message;
                    if (deadSend.Seq == null)
                    {
                        base.TellInternal(deadSend.Message, deadSend.SenderOption ?? ActorRefs.NoSender);
                    }
                }
                else
                {
                    base.TellInternal(message, sender);
                }               
            }
        }
    }
}

