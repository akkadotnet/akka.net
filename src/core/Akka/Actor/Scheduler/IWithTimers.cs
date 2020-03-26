namespace Akka.Actor
{
    /// <summary>
    /// Marker interface for adding Timers support
    /// </summary>
    public interface IWithTimers
    {
        /// <summary>
        /// Gets or sets the TimerScheduler. This will be automatically populated by the framework in base constructor.
        /// Implement this as an auto property.
        /// </summary>
        ITimerScheduler Timers { get; set; }
    }
}
