//-----------------------------------------------------------------------
// <copyright file="PipeAssociationHandle.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading;
using Akka.Actor;
using Google.Protobuf;

namespace Akka.Remote.Transport.Pipelines
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="AssociationHandle"/> implementation for <see cref="TcpPipeTransport"/>.
    ///
    /// <para>
    /// <see cref="Write"/> enqueues the payload onto the per-connection bounded
    /// <see cref="System.Threading.Channels.Channel{T}"/> write queue and returns
    /// <c>false</c> when the channel is at capacity (matching DotNetty water-mark semantics).
    /// </para>
    /// <para>
    /// <see cref="Disassociate()"/> signals the owning <see cref="PipeConnection"/> to
    /// cancel its read/write loops and close the socket.
    /// </para>
    ///
    /// <!-- CopilotNotes: The circular reference between PipeAssociationHandle and PipeConnection is
    ///      intentional — they are tightly coupled by design. Connection is set via the internal
    ///      setter immediately after construction, before Start() is called, so there is no null risk
    ///      in practice. The Interlocked guard on _disassociated prevents double-close. -->
    /// </summary>
    internal sealed class PipeAssociationHandle : AssociationHandle
    {
        // CAS guard: 0 = open, 1 = disassociated
        private int _disassociated;

        /// <summary>
        /// Back-reference to the owning connection. Set by <see cref="PipeConnection"/> constructor.
        /// </summary>
        internal PipeConnection? Connection { get; set; }

        /// <summary>
        /// Initializes a new <see cref="PipeAssociationHandle"/>.
        /// </summary>
        public PipeAssociationHandle(Address localAddress, Address remoteAddress)
            : base(localAddress, remoteAddress)
        {
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Thread-safe. Returns <c>false</c> if the channel is full
        /// or the association has already been disassociated.
        /// A return value of <c>false</c> means the write was dropped
        /// (guaranteed-no-duplication semantics per the <see cref="AssociationHandle"/> contract).
        /// </remarks>
        public override bool Write(ByteString payload)
        {
            if (Connection is null || Volatile.Read(ref _disassociated) == 1)
                return false;

            return Connection.TryEnqueueWrite(payload);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Safe to call multiple times; only the first call takes effect.
        /// </remarks>
#pragma warning disable CS0672 // override of obsolete member — suppressed globally via Directory.Build.props NoWarn
        public override void Disassociate()
#pragma warning restore CS0672
        {
            if (Interlocked.CompareExchange(ref _disassociated, 1, 0) == 0)
                Connection?.BeginDisassociate();
        }
    }
}

