using Pigeon.Event;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class SupervisorStrategy
    {
        protected Dictionary<ActorRef, Failures> actorFailures = new Dictionary<ActorRef, Failures>();
        public abstract Directive Handle(ActorRef child, Exception x);

        public bool HandleFailure(ActorCell actorCell, ActorRef child, Exception cause)
        {
            var directive = Handle(child, cause);
            switch (directive)
            {
                case Directive.Escalate:
                    LogFailure(actorCell, child, cause, directive);
                    return false;
                case Directive.Resume:
                    LogFailure(actorCell, child, cause, directive);
                    ResumeChild(child, cause);
                    return true;
                case Directive.Restart:
                    LogFailure(actorCell, child, cause, directive);
                    ProcessFailure(actorCell, true, child, cause);
                    return true;
                case Directive.Stop:
                    LogFailure(actorCell, child, cause, directive);
                    ProcessFailure(actorCell, false, child, cause);
                    return true;
                default:
                    break;
            }
            return false;
        }

        private void RestartChild(ActorRef child,Exception cause,bool suspendFirst)
        {
            var c = child.AsInstanceOf<InternalActorRef>();
            if (suspendFirst)
                c.Suspend();
            c.AsInstanceOf<InternalActorRef>().Restart(cause);
        }

        private void ProcessFailure(ActorCell actorCell, bool restart, ActorRef child, Exception cause)
        {
            if (restart)
            {
                RestartChild(child, cause, false);
            }
            else
            {
                child.AsInstanceOf<InternalActorRef>().Stop();
            }
            /*
    if (children.nonEmpty) {
      if (restart && children.forall(_.requestRestartPermission(retriesWindow)))
        children foreach (crs ⇒ restartChild(crs.child, cause, suspendFirst = (crs.child != child)))
      else
        for (c ← children) context.stop(c.child)
    }
             */

            //if (children.Any())
            //{
            //    if (restart)
            //    {

            //    }
            //    else
            //    {
            //        foreach (var child in children)
            //        {
            //            child.Stop();
            //        }
            //    }
            //}
        }

        private void ResumeChild(ActorRef child, Exception exception)
        {
            child.AsInstanceOf<InternalActorRef>().Resume(exception);
        }

        private void LogFailure(ActorCell actorCell, ActorRef child, Exception cause, Directive directive)
        {
            System.Diagnostics.Debug.WriteLine("Failute! supervisor: {0}, child: {1}, cause: {2}, directive: {3}", actorCell.Self.Path, child.Path, cause, directive);
            switch(directive)
            {
                case Directive.Resume:
                //    actorCell.System.EventStream.Publish(new Warning(child.Path.ToString(),this.GetType(),cause.Message);
                    break;
                case Directive.Escalate:
                    break;
                default:
                //case Directive.Restart:
                //case Directive.Stop:
                    actorCell.System.EventStream.Publish(new Error(cause, child.Path.ToString(), this.GetType(), cause.Message));
                    break;
            }


        }

        #region Static methods

        /// <summary>
        /// Returns A NEW INSTANCE of the Default Supervisor strategy
        /// </summary>
        public static SupervisorStrategy Default
        {
            get { return new OneForOneStrategy(10, TimeSpan.FromSeconds(30), OneForOneStrategy.DefaultDecider); }
        }

        #endregion
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

        
        public override Directive Handle(ActorRef child, Exception x)
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

            if (count > MaxNumberOfRetries)
            {
                return Directive.Stop;
            }

            var whatToDo = Decider(x);
            return whatToDo;
        }
    }

    public sealed class AllForOneStrategy : SupervisorStrategy
    {
        public override Directive Handle(ActorRef child, Exception x)
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
