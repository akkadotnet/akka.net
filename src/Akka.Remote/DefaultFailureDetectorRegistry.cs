using System;
using System.Collections.Generic;
using Akka.Tools;

namespace Akka.Remote
{
    /// <summary>
    /// A lock-less, thread-safe implementation of <see cref="IFailureDetectorRegistry{T}"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DefaultFailureDetectorRegistry<T> : IFailureDetectorRegistry<T>
    {
        /// <summary>
        /// Instantiates the DefaultFailureDetectorRegistry an uses a factory method for creating new instances
        /// </summary>
        /// <param name="factory"></param>
        public DefaultFailureDetectorRegistry(Func<FailureDetector> factory)
        {
            _factory = factory;
        }

        #region Internal State

        private readonly Func<FailureDetector> _factory;

        private AtomicReference<Dictionary<T, FailureDetector>> _resourceToFailureDetector = new AtomicReference<Dictionary<T, FailureDetector>>(new Dictionary<T, FailureDetector>());

        private readonly object _failureDetectorCreationLock = new object();

        private Dictionary<T, FailureDetector> ResourceToFailureDetector
        {
            get { return _resourceToFailureDetector; }
            set { _resourceToFailureDetector = value; }
        }

        #endregion

        #region IFailureDetectorRegistry<T> members

        public bool IsAvailable(T resource)
        {
            return !ResourceToFailureDetector.ContainsKey(resource) || ResourceToFailureDetector[resource].IsAvailable;
        }

        public bool IsMonitoring(T resource)
        {
            return ResourceToFailureDetector.ContainsKey(resource) && ResourceToFailureDetector[resource].IsMonitoring;
        }

        public void Heartbeat(T resource)
        {
            if (ResourceToFailureDetector.ContainsKey(resource))
            {
                ResourceToFailureDetector[resource].HeartBeat();
            }
            else
            {
                //First one wins and creates the new FailureDetector
                lock (_failureDetectorCreationLock)
                {
                    // First check for non-existing key wa outside the lock, and a second thread might just have released thelock
                    // when this one acquired it, so the second check is needed (double-check locking pattern)
                    var oldTable = new Dictionary<T, FailureDetector>(ResourceToFailureDetector);
                    if (oldTable.ContainsKey(resource))
                    {
                        oldTable[resource].HeartBeat();
                    }
                    else
                    {
                        var newDetector = _factory();
                        newDetector.HeartBeat();
                        oldTable.Add(resource, newDetector);
                        ResourceToFailureDetector = oldTable;
                    }
                }
            }
        }

        public void Remove(T resource)
        {
            while (true)
            {
                var oldTable = ResourceToFailureDetector;
                if (oldTable.ContainsKey(resource))
                {
                    var newTable = new Dictionary<T, FailureDetector>(oldTable);
                    newTable.Remove(resource); //if we won the race then update else try again
                    if (_resourceToFailureDetector.CompareAndSet(oldTable, newTable)) continue;
                }
                break;
            }
        }

        public void Reset()
        {
            while (true)
            {
                var oldTable = ResourceToFailureDetector;
                // if we won the race then update else try again
                if (_resourceToFailureDetector.CompareAndSet(oldTable, new Dictionary<T, FailureDetector>())) continue;
                break;
            }
        }

        #endregion

        #region INTERNAL API

        /// <summary>
        /// Get the underlying <see cref="FailureDetector"/> for a resource.
        /// </summary>
        internal FailureDetector GetFailureDetector(T resource)
        {
            FailureDetector f;
            ResourceToFailureDetector.TryGetValue(resource, out f);
            return f;
        }

        #endregion
    }
}
