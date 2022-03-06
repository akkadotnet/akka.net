//-----------------------------------------------------------------------
// <copyright file="SourceWithContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Annotations;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A source that provides operations which automatically propagate the context of an element.
    /// Only a subset of common operations from [[FlowOps]] is supported. As an escape hatch you can
    /// use [[FlowWithContextOps.via]] to manually provide the context propagation for otherwise unsupported
    /// operations.
    /// </summary>
    [ApiMayChange]
    public sealed class SourceWithContext<TOut, TCtx, TMat> : GraphDelegate<SourceShape<(TOut, TCtx)>, TMat>
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
        public SourceWithContext<TOut2, TCtx2, TMat> Via<TOut2, TCtx2, TMat2>(IGraph<FlowShape<(TOut, TCtx), (TOut2, TCtx2)>, TMat2> viaFlow) =>
            new SourceWithContext<TOut2, TCtx2, TMat>(Source.FromGraph(Inner).Via(viaFlow));

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
        public SourceWithContext<TOut2, TCtx2, TMat3> ViaMaterialized<TOut2, TCtx2, TMat2, TMat3>(
            IGraph<FlowShape<(TOut, TCtx), (TOut2, TCtx2)>, TMat2> viaFlow, Func<TMat, TMat2, TMat3> combine) =>
            new SourceWithContext<TOut2, TCtx2, TMat3>(Source.FromGraph(Inner).ViaMaterialized(viaFlow, combine));

        /// <summary>
        /// Connect this <see cref="SourceWithContext{TOut,TCtx,TMat}"/> to a Sink and run it. The returned value is the materialized value of the Sink.
        /// Note that the ActorSystem can be used as the implicit materializer parameter to use the SystemMaterializer for running the stream.
        /// </summary>
        /// <typeparam name="TMat2"></typeparam>
        /// <param name="sink"></param>
        /// <param name="materializer"></param>
        /// <returns></returns>
        public TMat2 RunWith<TMat2>(IGraph<SinkShape<(TOut, TCtx)>, TMat2> sink, IMaterializer materializer)
            => Source.FromGraph(Inner).RunWith(sink, materializer);

        ///<summary>
        ///Stops automatic context propagation from here and converts this to a regular
        ///stream of a pair of (data, context).
        ///</summary>
        public Source<(TOut, TCtx), TMat> AsSource() => Inner is Source<(TOut, TCtx), TMat> source ? source : Source.FromGraph(Inner);
    }
}
