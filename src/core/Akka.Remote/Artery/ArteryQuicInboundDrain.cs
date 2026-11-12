// -----------------------------------------------------------------------
// <copyright file="ArteryQuicInboundDrain.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Remote.Artery
{
    /// <summary>Accepted processing on one native connection; never owns native or receive storage.</summary>
    internal sealed class ArteryQuicInboundDrain(CancellationToken connectionStopped)
    {
        private readonly object _gate = new();
        private readonly List<Stream> _streams = new();
        private UniqueAddress? _peer;

        internal UniqueAddress? Peer
        {
            get
            {
                lock (_gate)
                    return _peer;
            }
        }

        internal Stream Register()
        {
            // Register before reading a preamble, including admissions whose role is not known yet.
            var stream = new Stream(this);
            lock (_gate)
                _streams.Add(stream);
            return stream;
        }

        private void Announce(UniqueAddress peer)
        {
            lock (_gate)
            {
                if (_peer is { } current && !current.Equals(peer))
                    throw new ArteryFramingException("QUIC connection shutdown announced conflicting peer identities.");
                _peer = peer;
            }
        }

        internal async Task WaitAsync(bool control, CancellationToken cancellationToken)
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(connectionStopped, cancellationToken);
            while (true)
            {
                Stream[] streams;
                lock (_gate)
                    streams = _streams.ToArray();
                var selected = new List<Stream>();
                var pending = new List<Task>();
                foreach (var stream in streams)
                    if (((await stream.Role.WaitAsync(lifetime.Token).ConfigureAwait(false)) == ArteryStreamId.Control) == control)
                    {
                        selected.Add(stream);
                        pending.Add(stream.Processing);
                    }
                await Task.WhenAll(pending).WaitAsync(lifetime.Token).ConfigureAwait(false);
                lifetime.Token.ThrowIfCancellationRequested();
                foreach (var stream in selected)
                    if (!stream.CompletedBoundary)
                        throw new IOException("QUIC peer processing did not reach natural EOF and admit required shutdown replies.");
                lock (_gate)
                    if (streams.Length == _streams.Count)
                        return;
                // Include streams admitted while waiting. Later admission after this boundary
                // is not a promise of lossless shutdown and still obeys the native stream cap.
            }
        }

        internal sealed class Stream
        {
            private readonly ArteryQuicInboundDrain _connection;
            private readonly TaskCompletionSource<Task> _admitted = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<ArteryStreamId> _role = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _completed;
            private int _replyRejected;
            internal Task<ArteryStreamId> Role => _role.Task;
            internal Task Processing { get; }
            internal bool CompletedBoundary => Volatile.Read(ref _completed) == 1 && Volatile.Read(ref _replyRejected) == 0;
            internal Stream(ArteryQuicInboundDrain connection)
            {
                _connection = connection;
                Processing = _admitted.Task.Unwrap();
            }

            internal void AnnouncePeerShutdown(UniqueAddress peer) => _connection.Announce(peer);
            internal void RejectReply() => Volatile.Write(ref _replyRejected, 1);
            internal void Complete(bool naturalEof, bool replyAdmitted) =>
                Volatile.Write(ref _completed, naturalEof && replyAdmitted ? 1 : -1);

            internal Task Admit(ArteryStreamId role, Func<Task> materialize)
            {
                _role.SetResult(role);
                try
                {
                    _admitted.SetResult(materialize());
                    return Processing;
                }
                catch
                {
                    CancelAdmission();
                    throw;
                }
            }

            internal void CancelAdmission()
            {
                _role.TrySetCanceled();
                _admitted.TrySetCanceled();
            }
        }
    }
}
