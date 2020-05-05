//-----------------------------------------------------------------------
// <copyright file="GraphImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <typeparam name="TShape">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    [InternalApi]
    public class GraphImpl<TShape, TMat> : IGraph<TShape, TMat> where TShape : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <param name="module">TBD</param>
        public GraphImpl(TShape shape, IModule module)
        {
            Shape = shape;
            Module = module;
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
        public IGraph<TShape, TMat> WithAttributes(Attributes attributes) => new GraphImpl<TShape, TMat>(Shape, Module.WithAttributes(attributes));

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

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Graph({Shape}, {Module})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public static class ModuleExtractor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TShape">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static Option<IModule> Unapply<TShape, TMat>(IGraph<TShape, TMat> graph) where TShape : Shape
        {
            var module = graph as IModule;
            return module != null ? new Option<IModule>(module) : Option<IModule>.None;
        }
    }
}
