namespace Akka.Remote
{
    /// <summary>
    /// A failure detector must be a thread-safe, mutable construct that registers heartbeat events of a resource and
    /// is able to decide the availability of that monitored resource
    /// </summary>
    public abstract class FailureDetector
    {
        /// <summary>
        /// Returns true if the resource is considered to be up and healthy; false otherwise
        /// </summary>
        public abstract bool IsAvailable { get; }

        /// <summary>
        /// Returns true if the failure detector has received any heartbeats and started monitoring
        /// the resource
        /// </summary>
        public abstract bool IsMonitoring { get; }

        /// <summary>
        /// Notifies the <see cref="FailureDetector"/> that a heartbeat arrived from the monitored resource.
        /// This causes the <see cref="FailureDetector"/> to update its state.
        /// </summary>
        public abstract void HeartBeat();
    }
}
