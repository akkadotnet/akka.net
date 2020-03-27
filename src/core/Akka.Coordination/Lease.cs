//-----------------------------------------------------------------------
// <copyright file="Lease.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Akka.Coordination
{
    /// <summary>
    /// API for a distributed lock.
    /// Any lease implementation should provide the following guarantees:
    /// <list type="bullet">A lease with the same name loaded multiple times, even on different nodes, is the same lease</list>
    /// <list type="bullet">Only one owner can acquire the lease at a time</list>
    /// </summary>
    public abstract class Lease
    {
        /// <summary>
        /// Lease settings
        /// </summary>
        public LeaseSettings Settings { get; }

        /// <summary>
        /// Creates a new <see cref="Lease"/> instance.
        /// </summary>
        /// <param name="settings">Lease settings</param>
        public Lease(LeaseSettings settings)
        {
            Settings = settings;
        }

        /// <summary>
        /// Try to acquire the lease. The returned <see cref="Task"/> will be completed with `true`
        /// if the lease could be acquired, i.e. no other owner is holding the lease.
        ///
        /// The returned <see cref="Task"/> will be completed with `false` if the lease for certain couldn't be
        /// acquired, e.g. because some other owner is holding it. It's completed with <see cref="LeaseException"/>
        /// failure if it might not have been able to acquire the lease, e.g. communication timeout
        /// with the lease resource.
        ///
        /// The lease will be held by the <see cref="LeaseSettings.OwnerName"/> until it is released
        /// with <see cref="Release"/>. A Lease implementation will typically also lose the ownership
        /// if it can't maintain its authority, e.g. if it crashes or is partitioned from the lease
        /// resource for too long.
        ///
        /// <see cref="CheckLease"/> can be used to verify that the owner still has the lease.
        /// </summary>
        /// <returns></returns>
        public abstract Task<bool> Acquire();

        /// <summary>
        /// Same as <see cref="Acquire()"/> with an additional callback
        /// that is called if the lease is lost. The lease can be lose due to being unable
        /// to communicate with the lease provider.
        /// Implementations should not call leaseLostCallback until after the returned future
        /// has been completed
        /// </summary>
        /// <param name="leaseLostCallback"></param>
        /// <returns></returns>
        public abstract Task<bool> Acquire(Action<Exception> leaseLostCallback);

        /// <summary>
        /// Release the lease so some other owner can acquire it.
        /// </summary>
        /// <returns></returns>
        public abstract Task<bool> Release();

        /// <summary>
        /// Check if the owner still holds the lease.
        /// `true` means that it certainly holds the lease.
        /// `false` means that it might not hold the lease, but it could, and for more certain
        /// response you would have to use <see cref="Acquire()"/> or <see cref="Release"/>.
        /// </summary>
        /// <returns></returns>
        public abstract bool CheckLease();
    }
}
