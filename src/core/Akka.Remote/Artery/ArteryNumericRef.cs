//-----------------------------------------------------------------------
// <copyright file="ArteryNumericRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// A ref addressed by number: the UID of the system that owns it, plus a 64-bit id. The top bit of
    /// the id is its namespace -- <see cref="TemporaryNamespaceBit"/> set means a temporary-ref counter
    /// (the value that names the ref below the owner's <c>/temp</c>, resolvable from the owner's own
    /// state alone); clear is reserved for ids that need shared state to resolve.
    /// </summary>
    internal readonly struct ArteryNumericRef
    {
        /// <summary>The namespace bit of <see cref="Id"/>: set for a temporary-ref counter.</summary>
        public const long TemporaryNamespaceBit = long.MinValue;

        /// <summary>Initializes a new instance of the <see cref="ArteryNumericRef"/> struct.</summary>
        public ArteryNumericRef(long ownerUid, long id)
        {
            OwnerUid = ownerUid;
            Id = id;
        }

        /// <summary>The ref of the temporary actor with counter value <paramref name="counter"/> owned by <paramref name="ownerUid"/>.</summary>
        public static ArteryNumericRef ForTemporary(long ownerUid, long counter) =>
            new(ownerUid, counter | TemporaryNamespaceBit);

        /// <summary>The UID of the system that owns the ref.</summary>
        public long OwnerUid { get; }

        /// <summary>The wire id, namespace bit included.</summary>
        public long Id { get; }

        /// <summary><see langword="true"/> when <see cref="Id"/> is in the temporary-ref namespace.</summary>
        public bool IsTemporary => (Id & TemporaryNamespaceBit) != 0;

        /// <summary>The temporary-ref counter value. Meaningful only when <see cref="IsTemporary"/>.</summary>
        public long TemporaryCounter => Id & long.MaxValue;
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// The numeric sender and/or recipient of an envelope, held behind one reference so an envelope that
    /// carries only literals (the default) pays a single null pointer for them.
    /// </summary>
    internal sealed class NumericWireRefs
    {
        /// <summary>Initializes a new instance of the <see cref="NumericWireRefs"/> class.</summary>
        public NumericWireRefs(ArteryNumericRef? sender, ArteryNumericRef? recipient)
        {
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>The numeric sender, if the sender is numeric.</summary>
        public ArteryNumericRef? Sender { get; }

        /// <summary>The numeric recipient, if the recipient is numeric.</summary>
        public ArteryNumericRef? Recipient { get; }

        /// <summary>Returns <see langword="null"/> when both are absent, else a holder for them.</summary>
        public static NumericWireRefs? Of(ArteryNumericRef? sender, ArteryNumericRef? recipient) =>
            sender is null && recipient is null ? null : new NumericWireRefs(sender, recipient);
    }
}
