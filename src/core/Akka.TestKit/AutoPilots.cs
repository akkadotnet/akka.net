using System;
using Akka.Actor;

namespace Akka.TestKit
{
    public abstract class AutoPilot
    {
        abstract public AutoPilot Run(ActorRef sender, object message);

        public static NoAutoPilot NoAutoPilot { get { return NoAutoPilot.Instance; } }
        public static KeepRunning KeepRunning { get { return KeepRunning.Instance; } }
    }

    public class NoAutoPilot : AutoPilot
    {
        public static NoAutoPilot Instance = new NoAutoPilot();

        private NoAutoPilot() { }
        public override AutoPilot Run(ActorRef sender, object message)
        {
            return this;
        }
    }

    public class KeepRunning : AutoPilot
    {
        public static KeepRunning Instance = new KeepRunning();

        private KeepRunning(){}

        public override AutoPilot Run(ActorRef sender, object message)
        {
            throw new Exception("Must not call");
        }
    }
}