//-----------------------------------------------------------------------
// <copyright file="Deadline.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Remote
{
    /// <summary>
    /// Import of the scala.concurrent.duration.Deadline class
    /// </summary>
    public class Deadline
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="when">TBD</param>
        public Deadline(DateTime when)
        {
            When = when;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsOverdue
        {
            get { return DateTime.UtcNow > When; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool HasTimeLeft
        {
            get { return DateTime.UtcNow < When; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public DateTime When { get; private set; }

        /// <summary>
        /// Warning: creates a new <see cref="TimeSpan"/> instance each time it's used
        /// </summary>
        public TimeSpan TimeLeft { get { return When - DateTime.UtcNow; } }

        #region Overrides

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            var deadlineObj = ((Deadline) obj);
            if (deadlineObj == null)
            {
                return false;
            }

            return When.Equals(deadlineObj.When);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return When.GetHashCode();
        }

        #endregion


        #region Static members

        /// <summary>
        /// Returns a deadline that is due <see cref="DateTime.UtcNow"/>
        /// </summary>
        public static Deadline Now
        {
            get
            {
                return new Deadline(DateTime.UtcNow);
            }
        }

        /// <summary>
        /// Adds a given <see cref="TimeSpan"/> to the due time of this <see cref="Deadline"/>
        /// </summary>
        /// <param name="deadline">TBD</param>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public static Deadline operator +(Deadline deadline, TimeSpan duration)
        {
            return new Deadline(deadline.When.Add(duration));
        }

        /// <summary>
        /// Adds a given <see cref="Nullable{TimeSpan}"/> to the due time of this <see cref="Deadline"/>
        /// </summary>
        /// <param name="deadline">TBD</param>
        /// <param name="duration">TBD</param>
        /// <returns>TBD</returns>
        public static Deadline operator +(Deadline deadline, TimeSpan? duration)
        {
            if (duration.HasValue)
                return new Deadline(deadline.When.Add(duration.Value));
            else
                return deadline;
        }

        #endregion

    }
}

