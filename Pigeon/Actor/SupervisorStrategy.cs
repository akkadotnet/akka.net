using Pigeon.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class SupervisorStrategy
    {
        public abstract void Handle(ActorRef child, Exception x);
    }

    public sealed class OneForOneStrategy : SupervisorStrategy
    {
        public static Directive DefaultDecider(Exception exception)
        {
            if (exception is ActorInitializationException)
                return Directive.Stop;
            if (exception is ActorKilledException)
                return Directive.Stop;
            if (exception is DeathPactException)
                return Directive.Stop;

            return Directive.Restart;
        }
        public OneForOneStrategy(int maxNumberOfRetries, TimeSpan duration, Func<Exception, Directive> decider)
        {
            this.MaxNumberOfRetries = maxNumberOfRetries;
            this.Duration = duration;
            this.Decider = decider;
        }

        public int MaxNumberOfRetries { get;private set; }

        public TimeSpan Duration { get; private set; }

        public Func<Exception, Directive> Decider { get; private set; }

        private Dictionary<ActorRef, Failures> actorFailures = new Dictionary<ActorRef, Failures>();
        public override void Handle(ActorRef child, Exception x)
        {
            Failures failures = null;
            actorFailures.TryGetValue(child, out failures);
            //create if missing
            if (failures == null)
            {
                failures = new Failures();
                actorFailures.Add(child, failures);
            }
            //add entry
            failures.Entries.Add(new Failure
            {
                Exception = x,
                Timestamp = DateTime.Now,
            });
            //remove expired
            failures.Entries.RemoveAll(f => f.Timestamp < DateTime.Now - Duration);
            //calc count of active
            var count = failures.Entries.Count();

            if (count >= MaxNumberOfRetries)
            {
                var whatToDo = Decider(x);
                if (whatToDo == Directive.Escalate)
                    child.Tell(new Escalate
                    {
                        Reason = x,
                    });
                if (whatToDo == Directive.Resume)
                    child.Tell(new Resume
                    {
                    });
                if (whatToDo == Directive.Restart)
                    child.Tell(new Restart
                    {
                    });
                if (whatToDo == Directive.Stop)
                    child.Tell(new Stop
                    {
                    });

            }
        }
    }

    public sealed class OneForAllStrategy : SupervisorStrategy
    {
        public override void Handle(ActorRef child, Exception x)
        {
            throw new NotImplementedException();
        }
    }

    public class Failures
    {
        public Failures()
        {
            this.Entries = new List<Failure>();
        }
        public List<Failure> Entries { get;private set; }
    }

    public class Failure
    {
        public Exception Exception { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public enum Directive
    {
        Resume,
        Restart,
        Escalate,
        Stop,
    }
}
