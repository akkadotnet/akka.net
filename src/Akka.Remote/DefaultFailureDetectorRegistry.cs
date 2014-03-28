using System;

namespace Akka.Remote
{
    /// <summary>
    /// A lock-less, thread-safe implementation of <see cref="IFailureDetectorRegistry{T}"/>.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class DefaultFailureDetectorRegistry<T> : IFailureDetectorRegistry<T>
    {
        private Func<FailureDetector> factory;

        /// <summary>
        /// Instantiates the DefaultFailureDetectorRegistry an uses a factory method for creating new instances
        /// </summary>
        /// <param name="factory"></param>
        public DefaultFailureDetectorRegistry(Func<FailureDetector> factory)
        {
            this.factory = factory;
        }

        public bool IsAvailable(T resource)
        {
            throw new NotImplementedException();
        }

        public bool IsMonitoring(T resource)
        {
            throw new NotImplementedException();
        }

        public void Heartbeat(T resource)
        {
            throw new NotImplementedException();
        }

        public void Remove(T resource)
        {
            throw new NotImplementedException();
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }
    }
}
