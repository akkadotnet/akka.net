// -----------------------------------------------------------------------
//  <copyright file="PersistenceRegistrations.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor.Setup;
using Akka.Hosting;

#nullable enable
namespace Akka.Persistence.Hosting
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Every Hosting call that registers a plugin, an event adapter or a read journal appends to the one
    /// <see cref="PersistenceSetup"/> on the builder, because an <see cref="ActorSystemSetup"/> keeps a single
    /// instance per setup type. The merge rules live in core, in <c>PersistencePluginRegistry</c>.
    /// </summary>
    internal static class PersistenceRegistrations
    {
        public static AkkaConfigurationBuilder AddPersistenceRegistrations(
            this AkkaConfigurationBuilder builder,
            Func<PersistenceSetup, PersistenceSetup> add)
        {
            var existing = builder.Setups.OfType<PersistenceSetup>().FirstOrDefault();
            var updated = add(existing ?? PersistenceSetup.Create());

            builder.AddSetup(updated);

            // AddSetup does nothing once the builder has started; only swap when the new setup went in
            if (existing is not null && !ReferenceEquals(existing, updated) && builder.Setups.Contains(updated))
                builder.Setups.Remove(existing);

            return builder;
        }
    }
}
