using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor.Internals;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor
{
    public interface ActorRefProvider
    {
        /// <summary>
        /// Reference to the supervisor of guardian and systemGuardian; this is
        /// exposed so that the ActorSystemImpl can use it as lookupRoot, i.e.
        /// for anchoring absolute actor look-ups.
        /// </summary>
        InternalActorRef RootGuardian { get; }

        /// <summary>Reference to the supervisor of guardian and systemGuardian at the specified address;
        /// this is exposed so that the ActorRefFactory can use it as lookupRoot, i.e.
        /// for anchoring absolute actor selections.
        /// </summary>
        ActorRef RootGuardianAt(Address address);

        /// <summary> Gets the supervisor used for all top-level user actors.</summary>
        LocalActorRef Guardian { get; }

        /// <summary>Gets the supervisor used for all top-level system actors.</summary>
        LocalActorRef SystemGuardian { get; }

        /// <summary>Gets the dead letters.</summary>
        ActorRef DeadLetters { get; }

        /// <summary>
        /// Gets the root path for all actors within this actor system, not including any remote address information.
        /// </summary>
        ActorPath RootPath { get; }

        /// <summary>Gets the settings.</summary>
        Settings Settings { get; }


        /// <summary>
        /// Initialization of an ActorRefProvider happens in two steps: first
        /// construction of the object with settings, eventStream, etc.
        /// and then—when the ActorSystem is constructed—the second phase during
        /// which actors may be created (e.g. the guardians).
        /// </summary>
        void Init(ActorSystemImpl system);

        /// <summary>Gets the deployer.</summary>
        Deployer Deployer { get; }

        /// <summary>Generates and returns a unique actor path below "/temp".</summary>
        ActorPath TempPath();

        /// <summary>Returns the actor reference representing the "/temp" path.</summary>
        InternalActorRef TempContainer { get; }

        /// <summary>Registers an actorRef at a path returned by <see cref="TempPath"/>; do NOT pass in any other path.</summary>
        /// <param name="actorRef">The actor reference.</param>
        /// <param name="path">A path returned by <see cref="TempPath"/>. Do NOT pass in any other path!</param>
        void RegisterTempActor(InternalActorRef actorRef, ActorPath path);

        /// <summary>Unregister a temporary actor (i.e. obtained from <see cref="TempPath"/>); do NOT pass in any other path.</summary>
        /// <param name="path">A path returned by <see cref="TempPath"/>. Do NOT pass in any other path!</param>
        void UnregisterTempActor(ActorPath path);


        /// <summary>
        /// Actor factory with create-only semantics: will create an actor as
        /// described by <paramref name="props"/> with the given <paramref name="supervisor"/> and <paramref name="path"/> (may be different
        /// in case of remote supervision). If <paramref name="systemService"/> is true, deployment is
        /// bypassed (local-only). If a value for<paramref name="deploy"/> is passed in, it should be
        /// regarded as taking precedence over the nominally applicable settings,
        /// but it should be overridable from external configuration; the lookup of
        /// the latter can be suppressed by setting "lookupDeploy" to "false".
        /// </summary>
        InternalActorRef ActorOf(ActorSystemImpl system, Props props, InternalActorRef supervisor, ActorPath path, bool systemService, Deploy deploy, bool lookupDeploy, bool async);

        /// <summary>Get the actor reference for a specified path. If no such actor exists, it will be (equivalent to) a dead letter reference.</summary>
        ActorRef ResolveActorRef(string path);

        /// <summary>Get the actor reference for a specified path. If no such actor exists, it will be (equivalent to) a dead letter reference.</summary>
        ActorRef ResolveActorRef(ActorPath actorPath);

        /// <summary>
        /// This Future is completed upon termination of this <see cref="ActorRefProvider"/>, which
        /// is usually initiated by stopping the guardian via <see cref="ActorSystem.Stop"/>.
        /// </summary>
        Task TerminationTask { get; }

        /// <summary>
        /// Obtain the address which is to be used within sender references when
        /// sending to the given other address or none if the other address cannot be
        /// reached from this system (i.e. no means of communication known; no
        /// attempt is made to verify actual reachability).
        /// </summary>
        Address GetExternalAddressFor(Address address);

        /// <summary>Gets the external address of the default transport. </summary>
        Address DefaultAddress { get; }
    }

    /// <summary>
    ///     Class LocalActorRefProvider. This class cannot be inherited.
    /// </summary>
    public sealed class LocalActorRefProvider : ActorRefProvider
    {
        private readonly Settings _settings;
        private readonly EventStream _eventStream;
        private readonly Deployer _deployer;
        private readonly InternalActorRef _deadLetters;
        private readonly RootActorPath _rootPath;
        private readonly LoggingAdapter _log;
        private readonly AtomicCounterLong _tempNumber;
        private readonly ActorPath _tempNode;
        private ActorSystemImpl _system;
        private readonly Dictionary<string, InternalActorRef> _extraNames = new Dictionary<string, InternalActorRef>();
        private readonly TaskCompletionSource<Status> _terminationPromise = new TaskCompletionSource<Status>();
        private SupervisorStrategy _systemGuardianStrategy;
        private VirtualPathContainer _tempContainer;
        private RootGuardianActorRef _rootGuardian;
        private LocalActorRef _userGuardian;    //This is called guardian in Akka
        private Func<Mailbox> _defaultMailbox;  //TODO: switch to MailboxType
        private LocalActorRef _systemGuardian;

        public LocalActorRefProvider(string systemName, Settings settings, EventStream eventStream)
            : this(systemName, settings, eventStream, null, null)
        {
            //Intentionally left blank
        }

        public LocalActorRefProvider(string systemName, Settings settings, EventStream eventStream, Deployer deployer, Func<ActorPath, InternalActorRef> deadLettersFactory)
        {
            _settings = settings;
            _eventStream = eventStream;
            _deployer = deployer ?? new Deployer(settings);
            _rootPath = new RootActorPath(new Address("akka", systemName));
            _log = Logging.GetLogger(eventStream, "LocalActorRefProvider(" + _rootPath.Address + ")");
            if(deadLettersFactory == null)
                deadLettersFactory = p => new DeadLetterActorRef(this, p, _eventStream);
            _deadLetters = deadLettersFactory(_rootPath / "deadLetters");
            _tempNumber = new AtomicCounterLong(1);
            _tempNode = _rootPath / "temp";

            //TODO: _guardianSupervisorStrategyConfigurator = dynamicAccess.createInstanceFor[SupervisorStrategyConfigurator](settings.SupervisorStrategyClass, EmptyImmutableSeq).get
            _systemGuardianStrategy = SupervisorStrategy.DefaultStrategy;

        }

        public ActorRef DeadLetters { get { return _deadLetters; } }

        public Deployer Deployer { get { return _deployer; } }

        public InternalActorRef RootGuardian { get { return _rootGuardian; } }

        public ActorPath RootPath { get { return _rootPath; } }

        public Settings Settings { get { return _settings; } }

        public LocalActorRef SystemGuardian { get { return _systemGuardian; } }

        public InternalActorRef TempContainer { get { return _tempContainer; } }

        public Task TerminationTask { get { return _terminationPromise.Task; } }

        public LocalActorRef Guardian { get { return _userGuardian; } }

        private MessageDispatcher DefaultDispatcher { get { return _system.Dispatchers.DefaultGlobalDispatcher; } }

        private SupervisorStrategy UserGuardianSupervisorStrategy { get { return SupervisorStrategy.DefaultStrategy; } }    //TODO: Implement Akka's _guardianSupervisorStrategyConfigurator.create()

        public ActorPath TempPath()
        {
            return _tempNode / GetNextTempName();
        }

        private string GetNextTempName()
        {
            return _tempNumber.GetAndIncrement().Base64Encode();
        }

        /// <summary>
        /// Higher-level providers (or extensions) might want to register new synthetic
        /// top-level paths for doing special stuff. This is the way to do just that.
        /// Just be careful to complete all this before <see cref="ActorSystem.Start"/> finishes,
        /// or before you start your own auto-spawned actors.
        /// </summary>
        public void RegisterExtraName(string name, InternalActorRef actor)
        {
            _extraNames.Add(name, actor);
        }



        private RootGuardianActorRef CreateRootGuardian(ActorSystemImpl system)
        {
            var supervisor = new RootGuardianSupervisor(_rootPath, this, _terminationPromise, _log);
            var rootGuardianStrategy = new OneForOneStrategy(ex =>
            {
                _log.Error(ex, "Guardian failed. Shutting down system");
                return Directive.Stop;
            });
            var props = Props.Create<GuardianActor>(rootGuardianStrategy);
            var rootGuardian = new RootGuardianActorRef(system, props, DefaultDispatcher, _defaultMailbox, supervisor, _rootPath, _deadLetters, _extraNames);
            return rootGuardian;
        }

        public ActorRef RootGuardianAt(Address address)
        {
            return address == _rootPath.Address ? _rootGuardian : _deadLetters;
        }

        private LocalActorRef CreateUserGuardian(LocalActorRef rootGuardian, string name)   //Corresponds to Akka's: override lazy val guardian: LocalActorRef
        {
            return CreateRootGuardianChild(rootGuardian, name, () =>
            {
                var props = Props.Create<GuardianActor>(UserGuardianSupervisorStrategy);

                var userGuardian = new LocalActorRef(_system, props, DefaultDispatcher, _defaultMailbox, rootGuardian, RootPath/name);
                return userGuardian;
            });         
        }

        private LocalActorRef CreateSystemGuardian(LocalActorRef rootGuardian, string name, LocalActorRef userGuardian)     //Corresponds to Akka's: override lazy val guardian: systemGuardian
        {
            //TODO: When SystemGuardianActor has been implemented switch to this:
            //return CreateRootGuardianChild(rootGuardian, name, () =>
            //{
            //    var props = Props.Create(() => new SystemGuardianActor(userGuardian), _systemGuardianStrategy);

            //    var systemGuardian = new LocalActorRef(_system, props, DefaultDispatcher, _defaultMailbox, rootGuardian, RootPath / name);
            //    return systemGuardian;
            //});   
            return (LocalActorRef)rootGuardian.Cell.ActorOf<GuardianActor>(name);           
        }

        private LocalActorRef CreateRootGuardianChild(LocalActorRef rootGuardian, string name, Func<LocalActorRef> childCreator)
        {
            var cell = rootGuardian.Cell;
            cell.ReserveChild(name);
            var child = childCreator();
            cell.InitChild(child);
            child.Start();
            return child;
        }

        public void RegisterTempActor(InternalActorRef actorRef, ActorPath path)
        {
            if(path.Parent != _tempNode)
                throw new Exception("Cannot RegisterTempActor() with anything not obtained from tempPath()");
            _tempContainer.AddChild(path.Name, actorRef);
        }

        public void UnregisterTempActor(ActorPath path)
        {
            if(path.Parent != _tempNode)
                throw new Exception("Cannot UnregisterTempActor() with anything not obtained from tempPath()");
            _tempContainer.RemoveChild(path.Name);
        }

        public void Init(ActorSystemImpl system)
        {
            _system = system;
            //The following are the lazy val statements in Akka
            var defaultDispatcher = system.Dispatchers.DefaultGlobalDispatcher;
            _defaultMailbox = () => new ConcurrentQueueMailbox(); //TODO:system.Mailboxes.FromConfig(Mailboxes.DefaultMailboxId)
            _rootGuardian = CreateRootGuardian(system);
            _tempContainer = new VirtualPathContainer(system.Provider, _tempNode, _rootGuardian, _log);
            _rootGuardian.SetTempContainer(_tempContainer);
            _userGuardian = CreateUserGuardian(_rootGuardian, "user");
            _systemGuardian = CreateSystemGuardian(_rootGuardian, "system", _userGuardian);
            //End of lazy val

            _rootGuardian.Start();
            // chain death watchers so that killing guardian stops the application
            _systemGuardian.Tell(new Watch(_userGuardian, _systemGuardian));    //Should be SendSystemMessage
            _rootGuardian.Tell(new Watch(_systemGuardian, _rootGuardian));      //Should be SendSystemMessage
            _eventStream.StartDefaultLoggers(_system);
        }

        public ActorRef ResolveActorRef(string path)
        {
            ActorPath actorPath;
            if(ActorPath.TryParse(path, out actorPath) && actorPath.Address == _rootPath.Address)
                return ResolveActorRef(_rootGuardian, actorPath.Elements);
            _log.Debug("Resolve of unknown path [{0}] failed. Invalid format.", path);
            return _deadLetters;
        }

        /// <summary>
        ///     Resolves the actor reference.
        /// </summary>
        /// <param name="path">The actor path.</param>
        /// <returns>ActorRef.</returns>
        /// <exception cref="System.NotSupportedException">The provided actor path is not valid in the LocalActorRefProvider</exception>
        public ActorRef ResolveActorRef(ActorPath path)
        {
            if(path.Root == _rootPath)
                return ResolveActorRef(_rootGuardian, path.Elements);
            _log.Debug("Resolve of foreign ActorPath [{0}] failed", path);
            return _deadLetters;

            //Used to be this, but the code above is what Akka has
            //if(_rootPath.Address==actorPath.Address)
            //{
            //    if(actorPath.Elements.Head() == "temp")
            //    {
            //        //skip ""/"temp", 
            //        string[] parts = actorPath.Elements.Drop(1).ToArray();
            //        return _tempContainer.GetChild(parts);
            //    }
            //    //standard
            //    ActorCell currentContext = _rootGuardian.Cell;
            //    foreach(string part in actorPath.Elements)
            //    {
            //        currentContext = ((LocalActorRef)currentContext.Child(part)).Cell;
            //    }
            //    return currentContext.Self;
            //}
            //throw new NotSupportedException("The provided actor path is not valid in the LocalActorRefProvider");
        }

        private ActorRef ResolveActorRef(InternalActorRef actorRef, IReadOnlyCollection<string> pathElements)
        {
            if(pathElements.Count == 0)
            {
                _log.Debug("Resolve of empty path sequence fails (per definition)");
                return _deadLetters;
            }
            var child = actorRef.GetChild(pathElements);
            if(child.IsNobody())
            {
                _log.Debug("Resolve of path sequence [/{0}] failed", ActorPath.FormatPathElements(pathElements));
                return new EmptyLocalActorRef(_system.Provider, actorRef.Path / pathElements, _eventStream);
            }
            return child;
        }


        public InternalActorRef ActorOf(ActorSystemImpl system, Props props, InternalActorRef supervisor, ActorPath path, bool systemService, Deploy deploy, bool lookupDeploy, bool async)
        {
            //TODO: This does not match Akka's ActorOf at all

            Deploy configDeploy = _system.Provider.Deployer.Lookup(path);
            deploy = configDeploy ?? props.Deploy ?? Deploy.None;
            if(deploy.Mailbox != null)
                props = props.WithMailbox(deploy.Mailbox);
            if(deploy.Dispatcher != null)
                props = props.WithDispatcher(deploy.Dispatcher);
            if(deploy.Scope is RemoteScope)
            {

            }

            if(string.IsNullOrEmpty(props.Mailbox))
            {
                //   throw new NotSupportedException("Mailbox can not be configured as null or empty");
            }
            if(string.IsNullOrEmpty(props.Dispatcher))
            {
                //TODO: fix this..
                //    throw new NotSupportedException("Dispatcher can not be configured as null or empty");
            }


            //TODO: how should this be dealt with?
            //akka simply passes the "deploy" var from remote daemon to ActorOf
            //so it atleast seems like they ignore if remote scope is provided here.
            //leaving this for now since it does work

            //if (props.Deploy != null && props.Deploy.Scope is RemoteScope)
            //{
            //    throw new NotSupportedException("LocalActorRefProvider can not deploy remote");
            //}

            if(props.RouterConfig is NoRouter || props.RouterConfig == null)
            {

                props = props.WithDeploy(deploy);
                var dispatcher = system.Dispatchers.FromConfig(props.Dispatcher);
                var mailbox = _system.Mailboxes.FromConfig(props.Mailbox);
                    //TODO: Should be: system.mailboxes.getMailboxType(props2, dispatcher.configurator.config)

                if (async)
                {
                    var reActorRef=new RepointableActorRef(system, props, dispatcher, () => mailbox, supervisor, path);
                    reActorRef.Initialize(async:true);
                    return reActorRef;
                }
                return new LocalActorRef(system, props, dispatcher, () => mailbox, supervisor, path);
            }
            else
            {
                //if no Router config value was specified, override with procedural input
                if(deploy.RouterConfig is NoRouter)
                {
                    deploy = deploy.WithRouterConfig(props.RouterConfig);
                }
                var routerDispatcher = system.Dispatchers.FromConfig(props.RouterConfig.RouterDispatcher);
                var routerMailbox = _system.Mailboxes.FromConfig(props.Mailbox);
                    //TODO: Should be val routerMailbox = system.mailboxes.getMailboxType(routerProps, routerDispatcher.configurator.config)

                // routers use context.actorOf() to create the routees, which does not allow us to pass
                // these through, but obtain them here for early verification
                var routerProps = Props.Empty.WithDeploy(deploy);
                var routeeProps = props.WithRouter(RouterConfig.NoRouter);

                var routedActorRef = new RoutedActorRef(system, routerProps, routerDispatcher, () => routerMailbox, routeeProps,supervisor, path);
                routedActorRef.Initialize(async);
                return routedActorRef;
            }
        }

        public Address GetExternalAddressFor(Address address)
        {
            return address == _rootPath.Address ? address : null;
        }

        public Address DefaultAddress { get { return _rootPath.Address; } }

        public LoggingAdapter Log { get { return _log; } }
    }
}