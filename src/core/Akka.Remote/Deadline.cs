using System;

namespace Akka.Remote
{
    /// <summary>
    /// Import of the scala.concurrenct.duration.Deadline class
    /// </summary>
    public class Deadline
    {
        public Deadline(DateTime when)
        {
            When = when;
        }

        public bool IsOverdue
        {
            get { return DateTime.Now > When; }
        }

        public bool HasTimeLeft
        {
            get { return DateTime.Now < When; }
        }

        public DateTime When { get; private set; }

        /// <summary>
        /// Warning: creates a new <see cref="TimeSpan"/> instance each time it's used
        /// </summary>
        public TimeSpan TimeLeft { get { return When - DateTime.Now; } }

        #region Overrides

        public override bool Equals(object obj)
        {
            var deadlineObj = ((Deadline) obj);
            if (deadlineObj == null)
            {
                return false;
            }

            return When.Equals(deadlineObj.When);
        }

        public override int GetHashCode()
        {
            return When.GetHashCode();
        }

        #endregion


        #region Static members

        /// <summary>
        /// Returns a deadline that is due <see cref="DateTime.Now"/>
        /// </summary>
        public static Deadline Now
        {
            get
            {
                return new Deadline(DateTime.Now);
            }
        }

        /// <summary>
        /// Adds a given <see cref="TimeSpan"/> to the due time of this <see cref="Deadline"/>
        /// </summary>
        public static Deadline operator +(Deadline deadline, TimeSpan duration)
        {
            return new Deadline(deadline.When.Add(duration));
        }

        /// <summary>
        /// Adds a given <see cref="Nullable{TimeSpan}"/> to the due time of this <see cref="Deadline"/>
        /// </summary>
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