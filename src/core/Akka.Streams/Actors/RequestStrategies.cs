//-----------------------------------------------------------------------
// <copyright file="RequestStrategies.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Actors
{
    ///<summary>
    /// An <see cref="ActorSubscriber"/> defines a <see cref="IRequestStrategy"/>
    /// to control the stream back pressure.
    /// </summary>
    public interface IRequestStrategy
    {
         /// <summary>
         /// Invoked by the <see cref="ActorSubscriber"/> after each incoming message to
         /// determine how many more elements to request from the stream.
         /// </summary>
         /// <param name="remainingRequested">current remaining number of elements
         /// that have been requested from upstream but not received yet</param>
         /// <returns>demand of more elements from the stream, returning 0 means that no
         /// more elements will be requested for now</returns>
        int RequestDemand(int remainingRequested);
    }

    /// <summary>
    /// Requests one more element when remainingRequested is 0, i.e.
    /// * max one element in flight.
    /// </summary>
    public sealed class OneByOneRequestStrategy : IRequestStrategy
    {
        /// <summary>
        /// The singleton strategy that requests another element when none is outstanding.
        /// </summary>
        public static readonly OneByOneRequestStrategy Instance = new();
        private OneByOneRequestStrategy() { }

        /// <summary>
        /// Returns one unit of demand when no elements are outstanding, and zero otherwise.
        /// </summary>
        /// <param name="remainingRequested">The number of requested elements not yet received.</param>
        /// <returns>One when <paramref name="remainingRequested"/> is zero; otherwise, zero.</returns>
        public int RequestDemand(int remainingRequested) => remainingRequested == 0 ? 1 : 0;
    }

    /// <summary>
    /// When request is only controlled with manual calls to <see cref="ActorSubscriber.Request"/>.
    /// </summary>
    public sealed class ZeroRequestStrategy : IRequestStrategy
    {
        /// <summary>
        /// The singleton strategy that never requests automatically.
        /// </summary>
        public static readonly ZeroRequestStrategy Instance = new();
        private ZeroRequestStrategy() { }

        /// <summary>
        /// Returns no additional demand; requests must be made explicitly through <see cref="ActorSubscriber.Request"/>.
        /// </summary>
        /// <param name="remainingRequested">The number of requested elements not yet received; this strategy does not use it.</param>
        /// <returns>Always zero.</returns>
        public int RequestDemand(int remainingRequested) => 0;
    }

    /// <summary>
    /// Requests up to the highWatermark when the remainingRequested is
    /// below the lowWatermark. This a good strategy when the actor performs work itself.
    /// </summary>
    public sealed class WatermarkRequestStrategy : IRequestStrategy
    {
        /// <summary>
        /// The upper bound for outstanding requested elements used by this strategy.
        /// </summary>
        public readonly int HighWatermark;
        /// <summary>
        /// The lower threshold below which this strategy replenishes demand.
        /// </summary>
        public readonly int LowWatermark;

        /// <summary>
        /// Creates a strategy with the supplied upper watermark and a lower watermark of at least one, approximately half the upper value.
        /// </summary>
        /// <param name="highWatermark">The target upper bound for outstanding requests.</param>
        public WatermarkRequestStrategy(int highWatermark)
        {
            HighWatermark = highWatermark;
            LowWatermark = Math.Max(1, highWatermark / 2);
        }

        /// <summary>
        /// Creates a strategy with explicit upper and lower watermarks.
        /// </summary>
        /// <param name="highWatermark">The target upper bound for outstanding requests.</param>
        /// <param name="lowWatermark">The threshold below which the strategy requests enough to reach <paramref name="highWatermark"/>.</param>
        public WatermarkRequestStrategy(int highWatermark, int lowWatermark)
        {
            HighWatermark = highWatermark;
            LowWatermark = lowWatermark;
        }

        /// <summary>
        /// Requests enough elements to reach the upper watermark whenever the outstanding count is below the lower watermark.
        /// </summary>
        /// <param name="remainingRequested">The number of requested elements not yet received.</param>
        /// <returns>The demand needed to reach <see cref="HighWatermark"/> when below <see cref="LowWatermark"/>; otherwise, zero.</returns>
        public int RequestDemand(int remainingRequested)
        {
            return remainingRequested < LowWatermark ? HighWatermark - remainingRequested : 0;
        }
    }

    /// <summary>
    /// Requests up to the max and also takes the number of messages
    /// that have been queued internally or delegated to other actors into account.
    /// Concrete subclass must implement <see cref="InFlight"/>.
    /// It will request elements in minimum batches of the defined <see cref="BatchSize"/>.
    /// </summary>
    public abstract class MaxInFlightRequestStrategy : IRequestStrategy
    {
        /// <summary>
        /// The maximum number of elements that may be requested but not yet processed, including the in-flight count.
        /// </summary>
        public readonly int Max;

        /// <summary>
        /// Initializes the strategy with the maximum in-flight element count.
        /// </summary>
        /// <param name="max">The maximum count used to calculate request demand.</param>
        protected MaxInFlightRequestStrategy(int max)
        {
            Max = max;
        }

        /// <summary>
        /// Concrete subclass must implement this method to define how many
        /// messages that are currently in progress or queued.
        /// </summary>
        public abstract int InFlight { get; }

        /// <summary>
        /// Elements will be requested in minimum batches of this size.
        /// Default is 5. Subclass may override to define the batch size.
        /// </summary>
        public virtual int BatchSize => 5;

        /// <summary>
        /// Requests more elements when there is capacity for at least one request batch.
        /// </summary>
        /// <param name="remainingRequested">The number of requested elements not yet received.</param>
        /// <returns>The remaining capacity when it meets the batch threshold; otherwise, zero.</returns>
        public int RequestDemand(int remainingRequested)
        {
            var batch = Math.Min(BatchSize, Max);
            return remainingRequested + InFlight <= (Max - batch) 
                ? Math.Max(0, Max - remainingRequested - InFlight) 
                : 0;
        }
    }
}
