//-----------------------------------------------------------------------
// <copyright file="ExtensionsSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Akka.Actor.Setup
{
    /// <summary>
    /// Extensions to start with the <see cref="ActorSystem"/>, the programmatic equivalent of
    /// <c>akka.extensions</c> that needs no type names. They load before the HOCON list, and an extension
    /// named in both is registered once, from this setup's id. Like any <see cref="Setup"/>, a second
    /// <see cref="ExtensionsSetup"/> passed to <see cref="ActorSystemSetup.And{T}"/> replaces the first.
    /// </summary>
    public sealed class ExtensionsSetup : Setup
    {
        private ExtensionsSetup(IList<IExtensionId> extensionIds)
        {
            ExtensionIds = new ReadOnlyCollection<IExtensionId>(extensionIds);
        }

        /// <summary>
        /// The extension ids to register when the <see cref="ActorSystem"/> starts.
        /// </summary>
        public IReadOnlyList<IExtensionId> ExtensionIds { get; }

        /// <summary>
        /// Creates an <see cref="ExtensionsSetup"/> that starts the given extensions.
        /// </summary>
        /// <param name="extensionIds">The extension ids to register when the <see cref="ActorSystem"/> starts.</param>
        /// <exception cref="ArgumentException">An entry is <c>null</c>.</exception>
        public static ExtensionsSetup Create(IEnumerable<IExtensionId> extensionIds)
        {
            var ids = extensionIds.ToArray();
            if (ids.Any(id => id is null))
                throw new ArgumentException("Extension ids must not be null.", nameof(extensionIds));
            return new ExtensionsSetup(ids);
        }

        /// <summary>
        /// Creates an <see cref="ExtensionsSetup"/> that starts the given extensions.
        /// </summary>
        /// <param name="extensionIds">The extension ids to register when the <see cref="ActorSystem"/> starts.</param>
        /// <exception cref="ArgumentException">An entry is <c>null</c>.</exception>
        public static ExtensionsSetup Create(params IExtensionId[] extensionIds)
            => Create((IEnumerable<IExtensionId>)extensionIds);
    }
}
