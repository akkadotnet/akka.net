//-----------------------------------------------------------------------
// <copyright file="RequestStrategies.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        public static readonly OneByOneRequestStrategy Instance = new OneByOneRequestStrategy();
        private OneByOneRequestStrategy() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remainingRequested">TBD</param>
        /// <returns>TBD</returns>
        public int RequestDemand(int remainingRequested) => remainingRequested == 0 ? 1 : 0;
    }

    /// <summary>
    /// When request is only controlled with manual calls to <see cref="ActorSubscriber.Request"/>.
    /// </summary>
    public sealed class ZeroRequestStrategy : IRequestStrategy
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly ZeroRequestStrategy Instance = new ZeroRequestStrategy();
        private ZeroRequestStrategy() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remainingRequested">TBD</param>
        /// <returns>TBD</returns>
        public int RequestDemand(int remainingRequested) => 0;
    }

    /// <summary>
    /// Requests up to the highWatermark when the remainingRequested is
    /// below the lowWatermark. This a good strategy when the actor performs work itself.
    /// </summary>
    public sealed class WatermarkRequestStrategy : IRequestStrategy
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int HighWatermark;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int LowWatermark;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="highWatermark">TBD</param>
        public WatermarkRequestStrategy(int highWatermark)
        {
            HighWatermark = highWatermark;
            LowWatermark = Math.Max(1, highWatermark / 2);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="highWatermark">TBD</param>
        /// <param name="lowWatermark">TBD</param>
        public WatermarkRequestStrategy(int highWatermark, int lowWatermark)
        {
            HighWatermark = highWatermark;
            LowWatermark = lowWatermark;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remainingRequested">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        public readonly int Max;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="max">TBD</param>
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
        /// TBD
        /// </summary>
        /// <param name="remainingRequested">TBD</param>
        /// <returns>TBD</returns>
        public int RequestDemand(int remainingRequested)
        {
            var batch = Math.Min(BatchSize, Max);
            return remainingRequested + InFlight <= (Max - batch) 
                ? Math.Max(0, Max - remainingRequested - InFlight) 
                : 0;
        }
    }
}
