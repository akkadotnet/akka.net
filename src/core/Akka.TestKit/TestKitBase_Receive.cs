using System;
using System.Collections.Generic;
using System.Threading;
using Akka.TestKit.Internals;

namespace Akka.TestKit
{
    public abstract partial class TestKitBase
    {
        //TODO: Implement fishForMessage
        // /**
        //  * Hybrid of expectMsgPF and receiveWhile: receive messages while the
        //  * partial function matches and returns false. Use it to ignore certain
        //  * messages while waiting for a specific message.
        //  *
        //  * @return the last received messsage, i.e. the first one for which the
        //  *         partial function returned true
        //  */
        // def fishForMessage(max: Duration = Duration.Undefined, hint: String = "")(f: PartialFunction[Any, Boolean]): Any = {
        //   val _max = remainingOrDilated(max)
        //   val end = now + _max
        //   @tailrec
        //   def recv: Any = {
        //     val o = receiveOne(end - now)
        //     assert(o ne null, s"timeout (${_max}) during fishForMessage, hint: $hint")
        //     assert(f.isDefinedAt(o), s"fishForMessage($hint) found unexpected message $o")
        //     if (f(o)) o else recv
        //   }
        //   recv
        // }



        /// <summary>
        /// Receive one message from the internal queue of the TestActor.
        /// This method blocks the specified duration or until a message
        /// is received. If no message was received, <c>null</c> is returned.
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="max">The maximum duration to wait. 
        /// If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        /// If set to a negative value or <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever.
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks></param>
        /// <returns>The message if one was received; <c>null</c> otherwise</returns>
        public object ReceiveOne(TimeSpan? max=null)
        {
            MessageEnvelope envelope;
            if(TryReceiveOne(out envelope, max, CancellationToken.None))
                return envelope.Message;
            return null;
        }

        /// <summary>
        /// Receive one message from the internal queue of the TestActor.
        /// This method blocks until cancelled. 
        /// </summary>
        /// <param name="cancellationToken">A token used to cancel the operation</param>
        /// <returns>The message if one was received; <c>null</c> otherwise</returns>
        public object ReceiveOne(CancellationToken cancellationToken)
        {
            MessageEnvelope envelope;
            if(TryReceiveOne(out envelope, Timeout.InfiniteTimeSpan, cancellationToken))
                return envelope.Message;
            return null;
        }


        /// <summary>
        /// Receive one message from the internal queue of the TestActor within 
        /// the specified duration. The method blocks the specified duration.
        /// <remarks><b>Note!</b> that the returned <paramref name="envelope"/> 
        /// is a <see cref="MessageEnvelope"/> containing the sender and the message.</remarks>
        /// <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="envelope">The received envelope.</param>
        /// <param name="max">Optional: The maximum duration to wait. 
        ///     If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        ///     If set to a negative value or <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever.
        ///     <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks></param>
        /// <returns><c>True</c> if a message was received within the specified duration; <c>false</c> otherwise.</returns>
        public bool TryReceiveOne(out MessageEnvelope envelope, TimeSpan? max=null)
        {
            return TryReceiveOne(out envelope, max, CancellationToken.None);
        }

        /// <summary>
        /// Receive one message from the internal queue of the TestActor within 
        /// the specified duration.
        /// <para><c>True</c> is returned if a message existed, and the message 
        /// is returned in <paramref name="envelope" />. The method blocks the 
        /// specified duration, and can be cancelled using the 
        /// <paramref name="cancellationToken" />.
        /// </para> 
        /// <remarks>This method does NOT automatically scale its duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </summary>
        /// <param name="envelope">The received envelope.</param>
        /// <param name="max">The maximum duration to wait. 
        ///     If <c>null</c> the config value "akka.test.single-expect-default" is used as timeout.
        ///     If set to a negative value or <see cref="Timeout.InfiniteTimeSpan"/>, blocks forever (or until cancelled).
        ///     <remarks>This method does NOT automatically scale its Duration parameter using <see cref="Dilated(TimeSpan)" />!</remarks>
        /// </param>
        /// <param name="cancellationToken">A token used to cancel the operation.</param>
        /// <returns><c>True</c> if a message was received within the specified duration; <c>false</c> otherwise.</returns>
        public bool TryReceiveOne(out MessageEnvelope envelope, TimeSpan? max, CancellationToken cancellationToken)
        {
            bool didTake;
            var maxDuration = GetTimeoutOrDefault(max);
            if(maxDuration.IsZero())
            {
                didTake = _queue.TryTake(out envelope);
            }
            else if(maxDuration.IsPositiveFinite())
            {
                didTake = _queue.TryTake(out envelope, (int)maxDuration.TotalMilliseconds, cancellationToken);
            }
            else  //Infinite
            {
                envelope = _queue.Take(cancellationToken);
                didTake = true;
            }

            _lastWasNoMsg = false;
            if(didTake)
            {
                _lastMessage = envelope;
                return true;
            }
            envelope = NullMessageEnvelope.Instance;
            _lastMessage = envelope;
            return false;
        }



        /// <summary>
        /// Receive a series of messages until the function returns null or the overall
        /// maximum duration is elapsed or expected messages count is reached.
        /// Returns the sequence of messages.
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        public IReadOnlyList<T> ReceiveWhile<T>(TimeSpan? max, Func<object, T> filter, int msgs = int.MaxValue)
        {
            return ReceiveWhile(filter, max, Timeout.InfiniteTimeSpan, msgs);
        }

        /// <summary>
        /// Receive a series of messages until the function returns null or the idle 
        /// timeout is met or the overall maximum duration is elapsed or 
        /// expected messages count is reached.
        /// Returns the sequence of messages.
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        public IReadOnlyList<T> ReceiveWhile<T>(TimeSpan? max, TimeSpan? idle, Func<object, T> filter, int msgs = int.MaxValue)
        {
            return ReceiveWhile(filter, max, idle, msgs);
        }

        /// <summary>
        /// Receive a series of messages until the function returns null or the idle 
        /// timeout is met (disabled by default) or the overall
        /// maximum duration is elapsed or expected messages count is reached.
        /// Returns the sequence of messages.
        /// 
        /// Note that it is not an error to hit the `max` duration in this case.
        /// The max duration is scaled by <see cref="Dilated(TimeSpan)"/>
        /// </summary>
        public IReadOnlyList<T> ReceiveWhile<T>(Func<object, T> filter, TimeSpan? max = null, TimeSpan? idle = null, int msgs = int.MaxValue)
        {      
            var stop = Now + RemainingOrDilated(max);

            var count = 0;
            var acc = new List<T>();
            var idleValue = idle.GetValueOrDefault(Timeout.InfiniteTimeSpan);
            MessageEnvelope msg = NullMessageEnvelope.Instance;
            while(count < msgs)
            {
                MessageEnvelope envelope;
                if(!TryReceiveOne(out envelope, (stop - Now).Min(idleValue)))
                {
                    _lastMessage = msg;
                    break;
                }
                var message = envelope.Message;
                var result = filter(message);
                if(result == null)
                {
                    //TODO: We use a BlockingQueue that do not allow AddFirst. _queue.AddFirst(message);  //Put the message back in the queue
                    _lastMessage = msg;
                    break;
                }
                msg = envelope;
                acc.Add(result);
                count++;
            }

            _lastWasNoMsg = true;
            return acc;
        }
        

        //TODO: Implement receiveN, make it public
        //   /**
        //   * Same as `receiveN(n, remaining)` but correctly taking into account
        //   * Duration.timeFactor.
        //   */
        //  def receiveN(n: Int): immutable.Seq[AnyRef] = receiveN_internal(n, remainingOrDefault)
  
        //  /**
        //   * Receive N messages in a row before the given deadline.
        //   */
        //  def receiveN(n: Int, max: FiniteDuration): immutable.Seq[AnyRef] = receiveN_internal(n, max.dilated)
  
        //  private def receiveN_internal(n: Int, max: Duration): immutable.Seq[AnyRef] = {
        //    val stop = max + now
        //    for { x ← 1 to n } yield {
        //      val timeout = stop - now
        //      val o = receiveOne(timeout)
        //      assert(o ne null, s"timeout ($max) while expecting $n messages (got ${x - 1})")
        //      o
        //    }
        //  }

    }
}