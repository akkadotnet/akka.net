﻿//-----------------------------------------------------------------------
// <copyright file="ActorSystemImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Serialization;
using Akka.Util;


namespace Akka.Actor.Internal
{
    /// <summary>
    /// TBD
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class ActorSystemImpl : ExtendedActorSystem
    {
        private IActorRef _logDeadLetterListener;
        private readonly ConcurrentDictionary<Type, Lazy<object>> _extensions = new ConcurrentDictionary<Type, Lazy<object>>();

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
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public ActorSystemImpl(string name)
            : this(name, ConfigurationFactory.Load())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorSystemImpl"/> class.
        /// </summary>
        /// <param name="name">The name given to the actor system.</param>
        /// <param name="config">The configuration used to configure the actor system.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="name"/> is an invalid name for an actor system.
        ///  Note that the name must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-').
        /// </exception>
        /// <exception cref="ArgumentNullException">This exception is thrown if the given <paramref name="config"/> is undefined.</exception>
        public ActorSystemImpl(string name, Config config)
        {
            if(!Regex.Match(name, "^[a-zA-Z0-9][a-zA-Z0-9-]*$").Success)
                throw new ArgumentException(
                    $"Invalid ActorSystem name [{name}], must contain only word characters (i.e. [a-zA-Z0-9] plus non-leading '-')");
            if(config == null)
                throw new ArgumentNullException(nameof(config), "Configuration must not be null.");

            _name = name;            
            ConfigureSettings(config);
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

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRefProvider Provider { get { return _provider; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override Settings Settings { get { return _settings; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override string Name { get { return _name; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override Serialization.Serialization Serialization { get { return _serialization; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override EventStream EventStream { get { return _eventStream; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRef DeadLetters { get { return Provider.DeadLetters; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override Dispatchers Dispatchers { get { return _dispatchers; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override Mailboxes Mailboxes { get { return _mailboxes; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override IScheduler Scheduler { get { return _scheduler; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override ILoggingAdapter Log { get { return _log; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorProducerPipelineResolver ActorPipelineResolver { get { return _actorProducerPipelineResolver; } }


        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef Guardian { get { return _provider.Guardian; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef LookupRoot => _provider.RootGuardian;
        /// <summary>
        /// TBD
        /// </summary>
        public override IInternalActorRef SystemGuardian { get { return _provider.SystemGuardian; } }


        /// <summary>
        /// Creates a new system actor.
        /// </summary>
        /// <param name="props">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IActorRef SystemActorOf(Props props, string name = null)
        {
            return _provider.SystemGuardian.Cell.AttachChild(props, true, name);
        }

        /// <summary>
        /// Creates a new system actor.
        /// </summary>
        /// <typeparam name="TActor">TBD</typeparam>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IActorRef SystemActorOf<TActor>(string name = null)
        {
            return _provider.SystemGuardian.Cell.AttachChild(Props.Create<TActor>(), true, name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal volatile bool Aborting = false;

        /// <summary>
        /// TBD
        /// </summary>
        public override void Abort()
        {
            Aborting = true;
            Terminate();
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
            var showSerializerWarning = Settings.Config.HasPath(configPath) && !Settings.Config.GetBoolean(configPath);

            if (showSerializerWarning &&
                Serialization.FindSerializerForType(typeof (object)) is NewtonSoftJsonSerializer)
            {
                Log.Warning($"NewtonSoftJsonSerializer has been detected as a default serializer. " +
                            $"It will be obsoleted in Akka.NET starting from version 1.5 in the favor of Wire " +
                            $"(for more info visit: http://getakka.net/docs/Serialization#how-to-setup-wire-as-default-serializer ). " +
                            $"If you want to suppress this message set HOCON `{configPath}` config flag to on.");
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="props">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override IActorRef ActorOf(Props props, string name = null)
        {
            return _provider.Guardian.Cell.AttachChild(props, false, name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorPath">TBD</param>
        /// <returns>TBD</returns>
        public override ActorSelection ActorSelection(ActorPath actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(actorPath, this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actorPath">TBD</param>
        /// <returns>TBD</returns>
        public override ActorSelection ActorSelection(string actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(actorPath, this, _provider.RootGuardian);
        }

        private void ConfigureScheduler()
        {
            var schedulerType = Type.GetType(_settings.SchedulerClass, true);
            _scheduler = (IScheduler) Activator.CreateInstance(schedulerType, _settings.Config, Log);
        }

        private void StopScheduler()
        {
            var sched = Scheduler as IDisposable;
            sched?.Dispose();
        }

        /// <summary>
        /// Load all of the extensions registered in the <see cref="ActorSystem.Settings"/>
        /// </summary>
        private void LoadExtensions()
        {
            var extensions = new List<IExtensionId>();
            foreach(var extensionFqn in _settings.Config.GetStringList("akka.extensions"))
            {
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
        /// TBD
        /// </summary>
        /// <param name="extension">TBD</param>
        /// <returns>TBD</returns>
        public override object RegisterExtension(IExtensionId extension)
        {
            if (extension == null) return null;

            _extensions.GetOrAdd(extension.ExtensionType, t => new Lazy<object>(() => extension.CreateExtension(this), LazyThreadSafetyMode.ExecutionAndPublication));

            return extension.Get(this);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="extensionId">TBD</param>
        /// <returns>TBD</returns>
        public override object GetExtension(IExtensionId extensionId)
        {
            object extension;
            TryGetExtension(extensionId.ExtensionType, out extension);
            return extension;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="extensionType">TBD</param>
        /// <param name="extension">TBD</param>
        /// <returns>TBD</returns>
        public override bool TryGetExtension(Type extensionType, out object extension)
        {
            Lazy<object> lazyExtension;
            var wasFound = _extensions.TryGetValue(extensionType, out lazyExtension);
            extension = wasFound ? lazyExtension.Value : null;
            return wasFound;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="extension">TBD</param>
        /// <returns>TBD</returns>
        public override bool TryGetExtension<T>(out T extension)
        {
            Lazy<object> lazyExtension;
            var wasFound = _extensions.TryGetValue(typeof(T), out lazyExtension);
            extension = wasFound ? lazyExtension.Value as T : null;
            return wasFound;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public override T GetExtension<T>()
        {
            T extension;
            TryGetExtension(out extension);
            return extension;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="t">TBD</param>
        /// <returns>TBD</returns>
        public override bool HasExtension(Type t)
        {
            if (typeof(IExtension).IsAssignableFrom(t))
            {
                return _extensions.ContainsKey(t);
            }
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public override bool HasExtension<T>()
        {
            return _extensions.ContainsKey(typeof(T));
        }

        /// <summary>
        ///     Configures the settings.
        /// </summary>
        /// <param name="config">The configuration.</param>
        private void ConfigureSettings(Config config)
        {
            _settings = new Settings(this, config);
        }

        /// <summary>
        ///     Configures the event stream.
        /// </summary>
        private void ConfigureEventStream()
        {
            _eventStream = new EventStream(_settings.DebugEventStream);
            _eventStream.StartStdoutLogger(_settings);
        }

        /// <summary>
        ///     Configures the serialization.
        /// </summary>
        private void ConfigureSerialization()
        {
            _serialization = new Serialization.Serialization(this);
        }

        /// <summary>
        ///     Configures the mailboxes.
        /// </summary>
        private void ConfigureMailboxes()
        {
            _mailboxes = new Mailboxes(this);
        }

        /// <summary>
        ///     Configures the provider.
        /// </summary>
        private void ConfigureProvider()
        {
            try
            {
                Type providerType = Type.GetType(_settings.ProviderClass);
                global::System.Diagnostics.Debug.Assert(providerType != null, "providerType != null");
                var provider =
                    (IActorRefProvider) Activator.CreateInstance(providerType, _name, _settings, _eventStream);
                _provider = provider;
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
        /// Extensions depends on loggers being configured before Start() is called
        /// </summary>
        private void ConfigureLoggers()
        {
            _log = new BusLogging(_eventStream, "ActorSystem(" + _name + ")", GetType(), new DefaultLogMessageFormatter());
        }

        /// <summary>
        ///     Configures the dispatchers.
        /// </summary>
        private void ConfigureDispatchers()
        {
            _dispatchers = new Dispatchers(this, new DefaultDispatcherPrerequisites(EventStream, Scheduler, Settings, Mailboxes));
        }

        /// <summary>
        /// Configures the actor producer pipeline.
        /// </summary>
        private void ConfigureActorProducerPipeline()
        {
            // we push Log in lazy manner since it may not be configured at point of pipeline initialization
            _actorProducerPipelineResolver = new ActorProducerPipelineResolver(() => Log);
        }

        /// <summary>
        /// Configures the termination callbacks.
        /// </summary>
        private void ConfigureTerminationCallbacks()
        {
            _terminationCallbacks = new TerminationCallbacks(Provider.TerminationTask);
        }

        /// <summary>
        /// Register a block of code (callback) to run after ActorSystem.shutdown has been issued and
        /// all actors in this actor system have been stopped.
        /// Multiple code blocks may be registered by calling this method multiple times.
        /// The callbacks will be run sequentially in reverse order of registration, i.e.
        /// last registration is run first.
        /// </summary>
        /// <param name="code">The code to run</param>
        /// <exception cref="Exception">Thrown if the System has already shut down or if shutdown has been initiated.</exception>
        public override void RegisterOnTermination(Action code)
        {
            _terminationCallbacks.Add(code);
        }

        /// <summary>
        ///     Stop this actor system. This will stop the guardian actor, which in turn
        ///     will recursively stop all its child actors, then the system guardian
        ///     (below which the logging actors reside) and the execute all registered
        ///     termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// </summary>
        [Obsolete("Use Terminate instead. This method will be removed in future versions")]
        public override void Shutdown()
        {
            Terminate();
        }

        /// <summary>
        /// Terminates this actor system. This will stop the guardian actor, which in turn
        /// will recursively stop all its child actors, then the system guardian
        /// (below which the logging actors reside) and the execute all registered
        /// termination handlers (<see cref="ActorSystem.RegisterOnTermination" />).
        /// Be careful to not schedule any operations on completion of the returned task
        /// using the `dispatcher` of this actor system as it will have been shut down before the
        /// task completes.
        /// </summary>
        /// <returns>TBD</returns>
        public override Task Terminate()
        {
            Log.Debug("System shutdown initiated");
            _provider.Guardian.Stop();
            return WhenTerminated;
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Use WhenTerminated instead. This property will be removed in future versions")]
        public override Task TerminationTask { get { return _terminationCallbacks.TerminationTask; } }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Use WhenTerminated instead. This method will be removed in future versions")]
        public override void AwaitTermination()
        {
            AwaitTermination(Timeout.InfiniteTimeSpan, CancellationToken.None);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Use WhenTerminated instead. This method will be removed in future versions")]
        public override bool AwaitTermination(TimeSpan timeout)
        {
            return AwaitTermination(timeout, CancellationToken.None);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timeout">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        [Obsolete("Use WhenTerminated instead. This method will be removed in future versions")]
        public override bool AwaitTermination(TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                return WhenTerminated.Wait((int)timeout.TotalMilliseconds, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                //The cancellationToken was canceled.
                return false;
            }
        }

        /// <summary>
        /// Returns a task which will be completed after the ActorSystem has been terminated
        /// and termination hooks have been executed. Be careful to not schedule any operations
        /// on the `dispatcher` of this actor system as it will have been shut down before this
        /// task completes.
        /// </summary>
        public override Task WhenTerminated { get { return _terminationCallbacks.TerminationTask; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
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

    }

    /// <summary>
    /// TBD
    /// </summary>
    class TerminationCallbacks
    {
        private Task _terminationTask;
        private readonly AtomicReference<Task> _atomicRef;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="upStreamTerminated">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="code">TBD</param>
        /// <exception cref="InvalidOperationException">This exception is thrown if the actor system has been terminated.</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public Task TerminationTask { get { return _atomicRef.Value ?? _terminationTask; } }
    }
}

