//-----------------------------------------------------------------------
// <copyright file="Fusing.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;

namespace Akka.Streams
{
    ///<summary>
    /// This class holds some graph transformation functions that can fuse together
    /// multiple operation stages into synchronous execution islands. The purpose is
    /// to reduce the number of Actors that are created in order to execute the stream
    /// and thereby improve start-up cost as well as reduce element traversal latency
    /// for large graphs. Fusing itself is a time-consuming operation, meaning that
    /// usually it is best to cache the result of this computation and reuse it instead
    /// of fusing the same graph many times.
    ///
    /// Fusing together all operations which allow this treatment will reduce the
    /// parallelism that is available in the stream graph’s execution—in the worst case
    /// it will become single-threaded and not benefit from multiple CPU cores at all.
    /// Where parallelism is required, the <see cref="Attributes.AsyncBoundary"/>
    /// attribute can be used to declare subgraph boundaries across which the graph
    /// shall not be fused.
    ///</summary>
    public static class Fusing
    {
        /// <summary>
        /// Fuse all operations where this is technically possible (i.e. all
        /// implementations based on <see cref="GraphStage{TShape}"/>) and not forbidden
        /// via <see cref="Attributes.AsyncBoundary"/>
        /// </summary>
        /// <typeparam name="TShape">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static FusedGraph<TShape, TMat> Aggressive<TShape, TMat>(IGraph<TShape, TMat> graph) where TShape : Shape
            => Implementation.Fusing.Fusing.Aggressive(graph);

        /// <summary>
        /// A fused graph of the right shape, containing a <see cref="FusedModule"/> which holds more information 
        /// on the operation structure of the contained stream topology for convenient graph traversal.
        /// </summary>
        /// <typeparam name="TShape">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        public sealed class FusedGraph<TShape, TMat> : IGraph<TShape, TMat> where TShape : Shape
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="module">TBD</param>
            /// <param name="shape">TBD</param>
            /// <exception cref="ArgumentException">TBD</exception>
            public FusedGraph(FusedModule module, TShape shape)
            {
                if (module == null) throw new ArgumentNullException(nameof(module));
                if (shape == null) throw new ArgumentNullException(nameof(shape));

                Module = module;
                Shape = shape;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TShape Shape { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public IModule Module { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="attributes">TBD</param>
            /// <returns>TBD</returns>
            public IGraph<TShape, TMat> WithAttributes(Attributes attributes) => new FusedGraph<TShape, TMat>(Module.WithAttributes(attributes) as FusedModule, Shape);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="attributes">TBD</param>
            /// <returns>TBD</returns>
            public IGraph<TShape, TMat> AddAttributes(Attributes attributes) => WithAttributes(Module.Attributes.And(attributes));

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="name">TBD</param>
            /// <returns>TBD</returns>
            public IGraph<TShape, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public IGraph<TShape, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }

        /// <summary>
        /// When fusing a <see cref="IGraph{TShape}"/> a part of the internal stage wirings are hidden within
        ///<see cref="GraphAssembly"/> objects that are
        /// optimized for high-speed execution. This structural information bundle contains
        /// the wirings in a more accessible form, allowing traversal from port to upstream
        /// or downstream port and from there to the owning module (or graph vertex).
        /// </summary>
        public struct StructuralInfo
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableDictionary<InPort, OutPort> Upstreams;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableDictionary<OutPort, InPort> Downstreams;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableDictionary<InPort, IModule> InOwners;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableDictionary<OutPort, IModule> OutOwners;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<IModule> AllModules;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="upstreams">TBD</param>
            /// <param name="downstreams">TBD</param>
            /// <param name="inOwners">TBD</param>
            /// <param name="outOwners">TBD</param>
            /// <param name="allModules">TBD</param>
            public StructuralInfo(IImmutableDictionary<InPort, OutPort> upstreams, IImmutableDictionary<OutPort, InPort> downstreams, IImmutableDictionary<InPort, IModule> inOwners, IImmutableDictionary<OutPort, IModule> outOwners, IImmutableSet<IModule> allModules)
            {
                Upstreams = upstreams;
                Downstreams = downstreams;
                InOwners = inOwners;
                OutOwners = outOwners;
                AllModules = allModules;
            }
        }
    }
}