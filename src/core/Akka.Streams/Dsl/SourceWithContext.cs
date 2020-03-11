//-----------------------------------------------------------------------
// <copyright file="SourceWithContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A source that provides operations which automatically propagate the context of an element.
    /// Only a subset of common operations from [[FlowOps]] is supported. As an escape hatch you can
    /// use [[FlowWithContextOps.via]] to manually provide the context propagation for otherwise unsupported
    /// operations.
    /// 
    /// Can be created by calling <see cref="Source{TOut,TMat}.StartContextPropagation"/>
    /// 
    /// API MAY CHANGE
    /// </summary>
    public sealed class SourceWithContext<TCtx, TOut, TMat> : GraphDelegate<SourceShape<(TOut, TCtx)>, TMat>
    {
        public SourceWithContext(Source<(TOut, TCtx), TMat> source)
        : base(source)
        {
        }
        
        
        ///<summary>
        /// Transform this flow by the regular flow. The given flow must support manual context propagation by
        /// taking and producing tuples of (data, context).
        /// 
        /// This can be used as an escape hatch for operations that are not (yet) provided with automatic
        /// context propagation here.
        ///</summary>
        public SourceWithContext<TCtx2, TOut2, TMat> Via<TCtx2, TOut2, TMat2>(
            IGraph<FlowShape<(TOut, TCtx), (TOut2, TCtx2)>, TMat2> viaFlow) =>
            new SourceWithContext<TCtx2, TOut2, TMat>(Source.FromGraph(Inner).Via(viaFlow));
        
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
        public SourceWithContext<TCtx2, TOut2, TMat3> ViaMaterialized<TCtx2, TOut2, TMat2, TMat3>(
            IGraph<FlowShape<(TOut, TCtx), (TOut2, TCtx2)>, TMat2> viaFlow, Func<TMat, TMat2, TMat3> combine) =>
            new SourceWithContext<TCtx2, TOut2, TMat3>(Source.FromGraph(Inner).ViaMaterialized(viaFlow, combine));

        
        ///<summary>
        ///Stops automatic context propagation from here and converts this to a regular
        ///stream of a pair of (data, context).
        ///</summary>
        public Source<(TOut, TCtx), TMat> AsSource() => 
            Inner is Source<(TOut, TCtx), TMat>  source ? source : Source.FromGraph(Inner);
    }
}
