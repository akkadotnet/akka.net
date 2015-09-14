using System;

namespace Akka.Streams.Actors
{
    /**
     * An [[ActorSubscriber]] defines a `RequestStrategy` to control the stream back pressure.
     */
    public interface IRequestStrategy
    {
        /**
         * Invoked by the [[ActorSubscriber]] after each incoming message to
         * determine how many more elements to request from the stream.
         *
         * @param remainingRequested current remaining number of elements that
         *   have been requested from upstream but not received yet
         * @return demand of more elements from the stream, returning 0 means that no
         *   more elements will be requested for now
         */
        int RequestDemand(int remainingRequested);
    }

    /**
     * Requests one more element when `remainingRequested` is 0, i.e.
     * max one element in flight.
     */
    public sealed class OneByOneRequestStrategy : IRequestStrategy
    {
        public static readonly OneByOneRequestStrategy Instance = new OneByOneRequestStrategy();
        private OneByOneRequestStrategy() { }

        public int RequestDemand(int remainingRequested)
        {
            return remainingRequested == 0 ? 1 : 0;
        }
    }

    /**
     * When request is only controlled with manual calls to
     * [[ActorSubscriber#request]].
     */
    public sealed class ZeroRequestStrategy : IRequestStrategy
    {
        public static readonly ZeroRequestStrategy Instance = new ZeroRequestStrategy();
        private ZeroRequestStrategy() { }

        public int RequestDemand(int remainingRequested)
        {
            return 0;
        }
    }

    /**
     * Requests up to the `highWatermark` when the `remainingRequested` is
     * below the `lowWatermark`. This a good strategy when the actor performs work itself.
     */
    public sealed class WatermarkRequestStrategy : IRequestStrategy
    {
        public readonly int HighWatermark;
        public readonly int LowWatermark;

        public WatermarkRequestStrategy(int highWatermark)
        {
            HighWatermark = highWatermark;
            LowWatermark = Math.Max(1, highWatermark / 2);
        }

        public WatermarkRequestStrategy(int highWatermark, int lowWatermark)
        {
            HighWatermark = highWatermark;
            LowWatermark = lowWatermark;
        }

        public int RequestDemand(int remainingRequested)
        {
            return remainingRequested < LowWatermark ? HighWatermark - remainingRequested : 0;
        }
    }

    /**
     * Requests up to the `max` and also takes the number of messages
     * that have been queued internally or delegated to other actors into account.
     * Concrete subclass must implement [[#inFlightInternally]].
     * It will request elements in minimum batches of the defined [[#batchSize]].
     */
    public abstract class MaxInFlightRequestStrategy : IRequestStrategy
    {
        public readonly int Max;

        protected MaxInFlightRequestStrategy(int max)
        {
            Max = max;
        }

        /**
         * Concrete subclass must implement this method to define how many
         * messages that are currently in progress or queued.
         */
        public abstract int InFlight { get; }

        /**
         * Elements will be requested in minimum batches of this size.
         * Default is 5. Subclass may override to define the batch size.
         */
        public virtual int BatchSize { get { return 5; } }

        public int RequestDemand(int remainingRequested)
        {
            var batch = Math.Min(BatchSize, Max);
            return (remainingRequested + InFlight) <= (Max - batch) 
                ? Math.Max(0, Max - remainingRequested - InFlight) 
                : 0;
        }
    }
}