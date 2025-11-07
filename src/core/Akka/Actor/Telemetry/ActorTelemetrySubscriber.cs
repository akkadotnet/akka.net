//-----------------------------------------------------------------------
// <copyright file="ActorTelemetrySubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Event;

namespace Akka.Actor.Telemetry;

/// <summary>
/// INTERNAL API
/// 
/// Consumes <see cref="IActorTelemetryEvent"/>s from the <see cref="EventStream"/>
/// </summary>
internal sealed class TelemetrySubscriber : ReceiveActor
{
    // Track actors by their full type to derive category when reporting
    private readonly Dictionary<string, (Type actorType, int count)> _aliveActorsByType = new();

    public TelemetrySubscriber()
    {
        // Receive each type of IActorTelemetryEvent
        Receive<ActorStarted>(PushActorStarted);
        Receive<ActorStopped>(PushActorStopped);
        Receive<ActorRestarted>(_ => { /* no-op */ });
        Receive<GetAliveActorsTelemetryRequest>(_ => Sender.Tell(_aliveActorsByType.ToImmutableDictionary()));
    }
        
    private void PushActorStarted(ActorStarted e)
    {
        var actorTypeName = string.IsNullOrWhiteSpace(e.ActorTypeOverride) ? e.ActorType.Name : e.ActorTypeOverride;
        if (!_aliveActorsByType.ContainsKey(actorTypeName))
        {
            _aliveActorsByType[actorTypeName] = (e.ActorType, 1);
        }
        else
        {
            var current = _aliveActorsByType[actorTypeName];
            _aliveActorsByType[actorTypeName] = (current.actorType, current.count + 1);
        }
    }

    private void PushActorStopped(ActorStopped e)
    {
        var actorTypeName = string.IsNullOrWhiteSpace(e.ActorTypeOverride) ? e.ActorType.Name : e.ActorTypeOverride;
        if (!_aliveActorsByType.ContainsKey(actorTypeName))
        {
            // Shouldn't normally happen, but handle gracefully
            _aliveActorsByType[actorTypeName] = (e.ActorType, 0);
        }
        else
        {
            var current = _aliveActorsByType[actorTypeName];
            if(current.count > 0)
                _aliveActorsByType[actorTypeName] = (current.actorType, current.count - 1);
        }
    }

    protected override void PreStart()
    {
        var context = Context;
        var shutdown = CoordinatedShutdown.Get(Context.System);
        shutdown.AddTask(CoordinatedShutdown.PhaseBeforeActorSystemTerminate, "terminate-telemetry-actor", () =>
        {
            context.Stop(Self);
            return Task.FromResult(Done.Instance);
        });
        
        Context.System.EventStream.Subscribe(Self, typeof(IActorTelemetryEvent));
    }
}