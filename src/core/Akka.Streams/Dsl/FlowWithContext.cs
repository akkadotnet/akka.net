//-----------------------------------------------------------------------
// <copyright file="FlowWithContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;

namespace Akka.Streams.Dsl
{
    ///<summary>
    /// A flow that provides operations which automatically propagate the context of an element.
    /// Only a subset of common operations from Flow is supported. As an escape hatch you can
    /// use [[FlowWithContextOps.via]] to manually provide the context propagation for otherwise unsupported
    /// operations.
    /// 
    /// An "empty" flow can be created by calling <see cref="FlowWithContext.Create{TCtx,TIn}"/>.
    /// 
    /// API MAY CHANGE
    ///</summary> 
    public sealed class FlowWithContext<TCtxIn, TIn, TCtxOut, TOut, TMat>
        : GraphDelegate<FlowShape<(TIn, TCtxIn), (TOut, TCtxOut)>, TMat>
    {
        internal FlowWithContext(Flow<(TIn, TCtxIn), (TOut, TCtxOut), TMat> flow) 
            : base(flow)
        {
        }
        
        ///<summary>
        /// Transform this flow by the regular flow. The given flow must support manual context propagation by
        /// taking and producing tuples of (data, context).
        /// 
        /// This can be used as an escape hatch for operations that are not (yet) provided with automatic
        /// context propagation here.
        ///</summary>
        public FlowWithContext<TCtxIn, TIn, TCtx2, TOut2, TMat> Via<TCtx2, TOut2, TMat2>(
            IGraph<FlowShape<(TOut, TCtxOut), (TOut2, TCtx2)>, TMat2> viaFlow) =>
            FlowWithContext.From(Flow.FromGraph(Inner).Via(viaFlow));
        
        ///<summary>
        /// Transform this flow by the regular flow. The given flow must support manual context propagation by
        /// taking and producing tuples of (data, context).
        /// 
        /// This can be used as an escape hatch for operations that are not (yet) provided with automatic
        /// context propagation here.
        /// 
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        ///</summary>
        public FlowWithContext<TCtxIn, TIn, TCtx2, TOut2, TMat3> ViaMaterialized<TCtx2, TOut2, TMat2, TMat3>(
            IGraph<FlowShape<(TOut, TCtxOut), (TOut2, TCtx2)>, TMat2> viaFlow, Func<TMat, TMat2, TMat3> combine) =>
            FlowWithContext.From(Flow.FromGraph(Inner).ViaMaterialized(viaFlow, combine));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Flow<(TIn, TCtxIn), (TOut, TCtxOut), TMat> AsFlow() => Flow.FromGraph(Inner);
    }

    public static class FlowWithContext
    {
        /// <summary>
        /// Creates an "empty" <see cref="FlowWithContext{TCtxIn,TIn,TCtxOut,TOut,TMat}"/> that passes elements through with their context unchanged.
        /// </summary>
        /// <typeparam name="TCtx"></typeparam>
        /// <typeparam name="TIn"></typeparam>
        /// <returns></returns>
        public static FlowWithContext<TCtx, TIn, TCtx, TIn, NotUsed> Create<TCtx, TIn>()
        {
            var under = Flow.Create<(TIn, TCtx), NotUsed>();
            return new FlowWithContext<TCtx, TIn, TCtx, TIn, NotUsed>(under);
        }
        
        /// <summary>
        /// Creates a FlowWithContext from a regular flow that operates on a pair of `(data, context)` elements.
        /// </summary>
        /// <param name="flow"></param>
        /// <typeparam name="TCtxIn"></typeparam>
        /// <typeparam name="TIn"></typeparam>
        /// <typeparam name="TCtxOut"></typeparam>
        /// <typeparam name="TOut"></typeparam>
        /// <typeparam name="TMat"></typeparam>
        /// <returns></returns>
        public static FlowWithContext<TCtxIn, TIn, TCtxOut, TOut, TMat> From<TCtxIn, TIn, TCtxOut, TOut, TMat>(
            Flow<(TIn, TCtxIn), (TOut, TCtxOut), TMat> flow) => 
            new FlowWithContext<TCtxIn, TIn, TCtxOut, TOut, TMat>(flow);
    }
}
