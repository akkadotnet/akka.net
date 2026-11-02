using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Annotations;
using Akka.Configuration;
using Akka.DependencyInjection;
using Akka.Hosting.HealthChecks;
using Akka.Hosting.Logging;
using Akka.Serialization;
using Akka.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Akka.Hosting
{
    /// <summary>
    /// Delegate used to configure how to merge new HOCON in with the previous HOCON
    /// that has already been added to the <see cref="AkkaConfigurationBuilder"/>.
    /// </summary>
    public delegate Config HoconConfigurator(Config currentConfig, Config configToAdd);

    /// <summary>
    /// Describes how to add a new <see cref="Config"/> section to our existing HOCON.
    /// </summary>
    public enum HoconAddMode
    {
        /// <summary>
        /// Appends this HOCON to the back as a fallback.
        /// </summary>
        Append,

        /// <summary>
        /// Prepend this HOCON to the front, overriding current values without deleting them.
        /// </summary>
        Prepend,

        /// <summary>
        /// Replace all current HOCON with this HOCON instead.
        /// </summary>
        /// <remarks>
        /// WARNING: this is a destructive action. If you are writing a plugin or extension, never
        /// call a method with this value directly. Always allow the user to choose. 
        /// </remarks>
        Replace
    }

    /// <summary>
    /// Delegate used to instantiate <see cref="IActorRef"/>s once the <see cref="ActorSystem"/> has booted.
    /// </summary>
    public delegate Task ActorStarter(ActorSystem system, IActorRegistry registry);
    
    public delegate Task ActorStarterWithResolver(ActorSystem system, IActorRegistry registry, IDependencyResolver resolver);

    public delegate Task StartupTask(ActorSystem system, IActorRegistry registry);
    
    /// <summary>
    /// Used to help populate a <see cref="SerializationSetup"/> upon starting the <see cref="ActorSystem"/>,
    /// if any are added to the builder;
    /// </summary>
    internal sealed class SerializerRegistration
    {
        public SerializerRegistration(string id, ImmutableHashSet<Type> typeBindings,
            Func<ExtendedActorSystem, Serializer> serializerFactory)
        {
            Id = id;
            TypeBindings = typeBindings;
            SerializerFactory = serializerFactory;
        }

        public string Id { get; }

        public Func<ExtendedActorSystem, Serializer> SerializerFactory { get; }

        public ImmutableHashSet<Type> TypeBindings { get; }
    }

    public sealed class AkkaConfigurationBuilder
    {
        internal readonly string ActorSystemName;
        internal readonly IServiceCollection ServiceCollection;
        internal readonly HashSet<SerializerRegistration> Serializers = new();
        
        // we use a name / registration dictionary to make health check registrations unique by name
        internal readonly Dictionary<string, AkkaHealthCheckRegistration> HealthChecks = new();
        internal readonly List<Type> Extensions = new();

        /// <summary>
        /// INTERNAL API.
        /// 
        /// <para>
        /// Do NOT modify this field directly. This field is exposed only for testing purposes and is subject to change in the future.
        /// </para>
        /// Use the provided <see cref="AddSetup"/> method instead.
        /// </summary>
        [InternalApi]
        public readonly HashSet<Setup> Setups = new();
        
        /// <summary>
        /// The currently configured <see cref="ProviderSelection"/>.
        /// </summary>
        public Option<ProviderSelection> ActorRefProvider { get; private set; } = Option<ProviderSelection>.None;

        /// <summary>
        /// The current HOCON configuration.
        /// </summary>
        public Option<Config> Configuration { get; private set; } = Option<Config>.None;

        /// <summary>
        /// INTERNAL API.
        ///
        /// Used to hold a reference to the <see cref="Sys"/> being started.
        /// </summary>
        internal Option<ActorSystem> Sys { get; set; } = Option<ActorSystem>.None;

        private readonly List<ActorStarter> _actorStarters = new();
        private readonly List<StartupTask> _startupTasks = new();
        private bool _complete;

        public AkkaConfigurationBuilder(IServiceCollection serviceCollection, string actorSystemName)
        {
            ServiceCollection = serviceCollection;
            ActorSystemName = actorSystemName;
        }

        internal AkkaConfigurationBuilder AddSetup(Setup setup)
        {
            if (_complete) return this;

            // we will recreate our own BootstrapSetup later - just extract the parts for now.
            if (setup is BootstrapSetup bootstrapSetup)
            {
                if (bootstrapSetup.Config.HasValue)
                    Configuration = Configuration.HasValue
                        ? Configuration.FlatSelect<Config>(c => bootstrapSetup.Config.Value.WithFallback(c))
                        : bootstrapSetup.Config;
                ActorRefProvider = bootstrapSetup.ActorRefProvider;
                return this;
            }

            // don't apply the diSetup
            if (setup is DependencyResolverSetup)
            {
                return this;
            }

            Setups.Add(setup);
            return this;
        }

        internal AkkaConfigurationBuilder WithActorRefProvider(ProviderSelection provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));
            if (_complete) return this;
            ActorRefProvider = provider;
            return this;
        }

        internal AkkaConfigurationBuilder AddHoconConfiguration(HoconConfigurator configurator, Config newHocon)
        {
            if (newHocon == null)
                throw new ArgumentNullException(nameof(newHocon));
            if (configurator == null)
                throw new ArgumentNullException(nameof(configurator));

            if (_complete) return this;
            Configuration = configurator(Configuration.GetOrElse(Config.Empty), newHocon);
            return this;
        }

        internal AkkaConfigurationBuilder AddHoconConfiguration(Config newHocon, HoconAddMode addMode)
        {
            return addMode switch
            {
                HoconAddMode.Append => AddHoconConfiguration((config, add) => config.WithFallback(add), newHocon),
                HoconAddMode.Prepend => AddHoconConfiguration((config, add) => add.WithFallback(config), newHocon),
                HoconAddMode.Replace => AddHoconConfiguration((config, add) => add.WithFallback(config), newHocon),
                _ => throw new ArgumentOutOfRangeException(nameof(addMode), addMode, null)
            };
        }

        private static ActorStarter ToAsyncStarter(Action<ActorSystem, IActorRegistry> nonAsyncStarter)
        {
            Task Starter(ActorSystem f, IActorRegistry registry)
            {
                nonAsyncStarter(f, registry);
                return Task.CompletedTask;
            }

            return Starter;
        }

        private static ActorStarter ToAsyncStarter(
            Action<ActorSystem, IActorRegistry, IDependencyResolver> nonAsyncStarter)
        {
            Task Starter(ActorSystem f, IActorRegistry registry)
            {
                nonAsyncStarter(f, registry, DependencyResolver.For(f).Resolver);
                return Task.CompletedTask;
            }

            return Starter;
        }
        
        private static StartupTask ToAsyncStartup(Action<ActorSystem, IActorRegistry> nonAsyncStartup)
        {
            Task Startup(ActorSystem f, IActorRegistry registry)
            {
                nonAsyncStartup(f, registry);
                return Task.CompletedTask;
            }

            return Startup;
        }

        public AkkaConfigurationBuilder StartActors(Action<ActorSystem, IActorRegistry> starter)
        {
            if (_complete) return this;
            _actorStarters.Add(ToAsyncStarter(starter));
            return this;
        }
        
        public AkkaConfigurationBuilder StartActors(Action<ActorSystem, IActorRegistry, IDependencyResolver> starter)
        {
            if (_complete) return this;
            _actorStarters.Add(ToAsyncStarter(starter));
            return this;
        }

        public AkkaConfigurationBuilder StartActors(ActorStarter starter)
        {
            if (_complete) return this;
            _actorStarters.Add(starter);
            return this;
        }
        
        public AkkaConfigurationBuilder StartActors(ActorStarterWithResolver starter)
        {
            if (_complete) return this;

            Task Starter1(ActorSystem f, IActorRegistry registry) => starter(f, registry, DependencyResolver.For(f).Resolver);

            _actorStarters.Add(Starter1);
            return this;
        }

        /// <summary>
        /// Adds a <see cref="StartupTask"/> delegate that will be executed exactly once for application initialization
        /// once the <see cref="ActorSystem"/> and all actors is started in this process.
        /// </summary>
        /// <param name="startupTask">A <see cref="StartupTask"/> delegate that will be run after all actors
        /// have been instantiated.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public AkkaConfigurationBuilder AddStartup(Action<ActorSystem, IActorRegistry> startupTask)
        {
            if (_complete) return this;
            _startupTasks.Add(ToAsyncStartup(startupTask));
            return this;
        }

        /// <summary>
        /// Adds a <see cref="StartupTask"/> delegate that will be executed exactly once for application initialization
        /// once the <see cref="ActorSystem"/> and all actors is started in this process.
        /// </summary>
        /// <param name="startupTask">A <see cref="StartupTask"/> delegate that will be run after all actors
        /// have been instantiated.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public AkkaConfigurationBuilder AddStartup(StartupTask startupTask)
        {
            if (_complete) return this;
            _startupTasks.Add(startupTask);
            return this;
        }
        
        public AkkaConfigurationBuilder WithCustomSerializer(
            string serializerIdentifier, IEnumerable<Type> boundTypes,
            Func<ExtendedActorSystem, Serializer> serializerFactory)
        {
            var serializerRegistration = new SerializerRegistration(serializerIdentifier,
                boundTypes.ToImmutableHashSet(), serializerFactory);
            Serializers.Add(serializerRegistration);
            return this;
        }

        /// <summary>
        /// Adds a list of Akka.NET extensions that will be started automatically when the <see cref="ActorSystem"/>
        /// starts up.
        /// </summary>
        /// <example>
        /// <code>
        /// // Starts distributed pub-sub, cluster metrics, and cluster bootstrap extensions at start-up
        /// builder.WithExtensions(
        ///     typeof(DistributedPubSubExtensionProvider),
        ///     typeof(ClusterMetricsExtensionProvider),
        ///     typeof(ClusterBootstrapProvider));
        /// </code>
        /// </example>
        /// <param name="extensions">An array of extension providers that will be automatically started
        /// when the <see cref="ActorSystem"/> starts</param>
        /// <returns>This <see cref="AkkaConfigurationBuilder"/> instance, for fluent building pattern</returns>
        public AkkaConfigurationBuilder WithExtensions(params Type[] extensions)
        {
            foreach (var extension in extensions)
            {
                if (!typeof(IExtensionId).IsAssignableFrom(extension))
                    throw new ConfigurationException($"Type must extends {nameof(IExtensionId)}: [{extension.FullName}]");
                
                var typeInfo = extension.GetTypeInfo();
                if (typeInfo.IsAbstract || !typeInfo.IsClass)
                    throw new ConfigurationException("Type class must not be abstract or static");
                
                if (Extensions.Contains(extension))
                    continue;
                Extensions.Add(extension);
            }
            return this;
        }

        public AkkaConfigurationBuilder WithExtension<T>() where T : IExtensionId
        {
            var type = typeof(T);
            if (Extensions.Contains(type)) 
                return this;
            
            var typeInfo = type.GetTypeInfo();
            if (typeInfo.IsAbstract || !typeInfo.IsClass)
                throw new ConfigurationException("Type class must not be abstract or static");

            Extensions.Add(type);

            return this;
        }

        /// <summary>
        /// Registers an <see cref="AkkaHealthCheckRegistration"/> with the <see cref="AkkaConfigurationBuilder"/>.
        /// </summary>
        /// <param name="registration">The health check registration.</param>
        public AkkaConfigurationBuilder WithHealthCheck(AkkaHealthCheckRegistration registration)
        {
            HealthChecks[registration.Name] = registration;
            return this;
        }
        
        /// <summary>
        /// Registers a DI-resolved health check with the <see cref="AkkaConfigurationBuilder"/>.
        /// The health check type must be registered in the DI container.
        /// </summary>
        /// <param name="name">The healthcheck name.</param>
        /// <param name="failureStatus">
        /// The <see cref="HealthStatus"/> that should be reported upon failure of the health check. If the provided value
        /// is <c>null</c>, then <see cref="HealthStatus.Unhealthy"/> will be reported.
        /// </param>
        /// <param name="tags">A list of tags that can be used for filtering health checks.</param>
        /// <param name="timeout">An optional <see cref="TimeSpan"/> representing the timeout of the check.</param>
        /// <typeparam name="T">The type of the health check that implements <see cref="IAkkaHealthCheck"/>.</typeparam>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public AkkaConfigurationBuilder WithHealthCheck<T>(string name, HealthStatus? failureStatus = null, 
            IEnumerable<string>? tags = null, TimeSpan? timeout = null) where T : class, IAkkaHealthCheck
        {
            // Create a health check instance that will be resolved from DI when needed
            var registration = new AkkaHealthCheckRegistration(name, GetServiceOrCreateInstance, failureStatus, tags, timeout);
            
            HealthChecks[registration.Name] = registration;
            return this;
            
            static T GetServiceOrCreateInstance(IServiceProvider sp) => ActivatorUtilities.GetServiceOrCreateInstance<T>(sp);
        }
        
        internal void Bind()
        {
            // register as singleton - not interested in supporting multi-Sys use cases
            ServiceCollection.AddSingleton(ActorSystemFactory());

            ServiceCollection.AddSingleton<ActorRegistry>(sp =>
            {
                return ActorRegistry.For(sp.GetRequiredService<ActorSystem>());
            });
            
            ServiceCollection.AddSingleton<IActorRegistry>(sp =>
            {
                return sp.GetRequiredService<ActorRegistry>();
            });
            
            ServiceCollection.AddSingleton<IReadOnlyActorRegistry>(sp =>
            {
                return sp.GetRequiredService<ActorRegistry>();
            });

            ServiceCollection.AddSingleton(typeof(IRequiredActor<>), typeof(RequiredActor<>));

            // Automatically register all Akka.NET health checks with the HealthCheckServiceOptions
            ServiceCollection.AddOptions<HealthCheckServiceOptions>()   // creates an OptionsBuilder
                .PostConfigure<AkkaConfigurationBuilder>((opts, akka) =>
                {
                    // Ensure the user’s builder delegate has run
                    // and health checks are available on `akka`
                    foreach (var reg in akka.HealthChecks.Select(h => h.Value.ToHealthCheckRegistration()))
                        opts.Registrations.Add(reg);
                });
        }

        /// <summary>
        /// Configure extensions
        /// </summary>
        private void AddExtensions()
        {
            if (Extensions.Count == 0)
                return;
            
            // check to see if there are any existing extensions set up inside the current HOCON configuration
            if (Configuration.HasValue)
            {
                var listedExtensions = Configuration.Value.GetStringList("akka.extensions");
                foreach (var listedExtension in listedExtensions)
                {
                    var trimmed = listedExtension.Trim();
                    
                    // sanity check, we should not get any empty entries
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;
                    
                    var type = Type.GetType(trimmed);
                    if (type != null)
                        Extensions.Add(type);
                }
            }
            
            AddHoconConfiguration(
                $"akka.extensions = [{string.Join(", ", Extensions.Select(s => $"\"{s.AssemblyQualifiedName}\""))}]", 
                HoconAddMode.Prepend);
        }
        
        private static Func<IServiceProvider, ActorSystem> ActorSystemFactory()
        {
            return sp =>
            {
                var config = sp.GetRequiredService<AkkaConfigurationBuilder>();

                /*
                 * Build setups
                 */
                
                // Add auto-started akka extensions, if any.
                config.AddExtensions();
                
                // check to see if we need a LoggerSetup
                var hasLoggerSetup = config.Setups.Any(c => c is LoggerFactorySetup);
                if (!hasLoggerSetup)
                {
                    var logger = sp.GetService<ILoggerFactory>();
                    
                    // on the off-chance that we're not running with ILogger support enabled
                    // (should be a rare case that only comes up during testing)
                    if (logger != null) 
                    {
                        var loggerSetup = new LoggerFactorySetup(logger);
                        config.AddSetup(loggerSetup);
                    }
                }
                
                var diSetup = DependencyResolverSetup.Create(sp);
                var bootstrapSetup = BootstrapSetup.Create().WithConfig(config.Configuration.GetOrElse(Config.Empty));
                if (config.ActorRefProvider.HasValue) // only set the provider when explicitly required
                {
                    bootstrapSetup = bootstrapSetup.WithActorRefProvider(config.ActorRefProvider.Value);
                }

                var actorSystemSetup = bootstrapSetup.And(diSetup);
                foreach (var setup in config.Setups)
                {
                    actorSystemSetup = actorSystemSetup.And(setup);
                }

                /* check to see if we have any custom serializers that need to be registered */
                if (config.Serializers.Count > 0)
                {
                    var serializationSetup = SerializationSetup.Create(system =>
                        config.Serializers
                            .Select(r =>
                                SerializerDetails.Create(r.Id, r.SerializerFactory(system), r.TypeBindings))
                            .ToImmutableHashSet());

                    actorSystemSetup = actorSystemSetup.And(serializationSetup);
                }

                /*
                 * Start ActorSystem
                 */
                var sys = ActorSystem.Create(config.ActorSystemName, actorSystemSetup);

                return sys;
            };
        }

        internal Task<ActorSystem> StartAsync(IServiceProvider sp)
        {
            return StartAsync(sp.GetRequiredService<ActorSystem>());
        }

        internal async Task<ActorSystem> StartAsync(ActorSystem sys)
        {
            if (_complete) return sys;
            _complete = true;

            /*
             * Start Actors
             */

            var registry = ActorRegistry.For(sys);

            foreach (var starter in _actorStarters)
            {
                await starter(sys, registry).ConfigureAwait(false);
            }

            foreach (var startupTask in _startupTasks)
            {
                await startupTask(sys, registry).ConfigureAwait(false);
            }

            return sys;
        }
    }
}