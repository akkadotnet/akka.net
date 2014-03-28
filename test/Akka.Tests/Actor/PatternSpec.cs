using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Tests.Event;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class PatternSpec : AkkaSpec
    {
        [TestMethod]
        public void GracefulStop_must_provide_Task_for_stopping_an_actor()
        {
            //arrange
            var target = sys.ActorOf<TargetActor>();

            //act
            var result = target.GracefulStop(TimeSpan.FromSeconds(5));
            result.Wait(TimeSpan.FromSeconds(6));

            //assert
            Assert.IsTrue(result.Result);

        }

        [TestMethod]
        public async Task GracefulStop_must_complete_Task_when_actor_already_terminated()
        {
            //arrange
            var target = sys.ActorOf<TargetActor>();

            //act
            

            //assert
            Assert.IsTrue(await target.GracefulStop(TimeSpan.FromSeconds(5)));
            Assert.IsTrue(await target.GracefulStop(TimeSpan.FromSeconds(5)));
        }

        [TestMethod]
        public void GracefulStop_must_complete_Task_with_TaskCanceledException_when_actor_not_terminated_within_timeout()
        {
            //arrange
            var target = sys.ActorOf<TargetActor>();
            var latch = new TestLatch(sys);

            //act
            target.Tell(Tuple.Create(latch, TimeSpan.FromSeconds(2)));

            //assert
            intercept<TaskCanceledException>(() =>
            {
                var task = target.GracefulStop(TimeSpan.FromMilliseconds(500));
                task.Wait();
                var result = task.Result;
            });
            latch.Open();

        }

        #region Actors

        public sealed class Work
        {
            public Work(TimeSpan duration)
            {
                Duration = duration;
            }

            public TimeSpan Duration { get; private set; }
        }

        public class TargetActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                PatternMatch.Match(message)
                    .With<Tuple<TestLatch, TimeSpan>>(t => t.Item1.Ready(t.Item2));
            }
        }

        #endregion
    }
}
