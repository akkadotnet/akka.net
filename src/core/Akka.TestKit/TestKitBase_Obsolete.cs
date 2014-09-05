using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Util;

namespace Akka.TestKit
{
    public abstract partial class TestKitBase
    {

        [Obsolete("Use TestActor instead. This member will be removed.")]
        protected ActorRef testActor { get { return TestActor; } }


        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected object expectMsg(object expected)
        {
            return ExpectMsg(expected);
        }

        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected object expectMsg(object expected, TimeSpan timeout)
        {
            return ExpectMsg(expected, timeout);
        }

        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected object expectMsg(object expected, Func<object, object, bool> comparer)
        {
            return ExpectMsg(expected, comparer);
        }


        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected object expectMsg(object expected, Func<object, object, bool> comparer, TimeSpan timeout)
        {
            return ExpectMsg(expected, timeout, comparer);
        }

        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected TMessage expectMsgType<TMessage>()
        {
            return ExpectMsg<TMessage>();
        }
        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected TMessage expectMsgType<TMessage>(TimeSpan timeout)
        {
            return ExpectMsg<TMessage>(timeout);
        }

        [Obsolete("Use ExpectTerminated instead. This member will be removed.")]
        protected Terminated expectTerminated(ActorRef @ref)
        {
            return ExpectTerminated(@ref);

        }
        [Obsolete("Use ExpectTerminated instead. This member will be removed.")]
        protected Terminated expectTerminated(ActorRef @ref, TimeSpan timeout)
        {
            return ExpectTerminated(@ref, timeout);
        }

        [Obsolete("Use Sys instead. This member will be removed.")]
        protected ActorSystem sys { get { return Sys; } }

        [Obsolete("Use Watch instead. This member will be removed.")]
        protected void watch(ActorRef @ref)
        {
            Watch(@ref);
        }

        [Obsolete("Use ExpectMsg instead. This member will be removed.")]
        protected TMessage ExpectMsgPF<TMessage>(TimeSpan timeout, Predicate<TMessage> isMessage, string hint = "")
        {
            return ExpectMsg<TMessage>(timeout, isMessage, hint);
        }

        [Obsolete("Use ExpectNoMsg instead. This member will be removed.")]
        protected void expectNoMsg(TimeSpan duration)
        {
            ExpectNoMsg(duration);
        }

        [Obsolete("Use ReceiveOne instead. This member will be removed.")]
        protected object receiveOne(TimeSpan duration)
        {
            return ReceiveOne(duration);
        }

        [Obsolete("Use ReceiveWhile instead. This member will be removed.")]
        protected IList<T> receiveWhile<T>(TimeSpan max, Func<object, T> filter, int msgs = int.MaxValue) where T: class 
        {
            return ReceiveWhile<T>(max, filter, msgs).ToList();
        }

        [Obsolete("Use ReceiveWhile instead. This member will be removed.")]
        protected IList<T> receiveWhile<T>(TimeSpan max, TimeSpan idle, Func<object, T> filter, int msgs = int.MaxValue) where T : class
        {
            return ReceiveWhile<T>(max, filter, msgs).ToList();
        }

        [Obsolete("Use AwaitCondition and remove Wait(). This member will be removed.")]
        protected Task AwaitCond(Func<bool> conditionIsFulfilled, TimeSpan max)
        {
            AwaitCondition(conditionIsFulfilled, max);
            return Task.FromResult(false);
        }
   }
}