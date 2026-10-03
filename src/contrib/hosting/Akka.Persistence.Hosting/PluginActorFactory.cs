// -----------------------------------------------------------------------
//  <copyright file="PluginActorFactory.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Configuration;

#nullable enable
namespace Akka.Persistence.Hosting
{
    /// <summary>
    /// How a journal or snapshot store plugin creates its actor without reflection. A plugin's
    /// <see cref="JournalOptions"/> or <see cref="SnapshotOptions"/> subclass returns one, which is all a
    /// plugin needs to start under Native AOT (<c>Akka.DynamicTypeLoading</c> off).
    /// </summary>
    /// <example>
    /// <code>
    /// protected override PluginActorFactory? Factory
    ///     => PluginActorFactory.For(config => new MyJournal(config));
    /// </code>
    /// </example>
    public sealed class PluginActorFactory
    {
        internal PluginActorFactory(Func<Config, Props> createProps)
        {
            CreateProps = createProps;
        }

        internal Func<Config, Props> CreateProps { get; }

        /// <summary>
        /// Creates the plugin actor with <paramref name="factory"/>. The factory runs inside the actor's creation
        /// context and gets the plugin's HOCON section, with its fallbacks applied.
        /// </summary>
        /// <typeparam name="TActor">The journal or snapshot store actor type.</typeparam>
        /// <param name="factory">Creates the actor.</param>
        /// <returns>The factory to return from <see cref="JournalOptions"/> or <see cref="SnapshotOptions"/>.</returns>
        public static PluginActorFactory For<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.Interfaces)] TActor>(
            Func<Config, TActor> factory) where TActor : ActorBase
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            // the closed generic producer is made here, where the type is known, so the trimmer keeps what Props needs
            return new PluginActorFactory(config => Props.CreateBy(new PluginActorProducer<TActor>(factory, config)));
        }
    }
}
