//-----------------------------------------------------------------------
// <copyright file="Transformer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public interface ITransformerLike<in TIn, out TOut>
    {
        /// <summary>
        /// Invoked after handing off the elements produced from one input element to the
        /// downstream subscribers to determine whether to end stream processing at this point;
        /// in that case the upstream subscription is canceled.
        /// </summary>
        bool IsComplete { get; }

        /// <summary>
        /// Invoked for each element to produce a (possibly empty) sequence of
        /// output elements.
        /// </summary>
        /// <param name="element">TBD</param>
        IEnumerable<TOut> OnNext(TIn element);
        
        /// <summary>
        /// Invoked before the Transformer terminates (either normal completion or after an onError)
        /// to produce a (possibly empty) sequence of elements in response to the
        /// end-of-stream event.
        /// 
        /// This method is only called if <see cref="OnError"/> does not throw an exception. The default implementation
        /// of <see cref="OnError"/> throws the received cause forcing the failure to propagate downstream immediately.
        /// </summary>
        /// <param name="cause">Contains a non-empty option with the error causing the termination or an empty option
        ///                     if the Transformer was completed normally</param>
        IEnumerable<TOut> OnTermination(Exception cause);
        
        /// <summary>
        /// Invoked when failure is signaled from upstream. If this method throws an exception, then onError is immediately
        /// propagated downstream. If this method completes normally then <see cref="OnTermination"/> is invoked as a final
        /// step, passing the original cause.
        /// </summary>
        /// <param name="cause">TBD</param>
        void OnError(Exception cause);
        
        /// <summary>
        /// Invoked after normal completion or failure.
        /// </summary>
        void Cleanup();
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public abstract class TransformerLikeBase<TIn, TOut> : ITransformerLike<TIn, TOut>
    {
        /// <summary>
        /// Invoked for each element to produce a (possibly empty) sequence of
        /// output elements.
        /// </summary>
        /// <param name="element">TBD</param>
        public abstract IEnumerable<TOut> OnNext(TIn element);

        /// <summary>
        /// Invoked after handing off the elements produced from one input element to the
        /// downstream subscribers to determine whether to end stream processing at this point;
        /// in that case the upstream subscription is canceled.
        /// </summary>
        public virtual bool IsComplete => false;

        /// <summary>
        /// Invoked before the Transformer terminates (either normal completion or after an onError)
        /// to produce a (possibly empty) sequence of elements in response to the
        /// end-of-stream event.
        /// 
        /// This method is only called if <see cref="OnError"/> does not throw an exception. The default implementation
        /// of <see cref="OnError"/> throws the received cause forcing the failure to propagate downstream immediately.
        /// </summary>
        /// <param name="cause">Contains a non-empty option with the error causing the termination or an empty option
        ///                     if the Transformer was completed normally</param>
        public virtual IEnumerable<TOut> OnTermination(Exception cause) => Enumerable.Empty<TOut>();


        /// <summary>
        /// Invoked when failure is signaled from upstream. If this method throws an exception, then onError is immediately
        /// propagated downstream. If this method completes normally then <see cref="OnTermination"/> is invoked as a final
        /// step, passing the original cause.
        /// </summary>
        /// <param name="cause">TBD</param>
        public virtual void OnError(Exception cause)
        {
            throw cause;
        }

        /// <summary>
        /// Invoked after normal completion or failure.
        /// </summary>
        public virtual void Cleanup() { }
    }
}
