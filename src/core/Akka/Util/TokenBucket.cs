//-----------------------------------------------------------------------
// <copyright file="TokenBucket.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using System;

namespace Akka.Util
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public abstract class TokenBucket
    {
        private readonly long _capacity;
        private readonly long _ticksBetweenTokens;
        private long _lastUpdate;
        private long _availableTokens;

        protected TokenBucket(long capacity, long ticksBetweenTokens)
        {
            if(capacity<0)
                throw new ArgumentException("Capacity must be non-negativ", nameof(capacity));
            if (ticksBetweenTokens <= 0)
                throw new ArgumentException("Time between tokens must be larger than zero ticks.",
                    nameof(ticksBetweenTokens));
            
            _capacity = capacity;
            _ticksBetweenTokens = ticksBetweenTokens;
        }

        /// <summary>
        /// This method must be called before the token bucket can be used.
        /// </summary>
        public void Init()
        {
            _availableTokens = _capacity;
            _lastUpdate = CurrentTime;
        }

        /// <summary>
        /// The current time in ticks. The returned value is monotonic, might wrap over and has no relationship with wall-clock. 
        /// </summary>
        /// <returns>The current time in ticks as Long</returns>
        public abstract long CurrentTime { get; }

        /// <summary>
        /// Call this (side-effecting) method whenever an element should be passed through the token-bucket. This method
        /// will return the number of nanoseconds the element needs to be delayed to conform with the token bucket parameters.
        /// Returns zero if the element can be emitted immediately. The method does not handle overflow, if an element is to
        /// be delayed longer in nanoseconds than what can be represented as a positive Long then an undefined value is returned.
        ///
        /// If a non-zero value is returned, it is the responsibility of the caller to not call this method before the
        /// returned delay has been elapsed (but can be called later). This class does not check or protect against early
        /// calls. 
        /// </summary>
        /// <param name="cost">How many tokens the element costs. Can be larger than the capacity of the bucket.</param>
        public long Offer(long cost)
        {
            if(cost < 0)
                throw new ArgumentException("Cost must be non-negativ", nameof(cost));

            var now = CurrentTime;
            var timeElapsed = now - _lastUpdate;

            long tokensArrived;
            // Was there even a tick since last time?
            if (timeElapsed >= _ticksBetweenTokens)
            {
                if (timeElapsed < _ticksBetweenTokens*2)
                {
                    // only one tick elapsed
                    _lastUpdate += _ticksBetweenTokens;
                    tokensArrived = 1;
                }
                else
                {
                    // Ok, no choice, do the slow integer division
                    tokensArrived = timeElapsed/_ticksBetweenTokens;
                    _lastUpdate += tokensArrived*_ticksBetweenTokens;
                }
            }
            else
                tokensArrived = 0;

            _availableTokens = Math.Min(_availableTokens + tokensArrived, _capacity);

            if (cost <= _availableTokens)
            {
                _availableTokens -= cost;
                return 0;
            }

            var remainingCost = cost - _availableTokens;
            // Tokens always arrive at exact multiples of the token generation period, we must account for that
            var timeSinceTokenArrival = now - _lastUpdate;
            var delay = remainingCost*_ticksBetweenTokens - timeSinceTokenArrival;
            _availableTokens = 0;
            _lastUpdate = now + delay;
            return delay;
        }
    }

    /// <summary>
    /// Default implementation of <see cref="TokenBucket"/> that uses <see cref="DateTime.Ticks"/> as the time source.
    /// </summary>
    public sealed class TickTimeTokenBucket : TokenBucket
    {
        public TickTimeTokenBucket(long capacity, long period) : base(capacity, period)
        {
        }

        public override long CurrentTime => DateTime.Now.Ticks;
    }
}
