//-----------------------------------------------------------------------
// <copyright file="DefaultFailureDetectorRegistry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Util;

namespace Akka.Remote
{
    /// <summary>
    /// A lock-less, thread-safe implementation of <see cref="IFailureDetectorRegistry{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type used to identify a monitored resource.</typeparam>
    public class DefaultFailureDetectorRegistry<T> : IFailureDetectorRegistry<T>
    {
        /// <summary>
        /// Instantiates the DefaultFailureDetectorRegistry an uses a factory method for creating new instances
        /// </summary>
        /// <param name="factory">Creates a failure detector when a resource is first registered.</param>
        public DefaultFailureDetectorRegistry(Func<FailureDetector> factory)
        {
            _factory = factory;
        }

        #region Internal State

        private readonly Func<FailureDetector> _factory;

        private readonly AtomicReference<ImmutableDictionary<T, FailureDetector>> _resourceToFailureDetector = new(ImmutableDictionary<T, FailureDetector>.Empty);

        private readonly object _failureDetectorCreationLock = new();

        private ImmutableDictionary<T, FailureDetector> ResourceToFailureDetector
        {
            get { return _resourceToFailureDetector.Value; }
            set { _resourceToFailureDetector.Value = value; }
        }

        #endregion

        #region IFailureDetectorRegistry<T> members

        /// <summary>
        /// Determines whether a resource is considered available by its registered failure detector.
        /// </summary>
        /// <param name="resource">The resource whose health is being checked.</param>
        /// <returns><c>true</c> if the resource is available or is not registered; otherwise, <c>false</c>.</returns>
        public bool IsAvailable(T resource)
        {
            if (ResourceToFailureDetector.TryGetValue(resource, out var failureDetector))
                return failureDetector.IsAvailable;
            return true;
        }

        /// <summary>
        /// Determines whether a failure detector has started monitoring a resource.
        /// </summary>
        /// <param name="resource">The resource whose monitoring state is being checked.</param>
        /// <returns><c>true</c> if its failure detector has received a heartbeat; otherwise, <c>false</c>.</returns>
        public bool IsMonitoring(T resource)
        {
            if (ResourceToFailureDetector.TryGetValue(resource, out var failureDetector))
                return failureDetector.IsMonitoring;
            return false;
        }

        /// <summary>
        /// Records a heartbeat, creating and registering a failure detector for a resource on its first heartbeat.
        /// </summary>
        /// <param name="resource">The resource for which to record a heartbeat.</param>
        public void Heartbeat(T resource)
        {
            if (ResourceToFailureDetector.TryGetValue(resource, out var failureDetector))
                failureDetector.HeartBeat();
            else
            {
                //First one wins and creates the new FailureDetector
                lock (_failureDetectorCreationLock)
                {
                    // First check for non-existing key wa outside the lock, and a second thread might just have released the lock
                    // when this one acquired it, so the second check is needed (double-check locking pattern)
                    var oldTable = ResourceToFailureDetector;
                    if (oldTable.TryGetValue(resource, out failureDetector))
                        failureDetector.HeartBeat();
                    else
                    {
                        var newDetector = _factory();

                        switch (newDetector)
                        {
                            case PhiAccrualFailureDetector phi:
                                phi.Address = resource.ToString();
                                break;
                        }

                        newDetector.HeartBeat();
                        var newTable = oldTable.Add(resource, newDetector);
                        ResourceToFailureDetector = newTable;
                    }
                }
            }
        }

        /// <summary>
        /// Removes a resource and its failure detector from the registry.
        /// </summary>
        /// <param name="resource">The resource to remove.</param>
        public void Remove(T resource)
        {
            while (true)
            {
                var oldTable = ResourceToFailureDetector;
                if (oldTable.ContainsKey(resource))
                {
                    var newTable = oldTable.Remove(resource); //if we won the race then update else try again
                    if (!_resourceToFailureDetector.CompareAndSet(oldTable, newTable)) continue;
                }
                break;
            }
        }

        /// <summary>
        /// Removes all registered resources and their failure detector state.
        /// </summary>
        public void Reset()
        {
            while (true)
            {
                var oldTable = ResourceToFailureDetector;
                // if we won the race then update else try again
                if (!_resourceToFailureDetector.CompareAndSet(oldTable, ImmutableDictionary<T, FailureDetector>.Empty)) continue;
                break;
            }
        }

        #endregion

        #region INTERNAL API

        /// <summary>
        /// Get the underlying <see cref="FailureDetector"/> for a resource.
        /// </summary>
        /// <param name="resource">The resource whose registered failure detector is requested.</param>
        /// <returns>The registered detector, or <c>null</c> if the resource is not registered.</returns>
        internal FailureDetector GetFailureDetector(T resource)
        {
            ResourceToFailureDetector.TryGetValue(resource, out var f);
            return f;
        }

        #endregion
    }
}

