//-----------------------------------------------------------------------
// <copyright file="GraphImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Implementation;
using Akka.Streams.Util;

namespace Akka.Streams.Dsl.Internal
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class GraphImpl<TShape, TMat> : IGraph<TShape, TMat> where TShape : Shape
    {
        public GraphImpl(TShape shape, IModule module)
        {
            Shape = shape;
            Module = module;
        }

        public TShape Shape { get; }

        public IModule Module { get; }

        public IGraph<TShape, TMat> WithAttributes(Attributes attributes) => new GraphImpl<TShape, TMat>(Shape, Module.WithAttributes(attributes));

        public IGraph<TShape, TMat> AddAttributes(Attributes attributes) => WithAttributes(Module.Attributes.And(attributes));

        public IGraph<TShape, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

        public IGraph<TShape, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        public override string ToString() => $"Graph({Shape}, {Module})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public static class ModuleExtractor
    {
        public static Option<IModule> Unapply<TShape, TMat>(IGraph<TShape, TMat> graph) where TShape : Shape
        {
            var module = graph as IModule;
            return module != null ? new Option<IModule>(module) : Option<IModule>.None;
        }
    }
}