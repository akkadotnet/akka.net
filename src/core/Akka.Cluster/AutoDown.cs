using System;
using Akka.Actor;

namespace Akka.Cluster
{
    public class AutoDown
    {
        public static Props Props(TimeSpan autoDownUnreachableAfter)
        {
            return new Props(typeof(AutoDown), new object[]{autoDownUnreachableAfter});
        }
    }
}
