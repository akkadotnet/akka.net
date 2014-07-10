using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;

namespace Akka.Routing
{
    /// <summary>
    /// [[Pool]] routers with dynamically resizable number of routees are implemented by providing a Resizer
    /// implementation in the [[akka.routing.Pool]] configuration
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
        /// <param name="messageCounter"></param>
        /// <returns></returns>
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
        /// <param name="currentRoutees"></param>
        /// <returns></returns>
        public abstract int Resize(IEnumerable<Routee> currentRoutees);
    }

    /// <summary>
    /// Implementation of [[Resizer]] that adjust the [[Pool]] based on specified thresholds.
    /// </summary>
    public class DefaultResizer : Resizer
    {
        /// <summary>
        /// The fewest number of routees the router should ever have.
        /// </summary>
        private readonly int lowerBound = 1;

        /// <summary>
        /// The most number of routees the router should ever have. 
        /// Must be greater than or equal to `lowerBound`.
        /// </summary>
        private readonly int upperBound = 10;

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
        private readonly int pressureThreshold = 1;

        /// <summary>
        /// Percentage to increase capacity whenever all routees are busy.
        /// For example, 0.2 would increase 20% (rounded up), i.e. if current
        /// capacity is 6 it will request an increase of 2 more routees.
        /// </summary>
        private readonly double rampupRate = 0.2;

        /// <summary>
        /// Minimum fraction of busy routees before backing off.
        /// For example, if this is 0.3, then we'll remove some routees only when
        /// less than 30% of routees are busy, i.e. if current capacity is 10 and
        /// 3 are busy then the capacity is unchanged, but if 2 or less are busy
        /// the capacity is decreased.
        ///
        /// Use 0.0 or negative to avoid removal of routees.
        /// </summary>
        private readonly double backoffThreshold = 0.3;

        /// <summary>
        /// Fraction of routees to be removed when the resizer reaches the
        /// backoffThreshold.
        /// For example, 0.1 would decrease 10% (rounded up), i.e. if current
        /// capacity is 9 it will request an decrease of 1 routee.
        /// </summary>
        private readonly double backoffRate = 0.1;

        /// <summary>
        /// Number of messages between resize operation.
        /// Use 1 to resize before each message.
        /// </summary>
        private readonly int messagesPerResize = 10;

        public DefaultResizer(int lower, int upper)
        {
            lowerBound = lower;
            upperBound = upper;
        }

        /// <summary>
        /// Creates a new DefaultResizer from the given configuration
        /// </summary>
        /// <param name="resizerConfig"></param>
        private DefaultResizer(Config resizerConfig)
        {
            lowerBound = resizerConfig.GetInt("lower-bound");
            upperBound = resizerConfig.GetInt("upper-bound");
            pressureThreshold = resizerConfig.GetInt("pressure-threshold");
            rampupRate = resizerConfig.GetDouble("rampup-rate");
            backoffThreshold = resizerConfig.GetDouble("backoff-threshold");
            backoffRate = resizerConfig.GetDouble("backoff-rate");
            messagesPerResize = resizerConfig.GetInt("messages-per-resize");

            Validate();
        }

        private void Validate()
        {
            if (lowerBound < 0)
                throw new ArgumentException(string.Format("lowerBound must be >= 0, was: {0}", lowerBound));

            if (upperBound < 0)
                throw new ArgumentException(string.Format("upperBound must be >= 0, was: {0}", upperBound));
            if (upperBound < lowerBound)
                throw new ArgumentException(string.Format("upperBound must be >= lowerBound, was: {0} < {1}",
                    upperBound, lowerBound));
            if (rampupRate < 0.0)
                throw new ArgumentException(string.Format("rampupRate must be >= 0.0, was {0}", rampupRate));
            if (backoffThreshold > 1.0)
                throw new ArgumentException(string.Format("backoffThreshold must be <= 1.0, was {0}", backoffThreshold));
            if (backoffRate < 0.0)
                throw new ArgumentException(string.Format("backoffRate must be >= 0.0, was {0}", backoffRate));
            if (messagesPerResize <= 0)
                throw new ArgumentException(string.Format("messagesPerResize must be > 0, was {0}", messagesPerResize));
        }

        public static DefaultResizer FromConfig(Config resizerConfig)
        {
            return resizerConfig.GetBoolean("resizer.enabled") ? new DefaultResizer(resizerConfig) : null;
        }

        public override bool IsTimeForResize(long messageCounter)
        {
            return messageCounter%messagesPerResize == 0;
        }

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
        private int Capacity(IEnumerable<Routee> currentRoutees)
        {
            var routees = currentRoutees as Routee[] ?? currentRoutees.ToArray();
            var currentSize = routees.Count();
            var pressure = Pressure(routees);
            var delta = Filter(pressure, currentSize);
            var proposed = currentSize + delta;


            if (proposed < lowerBound)
                return delta + (lowerBound - proposed);
            if (proposed > upperBound)
                return delta - (proposed - upperBound);
            return delta;
        }

        /// <summary>
        /// This method can be used to smooth the capacity delta by considering
        /// the current pressure and current capacity.
        /// </summary>
        /// <param name="pressure">pressure current number of busy routees</param>
        /// <param name="capacity">capacity current number of routees</param>
        /// <returns>proposed change in the capacity</returns>
        private int Filter(int pressure, int capacity)
        {
            return Rampup(pressure, capacity) + Backoff(pressure, capacity);
        }

        /// <summary>
        /// Computes a proposed negative (or zero) capacity delta using
        /// the configured `backoffThreshold` and `backoffRate`
        /// </summary>
        /// <param name="pressure">pressure current number of busy routees</param>
        /// <param name="capacity">capacity current number of routees</param>
        /// <returns>proposed decrease in capacity (as a negative number)</returns>
        private int Backoff(int pressure, int capacity)
        {
            return backoffThreshold > 0.0 && backoffRate > 0.0 && capacity > 0 &&
                   (Convert.ToDouble(pressure)/Convert.ToDouble(capacity)) < backoffThreshold
                ? Convert.ToInt32(Math.Floor(-1.0*backoffRate*capacity))
                : 0;
        }

        /// <summary>
        /// Computes a proposed positive (or zero) capacity delta using
        /// the configured `rampupRate`.
        /// </summary>
        /// <param name="pressure">the current number of busy routees</param>
        /// <param name="capacity">the current number of total routees</param>
        /// <returns>proposed increase in capacity</returns>
        private int Rampup(int pressure, int capacity)
        {
            return (pressure < capacity) ? 0 : Convert.ToInt32(Math.Ceiling(rampupRate*capacity));
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
        /// <param name="currentRoutees"></param>
        /// <returns></returns>
        private int Pressure(IEnumerable<Routee> currentRoutees)
        {
            return currentRoutees.Count(
                routee =>
                {
                    var actorRefRoutee = routee as ActorRefRoutee;
                    if (actorRefRoutee != null)
                    {
                        var actorRef = actorRefRoutee.Actor as ActorRefWithCell;
                        if (actorRef != null)
                        {
                            var underlying = actorRef.Underlying;
                            var cell = underlying as ActorCell;
                            if(cell != null)
                            {
                                return
                                    pressureThreshold == 1
                                        ? cell.Mailbox.Status == Mailbox.MailboxStatus.Busy &&
                                          cell.Mailbox.HasUnscheduledMessages
                                        : (pressureThreshold < 1
                                            ? cell.Mailbox.Status == Mailbox.MailboxStatus.Busy &&
                                              cell.CurrentMessage != null
                                            : cell.Mailbox.NumberOfMessages > pressureThreshold);
                            }
                            else
                            {
                                return
                                    pressureThreshold == 1
                                        ? underlying.HasMessages
                                        : (pressureThreshold < 1
                                            ? true // unstarted cells are always busy, for example
                                            : underlying.NumberOfMessages >= pressureThreshold);
                            }
                        }
                    }
                    return false;
                });
        }
    }
}