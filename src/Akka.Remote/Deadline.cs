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
    }
}