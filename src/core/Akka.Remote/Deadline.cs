//-----------------------------------------------------------------------
// <copyright file="Deadline.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Remote
{
    /// <summary>
    /// This class represents the latest date or time by which an operation should be completed.
    /// </summary>
    public class Deadline
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Deadline"/> class.
        /// </summary>
        /// <param name="when">The <see cref="DateTime"/> that the deadline is due.</param>
        public Deadline(DateTime when)
        {
            When = when;
        }

        /// <summary>
        /// Determines whether the deadline has past.
        /// </summary>
        public bool IsOverdue
        {
            get { return DateTime.UtcNow > When; }
        }

        /// <summary>
        /// Determines whether there is still time left until the deadline.
        /// </summary>
        public bool HasTimeLeft
        {
            get { return DateTime.UtcNow < When; }
        }

        /// <summary>
        /// The <see cref="DateTime"/> that the deadline is due.
        /// </summary>
        public DateTime When { get; private set; }

        /// <summary>
        /// <para>
        /// The amount of time left until the deadline is reached.
        /// </para>
        /// <note>
        /// Warning: creates a new <see cref="TimeSpan"/> instance each time it's used
        /// </note>
        /// </summary>
        public TimeSpan TimeLeft { get { return When - DateTime.UtcNow; } }

        #region Overrides

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            var deadlineObj = ((Deadline) obj);
            if (deadlineObj == null)
            {
                return false;
            }

            return When.Equals(deadlineObj.When);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return When.GetHashCode();
        }

        #endregion


        #region Static members

        /// <summary>
        /// A deadline that is due <see cref="DateTime.UtcNow"/>
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
        /// <param name="deadline">The deadline whose time is being extended</param>
        /// <param name="duration">The amount of time being added to the deadline</param>
        /// <returns>A new deadline with the specified duration added to the due time</returns>
        public static Deadline operator +(Deadline deadline, TimeSpan duration)
        {
            return new Deadline(deadline.When.Add(duration));
        }

        /// <summary>
        /// Adds a given <see cref="Nullable{TimeSpan}"/> to the due time of this <see cref="Deadline"/>
        /// </summary>
        /// <param name="deadline">The deadline whose time is being extended</param>
        /// <param name="duration">The amount of time being added to the deadline</param>
        /// <returns>A new deadline with the specified duration added to the due time</returns>
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
