//-----------------------------------------------------------------------
// <copyright file="V2Port.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>Which serializer a corpus message is bound to by <see cref="Serialization.FindSerializerFor"/>.</summary>
    public enum V2PortBinding
    {
        /// <summary>The legacy serializer. This is the state of a read-only V2 row, which has no bindings.</summary>
        Legacy,

        /// <summary>The V2 serializer, once the global switch (or an explicit binding) takes the types over.</summary>
        V2
    }

    /// <summary>
    /// What a serializer port is made of: the legacy serializer, the V2 serializer that takes over its wire contract,
    /// and a corpus of messages. <see cref="V2PortSpecs"/> and <see cref="V2PortSpec"/> check one against the other.
    /// </summary>
    public sealed class V2Port
    {
        /// <param name="createLegacy">Builds the legacy serializer, such as <c>system =&gt; new ReliableDeliverySerializer(system)</c>.</param>
        /// <param name="createV2">Builds the V2 serializer. The kit calls it again under other feature switches, so build a fresh one each time.</param>
        /// <param name="cases">The corpus. Cover every manifest the legacy serializer writes, edge values included.</param>
        public V2Port(
            Func<ExtendedActorSystem, Serializer> createLegacy,
            Func<ExtendedActorSystem, Serializer> createV2,
            IEnumerable<V2PortCase> cases)
        {
            CreateLegacy = createLegacy ?? throw new ArgumentNullException(nameof(createLegacy));
            CreateV2 = createV2 ?? throw new ArgumentNullException(nameof(createV2));
            Cases = (cases ?? throw new ArgumentNullException(nameof(cases))).ToList();
        }

        /// <summary>Builds the legacy serializer.</summary>
        public Func<ExtendedActorSystem, Serializer> CreateLegacy { get; }

        /// <summary>Builds the V2 serializer.</summary>
        public Func<ExtendedActorSystem, Serializer> CreateV2 { get; }

        /// <summary>The corpus.</summary>
        public IReadOnlyList<V2PortCase> Cases { get; }

        /// <summary>The V2 id is the legacy id plus this. The epic fixes it at 40.</summary>
        public int IdOffset { get; set; } = 40;

        /// <summary>Lowest id a V2 port may take. The epic reserves 40 to 79 for ports.</summary>
        public int V2IdMin { get; set; } = 40;

        /// <summary>Highest id a V2 port may take.</summary>
        public int V2IdMax { get; set; } = 79;

        /// <summary>
        /// Which serializer the corpus types resolve to. A port keeps this at <see cref="V2PortBinding.Legacy"/> until
        /// the global switch makes the V2 row take over.
        /// </summary>
        public V2PortBinding ExpectedBinding { get; set; } = V2PortBinding.Legacy;

        /// <summary>
        /// Manifests the corpus must cover. Leave empty to skip the coverage check. Listing every token the legacy
        /// serializer handles makes a forgotten message type a test failure, not a gap nobody sees.
        /// </summary>
        public IReadOnlyCollection<string> RequiredManifests { get; set; } = Array.Empty<string>();

        /// <summary>
        /// Whether the legacy serializer must reproduce its golden bytes exactly. Turn off for a legacy serializer
        /// whose output isn't deterministic (set ordering, say); the V2 node must still decode the golden bytes.
        /// </summary>
        public bool CompareLegacyBytes { get; set; } = true;
    }
}
