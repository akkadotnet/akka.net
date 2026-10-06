using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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
    // the adapters above as setup registrations, which core builds them from with Akka.DynamicTypeLoading off
    internal readonly List<EventAdapterDetails> RegisteredAdapters = new();

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
    /// Adds an event adapter that reads and writes. It goes into the HOCON as it always did, which the JIT builds it
    /// from, and into the persistence setup, which core builds it from when <c>Akka.DynamicTypeLoading</c> is off.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddEventAdapter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAdapter>(string eventAdapterName,
        IEnumerable<Type> boundTypes) where TAdapter : IEventAdapter
    {
        var types = AddAdapter<TAdapter>(eventAdapterName, boundTypes);
        if (!string.IsNullOrWhiteSpace(eventAdapterName))
            RegisteredAdapters.Add(EventAdapterDetails.Create(eventAdapterName,
                (Func<ExtendedActorSystem, IEventAdapter>)(system => Instantiate<TAdapter>(system)), types.ToArray()));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that only reads. See <see cref="AddEventAdapter{TAdapter}(string, IEnumerable{Type})"/>.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddReadEventAdapter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAdapter>(string eventAdapterName,
        IEnumerable<Type> boundTypes) where TAdapter : IReadEventAdapter
    {
        var types = AddAdapter<TAdapter>(eventAdapterName, boundTypes);
        if (!string.IsNullOrWhiteSpace(eventAdapterName))
            RegisteredAdapters.Add(EventAdapterDetails.Create(eventAdapterName,
                (Func<ExtendedActorSystem, IReadEventAdapter>)(system => Instantiate<TAdapter>(system)), types.ToArray()));

        return this;
    }

    /// <summary>
    /// Adds an event adapter that only writes. See <see cref="AddEventAdapter{TAdapter}(string, IEnumerable{Type})"/>.
    /// </summary>
    public AkkaPersistenceJournalBuilder AddWriteEventAdapter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAdapter>(string eventAdapterName,
        IEnumerable<Type> boundTypes) where TAdapter : IWriteEventAdapter
    {
        var types = AddAdapter<TAdapter>(eventAdapterName, boundTypes);
        if (!string.IsNullOrWhiteSpace(eventAdapterName))
            RegisteredAdapters.Add(EventAdapterDetails.Create(eventAdapterName,
                (Func<ExtendedActorSystem, IWriteEventAdapter>)(system => Instantiate<TAdapter>(system)), types.ToArray()));

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

    // core's reflection path (EventAdapters.Instantiate) on a type named in code: the constructor that takes the
    // system, and when there is none, the parameterless one
    private static TAdapter Instantiate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TAdapter>(ExtendedActorSystem system)
    {
        try
        {
            return (TAdapter)Activator.CreateInstance(typeof(TAdapter), system)!;
        }
        catch (MissingMethodException)
        {
            return (TAdapter)Activator.CreateInstance(typeof(TAdapter))!;
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

        // The adapters go into the setup only when the HOCON below is written, so that a call which built nothing
        // before still builds nothing. The HOCON stays as it always was.
        if (Adapters.Count > 0 && Bindings.Count > 0 && RegisteredAdapters.Count > 0)
        {
            var pluginId = Options?.PluginId ?? $"akka.persistence.journal.{JournalId}";
            var registered = RegisteredAdapters.ToList();
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