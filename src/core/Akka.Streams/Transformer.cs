//-----------------------------------------------------------------------
// <copyright file="Transformer.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Streams
{
    public interface ITransformerLike<in TIn, out TOut>
    {
        /**
         * Invoked after handing off the elements produced from one input element to the
         * downstream subscribers to determine whether to end stream processing at this point;
         * in that case the upstream subscription is canceled.
         */
        bool IsComplete { get; }

        /**
         * Invoked for each element to produce a (possibly empty) sequence of
         * output elements.
         */
        IEnumerable<TOut> OnNext(TIn element);

        /**
         * Invoked before the Transformer terminates (either normal completion or after an onError)
         * to produce a (possibly empty) sequence of elements in response to the
         * end-of-stream event.
         *
         * This method is only called if [[#onError]] does not throw an exception. The default implementation
         * of [[#onError]] throws the received cause forcing the failure to propagate downstream immediately.
         *
         * @param e Contains a non-empty option with the error causing the termination or an empty option
         *          if the Transformer was completed normally
         */
        IEnumerable<TOut> OnTermination(Exception cause);

        /**
         * Invoked when failure is signaled from upstream. If this method throws an exception, then onError is immediately
         * propagated downstream. If this method completes normally then [[#onTermination]] is invoked as a final
         * step, passing the original cause.
         */
        void OnError(Exception cause);

        /**
         * Invoked after normal completion or failure.
         */
        void Cleanup();
    }

    public abstract class TransformerLikeBase<TIn, TOut> : ITransformerLike<TIn, TOut>
    {
        /**
         * Invoked for each element to produce a (possibly empty) sequence of
         * output elements.
         */
        public abstract IEnumerable<TOut> OnNext(TIn element);

        /**
         * Invoked after handing off the elements produced from one input element to the
         * downstream subscribers to determine whether to end stream processing at this point;
         * in that case the upstream subscription is canceled.
         */
        public virtual bool IsComplete { get { return false; } }

        /**
         * Invoked before the Transformer terminates (either normal completion or after an onError)
         * to produce a (possibly empty) sequence of elements in response to the
         * end-of-stream event.
         *
         * This method is only called if [[#onError]] does not throw an exception. The default implementation
         * of [[#onError]] throws the received cause forcing the failure to propagate downstream immediately.
         *
         * @param e Contains a non-empty option with the error causing the termination or an empty option
         *          if the Transformer was completed normally
         */
        public virtual IEnumerable<TOut> OnTermination(Exception cause)
        {
            return Enumerable.Empty<TOut>();
        }

        /**
         * Invoked when failure is signaled from upstream. If this method throws an exception, then onError is immediately
         * propagated downstream. If this method completes normally then [[#onTermination]] is invoked as a final
         * step, passing the original cause.
         */
        public virtual void OnError(Exception cause)
        {
            throw cause;
        }

        /**
         * Invoked after normal completion or failure.
         */
        public virtual void Cleanup() { }
    }
}