//-----------------------------------------------------------------------
// <copyright file="RequestStrategies.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public static readonly OneByOneRequestStrategy Instance = new OneByOneRequestStrategy();
        private OneByOneRequestStrategy() { }

        public int RequestDemand(int remainingRequested) => remainingRequested == 0 ? 1 : 0;
    }

    /// <summary>
    /// When request is only controlled with manual calls to <see cref="ActorSubscriber.Request"/>.
    /// </summary>
    public sealed class ZeroRequestStrategy : IRequestStrategy
    {
        public static readonly ZeroRequestStrategy Instance = new ZeroRequestStrategy();
        private ZeroRequestStrategy() { }

        public int RequestDemand(int remainingRequested) => 0;
    }

    /// <summary>
    /// Requests up to the highWatermark when the remainingRequested is
    /// below the lowWatermark. This a good strategy when the actor performs work itself.
    /// </summary>
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

    /// <summary>
    /// Requests up to the max and also takes the number of messages
    /// that have been queued internally or delegated to other actors into account.
    /// Concrete subclass must implement <see cref="InFlight"/>.
    /// It will request elements in minimum batches of the defined <see cref="BatchSize"/>.
    /// </summary>
    public abstract class MaxInFlightRequestStrategy : IRequestStrategy
    {
        public readonly int Max;

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

        public int RequestDemand(int remainingRequested)
        {
            var batch = Math.Min(BatchSize, Max);
            return remainingRequested + InFlight <= (Max - batch) 
                ? Math.Max(0, Max - remainingRequested - InFlight) 
                : 0;
        }
    }
}