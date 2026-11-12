// -----------------------------------------------------------------------
// <copyright file="ArteryQuicResourceLifetime.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor.Setup;

namespace Akka.Remote.Artery
{
    /// <summary>Shares factory-owned credential lifetime with native transport ownership, including failed startup.</summary>
    internal sealed class ArteryQuicResourceSetup(ArteryQuicResourceLifetime lifetime) : Setup
    {
        internal ArteryQuicResourceLifetime Lifetime { get; } = lifetime;
    }

    /// <summary>Native owners outlive bounded shutdown; managed receive processing is tracked separately.</summary>
    internal sealed class ArteryQuicResourceLifetime
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource<Done> _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _owners;
        private bool _stopping;

        internal Task WhenReleased => _released.Task;

        internal Owner Acquire()
        {
            lock (_gate)
            {
                if (_stopping)
                    throw new InvalidOperationException("Artery QUIC no longer admits native owners.");
                _owners++;
                return new Owner(this);
            }
        }

        private Owner Fork(Owner parent)
        {
            lock (_gate)
            {
                if (parent.Released)
                    throw new InvalidOperationException("A released QUIC owner cannot transfer native resources.");
                // An accept already in progress can return a connection while stop races it.
                // The live parent prevents a zero-owner gap during that handoff.
                _owners++;
                return new Owner(this);
            }
        }

        internal void Stop()
        {
            lock (_gate)
            {
                _stopping = true;
                if (_owners == 0)
                    _released.TrySetResult(Done.Instance);
            }
        }

        private void Release(Owner owner)
        {
            lock (_gate)
            {
                if (owner.Released)
                    return;
                owner.Released = true;
                if (--_owners == 0 && _stopping)
                    _released.TrySetResult(Done.Instance);
            }
        }

        internal sealed class Owner(ArteryQuicResourceLifetime lifetime) : IDisposable
        {
            // Accessed only under the lifetime gate.
            internal bool Released { get; set; }
            internal Owner Fork() => lifetime.Fork(this);
            public void Dispose() => lifetime.Release(this);
        }
    }
}
