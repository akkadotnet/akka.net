using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ChildrenContainer {

        public abstract void add(string name, ChildRestartStats stats);
        public abstract void remove(ActorRef child);

        public abstract ChildStats getByName(string name);
        public abstract ChildRestartStats getByRef(ActorRef actor);

        public abstract ActorRef[] children { get; }
        public abstract ChildRestartStats[] stats { get; }

        public abstract void shallDie(ActorRef actor);

      // reserve that name or throw an exception
        public abstract void reserve(string name);
      // cancel a reservation
        public abstract void unreserve(string name);

        public bool isTerminating = false;
        public bool isNormal = true;
    }

    public class ChildRestartStats
    {}

    public class ChildStats{
    }
}
