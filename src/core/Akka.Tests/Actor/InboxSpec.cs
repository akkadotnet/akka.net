using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    public class InboxSpec : AkkaSpec
    {
        private Inbox _inbox;
        public InboxSpec()
        {
            _inbox = Inbox.Create(sys);
        }

        [Fact]
        public void Inbox_support_watch()
        {
            _inbox.Watch(testActor);

            // check watch
            testActor.Tell(PoisonPill.Instance);
            var received = _inbox.Receive(TimeSpan.FromSeconds(1));

            received.GetType().ShouldBe(typeof(Terminated));
            var terminated = (Terminated)received;
            terminated.ActorRef.ShouldBe(testActor);
        }

        [Fact]
        public void Inbox_support_queueing_multiple_queries()
        {
            var tasks = new[]
                {
                    Task.Factory.StartNew(() => _inbox.Receive()),
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(100);
                        return _inbox.ReceiveWhere(x => x.ToString() == "world"); 
                    }), 
                    Task.Factory.StartNew(() =>
                    {
                        Thread.Sleep(200);
                        return _inbox.ReceiveWhere(x => x.ToString() == "hello"); 
                    }) 
                };

            _inbox.Receiver.Tell(42);
            _inbox.Receiver.Tell("hello");
            _inbox.Receiver.Tell("world");

            Task.WaitAll(tasks);

            tasks[0].Result.ShouldBe(42);
            tasks[1].Result.ShouldBe("world");
            tasks[2].Result.ShouldBe("hello");
        }

        [Fact]
        public void Inbox_support_selective_receives()
        {
            _inbox.Receiver.Tell("hello");
            _inbox.Receiver.Tell("world");

            var selection = _inbox.ReceiveWhere(x => x.ToString() == "world");       
            selection.ShouldBe("world");
            _inbox.Receive().ShouldBe("hello");
        }

        [Fact]
        public void Inbox_have_maximum_queue_size()
        {
            sys.EventStream.Subscribe(testActor, typeof(Warning));
            try
            {
                foreach (var zero in Enumerable.Repeat(0, 1000))
                    _inbox.Receiver.Tell(zero);

                expectNoMsg(TimeSpan.FromSeconds(1));
                EventFilterLog<Warning>("dropping message", 1, () => _inbox.Receiver.Tell(42));
                _inbox.Receiver.Tell(42);
                expectNoMsg(TimeSpan.FromSeconds(1));

                var gotit = Enumerable.Repeat(0, 1000).Select(_ => _inbox.Receive());
                foreach (var o in gotit)
                {
                    o.ShouldBe(0);
                }

                intercept<TimeoutException>(() => _inbox.Receive(TimeSpan.FromSeconds(1)));
            }
            finally
            {
                sys.EventStream.Unsubscribe(testActor, typeof(Warning));
            }
        }


        [Fact]
        public void Inbox_have_a_default_and_custom_timeouts()
        {
            Within(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6), () =>
            {
                intercept<TimeoutException>(() => _inbox.Receive());
                return true;
            });
            Within(TimeSpan.FromSeconds(1), () =>
            {
                intercept<TimeoutException>(() => _inbox.Receive(TimeSpan.FromMilliseconds(100)));
                return true;
            });
        }
    }
}