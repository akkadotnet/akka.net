//-----------------------------------------------------------------------
// <copyright file="GraphApply.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Dsl.Internal;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A graph DSL, which defines an API for building complex graphs. Graph definitions 
    /// are enclosed within a scope of functions defined by user, using a dedicated
    /// <see cref="Builder{T}"/> helper to connect specific graph stages with each other.
    /// </summary>
    public partial class GraphDsl
    {
        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by passing a <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShape">Type describing shape of the returned graph.</typeparam>
        /// <param name="buildBlock">A builder function used to construct the graph.</param>
        /// <returns>A graph with no materialized value.</returns>
        public static IGraph<TShape, NotUsed> Create<TShape>(Func<Builder<NotUsed>, TShape> buildBlock)
            where TShape : Shape
            => CreateMaterialized<TShape, NotUsed>(buildBlock);

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by passing a <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShape">Shape of the produced graph.</typeparam>
        /// <typeparam name="TMat">A type of value, that graph will materialize to after completion.</typeparam>
        /// <param name="buildBlock">Graph construction function.</param>
        /// <returns>A graph with materialized value.</returns>
        public static IGraph<TShape, TMat> CreateMaterialized<TShape, TMat>(Func<Builder<TMat>, TShape> buildBlock) where TShape : Shape
        {
            var builder = new Builder<TMat>();
            var shape = buildBlock(builder);
            var module = builder.Module.ReplaceShape(shape);

            return new GraphImpl<TShape, TMat>(shape, module);
        }

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graph <paramref name="g1"/> 
        /// and passing its <see cref="Shape"/> along with the <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShapeOut">A type describing shape of the returned graph.</typeparam>
        /// <typeparam name="TMat">A type of value, that graph will materialize to after completion.</typeparam>
        /// <typeparam name="TShape1">A type of shape of the input graph.</typeparam>
        /// <param name="g1">Graph used as input parameter.</param>
        /// <param name="buildBlock">Graph construction function.</param>
        /// <returns>A graph with materialized value.</returns>
        public static IGraph<TShapeOut, TMat> Create<TShapeOut, TMat, TShape1>(IGraph<TShape1, TMat> g1, Func<Builder<TMat>, TShape1, TShapeOut> buildBlock) 
            where TShapeOut: Shape
            where TShape1: Shape
        {
            var builder = new Builder<TMat>();
            var shape1 = builder.Add<TShape1, object, TMat, TMat>(g1, Keep.Right);
            var shape = buildBlock(builder, shape1);
            var module = builder.Module.ReplaceShape(shape);

            return new GraphImpl<TShapeOut, TMat>(shape, module);
        }

        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
        /// along with the <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShapeOut">A type describing shape of the returned graph.</typeparam>
        /// <typeparam name="TMatOut">A type of value, that graph will materialize to after completion.</typeparam>
        /// <typeparam name="TMat0">A type of materialized value of the first input graph parameter.</typeparam>
        /// <typeparam name="TMat1">A type of materialized value of the second input graph parameter.</typeparam>
        /// <typeparam name="TShape0">A type describing the shape of a first input graph parameter.</typeparam>
        /// <typeparam name="TShape1">A type describing the shape of a second input graph parameter.</typeparam>
        /// <param name="g0">A first input graph.</param>
        /// <param name="g1">A second input graph.</param>
        /// <param name="combineMaterializers">Function used to determine output materialized value based on the materialized values of the passed graphs.</param>
        /// <param name="buildBlock">A graph constructor function.</param>
        /// <returns>A graph with materialized value.</returns>
        public static IGraph<TShapeOut, TMatOut> Create<TShapeOut, TMatOut, TMat0, TMat1, TShape0, TShape1>(
            IGraph<TShape0, TMat0> g0, IGraph<TShape1, TMat1> g1, 
            Func<TMat0, TMat1, TMatOut> combineMaterializers,
            Func<Builder<TMatOut>, TShape0, TShape1, TShapeOut> buildBlock) 
            where TShapeOut: Shape
            where TShape0: Shape
            where TShape1: Shape
        {
            var builder = new Builder<TMatOut>();
            
            var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, TMatOut>>(g0, m0 => (m1 => combineMaterializers(m0, m1)));
            var shape1 = builder.Add<TShape1, Func<TMat1, TMatOut>, TMat1, TMatOut>(g1, (f, m1) => f(m1));

            var shape = buildBlock(builder, shape0, shape1);
            var module = builder.Module.ReplaceShape(shape);

            return new GraphImpl<TShapeOut, TMatOut>(shape, module);
        }
        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
        /// along with the <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShapeOut">A type describing shape of the returned graph.</typeparam>
        /// <typeparam name="TMatOut">A type of value, that graph will materialize to after completion.</typeparam>
        /// <typeparam name="TMat0">A type of materialized value of the first input graph parameter.</typeparam>
        /// <typeparam name="TMat1">A type of materialized value of the second input graph parameter.</typeparam>
        /// <typeparam name="TMat2">A type of materialized value of the third input graph parameter.</typeparam>
        /// <typeparam name="TShape0">A type describing the shape of a first input graph parameter.</typeparam>
        /// <typeparam name="TShape1">A type describing the shape of a second input graph parameter.</typeparam>
        /// <typeparam name="TShape2">A type describing the shape of a third input graph parameter.</typeparam>
        /// <param name="g0">A first input graph.</param>
        /// <param name="g1">A second input graph.</param>
        /// <param name="g2">A third input graph.</param>
        /// <param name="combineMaterializers">Function used to determine output materialized value based on the materialized values of the passed graphs.</param>
        /// <param name="buildBlock">A graph constructor function.</param>
        /// <returns>A graph with materialized value.</returns>
        public static IGraph<TShapeOut, TMatOut> Create<TShapeOut, TMatOut, TMat0, TMat1, TMat2, TShape0, TShape1, TShape2>(
            IGraph<TShape0, TMat0> g0, IGraph<TShape1, TMat1> g1, IGraph<TShape2, TMat2> g2, 
            Func<TMat0, TMat1, TMat2, TMatOut> combineMaterializers,
            Func<Builder<TMatOut>, TShape0, TShape1, TShape2, TShapeOut> buildBlock) 
            where TShapeOut: Shape
            where TShape0: Shape
            where TShape1: Shape
            where TShape2: Shape
        {
            var builder = new Builder<TMatOut>();
            
            var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, Func<TMat2, TMatOut>>>(g0, m0 => (m1 => (m2 => combineMaterializers(m0, m1, m2))));
            var shape1 = builder.Add<TShape1, Func<TMat1, Func<TMat2, TMatOut>>, TMat1, Func<TMat2, TMatOut>>(g1, (f, m1) => f(m1));
            var shape2 = builder.Add<TShape2, Func<TMat2, TMatOut>, TMat2, TMatOut>(g2, (f, m2) => f(m2));

            var shape = buildBlock(builder, shape0, shape1, shape2);
            var module = builder.Module.ReplaceShape(shape);

            return new GraphImpl<TShapeOut, TMatOut>(shape, module);
        }
        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
        /// along with the <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShapeOut">A type describing shape of the returned graph.</typeparam>
        /// <typeparam name="TMatOut">A type of value, that graph will materialize to after completion.</typeparam>
        /// <typeparam name="TMat0">A type of materialized value of the first input graph parameter.</typeparam>
        /// <typeparam name="TMat1">A type of materialized value of the second input graph parameter.</typeparam>
        /// <typeparam name="TMat2">A type of materialized value of the third input graph parameter.</typeparam>
        /// <typeparam name="TMat3">A type of materialized value of the fourth input graph parameter.</typeparam>
        /// <typeparam name="TShape0">A type describing the shape of a first input graph parameter.</typeparam>
        /// <typeparam name="TShape1">A type describing the shape of a second input graph parameter.</typeparam>
        /// <typeparam name="TShape2">A type describing the shape of a third input graph parameter.</typeparam>
        /// <typeparam name="TShape3">A type describing the shape of a fourth input graph parameter.</typeparam>
        /// <param name="g0">A first input graph.</param>
        /// <param name="g1">A second input graph.</param>
        /// <param name="g2">A third input graph.</param>
        /// <param name="g3">A fourth input graph.</param>
        /// <param name="combineMaterializers">Function used to determine output materialized value based on the materialized values of the passed graphs.</param>
        /// <param name="buildBlock">A graph constructor function.</param>
        /// <returns>A graph with materialized value.</returns>
        public static IGraph<TShapeOut, TMatOut> Create<TShapeOut, TMatOut, TMat0, TMat1, TMat2, TMat3, TShape0, TShape1, TShape2, TShape3>(
            IGraph<TShape0, TMat0> g0, IGraph<TShape1, TMat1> g1, IGraph<TShape2, TMat2> g2, IGraph<TShape3, TMat3> g3, 
            Func<TMat0, TMat1, TMat2, TMat3, TMatOut> combineMaterializers,
            Func<Builder<TMatOut>, TShape0, TShape1, TShape2, TShape3, TShapeOut> buildBlock) 
            where TShapeOut: Shape
            where TShape0: Shape
            where TShape1: Shape
            where TShape2: Shape
            where TShape3: Shape
        {
            var builder = new Builder<TMatOut>();
            
            var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, Func<TMat2, Func<TMat3, TMatOut>>>>(g0, m0 => (m1 => (m2 => (m3 => combineMaterializers(m0, m1, m2, m3)))));
            var shape1 = builder.Add<TShape1, Func<TMat1, Func<TMat2, Func<TMat3, TMatOut>>>, TMat1, Func<TMat2, Func<TMat3, TMatOut>>>(g1, (f, m1) => f(m1));
            var shape2 = builder.Add<TShape2, Func<TMat2, Func<TMat3, TMatOut>>, TMat2, Func<TMat3, TMatOut>>(g2, (f, m2) => f(m2));
            var shape3 = builder.Add<TShape3, Func<TMat3, TMatOut>, TMat3, TMatOut>(g3, (f, m3) => f(m3));

            var shape = buildBlock(builder, shape0, shape1, shape2, shape3);
            var module = builder.Module.ReplaceShape(shape);

            return new GraphImpl<TShapeOut, TMatOut>(shape, module);
        }
        /// <summary>
        /// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
        /// along with the <see cref="Builder{TMat}"/> to the given create function.
        /// </summary>
        /// <typeparam name="TShapeOut">A type describing shape of the returned graph.</typeparam>
        /// <typeparam name="TMatOut">A type of value, that graph will materialize to after completion.</typeparam>
        /// <typeparam name="TMat0">A type of materialized value of the first input graph parameter.</typeparam>
        /// <typeparam name="TMat1">A type of materialized value of the second input graph parameter.</typeparam>
        /// <typeparam name="TMat2">A type of materialized value of the third input graph parameter.</typeparam>
        /// <typeparam name="TMat3">A type of materialized value of the fourth input graph parameter.</typeparam>
        /// <typeparam name="TMat4">A type of materialized value of the fifth input graph parameter.</typeparam>
        /// <typeparam name="TShape0">A type describing the shape of a first input graph parameter.</typeparam>
        /// <typeparam name="TShape1">A type describing the shape of a second input graph parameter.</typeparam>
        /// <typeparam name="TShape2">A type describing the shape of a third input graph parameter.</typeparam>
        /// <typeparam name="TShape3">A type describing the shape of a fourth input graph parameter.</typeparam>
        /// <typeparam name="TShape4">A type describing the shape of a fifth input graph parameter.</typeparam>
        /// <param name="g0">A first input graph.</param>
        /// <param name="g1">A second input graph.</param>
        /// <param name="g2">A third input graph.</param>
        /// <param name="g3">A fourth input graph.</param>
        /// <param name="g4">A fifth input graph.</param>
        /// <param name="combineMaterializers">Function used to determine output materialized value based on the materialized values of the passed graphs.</param>
        /// <param name="buildBlock">A graph constructor function.</param>
        /// <returns>A graph with materialized value.</returns>
        public static IGraph<TShapeOut, TMatOut> Create<TShapeOut, TMatOut, TMat0, TMat1, TMat2, TMat3, TMat4, TShape0, TShape1, TShape2, TShape3, TShape4>(
            IGraph<TShape0, TMat0> g0, IGraph<TShape1, TMat1> g1, IGraph<TShape2, TMat2> g2, IGraph<TShape3, TMat3> g3, IGraph<TShape4, TMat4> g4, 
            Func<TMat0, TMat1, TMat2, TMat3, TMat4, TMatOut> combineMaterializers,
            Func<Builder<TMatOut>, TShape0, TShape1, TShape2, TShape3, TShape4, TShapeOut> buildBlock) 
            where TShapeOut: Shape
            where TShape0: Shape
            where TShape1: Shape
            where TShape2: Shape
            where TShape3: Shape
            where TShape4: Shape
        {
            var builder = new Builder<TMatOut>();
            
            var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, Func<TMat2, Func<TMat3, Func<TMat4, TMatOut>>>>>(g0, m0 => (m1 => (m2 => (m3 => (m4 => combineMaterializers(m0, m1, m2, m3, m4))))));
            var shape1 = builder.Add<TShape1, Func<TMat1, Func<TMat2, Func<TMat3, Func<TMat4, TMatOut>>>>, TMat1, Func<TMat2, Func<TMat3, Func<TMat4, TMatOut>>>>(g1, (f, m1) => f(m1));
            var shape2 = builder.Add<TShape2, Func<TMat2, Func<TMat3, Func<TMat4, TMatOut>>>, TMat2, Func<TMat3, Func<TMat4, TMatOut>>>(g2, (f, m2) => f(m2));
            var shape3 = builder.Add<TShape3, Func<TMat3, Func<TMat4, TMatOut>>, TMat3, Func<TMat4, TMatOut>>(g3, (f, m3) => f(m3));
            var shape4 = builder.Add<TShape4, Func<TMat4, TMatOut>, TMat4, TMatOut>(g4, (f, m4) => f(m4));

            var shape = buildBlock(builder, shape0, shape1, shape2, shape3, shape4);
            var module = builder.Module.ReplaceShape(shape);

            return new GraphImpl<TShapeOut, TMatOut>(shape, module);
        }
    }
}
