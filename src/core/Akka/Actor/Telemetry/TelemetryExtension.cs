// -----------------------------------------------------------------------
//  <copyright file="TelemetryExtension.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace Akka.Actor.Telemetry;

/// <summary>
/// This class represents an <see cref="ActorSystem"/> provider used to create the <see cref="TelemetryExtension"/> extension.
/// </summary>
internal class TelemetryExtensionsProvider : ExtensionIdProvider<TelemetryExtension>
{
    /// <summary>
    /// Creates the <see cref="TelemetryExtension"/> extension using a given actor system.
    /// </summary>
    /// <param name="system">The actor system to use when creating the extension.</param>
    /// <returns>The extension created using the given actor system.</returns>
    public override TelemetryExtension CreateExtension(ExtendedActorSystem system) => new (system);
}

internal class TelemetryExtension: IExtension
{
    private readonly IActorRef _telemetrySubscriber;
    
    public TelemetryExtension(ExtendedActorSystem system)
    {
        _telemetrySubscriber = system.SystemActorOf(Props.Create(() => new TelemetrySubscriber()), "actor-telemetry-subscriber");
    }
    
    /// <summary>
    /// Retrieves the extension from the specified actor system.
    /// </summary>
    /// <param name="system">The actor system from which to retrieve the extension.</param>
    /// <returns>The extension retrieved from the given actor system.</returns>
    public static TelemetryExtension Get(ActorSystem system)
        => system.WithExtension<TelemetryExtension>(typeof(TelemetryExtensionsProvider));

    public Task<ImmutableDictionary<string, (Type actorType, int count)>> GetAliveActors()
        => _telemetrySubscriber
            .Ask<ImmutableDictionary<string, (Type actorType, int count)>>(
                message: GetAliveActorsTelemetryRequest.Instance, 
                timeout: TimeSpan.FromSeconds(1));
}
