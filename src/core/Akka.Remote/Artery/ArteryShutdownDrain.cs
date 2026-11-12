// -----------------------------------------------------------------------
// <copyright file="ArteryShutdownDrain.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Remote.Artery
{
    /// <summary>Single-use shutdown receipt set. Registration is per materialized stream, not per message.</summary>
    internal sealed class ArteryShutdownDrain
    {
        private static long _nextId;
        private readonly object _gate = new();
        private readonly Dictionary<long, Target> _active = new();
        private readonly Dictionary<long, Target> _expected = new();
        private UniqueAddress? _local;
        private Func<UniqueAddress, bool>? _isCurrentPeer;
        private bool _started;
        private bool _closed;
        internal int Unconfirmable { get; private set; }
        internal TaskCompletionSource<bool> DataWritesSettled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal Target Register(Association association, ArteryStreamId role)
        {
            lock (_gate)
            {
                if (_started)
                    throw new InvalidOperationException("Artery shutdown no longer admits outbound streams.");
                var target = new Target(Interlocked.Increment(ref _nextId), association, role);
                _active.Add(target.Id, target);
                return target;
            }
        }

        internal void Remove(Target target)
        {
            lock (_gate)
                _active.Remove(target.Id);
            // Expected receipts survive local stream completion or failure. A failed writer
            // cannot turn a missing marker into successful peer progress by removing itself.
        }

        internal IReadOnlyList<Target> Begin(UniqueAddress local, Func<UniqueAddress, bool> isCurrentPeer)
        {
            lock (_gate)
            {
                if (_started)
                    return Array.Empty<Target>();
                _started = true;
                _local = local;
                _isCurrentPeer = isCurrentPeer;
                foreach (var target in _active.Values)
                {
                    if (target.Association.CurrentState.UniqueRemoteAddress is not { } peer ||
                        !isCurrentPeer(peer))
                    {
                        Unconfirmable++;
                        continue;
                    }
                    target.Request = new ArteryDrainRequest(local, peer.Uid, target.Id, (byte)target.Role);
                    _expected.Add(target.Id, target);
                }
                return _expected.Values.ToArray();
            }
        }

        internal bool Acknowledge(long originUid, ArteryDrainAck acknowledgement)
        {
            lock (_gate)
            {
                if (_closed || _local is not { } local || acknowledgement.ToUid != local.Uid ||
                    acknowledgement.From.Uid != originUid || !_expected.TryGetValue(acknowledgement.Id, out var target) ||
                    target.Request is not { } request || acknowledgement.From.Uid != request.ToUid ||
                    target.Association.RemoteAddress != acknowledgement.From.Address ||
                    !_isCurrentPeer!(acknowledgement.From))
                    return false;
                return target.Received.TrySetResult(Done.Instance);
            }
        }

        internal Task WhenReceived
        {
            get
            {
                lock (_gate)
                    return Task.WhenAll(_expected.Values.Select(target => target.Received.Task));
            }
        }

        internal async Task WaitForReceiptsAsync(CancellationToken cancellationToken)
        {
            await WhenReceived.WaitAsync(cancellationToken).ConfigureAwait(false);
            // WaitAsync returns an already-completed task even if its token has expired.
            // Observe the deadline again before confirming potentially late receipts.
            cancellationToken.ThrowIfCancellationRequested();
        }

        internal int Outstanding
        {
            get
            {
                lock (_gate)
                    return _expected.Values.Count(target => !target.Received.Task.IsCompletedSuccessfully);
            }
        }

        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
                _started = true;
                foreach (var target in _expected.Values)
                {
                    target.Request = null;
                    target.Received.TrySetCanceled();
                }
            }
        }

        internal sealed class Target(long id, Association association, ArteryStreamId role)
        {
            private ArteryDrainRequest? _request;
            internal long Id { get; } = id;
            internal Association Association { get; } = association;
            internal ArteryStreamId Role { get; } = role;
            internal TaskCompletionSource<Done> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal ArteryDrainRequest? Request
            {
                get => Volatile.Read(ref _request);
                set => Volatile.Write(ref _request, value);
            }
        }
    }
}
