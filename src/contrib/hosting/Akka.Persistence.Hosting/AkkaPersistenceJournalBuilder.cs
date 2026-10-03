using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Util;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Persistence.Hosting;

/// <summary>
/// Used to help build journal configurations
/// </summary>
public sealed class AkkaPersistenceJournalBuilder
{
    internal readonly string JournalId;
    internal readonly AkkaConfigurationBuilder Builder;
    internal readonly Dictionary<Type, HashSet<string>> Bindings = new Dictionary<Type, HashSet<string>>();
    internal readonly Dictionary<string, Type> Adapters = new Dictionary<string, Type>();
    internal readonly HashSet<AkkaHealthCheckRegistration> HealthCheckRegistrations = [];
    // adapters from the reflection overloads: their HOCON is written, and this is its copy for Akka.DynamicTypeLoading off
    internal readonly List<EventAdapterDetails> HoconAdapters = new();
    // adapters from the factory overloads: no HOCON, so this is their only source
    internal readonly List<EventAdapterDetails> FactoryAdapters = new();

    /// <summary>
    /// The <see cref="JournalOptions"/> instance used to configure this journal.
    /// This property allows extension methods to access journal configuration details
    /// (such as connection strings) without requiring them as explicit parameters.
    /// </summary>
    public JournalOptions? Options { get; }

    public AkkaPersistenceJournalBuilder(string journalId, AkkaConfigurationBuilder builder)
    {
        JournalId = journalId;
        Builder = builder;
        Options = null;
    }

    /// <summary>
    /// Constructor that accepts journal options for improved extension method ergonomics.
    /// </summary>
    /// <param name="journalId">The journal identifier</param>
    /// <param name="builder">The Akka configuration builder</param>
    /// <param name="options">The journal options instance</param>
    public AkkaPersistenceJournalBuilder(string journalId, AkkaConfigurationBuilder builder, JournalOptions options)
    {
        JournalId = journalId;
        Builder = builder;
        Options = options;
    }

    /// <summary>
    /// Uses the built-in journal health check on the Akka.Persistence.Journal.
    /// </summary>
    /// <param name="unHealthyStatus">Default status to return when the plugin reports <see cref="PersistenceHealthStatus.Unhealthy"/>
    /// or <see cref="PersistenceHealthStatus.Degraded"/>. Defaults to degraded.</param>
    /// <param name="name">Optional name to add to the health check.</param>
    /// <param name="tags">Custom tags for the health check. If null, defaults to ["akka", "persistence", "journal"].</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public AkkaPersistenceJournalBuilder WithHealthCheck(HealthStatus unHealthyStatus = HealthStatus.Degraded,
        string? name = null,
        IEnumerable<string>? tags = null)
    {
        var registration = AddDefaultHealthCheck(name, unHealthyStatus, tags);
        HealthCheckRegistrations.Add(registration);
        return this;
    }

    /// <summary>
    /// For Akka.Persistence plugins that have custom health checks (see https://github.com/akkadotnet/Akka.Hosting/issues/678)
    /// </summary>
    /// <param name="registration">The custom health check registration.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public AkkaPersistenceJournalBuilder WithCustomHealthCheck(AkkaHealthCheckRegistration registration)
    {
        HealthCheckRegistrations.Add(registration);
        return this;
    }

    /// <summary>
    /// Adds an event adapter that reads and writes. It is written to the HOCON as it always was, and the JIT builds
    /// it from there. With <c>Akka.DynamicTypeLoading</c> off, where Native AOT has no use for the HOCON type name,
    /// core builds it with the same <c>Activator</c> call, which the trimmer cannot follow; use the overload that
    /// takes a factory for Native AOT.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddEventAdapter<TAdapter>(string eventAdapterName,
        IEnumerable<Type> boundTypes) where TAdapter : IEventAdapter
    {
        var types = AddAdapter<TAdapter>(eventAdapterName, boundTypes);
        AddHoconAdapter(eventAdapterName, types, system => (IEventAdapter)Instantiate(typeof(TAdapter), system));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that reads and writes, created by <paramref name="factory"/> without reflection, so it
    /// also works under Native AOT. The factory is the only source of this adapter: nothing goes into the HOCON.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddEventAdapter<TAdapter>(string eventAdapterName,
        Func<ExtendedActorSystem, TAdapter> factory, params Type[] boundTypes) where TAdapter : IEventAdapter
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        FactoryAdapters.Add(EventAdapterDetails.Create(eventAdapterName,
            (Func<ExtendedActorSystem, IEventAdapter>)(system => factory(system)), boundTypes));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that only reads. See <see cref="AddEventAdapter{TAdapter}(string, IEnumerable{Type})"/>.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddReadEventAdapter<TAdapter>(string eventAdapterName,
        IEnumerable<Type> boundTypes) where TAdapter : IReadEventAdapter
    {
        var types = AddAdapter<TAdapter>(eventAdapterName, boundTypes);
        AddHoconAdapter(eventAdapterName, types, system => new NoopWriteEventAdapter((IReadEventAdapter)Instantiate(typeof(TAdapter), system)));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that only reads, created by <paramref name="factory"/> without reflection, so it
    /// also works under Native AOT. The factory is the only source of this adapter: nothing goes into the HOCON.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddReadEventAdapter<TAdapter>(string eventAdapterName,
        Func<ExtendedActorSystem, TAdapter> factory, params Type[] boundTypes) where TAdapter : IReadEventAdapter
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        FactoryAdapters.Add(EventAdapterDetails.Create(eventAdapterName,
            (Func<ExtendedActorSystem, IReadEventAdapter>)(system => factory(system)), boundTypes));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that only writes. See <see cref="AddEventAdapter{TAdapter}(string, IEnumerable{Type})"/>.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddWriteEventAdapter<TAdapter>(string eventAdapterName,
        IEnumerable<Type> boundTypes) where TAdapter : IWriteEventAdapter
    {
        var types = AddAdapter<TAdapter>(eventAdapterName, boundTypes);
        AddHoconAdapter(eventAdapterName, types, system => new NoopReadEventAdapter((IWriteEventAdapter)Instantiate(typeof(TAdapter), system)));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that only writes, created by <paramref name="factory"/> without reflection, so it
    /// also works under Native AOT. The factory is the only source of this adapter: nothing goes into the HOCON.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddWriteEventAdapter<TAdapter>(string eventAdapterName,
        Func<ExtendedActorSystem, TAdapter> factory, params Type[] boundTypes) where TAdapter : IWriteEventAdapter
    {
        if (factory is null)
            throw new ArgumentNullException(nameof(factory));

        FactoryAdapters.Add(EventAdapterDetails.Create(eventAdapterName,
            (Func<ExtendedActorSystem, IWriteEventAdapter>)(system => factory(system)), boundTypes));

        return this;
    }

    // Enumerates boundTypes exactly once and in the same order as before, so a null or a lazy sequence behaves as it always did.
    private List<Type> AddAdapter<TAdapter>(string eventAdapterName, IEnumerable<Type> boundTypes)
    {
        Adapters[eventAdapterName] = typeof(TAdapter);
        var types = new List<Type>();
        foreach (var t in boundTypes)
        {
            types.Add(t);
            if (!Bindings.ContainsKey(t))
                Bindings[t] = new HashSet<string>();
            Bindings[t].Add(eventAdapterName);
        }

        return types;
    }

    // A second copy of what the HOCON says. It is only used with Akka.DynamicTypeLoading off, and Build adds it only
    // when the HOCON is written, so a call that built nothing before still builds nothing. A name the HOCON
    // would reject is skipped rather than made to throw here.
    private void AddHoconAdapter(string eventAdapterName, List<Type> types, Func<ExtendedActorSystem, IEventAdapter> factory)
    {
        if (!string.IsNullOrWhiteSpace(eventAdapterName))
            HoconAdapters.Add(EventAdapterDetails.Create(eventAdapterName, factory, types.ToArray()));
    }

    // core's reflection path: the constructor that takes the system, and when there is none, the parameterless one
    private static object Instantiate(Type type, ExtendedActorSystem system)
    {
        try
        {
            return Activator.CreateInstance(type, system)!;
        }
        catch (MissingMethodException)
        {
            return Activator.CreateInstance(type)!;
        }
    }

    private AkkaHealthCheckRegistration AddDefaultHealthCheck(string? name, HealthStatus unHealthyStatus, IEnumerable<string>? tags)
    {
        var pluginId = $"akka.persistence.journal.{JournalId}";
        var healthCheckTags = tags?.ToList() ?? ["akka", "persistence", "journal"];
        var registration = new AkkaHealthCheckRegistration(
            name ?? pluginId,
            new JournalHealthCheck(pluginId),
            unHealthyStatus,
            healthCheckTags);
        return registration;
    }

    /// <summary>
    /// INTERNAL API - Builds the HOCON and then injects it.
    /// </summary>
    internal void Build()
    {
        // add the health checks if specified - do this FIRST before any early returns
        foreach(var hc in HealthCheckRegistrations)
            Builder.WithHealthCheck(hc);

        // The adapters in code. The factory ones are always registered. The reflection ones are registered only when
        // the HOCON below is written, so that a call which built nothing before still builds nothing. The HOCON stays
        // as it always was.
        var registered = new List<EventAdapterDetails>();
        if (Adapters.Count > 0 && Bindings.Count > 0)
            registered.AddRange(HoconAdapters);
        registered.AddRange(FactoryAdapters);
        if (registered.Count > 0)
        {
            var pluginId = Options?.PluginId ?? $"akka.persistence.journal.{JournalId}";
            Builder.AddPersistenceRegistrations(setup => setup.WithEventAdapters(pluginId, registered));
        }

        // useless configuration - don't bother.
        if (Adapters.Count == 0 || Bindings.Count == 0)
            return;

        var adapters = new StringBuilder()
            .Append($"akka.persistence.journal.{JournalId}").Append("{");

        AppendAdapters(adapters);

        adapters.AppendLine("}");

        var finalHocon = ConfigurationFactory.ParseString(adapters.ToString());
        Builder.AddHocon(finalHocon, HoconAddMode.Prepend);
    }

    internal void AppendAdapters(StringBuilder sb)
    {
        // useless configuration - don't bother.
        if (Adapters.Count == 0 || Bindings.Count == 0)
            return;

        sb.AppendLine("event-adapters {");
        foreach (var kv in Adapters)
        {
            sb.AppendLine($"{kv.Key} = \"{kv.Value.TypeQualifiedName()}\"");
        }

        sb.AppendLine("}").AppendLine("event-adapter-bindings {");
        foreach (var kv in Bindings)
        {
            sb.AppendLine($"\"{kv.Key.TypeQualifiedName()}\" = [{string.Join(",", kv.Value)}]");
        }

        sb.AppendLine("}");
    }
}