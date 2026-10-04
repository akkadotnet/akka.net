//-----------------------------------------------------------------------
// <copyright file="GraphImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Annotations;
using Akka.Streams.Implementation;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Dsl.Internal
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TShape">The type of the graph's input and output ports.</typeparam>
    /// <typeparam name="TMat">The type of the value produced when the graph is materialized.</typeparam>
    [InternalApi]
    public class GraphImpl<TShape, TMat> : IGraph<TShape, TMat> where TShape : Shape
    {
        /// <summary>
        /// Creates a graph implementation from its shape and module.
        /// </summary>
        /// <param name="shape">The graph shape describing its ports.</param>
        /// <param name="module">The module that defines the graph's structure and attributes.</param>
        public GraphImpl(TShape shape, IModule module)
        {
            Shape = shape;
            Module = module;
        }

        /// <summary>
        /// The graph shape describing this graph's ports.
        /// </summary>
        public TShape Shape { get; }

        /// <summary>
        /// The module that defines this graph's structure and attributes.
        /// </summary>
        public IModule Module { get; }

        /// <summary>
        /// Returns a graph with the supplied attributes replacing the module's current attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply to the graph module.</param>
        /// <returns>A graph with the same shape and a module carrying the supplied attributes.</returns>
        public IGraph<TShape, TMat> WithAttributes(Attributes attributes) => new GraphImpl<TShape, TMat>(Shape, Module.WithAttributes(attributes));

        /// <summary>
        /// Returns a graph with the supplied attributes combined with its current module attributes.
        /// </summary>
        /// <param name="attributes">The attributes to add to the graph module.</param>
        /// <returns>A graph with the same shape and the combined attributes.</returns>
        public IGraph<TShape, TMat> AddAttributes(Attributes attributes) => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// Returns a graph with the supplied name attribute added.
        /// </summary>
        /// <param name="name">The name to assign to the graph.</param>
        /// <returns>A graph with the same shape and the added name attribute.</returns>
        public IGraph<TShape, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// Adds an asynchronous boundary to this graph.
        /// </summary>
        /// <returns>A graph with the same shape and an asynchronous boundary attribute.</returns>
        public IGraph<TShape, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        /// <summary>
        /// Returns a string representation containing this graph's shape and module.
        /// </summary>
        /// <returns>The shape and module formatted as a graph description.</returns>
        public override string ToString() => $"Graph({Shape}, {Module})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public static class ModuleExtractor
    {
        /// <summary>
        /// Extracts a graph module when the graph object implements <see cref="IModule"/>.
        /// </summary>
        /// <typeparam name="TShape">The type of the graph's input and output ports.</typeparam>
        /// <typeparam name="TMat">The type of the value produced when the graph is materialized.</typeparam>
        /// <param name="graph">The graph whose module should be extracted.</param>
        /// <returns>The graph module if <paramref name="graph"/> implements <see cref="IModule"/>; otherwise, <see cref="Option{T}.None"/>.</returns>
        public static Option<IModule> Unapply<TShape, TMat>(IGraph<TShape, TMat> graph) where TShape : Shape
        {
            var module = graph as IModule;
            return module != null ? Option<IModule>.Create(module) : Option<IModule>.None;
        }
    }
}
