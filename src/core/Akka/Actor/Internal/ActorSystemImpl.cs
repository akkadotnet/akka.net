﻿// -----------------------------------------------------------------------
//  <copyright file="ActorSystemImpl.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Serialization;
using Akka.Util;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;
using Debug = System.Diagnostics.Debug;

namespace Akka.Actor.Internal;

internal interface ISupportSerializationConfigReload
{
    void ReloadSerialization();
}

/// <summary>
///     INTERNAL API
///     <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
/// </summary>
public class ActorSystemImpl : ExtendedActorSystem, ISupportSerializationConfigReload
{
    private readonly ConcurrentDictionary<Type, Lazy<object>> _extensions = new();
    private readonly string _name;
    private ActorProducerPipelineResolver _actorProducerPipelineResolver;
    private Dispatchers _dispatchers;
    private EventStream _eventStream;

    private ILoggingAdapter _log;
    private IActorRef _logDeadLetterListener;
    private Mailboxes _mailboxes;
    private IActorRefProvider _provider;
    private IScheduler _scheduler;
    private Serialization.Serialization _serialization;
    private Settings _settings;
    private TerminationCallbacks _terminationCallbacks;

    /// <summary>
    ///     If <c>true</c>, then the <see cref="ActorSystem" /> is attempting to abort.
    /// </summary>
    internal volatile bool Aborting;

    /// <summary>
    ///     Initializes a new instance of the <see cref="ActorSystemImpl" /> class.
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
    ///     Initializes a new instance of the <see cref="ActorSystemImpl" /> class.
    /// </summary>
    /// <param name="name">The name given to the actor system.</param>
    /// <param name="config">The configuration used to configure the actor system.</param>
    /// <param name="setup">The <see cref="ActorSystemSetup" /> used to help programmatically bootstrap the actor system.</param>
    /// <param name="guardianProps">Optional - the props from the /user guardian actor.</param>
    /// <exception cref="ArgumentException">
    ///     This exception is thrown if the given <paramref name="name" /> is an invalid name for an actor system.
    ///     Note that the name must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-').
    /// </exception>
    /// <exception cref="ArgumentNullException">This exception is thrown if the given <paramref name="config" /> is undefined.</exception>
    public ActorSystemImpl(
        string name,
        Config config,
        ActorSystemSetup setup,
        Option<Props>? guardianProps = null)
    {
        if (!Regex.Match(name, "^[a-zA-Z0-9][a-zA-Z0-9-]*$").Success)
            throw new ArgumentException(
                $"Invalid ActorSystem name [{name}], must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-')",
                nameof(name));

        // Not checking for empty Config here, default values will be substituted in Settings class constructor (called in ConfigureSettings)
        if (config is null)
            throw new ArgumentNullException(nameof(config),
                $"Cannot create {typeof(ActorSystemImpl)}: Configuration must not be null.");

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

    /// <inheritdoc cref="ActorSystem" />
    public override IActorRefProvider Provider => _provider;

    /// <inheritdoc cref="ActorSystem" />
    public override Settings Settings => _settings;

    /// <inheritdoc cref="ActorSystem" />
    public override string Name => _name;

    /// <inheritdoc cref="ActorSystem" />
    public override Serialization.Serialization Serialization => _serialization;

    /// <inheritdoc cref="ActorSystem" />
    public override EventStream EventStream => _eventStream;

    /// <inheritdoc cref="ActorSystem" />
    public override IActorRef DeadLetters => Provider.DeadLetters;

    /// <inheritdoc cref="ActorSystem" />
    public override IActorRef IgnoreRef => Provider.IgnoreRef;

    /// <inheritdoc cref="ActorSystem" />
    public override Dispatchers Dispatchers => _dispatchers;

    /// <inheritdoc cref="ActorSystem" />
    public override Mailboxes Mailboxes => _mailboxes;

    /// <inheritdoc cref="ActorSystem" />
    public override IScheduler Scheduler => _scheduler;

    /// <inheritdoc cref="ActorSystem" />
    public override ILoggingAdapter Log => _log;

    /// <inheritdoc cref="ActorSystem" />

    public override ActorProducerPipelineResolver ActorPipelineResolver => _actorProducerPipelineResolver;

    /// <inheritdoc cref="ActorSystem" />
    public override IInternalActorRef Guardian => _provider.Guardian;

    /// <inheritdoc cref="ActorSystem" />
    public override IInternalActorRef LookupRoot => _provider.RootGuardian;

    /// <inheritdoc cref="ActorSystem" />
    public override IInternalActorRef SystemGuardian => _provider.SystemGuardian;

    public Option<Props> GuardianProps { get; }

    /// <summary>
    ///     Returns a task which will be completed after the <see cref="ActorSystem" /> has been
    ///     terminated and termination hooks have been executed. Be careful to not schedule any
    ///     operations on the `dispatcher` of this actor system as it will have been shut down
    ///     before this task completes.
    /// </summary>
    public override Task WhenTerminated => _terminationCallbacks.TerminationTask;

    void ISupportSerializationConfigReload.ReloadSerialization()
    {
        if (_serialization != null)
            ConfigureSerialization();
    }

    /// <summary>
    ///     Creates a new system actor that lives under the "/system" guardian.
    /// </summary>
    /// <param name="props">The <see cref="Props" /> used to create the actor.</param>
    /// <param name="name">The name of the actor to create. The default value is <see langword="null" />.</param>
    /// <exception cref="InvalidActorNameException">
    ///     This exception is thrown when the given name is invalid or already in use.
    /// </exception>
    /// <exception cref="ConfigurationException">
    ///     This exception is thrown when deployment, dispatcher or mailbox configuration is incorrect.
    /// </exception>
    /// <returns>A reference to the underlying actor.</returns>
    public override IActorRef SystemActorOf(Props props, string name = null)
    {
        return _provider.SystemGuardian.Cell.AttachChild(props, true, name);
    }

    /// <summary>
    ///     Creates a new system actor that lives under the "/system" guardian.
    /// </summary>
    /// <typeparam name="TActor">
    ///     The type of the actor to create. Must have a default constructor declared.
    /// </typeparam>
    /// <param name="name">The name of the actor to create. The default value is <see langword="null" />.</param>
    /// <exception cref="InvalidActorNameException">
    ///     This exception is thrown when the given name is invalid or already in use.
    /// </exception>
    /// <exception cref="ConfigurationException">
    ///     This exception is thrown when deployment, dispatcher or mailbox configuration is incorrect.
    /// </exception>
    /// <returns>A reference to the underlying actor.</returns>
    public override IActorRef SystemActorOf<TActor>(string name = null)
    {
        return _provider.SystemGuardian.Cell.AttachChild(Props.Create<TActor>(), true, name);
    }

    /// <summary>
    ///     Shuts down the <see cref="ActorSystem" /> without all of the usual guarantees,
    ///     i.e. we may not guarantee that remotely deployed actors are properly shut down
    ///     when we abort.
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

            if (_settings.LogConfigOnStart) _log.Info(Settings.ToString());
        }
        catch (Exception)
        {
            try
            {
                Terminate();
            }
            catch (Exception)
            {
                try
                {
                    StopScheduler();
                }
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
        var showSerializerWarning = Settings.Config.HasPath(configPath) && !Settings.Config.GetBoolean(configPath);

        if (showSerializerWarning &&
            Serialization.FindSerializerForType(typeof(object)) is NewtonSoftJsonSerializer)
            Log.Warning($"NewtonSoftJsonSerializer has been detected as a default serializer. " +
                        $"It will be obsoleted in Akka.NET starting from version 1.5 in the favor of Hyperion " +
                        $"(for more info visit: http://getakka.net/articles/networking/serialization.html#how-to-setup-hyperion-as-default-serializer ). " +
                        $"If you want to suppress this message set HOCON `{configPath}` config flag to on.");
    }

    /// <inheritdoc />
    public override IActorRef ActorOf(Props props, string name = null)
    {
        if (GuardianProps.IsEmpty)
            return _provider.Guardian.Cell.AttachChild(props, false, name);
        throw new InvalidOperationException(
            $"cannot create top-level actor {(string.IsNullOrEmpty(name) ? "" : $"[{name} ]")}from the outside on ActorSystem with custom user guardian");
    }

    /// <inheritdoc />
    public override ActorSelection ActorSelection(ActorPath actorPath)
    {
        return ActorRefFactoryShared.ActorSelection(actorPath, this);
    }

    /// <inheritdoc />
    public override ActorSelection ActorSelection(string actorPath)
    {
        return ActorRefFactoryShared.ActorSelection(actorPath, this, _provider.RootGuardian);
    }

    private void ConfigureScheduler()
    {
        var schedulerType = Type.GetType(_settings.SchedulerClass, true);
        _scheduler = (IScheduler)Activator.CreateInstance(schedulerType, _settings.Config, Log);
    }

    private void StopScheduler()
    {
        var sched = Scheduler as IDisposable;
        sched?.Dispose();
    }

    private void LoadExtensions()
    {
        var extensions = new List<IExtensionId>();
        foreach (var extensionFqn in _settings.Config.GetStringList("akka.extensions", new string[] { }))
        {
            var extensionType = Type.GetType(extensionFqn);
            if (extensionType == null || !typeof(IExtensionId).IsAssignableFrom(extensionType) ||
                extensionType.IsAbstract || !extensionType.IsClass)
            {
                _log.Error("[{0}] is not an 'ExtensionId', skipping...", extensionFqn);
                continue;
            }

            try
            {
                var extension = (IExtensionId)Activator.CreateInstance(extensionType);
                extensions.Add(extension);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "While trying to load extension [{0}], skipping...", extensionFqn);
            }
        }

        ConfigureExtensions(extensions);
    }

    private void ConfigureExtensions(IEnumerable<IExtensionId> extensionIdProviders)
    {
        foreach (var extensionId in extensionIdProviders) RegisterExtension(extensionId);
    }

    /// <summary>
    ///     Registers the specified extension with this actor system.
    /// </summary>
    /// <param name="extension">The extension to register with this actor system</param>
    /// <returns>The extension registered with this actor system</returns>
    public override object RegisterExtension(IExtensionId extension)
    {
        if (extension == null) return null;

        _extensions.GetOrAdd(extension.ExtensionType,
            _ => new Lazy<object>(() => extension.CreateExtension(this), LazyThreadSafetyMode.ExecutionAndPublication));

        return extension.Get(this);
    }

    /// <summary>
    ///     Retrieves the specified extension that is registered to this actor system.
    /// </summary>
    /// <param name="extensionId">The extension to retrieve</param>
    /// <returns>The specified extension registered to this actor system</returns>
    public override object GetExtension(IExtensionId extensionId)
    {
        TryGetExtension(extensionId.ExtensionType, out var extension);
        return extension;
    }

    /// <summary>
    ///     Tries to retrieve an extension with the specified type.
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
    ///     Tries to retrieve an extension with the specified type.
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
    ///     Retrieves an extension with the specified type that is registered to this actor system.
    /// </summary>
    /// <typeparam name="T">The type of extension to retrieve</typeparam>
    /// <returns>The specified extension registered to this actor system</returns>
    public override T GetExtension<T>()
    {
        TryGetExtension(out T extension);
        return extension;
    }

    /// <summary>
    ///     Determines whether this actor system has an extension with the specified type.
    /// </summary>
    /// <param name="type">The type of the extension being queried.</param>
    /// <returns><c>true</c> if this actor system has the extension; otherwise <c>false</c>.</returns>
    public override bool HasExtension(Type type)
    {
        if (typeof(IExtension).IsAssignableFrom(type)) return _extensions.ContainsKey(type);
        return false;
    }

    /// <summary>
    ///     Determines whether this actor system has the specified extension.
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

    private void ConfigureMailboxes()
    {
        _mailboxes = new Mailboxes(this);
    }

    private void ConfigureProvider()
    {
        try
        {
            var providerType = Type.GetType(_settings.ProviderClass);
            Debug.Assert(providerType != null, "providerType != null");
            var provider =
                (IActorRefProvider)Activator.CreateInstance(providerType, _name, _settings, _eventStream);
            _provider = provider;
        }
        catch (Exception)
        {
            try
            {
                StopScheduler();
            }
            catch
            {
                // ignored
            }

            throw;
        }
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
    ///     <para>
    ///         Registers a block of code (callback) to run after ActorSystem.shutdown has been issued and all actors
    ///         in this actor system have been stopped. Multiple code blocks may be registered by calling this method
    ///         multiple times.
    ///     </para>
    ///     <para>
    ///         The callbacks will be run sequentially in reverse order of registration, i.e. last registration is run first.
    ///     </para>
    /// </summary>
    /// <param name="code">The code to run</param>
    /// <exception cref="Exception">
    ///     This exception is thrown if the system has already shut down or if shutdown has been initiated.
    /// </exception>
    public override void RegisterOnTermination(Action code)
    {
        _terminationCallbacks.Add(code);
    }

    /// <summary>
    ///     <para>
    ///         Terminates this actor system. This will stop the guardian actor, which in turn will recursively stop
    ///         all its child actors, then the system guardian (below which the logging actors reside) and the execute
    ///         all registered termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
    ///     </para>
    ///     <para>
    ///         Be careful to not schedule any operations on completion of the returned task using the `dispatcher`
    ///         of this actor system as it will have been shut down before the task completes.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     A <see cref="Task" /> that will complete once the actor system has finished terminating and all actors are stopped.
    /// </returns>
    public override Task Terminate()
    {
        if (Settings.CoordinatedShutdownRunByActorSystemTerminate)
            return CoordinatedShutdown.Get(this)
                .Run(CoordinatedShutdown.ActorSystemTerminateReason.Instance);
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
    ///     Stops the specified actor permanently.
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
                sb.Append(string.IsNullOrEmpty(indent) ? "-> " : indent.Remove(indent.Length - 1) + "L-> ")
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
///     This class represents a callback used to run a task when the actor system is terminating.
/// </summary>
internal class TerminationCallbacks
{
    private readonly AtomicReference<Task> _atomicRef;
    private Task _terminationTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="TerminationCallbacks" /> class.
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
    ///     The task that is currently being performed
    /// </summary>
    public Task TerminationTask => _atomicRef.Value ?? _terminationTask;

    /// <summary>
    ///     Adds a continuation to the current task being performed.
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
}