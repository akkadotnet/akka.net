using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class SupervisionTests
    {
        public class ParentActor : UntypedActor
        {
            private ActorRef child = Context.ActorOf<ChildActor>();
            private TaskCompletionSource<bool> done;

            public ParentActor(TaskCompletionSource<bool> done)
            {
                this.done = done;
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(10, TimeSpan.FromSeconds(100), x =>
                {
                    if (x is NullReferenceException)
                        return Directive.Stop;

                    return Directive.Resume;
                });
            }

            protected override void OnReceive(object message)
            {
                child.Tell("throw");
            }
        }

        public class ChildActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                throw new NullReferenceException();
            }

        }

        [TestMethod]
        public void OneForOneStopsChildOnStopDirective()
        {
            var done = new TaskCompletionSource<bool>();
            using (var system = ActorSystem.Create("test"))
            {
                var parent = system.ActorOf(Props.Create(() => new ParentActor(done)));
                parent.Tell("foo");

                Task.WaitAll(done.Task);
            }
        }
    }
}
