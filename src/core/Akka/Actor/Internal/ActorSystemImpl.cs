//-----------------------------------------------------------------------
// <copyright file="ActorSystemImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using System.Reflection;
using System.Text;
using Akka.Actor.Setup;
using Akka.Serialization;
using Akka.Util;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Actor.Internal
{
    internal interface ISupportSerializationConfigReload
    {
        void ReloadSerialization();
    }

    /// <summary>
    /// INTERNAL API
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class ActorSystemImpl : ExtendedActorSystem, ISupportSerializationConfigReload
    {
        private IActorRef _logDeadLetterListener;
        private readonly ConcurrentDictionary<Type, Lazy<object>> _extensions = new();

        private ILoggingAdapter _log;
        private IActorRefProvider _provider;
        private Settings _settings;
        private readonly string _name;
        private Serialization.Serialization _serialization;
        private EventStream _eventStream;
        private Dispatchers _dispatchers;
        private Mailboxes _mailboxes;
        private IScheduler _scheduler;
        private ActorProducerPipelineResolver _actorProducerPipelineResolver;
        private TerminationCallbacks _terminationCallbacks;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSystemImpl"/> class.
        /// </summary>
        /// <param name="name">The name given to the actor system.</param>
        public ActorSystemImpl(string name)
            : this(
                name,
                ConfigurationFactory.Default(),
                ActorSystemSetup.Empty,
                Option<Props>.None)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSystemImpl"/> class.
        /// </summary>
        /// <param name="name">The name given to the actor system.</param>
        /// <param name="config">The configuration used to configure the actor system.</param>
        /// <param name="setup">The <see cref="ActorSystemSetup"/> used to help programmatically bootstrap the actor system.</param>
        /// <param name="guardianProps">Optional - the props from the /user guardian actor.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="name"/> is an invalid name for an actor system.
        ///  Note that the name must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-').
        /// </exception>
        /// <exception cref="ArgumentNullException">This exception is thrown if the given <paramref name="config"/> is undefined.</exception>
        public ActorSystemImpl(
            string name,
            Config config,
            ActorSystemSetup setup,
            Option<Props>? guardianProps = null)
        {
            if(!Regex.Match(name, "^[a-zA-Z0-9][a-zA-Z0-9-]*$").Success)
                throw new ArgumentException(
                    $"Invalid ActorSystem name [{name}], must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-')", nameof(name));

            // Not checking for empty Config here, default values will be substituted in Settings class constructor (called in ConfigureSettings)
            if(config is null)
                throw new ArgumentNullException(nameof(config), $"Cannot create {typeof(ActorSystemImpl)}: Configuration must not be null.");

            _name = name;

            GuardianProps = guardianProps ?? Option<Props>.None;

            ConfigureSettings(config, setup);
            ConfigureEventStream();
            ConfigureLoggers();
            ConfigureScheduler();
            ConfigureProvider();
            ConfigureTerminationCallbacks();
            ConfigureSerialization();
            ConfigureMailboxes();
            ConfigureDispatchers();
            ConfigureActorProducerPipeline();
        }

        /// <inheritdoc cref="ActorSystem"/>
        public override IActorRefProvider Provider { get { return _provider; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override Settings Settings { get { return _settings; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override string Name { get { return _name; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override Serialization.Serialization Serialization { get { return _serialization; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override EventStream EventStream { get { return _eventStream; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override IActorRef DeadLetters { get { return Provider.DeadLetters; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override IActorRef IgnoreRef { get { return Provider.IgnoreRef; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override Dispatchers Dispatchers { get { return _dispatchers; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override Mailboxes Mailboxes { get { return _mailboxes; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override IScheduler Scheduler { get { return _scheduler; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override ILoggingAdapter Log { get { return _log; } }

        /// <inheritdoc cref="ActorSystem"/>

        public override ActorProducerPipelineResolver ActorPipelineResolver { get { return _actorProducerPipelineResolver; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override IInternalActorRef Guardian { get { return _provider.Guardian; } }

        /// <inheritdoc cref="ActorSystem"/>
        public override IInternalActorRef LookupRoot => _provider.RootGuardian;

        /// <inheritdoc cref="ActorSystem"/>
        public override IInternalActorRef SystemGuardian { get { return _provider.SystemGuardian; } }

        public Option<Props> GuardianProps { get; }

        /// <summary>
        /// Creates a new system actor that lives under the "/system" guardian.
        /// </summary>
        /// <param name="props">The <see cref="Props"/> used to create the actor.</param>
        /// <param name="name">The name of the actor to create. The default value is <see langword="null"/>.</param>
        /// <exception cref="InvalidActorNameException">
        /// This exception is thrown when the given name is invalid or already in use.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when deployment, dispatcher or mailbox configuration is incorrect.
        /// </exception>
        /// <returns>A reference to the underlying actor.</returns>
        public override IActorRef SystemActorOf(Props props, string name = null)
        {
            return _provider.SystemGuardian.Cell.AttachChild(props, true, name);
        }

        /// <summary>
        /// Creates a new system actor that lives under the "/system" guardian.
        /// </summary>
        /// <typeparam name="TActor">
        /// The type of the actor to create. Must have a default constructor declared.
        /// </typeparam>
        /// <param name="name">The name of the actor to create. The default value is <see langword="null"/>.</param>
        /// <exception cref="InvalidActorNameException">
        /// This exception is thrown when the given name is invalid or already in use.
        /// </exception>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown when deployment, dispatcher or mailbox configuration is incorrect.
        /// </exception>
        /// <returns>A reference to the underlying actor.</returns>
        public override IActorRef SystemActorOf<[DynamicallyAccessedMembers(Props.ActorTypeMembers)] TActor>(string name = null)
        {
            return _provider.SystemGuardian.Cell.AttachChild(Props.Create<TActor>(), true, name);
        }

        /// <summary>
        /// If <c>true</c>, then the <see cref="ActorSystem"/> is attempting to abort.
        /// </summary>
        internal volatile bool Aborting = false;

        /// <summary>
        /// Shuts down the <see cref="ActorSystem"/> without all of the usual guarantees,
        /// i.e. we may not guarantee that remotely deployed actors are properly shut down
        /// when we abort.
        /// </summary>
        public override void Abort()
        {
            Aborting = true;
            FinalTerminate(); // Skip CoordinatedShutdown check and aggressively shutdown the ActorSystem
        }

        /// <summary>Starts this system</summary>
        public void Start()
        {
            try
            {
                RegisterOnTermination(StopScheduler);
                _provider.Init(this);
                LoadExtensions();

                if (_settings.LogDeadLetters > 0)
                    _logDeadLetterListener = SystemActorOf<DeadLetterListener>("deadLetterListener");

                _eventStream.StartUnsubscriber(this);

                WarnIfJsonIsDefaultSerializer();

                if (_settings.LogConfigOnStart)
                {
                    _log.Info(Settings.ToString());
                }
            }
            catch (Exception)
            {
                try
                {
                    Terminate();
                }
                catch (Exception)
                {
                    try { StopScheduler();}
                    catch
                    {
                        // ignored
                    }
                }
                throw;
            }
        }

        private void WarnIfJsonIsDefaultSerializer()
        {
            const string configPath = "akka.suppress-json-serializer-warning";
            var showSerializerWarning = Settings.Config.HasPath(configPath) && !Settings.Config.GetBoolean(configPath, false);

            if (showSerializerWarning &&
                Serialization.FindSerializerForType(typeof (object)) is NewtonSoftJsonSerializer)
            {
                Log.Warning($"NewtonSoftJsonSerializer has been detected as a default serializer. " +
                            $"It will be obsoleted in Akka.NET starting from version 1.5 in the favor of Hyperion " +
                            $"(for more info visit: http://getakka.net/articles/networking/serialization.html#how-to-setup-hyperion-as-default-serializer ). " +
                            $"If you want to suppress this message set HOCON `{configPath}` config flag to on.");
            }
        }

        /// <inheritdoc/>
        public override IActorRef ActorOf(Props props, string name = null)
        {
            if(GuardianProps.IsEmpty)
                return _provider.Guardian.Cell.AttachChild(props, false, name);
            throw new InvalidOperationException($"cannot create top-level actor { (string.IsNullOrEmpty(name) ? "" : $"[{name} ]")}from the outside on ActorSystem with custom user guardian");
        }

        /// <inheritdoc/>
        public override ActorSelection ActorSelection(ActorPath actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(actorPath, this);
        }

        /// <inheritdoc/>
        public override ActorSelection ActorSelection(string actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(actorPath, this, _provider.RootGuardian);
        }

        // The akka.scheduler.implementation values that ship inside Akka.dll, constructed directly so that
        // neither the trimmer nor the Native AOT compiler has to see through a Type.GetType call.
        //
        // Keyed by bare type name; TypeExtensions.ToBuiltInAkkaTypeName normalizes what HOCON carries.
        private static readonly Dictionary<string, Func<Config, ILoggingAdapter, IScheduler>> BuiltInSchedulers =
            new(StringComparer.Ordinal)
            {
                ["Akka.Actor.HashedWheelTimerScheduler"] = static (config, log) => new HashedWheelTimerScheduler(config, log)
            };

        private void ConfigureScheduler()
        {
            var schedulerClass = _settings.SchedulerClass;
            // fully qualified: this file also imports System.Reflection, which has its own TypeExtensions
            if (Util.TypeExtensions.ToBuiltInAkkaTypeName(schedulerClass) is { } builtInSchedulerName &&
                BuiltInSchedulers.TryGetValue(builtInSchedulerName, out var factory))
            {
                _scheduler = factory(_settings.Config, Log);
            }
            else if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            {
                _scheduler = CreateScheduler(schedulerClass, _settings.Config, Log);
            }
            else
            {
                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    "akka.scheduler.implementation", schedulerClass, "one of the built-in schedulers"));
            }
        }

        [RequiresUnreferencedCode("Loads the [akka.scheduler.implementation] type by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static IScheduler CreateScheduler(string schedulerClass, Config config, ILoggingAdapter log)
        {
            var schedulerType = Type.GetType(schedulerClass, true);
            return (IScheduler)Activator.CreateInstance(schedulerType, config, log);
        }

        private void StopScheduler()
        {
            var sched = Scheduler as IDisposable;
            sched?.Dispose();
        }

        // Akka's own extensions that akka.extensions commonly names: bare type name -> the assembly it must name, and a
        // factory passing its own literal so the trimmer keeps that type. Nothing is probed unless a row matches.
        private static readonly Dictionary<string, (string Assembly, Func<IExtensionId> Create)> FirstPartyExtensions =
            new(StringComparer.Ordinal)
            {
                ["Akka.DistributedData.DistributedDataProvider"] = ("Akka.DistributedData",
                    static () => CreateFirstPartyExtension("Akka.DistributedData.DistributedDataProvider, Akka.DistributedData")),
                ["Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider"] = ("Akka.Cluster.Tools",
                    static () => CreateFirstPartyExtension("Akka.Cluster.Tools.PublishSubscribe.DistributedPubSubExtensionProvider, Akka.Cluster.Tools")),
                ["Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider"] = ("Akka.Cluster.Tools",
                    static () => CreateFirstPartyExtension("Akka.Cluster.Tools.Client.ClusterClientReceptionistExtensionProvider, Akka.Cluster.Tools")),
                ["Akka.Cluster.Metrics.ClusterMetricsExtensionProvider"] = ("Akka.Cluster.Metrics",
                    static () => CreateFirstPartyExtension("Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics")),
            };

        /// <summary>
        /// The first-party extension <paramref name="extensionFqn"/> names, or <c>null</c> when it names none or its
        /// assembly is absent. Type name and assembly must both match; the assembly identity is ignored.
        /// </summary>
        internal static IExtensionId TryCreateFirstPartyExtension(string extensionFqn)
            => Util.TypeExtensions.TrySplitTypeName(extensionFqn, out var name, out var assembly) &&
               FirstPartyExtensions.TryGetValue(name, out var row) &&
               string.Equals(assembly, row.Assembly, StringComparison.OrdinalIgnoreCase)
                ? row.Create()
                : null;

        /// <summary>Pass a literal: the annotation lets the trimmer keep that type and its constructor.</summary>
        private static IExtensionId CreateFirstPartyExtension(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] string typeName)
        {
            Type type;
            try
            {
                type = Type.GetType(typeName);
            }
            catch (Exception e) when (e is System.IO.FileLoadException or BadImageFormatException or TypeLoadException)
            {
                return null; // an assembly that will not load counts as absent
            }

            return type is null ? null : (IExtensionId)Activator.CreateInstance(type);
        }

        private void LoadExtensions()
        {
            // Setup ids go first, as Hosting's did; one also named in HOCON registers once, since RegisterExtension keys by type
            var extensions = new List<IExtensionId>(_settings.Setup.Get<ExtensionsSetup>()
                .Select(s => s.ExtensionIds).GetOrElse(Array.Empty<IExtensionId>()));
            foreach(var extensionFqn in _settings.Config.GetStringList("akka.extensions", new string[] { }))
            {
                try
                {
                    if (TryCreateFirstPartyExtension(extensionFqn) is { } firstParty)
                    {
                        extensions.Add(firstParty);
                        continue;
                    }
                }
                catch(Exception ex)
                {
                    _log.Error(ex, "While trying to load extension [{0}], skipping...", extensionFqn);
                    continue;
                }

                var extensionType = Type.GetType(extensionFqn);
                if(extensionType == null || !typeof(IExtensionId).IsAssignableFrom(extensionType) || extensionType.IsAbstract || !extensionType.IsClass)
                {
                    _log.Error("[{0}] is not an 'ExtensionId', skipping...", extensionFqn);
                    continue;
                }

                try
                {
                    var extension = (IExtensionId)Activator.CreateInstance(extensionType);
                    extensions.Add(extension);
                }
                catch(Exception ex)
                {
                    _log.Error(ex, "While trying to load extension [{0}], skipping...", extensionFqn);
                }

            }

            ConfigureExtensions(extensions);
        }

        private void ConfigureExtensions(IEnumerable<IExtensionId> extensionIdProviders)
        {
            foreach(var extensionId in extensionIdProviders)
            {
                RegisterExtension(extensionId);
            }
        }

        /// <summary>
        /// Registers the specified extension with this actor system.
        /// </summary>
        /// <param name="extension">The extension to register with this actor system</param>
        /// <returns>The extension registered with this actor system</returns>
        public override object RegisterExtension(IExtensionId extension)
        {
            if (extension == null) return null;

            _extensions.GetOrAdd(extension.ExtensionType, _ => new Lazy<object>(() => extension.CreateExtension(this), LazyThreadSafetyMode.ExecutionAndPublication));

            return extension.Get(this);
        }

        /// <summary>
        /// Retrieves the specified extension that is registered to this actor system.
        /// </summary>
        /// <param name="extensionId">The extension to retrieve</param>
        /// <returns>The specified extension registered to this actor system</returns>
        public override object GetExtension(IExtensionId extensionId)
        {
            TryGetExtension(extensionId.ExtensionType, out var extension);
            return extension;
        }

        /// <summary>
        /// Tries to retrieve an extension with the specified type.
        /// </summary>
        /// <param name="extensionType">The type of extension to retrieve</param>
        /// <param name="extension">The extension that is retrieved if successful</param>
        /// <returns><c>true</c> if the retrieval was successful; otherwise <c>false</c>.</returns>
        public override bool TryGetExtension(Type extensionType, out object extension)
        {
            var wasFound = _extensions.TryGetValue(extensionType, out var lazyExtension);
            extension = wasFound ? lazyExtension.Value : null;
            return wasFound;
        }

        /// <summary>
        /// Tries to retrieve an extension with the specified type.
        /// </summary>
        /// <typeparam name="T">The type of extension to retrieve</typeparam>
        /// <param name="extension">The extension that is retrieved if successful</param>
        /// <returns><c>true</c> if the retrieval was successful; otherwise <c>false</c>.</returns>
        public override bool TryGetExtension<T>(out T extension)
        {
            var wasFound = _extensions.TryGetValue(typeof(T), out var lazyExtension);
            extension = wasFound ? lazyExtension.Value as T : null;
            return wasFound;
        }

        /// <summary>
        /// Retrieves an extension with the specified type that is registered to this actor system.
        /// </summary>
        /// <typeparam name="T">The type of extension to retrieve</typeparam>
        /// <returns>The specified extension registered to this actor system</returns>
        public override T GetExtension<T>()
        {
            TryGetExtension(out T extension);
            return extension;
        }

        /// <summary>
        /// Determines whether this actor system has an extension with the specified type.
        /// </summary>
        /// <param name="type">The type of the extension being queried.</param>
        /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
        public override bool HasExtension(Type type)
        {
            if (typeof(IExtension).IsAssignableFrom(type))
            {
                return _extensions.ContainsKey(type);
            }
            return false;
        }

        /// <summary>
        /// Determines whether this actor system has the specified extension.
        /// </summary>
        /// <typeparam name="T">The type of the extension being queried</typeparam>
        /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
        public override bool HasExtension<T>()
        {
            return _extensions.ContainsKey(typeof(T));
        }

        private void ConfigureSettings(Config config, ActorSystemSetup setup)
        {
            // TODO: on this line, in scala, the config is validated with `Dispatchers.InternalDispatcherId` path removed.
            _settings = new Settings(this, config, setup);
        }

        private void ConfigureEventStream()
        {
            _eventStream = new EventStream(_settings.DebugEventStream);
            _eventStream.StartStdoutLogger(_settings);
        }

        private void ConfigureSerialization()
        {
            _serialization = new Serialization.Serialization(this);
        }

        void ISupportSerializationConfigReload.ReloadSerialization() {
            if(_serialization != null)
                ConfigureSerialization();
        }

        private void ConfigureMailboxes()
        {
            _mailboxes = new Mailboxes(this);
        }

        private void ConfigureProvider()
        {
            try
            {
                // The two built-in out-of-process providers are resolved from compile-time constant type
                // names so the trimmer / Native AOT compiler can see which type each call site loads and
                // keep it. Anything else is named only at runtime and stays fully dynamic.
                _provider = _settings.ProviderSelectionType switch
                {
                    ProviderSelection.Local => new LocalActorRefProvider(_name, _settings, _eventStream),
                    ProviderSelection.Remote => CreateProvider(
                        ProviderSelection.RemoteActorRefProvider,
                        "akka.actor.provider = remote, but Akka.Remote is not referenced by this application.",
                        _name, _settings, _eventStream),
                    ProviderSelection.Cluster => CreateProvider(
                        ProviderSelection.ClusterActorRefProvider,
                        "akka.actor.provider = cluster, but Akka.Cluster is not referenced by this application.",
                        _name, _settings, _eventStream),
                    _ => CreateProvider(
                        _settings.ProviderClass,
                        $"Could not resolve provider type [{_settings.ProviderClass}]. Ensure the type name is fully qualified.",
                        _name, _settings, _eventStream)
                };
            }
            catch (Exception)
            {
                try { StopScheduler(); }
                catch
                {
                    // ignored
                }
                throw;
            }
        }

        /// <summary>
        /// Resolves an <see cref="IActorRefProvider"/> from its type name and constructs it.
        /// </summary>
        /// <remarks>
        /// <paramref name="typeName"/> carries <see cref="DynamicallyAccessedMembersAttribute"/>, which is what
        /// makes this trimmer-safe: the trimmer treats a <see cref="string"/> parameter annotated that way as a
        /// type name, so a call site passing a compile-time constant - the <see cref="ProviderSelection"/>
        /// constants for the built-in providers - has its type resolved statically and kept, along with the public
        /// constructor this method invokes. The annotation flows into <see cref="Type.GetType(string)"/> here, so
        /// neither that call nor the <see cref="Activator.CreateInstance(Type, object[])"/> below needs the
        /// constant inlined in this method. A call site that passes a string only known at runtime cannot be
        /// analyzed and warns there instead, which is the honest answer for a provider named only in HOCON.
        /// </remarks>
        private static IActorRefProvider CreateProvider(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] string typeName,
            string notResolvedMessage,
            string name,
            Settings settings,
            EventStream eventStream)
        {
            var providerType = Type.GetType(typeName);
            if (providerType is null)
                throw new ConfigurationException(notResolvedMessage);

            return (IActorRefProvider)Activator.CreateInstance(providerType, name, settings, eventStream);
        }

        private void ConfigureLoggers()
        {
            _log = new BusLogging(_eventStream, "ActorSystem(" + _name + ")", GetType(), _settings.LogFormatter);
        }

        private void ConfigureDispatchers()
        {
            _dispatchers = new Dispatchers(
                this,
                new DefaultDispatcherPrerequisites(
                    EventStream,
                    Scheduler,
                    Settings,
                    Mailboxes),
                _log);
        }

        private void ConfigureActorProducerPipeline()
        {
            // we push Log in lazy manner since it may not be configured at point of pipeline initialization

            _actorProducerPipelineResolver = new ActorProducerPipelineResolver(() => Log);

        }

        private void ConfigureTerminationCallbacks()
        {
            _terminationCallbacks = new TerminationCallbacks(Provider.TerminationTask);
        }

        /// <summary>
        /// <para>
        /// Registers a block of code (callback) to run after ActorSystem.shutdown has been issued and all actors
        /// in this actor system have been stopped. Multiple code blocks may be registered by calling this method
        /// multiple times.
        /// </para>
        /// <para>
        /// The callbacks will be run sequentially in reverse order of registration, i.e. last registration is run first.
        /// </para>
        /// </summary>
        /// <param name="code">The code to run</param>
        /// <exception cref="Exception">
        /// This exception is thrown if the system has already shut down or if shutdown has been initiated.
        /// </exception>
        public override void RegisterOnTermination(Action code)
        {
            _terminationCallbacks.Add(code);
        }

        /// <summary>
        /// <para>
        /// Terminates this actor system. This will stop the guardian actor, which in turn will recursively stop
        /// all its child actors, then the system guardian (below which the logging actors reside) and the execute
        /// all registered termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </para>
        /// <para>
        /// Be careful to not schedule any operations on completion of the returned task using the `dispatcher`
        /// of this actor system as it will have been shut down before the task completes.
        /// </para>
        /// </summary>
        /// <returns>
        /// A <see cref="Task"/> that will complete once the actor system has finished terminating and all actors are stopped.
        /// </returns>
        public override Task Terminate()
        {
            if(Settings.CoordinatedShutdownRunByActorSystemTerminate)
            {
                return CoordinatedShutdown.Get(this)
                    .Run(CoordinatedShutdown.ActorSystemTerminateReason.Instance);
            }
            return FinalTerminate();
        }

        internal override Task FinalTerminate()
        {
            Log.Debug("System shutdown initiated");
            if (!Settings.LogDeadLettersDuringShutdown && _logDeadLetterListener != null)
                Stop(_logDeadLetterListener);
            _provider.Guardian.Stop();
            return WhenTerminated;
        }

        /// <summary>
        /// Returns a task which will be completed after the <see cref="ActorSystem"/> has been
        /// terminated and termination hooks have been executed. Be careful to not schedule any
        /// operations on the `dispatcher` of this actor system as it will have been shut down
        /// before this task completes.
        /// </summary>
        public override Task WhenTerminated { get { return _terminationCallbacks.TerminationTask; } }

        /// <summary>
        /// Stops the specified actor permanently.
        /// </summary>
        /// <param name="actor">The actor to stop</param>
        public override void Stop(IActorRef actor)
        {
            var path = actor.Path;
            var parentPath = path.Parent;
            if (parentPath == _provider.Guardian.Path)
                _provider.Guardian.Tell(new StopChild(actor));
            else if (parentPath == _provider.SystemGuardian.Path)
                _provider.SystemGuardian.Tell(new StopChild(actor));
            else
                ((IInternalActorRef)actor).Stop();
        }

        public override string ToString()
        {
            return LookupRoot.Path.Root.Address.ToString();
        }

        public override string PrintTree()
        {
            string PrintNode(IActorRef node, string indent)
            {
                var sb = new StringBuilder();
                if (node is ActorRefWithCell wc)
                {
                    const string space = " ";
                    var cell = wc.Underlying;
                    sb.Append(string.IsNullOrEmpty(indent) ? "-> " : indent.Remove(indent.Length-1) + "L-> ")
                        .Append($"{node.Path.Name} {Logging.SimpleName(node)} ");
                    
                    if (cell is ActorCell real)
                    {
                        var realActor = real.Actor;
                        sb.Append(realActor is null ? "null" : realActor.GetType().ToString())
                            .Append($" status={real.Mailbox.CurrentStatus()}");
                    }
                    else
                    {
                        sb.Append(Logging.SimpleName(cell));
                    }
                    sb.Append(space);

                    switch (cell.ChildrenContainer)
                    {
                        case TerminatingChildrenContainer t:
                            var toDie = t.ToDie.ToList();
                            toDie.Sort();
                            var reason = t.Reason;
                            sb.Append($"Terminating({reason})")
                                .Append($"\n{indent}   |    toDie: ")
                                .Append(string.Join($"\n{indent}   |           ", toDie));
                            break;
                        case TerminatedChildrenContainer x:
                            sb.Append(x);
                            break;
                        case EmptyChildrenContainer x:
                            sb.Append(x);
                            break;
                        case NormalChildrenContainer n:
                            sb.Append($"{n.Children.Count} children");
                            break;
                        case var x:
                            sb.Append(Logging.SimpleName(x));
                            break;
                    }

                    if (cell.ChildrenContainer.Children.Count > 0)
                    {
                        sb.Append("\n");

                        var children = cell.ChildrenContainer.Children.ToList();
                        children.Sort();
                        var childStrings = children.Select((t, i) => i == 0
                            ? PrintNode(t, $"{indent}   |")
                            : PrintNode(t, $"{indent}    "));

                        sb.Append(string.Join("\n", childStrings));
                    }
                }
                else
                {
                    sb.Append($"{indent}{node.Path.Name} {Logging.SimpleName(node)}");
                }

                return sb.ToString();
            }

            return PrintNode(LookupRoot, "");
        }
    }

    /// <summary>
    /// This class represents a callback used to run a task when the actor system is terminating.
    /// </summary>
    internal class TerminationCallbacks
    {
        private Task _terminationTask;
        private readonly AtomicReference<Task> _atomicRef;

        /// <summary>
        /// Initializes a new instance of the <see cref="TerminationCallbacks" /> class.
        /// </summary>
        /// <param name="upStreamTerminated">The task to run when the actor system is terminating</param>
        public TerminationCallbacks(Task upStreamTerminated)
        {
            _atomicRef = new AtomicReference<Task>(new Task(() => { }));

            upStreamTerminated.ContinueWith(_ =>
            {
                _terminationTask = _atomicRef.GetAndSet(null);
                _terminationTask.Start();
            });
        }

        /// <summary>
        /// Adds a continuation to the current task being performed.
        /// </summary>
        /// <param name="code">The method to run as part of the continuation</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the actor system has been terminated.</exception>
        public void Add(Action code)
        {
            var previous = _atomicRef.Value;

            if (_atomicRef.Value == null)
                throw new InvalidOperationException("ActorSystem already terminated.");

            var t = new Task(code);

            if (_atomicRef.CompareAndSet(previous, t))
            {
                t.ContinueWith(_ => previous.Start());
                return;
            }

            Add(code);
        }

        /// <summary>
        /// The task that is currently being performed
        /// </summary>
        public Task TerminationTask { get { return _atomicRef.Value ?? _terminationTask; } }
    }
}
