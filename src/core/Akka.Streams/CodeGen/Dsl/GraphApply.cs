// --- auto generated: 05.02.2016 09:30:42 --- //
using System;
using Akka.Streams.Dsl.Internal;

namespace Akka.Streams.Dsl
{
	public partial class GraphDsl
	{
		/// <summary>
		/// Creates a new <see cref="IGraph{TShape, TMat}"/> by passing a <see cref="GraphDsl.Builder{TMat}"/> to the given create function.
		/// </summary>
		public static IGraph<TShape, TMat> Create<TShape, TMat>(Func<Builder<TMat>, TShape> buildBlock) where TShape: Shape
		{
			var builder = new Builder<TMat>();
			var shape = buildBlock(builder);
			var module = builder.Module.Nest().ReplaceShape(shape);

			return new GraphImpl<TShape, TMat>(shape, module);
		}
		
		/// <summary>
		/// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graph <paramref name="g1"/> 
		/// and passing its <see cref="Shape"/> along with the <see cref="GraphDsl.Builder{TMat}"/> to the given create function.
		/// </summary>
		public static IGraph<TShapeOut, TMat> Create<TShapeOut, TMat, TShape1>(IGraph<TShape1, TMat> g1, Func<Builder<TMat>, TShape1, TShapeOut> buildBlock) 
			where TShapeOut: Shape
			where TShape1: Shape
		{
			var builder = new Builder<TMat>();
			var shape1 = builder.Add(g1);
			var shape = buildBlock(builder, shape1);
			var module = builder.Module.Nest().ReplaceShape(shape);

			return new GraphImpl<TShapeOut, TMat>(shape, module);
		}
				
		/// <summary>
		/// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
		/// along with the <see cref="GraphDsl.Builder{TMat}"/> to the given create function.
		/// </summary>
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
			var module = builder.Module.Nest().ReplaceShape(shape);

			return new GraphImpl<TShapeOut, TMatOut>(shape, module);
		}
				
		/// <summary>
		/// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
		/// along with the <see cref="GraphDsl.Builder{TMat}"/> to the given create function.
		/// </summary>
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
			
			var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, TMat2, TMatOut>>(g0, m0 => ((m1, m2) => combineMaterializers(m0, m1, m2)));
			var shape1 = builder.Add<TShape1, Func<TMat1, TMatOut>, TMat1, TMatOut>(g1, (f, m1) => f(m1));
			var shape2 = builder.Add<TShape2, Func<TMat2, TMatOut>, TMat2, TMatOut>(g2, (f, m2) => f(m2));
			
			var shape = buildBlock(builder, shape0, shape1, shape2);
			var module = builder.Module.Nest().ReplaceShape(shape);

			return new GraphImpl<TShapeOut, TMatOut>(shape, module);
		}
				
		/// <summary>
		/// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
		/// along with the <see cref="GraphDsl.Builder{TMat}"/> to the given create function.
		/// </summary>
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
			
			var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, TMat2, TMat3, TMatOut>>(g0, m0 => ((m1, m2, m3) => combineMaterializers(m0, m1, m2, m3)));
			var shape1 = builder.Add<TShape1, Func<TMat1, TMatOut>, TMat1, TMatOut>(g1, (f, m1) => f(m1));
			var shape2 = builder.Add<TShape2, Func<TMat2, TMatOut>, TMat2, TMatOut>(g2, (f, m2) => f(m2));
			var shape3 = builder.Add<TShape3, Func<TMat3, TMatOut>, TMat3, TMatOut>(g3, (f, m3) => f(m3));
			
			var shape = buildBlock(builder, shape0, shape1, shape2, shape3);
			var module = builder.Module.Nest().ReplaceShape(shape);

			return new GraphImpl<TShapeOut, TMatOut>(shape, module);
		}
				
		/// <summary>
		/// Creates a new <see cref="IGraph{TShape, TMat}"/> by importing the given graphs and passing their <see cref="Shape"/>s 
		/// along with the <see cref="GraphDsl.Builder{TMat}"/> to the given create function.
		/// </summary>
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
			
			var shape0 = builder.Add<TShape0, TMat0, Func<TMat1, TMat2, TMat3, TMat4, TMatOut>>(g0, m0 => ((m1, m2, m3, m4) => combineMaterializers(m0, m1, m2, m3, m4)));
			var shape1 = builder.Add<TShape1, Func<TMat1, TMatOut>, TMat1, TMatOut>(g1, (f, m1) => f(m1));
			var shape2 = builder.Add<TShape2, Func<TMat2, TMatOut>, TMat2, TMatOut>(g2, (f, m2) => f(m2));
			var shape3 = builder.Add<TShape3, Func<TMat3, TMatOut>, TMat3, TMatOut>(g3, (f, m3) => f(m3));
			var shape4 = builder.Add<TShape4, Func<TMat4, TMatOut>, TMat4, TMatOut>(g4, (f, m4) => f(m4));
			
			var shape = buildBlock(builder, shape0, shape1, shape2, shape3, shape4);
			var module = builder.Module.Nest().ReplaceShape(shape);

			return new GraphImpl<TShapeOut, TMatOut>(shape, module);
		}
		
	}
}