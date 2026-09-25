//-----------------------------------------------------------------------
// <copyright file="ExtensionsSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace Akka.Actor.Setup
{
    /// <summary>
    /// Extensions to start with the <see cref="ActorSystem"/>, the programmatic equivalent of
    /// <c>akka.extensions</c> that needs no type names. They load alongside the HOCON list, and an
    /// extension named in both is registered once.
    /// </summary>
    public sealed class ExtensionsSetup : Setup
    {
        private ExtensionsSetup(IReadOnlyList<IExtensionId> extensionIds)
        {
            ExtensionIds = extensionIds;
        }

        /// <summary>
        /// The extension ids to register when the <see cref="ActorSystem"/> starts.
        /// </summary>
        public IReadOnlyList<IExtensionId> ExtensionIds { get; }

        /// <summary>
        /// Creates an <see cref="ExtensionsSetup"/> that starts the given extensions.
        /// </summary>
        /// <param name="extensionIds">The extension ids to register when the <see cref="ActorSystem"/> starts.</param>
        public static ExtensionsSetup Create(IEnumerable<IExtensionId> extensionIds)
            => new(extensionIds.ToArray());

        /// <summary>
        /// Creates an <see cref="ExtensionsSetup"/> that starts the given extensions.
        /// </summary>
        /// <param name="extensionIds">The extension ids to register when the <see cref="ActorSystem"/> starts.</param>
        public static ExtensionsSetup Create(params IExtensionId[] extensionIds)
            => Create((IEnumerable<IExtensionId>)extensionIds);
    }
}
