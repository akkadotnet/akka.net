//-----------------------------------------------------------------------
// <copyright file="Resizer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Routing
{
    /// <summary>
    /// <see cref="Akka.Routing.Pool"/> routers with dynamically resizable number of routees are implemented by providing a Resizer
    /// implementation in the <see cref="Akka.Routing.Pool"/> configuration
    /// </summary>
    public abstract class Resizer
    {
        /// <summary>
        /// Is it time for resizing. Typically implemented with modulo of nth message, but
        /// could be based on elapsed time or something else. The messageCounter starts with 0
        /// for the initial resize and continues with 1 for the first message. Make sure to perform
        /// initial resize before first message (messageCounter == 0), because there is no guarantee
        /// that resize will be done when concurrent messages are in play.
        /// 
        /// CAUTION: this method is invoked from the thread which tries to send a
        /// message to the pool, i.e. the ActorRef.!() method, hence it may be called
        /// concurrently.
        /// </summary>
        /// <param name="messageCounter">TBD</param>
        /// <returns>TBD</returns>
        public abstract bool IsTimeForResize(long messageCounter);

        /// <summary>
        /// Decide if the capacity of the router need to be changed. Will be invoked when `isTimeForResize`
        /// returns true and no other resize is in progress.
        ///
        /// Return the number of routees to add or remove. Negative value will remove that number of routees.
        /// Positive value will add that number of routess. 0 will not change the routees.
        ///
        /// This method is invoked only in the context of the Router actor.
        /// </summary>
        /// <param name="currentRoutees">TBD</param>
        /// <returns>TBD</returns>
        public abstract int Resize(IEnumerable<Routee> currentRoutees);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="parentConfig">TBD</param>
        /// <returns>TBD</returns>
        public static Resizer FromConfig(Config parentConfig)
        {
            var defaultResizerConfig = parentConfig.GetConfig("resizer");

            if (!defaultResizerConfig.IsNullOrEmpty() && defaultResizerConfig.GetBoolean("enabled", false))
            {
                return DefaultResizer.Apply(defaultResizerConfig);
            }
            else
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Implementation of <see cref="Akka.Routing.Resizer"/> that adjust the <see cref="Akka.Routing.Pool"/> based on specified thresholds.
    /// </summary>
    public class DefaultResizer : Resizer, IEquatable<DefaultResizer>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultResizer"/> class.
        /// </summary>
        /// <param name="lower">TBD</param>
        /// <param name="upper">TBD</param>
        /// <param name="pressureThreshold">TBD</param>
        /// <param name="rampupRate">TBD</param>
        /// <param name="backoffThreshold">TBD</param>
        /// <param name="backoffRate">TBD</param>
        /// <param name="messagesPerResize">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception can be thrown for a number of reasons. These include:
        /// <ul>
        /// <li>The given <paramref name="lower"/> bound was negative.</li>
        /// <li>The given <paramref name="upper"/> bound was negative.</li>
        /// <li>The given <paramref name="upper"/> bound was below the <paramref name="lower"/>bound.</li>
        /// <li>The given <paramref name="rampupRate"/> was negative.</li>
        /// <li>The given <paramref name="backoffThreshold"/> was greater than one.</li>
        /// <li>The given <paramref name="backoffRate"/> was negative.</li>
        /// <li>The given <paramref name="messagesPerResize"/> was less than one.</li>
        /// </ul>
        /// </exception>
        public DefaultResizer(
            int lower,
            int upper,
            int pressureThreshold = 1,
            double rampupRate = 0.2d,
            double backoffThreshold = 0.3d,
            double backoffRate = 0.1d,
            int messagesPerResize = 10)
        {
            if (lower < 0)
                throw new ArgumentException($"lowerBound must be >= 0, was: {lower}", nameof(lower));
            if (upper < 0)
                throw new ArgumentException($"upperBound must be >= 0, was: {upper}", nameof(upper));
            if (upper < lower)
                throw new ArgumentException($"upperBound must be >= lowerBound, was: {upper} < {lower}", nameof(upper));
            if (rampupRate < 0.0)
                throw new ArgumentException($"rampupRate must be >= 0.0, was {rampupRate}", nameof(rampupRate));
            if (backoffThreshold > 1.0)
                throw new ArgumentException($"backoffThreshold must be <= 1.0, was {backoffThreshold}", nameof(backoffThreshold));
            if (backoffRate < 0.0)
                throw new ArgumentException($"backoffRate must be >= 0.0, was {backoffRate}", nameof(backoffRate));
            if (messagesPerResize <= 0)
                throw new ArgumentException($"messagesPerResize must be > 0, was {messagesPerResize}", nameof(messagesPerResize));

            LowerBound = lower;
            UpperBound = upper;
            PressureThreshold = pressureThreshold;
            RampupRate = rampupRate;
            BackoffThreshold = backoffThreshold;
            BackoffRate = backoffRate;
            MessagesPerResize = messagesPerResize;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="resizerConfig">TBD</param>
        /// <returns>TBD</returns>
        public new static DefaultResizer FromConfig(Config resizerConfig)
        {
            return resizerConfig.GetBoolean("resizer.enabled", false) ? DefaultResizer.Apply(resizerConfig.GetConfig("resizer")) : null;
        }

        /// <summary>
        /// Creates a new DefaultResizer from the given configuration
        /// </summary>
        /// <param name="resizerConfig">TBD</param>
        /// <returns>TBD</returns>
        internal static DefaultResizer Apply(Config resizerConfig)
        {
            if (resizerConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DefaultResizer>();

            return new DefaultResizer(
                  resizerConfig.GetInt("lower-bound", 0),
                  resizerConfig.GetInt("upper-bound", 0),
                  resizerConfig.GetInt("pressure-threshold", 0),
                  resizerConfig.GetDouble("rampup-rate", 0),
                  resizerConfig.GetDouble("backoff-threshold", 0),
                  resizerConfig.GetDouble("backoff-rate", 0),
                  resizerConfig.GetInt("messages-per-resize", 0)
                );
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="messageCounter">TBD</param>
        /// <returns>TBD</returns>
        public override bool IsTimeForResize(long messageCounter)
        {
            return messageCounter % MessagesPerResize == 0;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="currentRoutees">TBD</param>
        /// <returns>TBD</returns>
        public override int Resize(IEnumerable<Routee> currentRoutees)
        {
            return Capacity(currentRoutees);
        }

        /// <summary>
        /// Returns the overall desired change in resizer capacity. Positive value will
        /// add routees to the resizer. Negative value will remove routees from the
        /// resizer
        /// </summary>
        /// <param name="currentRoutees">The current actor in the resizer</param>
        /// <returns>the number of routees by which the resizer should be adjusted (positive, negative or zero)</returns>
        public int Capacity(IEnumerable<Routee> currentRoutees)
        {
            var routees = currentRoutees as Routee[] ?? currentRoutees.ToArray();
            var currentSize = routees.Length;
            var pressure = Pressure(routees);
            var delta = Filter(pressure, currentSize);
            var proposed = currentSize + delta;

            if (proposed < LowerBound)
                return delta + (LowerBound - proposed);
            if (proposed > UpperBound)
                return delta - (proposed - UpperBound);
            return delta;
        }

        /// <summary>
        /// Number of routees considered busy, or above 'pressure level'.
        ///
        /// Implementation depends on the value of `pressureThreshold`
        /// (default is 1).
        /// <ul>
        /// <li> 0:   number of routees currently processing a message.</li>
        /// <li> 1:   number of routees currently processing a message has
        ///           some messages in mailbox.</li>
        /// <li> > 1: number of routees with at least the configured `pressureThreshold`
        ///           messages in their mailbox. Note that estimating mailbox size of
        ///           default UnboundedMailbox is O(N) operation.</li>
        /// </ul>
        /// </summary>
        /// <param name="currentRoutees">An enumeration of the current routees</param>
        /// <returns>The number of routees considered to be above pressure level.</returns>
        public int Pressure(IEnumerable<Routee> currentRoutees)
        {
            return currentRoutees.Count(
                routee =>
                {
                    if (routee is ActorRefRoutee actorRefRoutee && actorRefRoutee.Actor is ActorRefWithCell actorRef)
                    {
                        var underlying = actorRef.Underlying;
                        if (underlying is ActorCell cell)
                        {
                            if (PressureThreshold == 1)
                                return cell.Mailbox.IsScheduled() && cell.Mailbox.HasMessages;
                            if (PressureThreshold < 1)
                                return cell.Mailbox.IsScheduled() && cell.CurrentMessage != null;

                            return cell.Mailbox.NumberOfMessages >= PressureThreshold;
                        }
                        else
                        {
                            if (PressureThreshold == 1)
                                return underlying.HasMessages;
                            if (PressureThreshold < 1)
                                return true; //unstarted cells are always busy, for instance
                            return underlying.NumberOfMessages >= PressureThreshold;
                        }
                    }
                    return false;
                });
        }

        /// <summary>
        /// This method can be used to smooth the capacity delta by considering
        /// the current pressure and current capacity.
        /// </summary>
        /// <param name="pressure">pressure current number of busy routees</param>
        /// <param name="capacity">capacity current number of routees</param>
        /// <returns>proposed change in the capacity</returns>
        public int Filter(int pressure, int capacity)
        {
            return Rampup(pressure, capacity) + Backoff(pressure, capacity);
        }

        /// <summary>
        /// Computes a proposed positive (or zero) capacity delta using
        /// the configured `rampupRate`.
        /// </summary>
        /// <param name="pressure">the current number of busy routees</param>
        /// <param name="capacity">the current number of total routees</param>
        /// <returns>proposed increase in capacity</returns>
        public int Rampup(int pressure, int capacity)
        {
            return pressure < capacity ? 0 : Convert.ToInt32(Math.Ceiling(RampupRate * capacity));
        }

        /// <summary>
        /// Computes a proposed negative (or zero) capacity delta using
        /// the configured `backoffThreshold` and `backoffRate`
        /// </summary>
        /// <param name="pressure">pressure current number of busy routees</param>
        /// <param name="capacity">capacity current number of routees</param>
        /// <returns>proposed decrease in capacity (as a negative number)</returns>
        public int Backoff(int pressure, int capacity)
        {
            if (BackoffThreshold > 0.0 && BackoffRate > 0.0 && capacity > 0 && (Convert.ToDouble(pressure) / Convert.ToDouble(capacity)) < BackoffThreshold)
            {
                return Convert.ToInt32(Math.Floor(-1.0 * BackoffRate * capacity));
            }

            return 0;
        }

        /// <summary>
        /// The fewest number of routees the router should ever have.
        /// </summary>
        public int LowerBound { get; set; }

        /// <summary>
        /// The most number of routees the router should ever have. 
        /// Must be greater than or equal to `lowerBound`.
        /// </summary>
        public int UpperBound { get; set; }

        /// <summary>
        /// * Threshold to evaluate if routee is considered to be busy (under pressure).
        /// Implementation depends on this value (default is 1).
        /// <ul>
        /// <li> 0:   number of routees currently processing a message.</li>
        /// <li> 1:   number of routees currently processing a message has
        ///           some messages in mailbox.</li>
        /// <li> > 1: number of routees with at least the configured `pressureThreshold`
        ///           messages in their mailbox. Note that estimating mailbox size of
        ///           default UnboundedMailbox is O(N) operation.</li>
        /// </ul>
        /// </summary>
        public int PressureThreshold { get; private set; }

        /// <summary>
        /// Percentage to increase capacity whenever all routees are busy.
        /// For example, 0.2 would increase 20% (rounded up), i.e. if current
        /// capacity is 6 it will request an increase of 2 more routees.
        /// </summary>
        public double RampupRate { get; private set; }

        /// <summary>
        /// Minimum fraction of busy routees before backing off.
        /// For example, if this is 0.3, then we'll remove some routees only when
        /// less than 30% of routees are busy, i.e. if current capacity is 10 and
        /// 3 are busy then the capacity is unchanged, but if 2 or less are busy
        /// the capacity is decreased.
        ///
        /// Use 0.0 or negative to avoid removal of routees.
        /// </summary>
        public double BackoffThreshold { get; private set; }

        /// <summary>
        /// Fraction of routees to be removed when the resizer reaches the
        /// backoffThreshold.
        /// For example, 0.1 would decrease 10% (rounded up), i.e. if current
        /// capacity is 9 it will request an decrease of 1 routee.
        /// </summary>
        public double BackoffRate { get; private set; }

        /// <summary>
        /// Number of messages between resize operation.
        /// Use 1 to resize before each message.
        /// </summary>
        public int MessagesPerResize { get; private set; }

        /// <inheritdoc/>
        public bool Equals(DefaultResizer other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return MessagesPerResize == other.MessagesPerResize && BackoffRate.Equals(other.BackoffRate) && RampupRate.Equals(other.RampupRate) && BackoffThreshold.Equals(other.BackoffThreshold) && UpperBound == other.UpperBound && PressureThreshold == other.PressureThreshold && LowerBound == other.LowerBound;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((DefaultResizer)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = MessagesPerResize;
                hashCode = (hashCode * 397) ^ BackoffRate.GetHashCode();
                hashCode = (hashCode * 397) ^ RampupRate.GetHashCode();
                hashCode = (hashCode * 397) ^ BackoffThreshold.GetHashCode();
                hashCode = (hashCode * 397) ^ UpperBound;
                hashCode = (hashCode * 397) ^ PressureThreshold;
                hashCode = (hashCode * 397) ^ LowerBound;
                return hashCode;
            }
        }
    }
}
