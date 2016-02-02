
// --- auto generated: 2016-02-02 11:10:25 --- //
using System;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Implementation;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
	public partial class ZipWith
	{
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TOut> Apply<TIn0, TIn1, TOut>(Func<TIn0, TIn1, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TOut> Apply<TIn0, TIn1, TIn2, TOut>(Func<TIn0, TIn1, TIn2, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TIn3, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TIn3, TOut> Apply<TIn0, TIn1, TIn2, TIn3, TOut>(Func<TIn0, TIn1, TIn2, TIn3, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TIn3, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TIn3, TIn4, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> Apply<TIn0, TIn1, TIn2, TIn3, TIn4, TOut>(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> Apply<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut>(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> Apply<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut>(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> Apply<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut>(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut>(zipper);
		}
		
		/// <summary>
		/// Create a new <see cref="ZipWith{TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut}"/> specialized for 1 inputs.
		/// </summary>
		/// <param name="zipper">zipping-function from the input values to the output value</param>
		public static ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> Apply<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut>(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> zipper)
		{
			return new ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut>(zipper);
		}
		
	}
	
	
	public class ZipWith<TIn0, TIn1, TOut> : GraphStage<FanInShape<TIn0, TIn1, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TIn3, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TIn3, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TIn3, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TIn3, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In3, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In3)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						Pull(_stage.In3);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2), Grab(_stage.In3)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TIn3, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TIn3, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TIn3, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
			In3 = Shape.In3;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		public Inlet<TIn3> In3 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TIn3, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In3, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In3)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In4, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In4)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						Pull(_stage.In3);
						Pull(_stage.In4);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2), Grab(_stage.In3), Grab(_stage.In4)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
			In3 = Shape.In3;
			In4 = Shape.In4;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		public Inlet<TIn3> In3 { get; }
		public Inlet<TIn4> In4 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In3, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In3)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In4, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In4)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In5, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In5)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						Pull(_stage.In3);
						Pull(_stage.In4);
						Pull(_stage.In5);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2), Grab(_stage.In3), Grab(_stage.In4), Grab(_stage.In5)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
			In3 = Shape.In3;
			In4 = Shape.In4;
			In5 = Shape.In5;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		public Inlet<TIn3> In3 { get; }
		public Inlet<TIn4> In4 { get; }
		public Inlet<TIn5> In5 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In3, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In3)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In4, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In4)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In5, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In5)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In6, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In6)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						Pull(_stage.In3);
						Pull(_stage.In4);
						Pull(_stage.In5);
						Pull(_stage.In6);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2), Grab(_stage.In3), Grab(_stage.In4), Grab(_stage.In5), Grab(_stage.In6)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
			In3 = Shape.In3;
			In4 = Shape.In4;
			In5 = Shape.In5;
			In6 = Shape.In6;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		public Inlet<TIn3> In3 { get; }
		public Inlet<TIn4> In4 { get; }
		public Inlet<TIn5> In5 { get; }
		public Inlet<TIn6> In6 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In3, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In3)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In4, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In4)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In5, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In5)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In6, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In6)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In7, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In7)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						Pull(_stage.In3);
						Pull(_stage.In4);
						Pull(_stage.In5);
						Pull(_stage.In6);
						Pull(_stage.In7);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2), Grab(_stage.In3), Grab(_stage.In4), Grab(_stage.In5), Grab(_stage.In6), Grab(_stage.In7)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
			In3 = Shape.In3;
			In4 = Shape.In4;
			In5 = Shape.In5;
			In6 = Shape.In6;
			In7 = Shape.In7;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		public Inlet<TIn3> In3 { get; }
		public Inlet<TIn4> In4 { get; }
		public Inlet<TIn5> In5 { get; }
		public Inlet<TIn6> In6 { get; }
		public Inlet<TIn7> In7 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
	public class ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> : GraphStage<FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut>>
	{
		private sealed class ZipWithStageLogic : GraphStageLogic
		{
			private readonly ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> _stage;
			// Without this field the completion signalling would take one extra pull
			private bool _willShutDown = false;
			private int _pending = 1;
			public ZipWithStageLogic(Shape shape, ZipWith<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> stage) : base(shape)
			{
				_stage = stage;
				
				SetHandler(_stage.In0, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In0)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In1, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In1)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In2, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In2)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In3, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In3)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In4, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In4)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In5, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In5)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In6, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In6)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In7, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In7)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.In8, onPush: () => {
					_pending--;
					if (_pending == 0) PushAll();
				},
				onUpstreamFinish: () =>{
					if (!IsAvailable(_stage.In8)) CompleteStage();
					_willShutDown = true;
				});
				
				SetHandler(_stage.Out, onPull: () => {
					_pending = stage.Shape.Inlets.Count();
					if (_willShutDown) CompleteStage();
					else 
					{
						Pull(_stage.In0);
						Pull(_stage.In1);
						Pull(_stage.In2);
						Pull(_stage.In3);
						Pull(_stage.In4);
						Pull(_stage.In5);
						Pull(_stage.In6);
						Pull(_stage.In7);
						Pull(_stage.In8);
						
					}
				});
			}

			private void PushAll()
			{
				Push(_stage.Out, _stage._zipper(Grab(_stage.In0), Grab(_stage.In1), Grab(_stage.In2), Grab(_stage.In3), Grab(_stage.In4), Grab(_stage.In5), Grab(_stage.In6), Grab(_stage.In7), Grab(_stage.In8)));
				if (_willShutDown) CompleteStage();
			}
		}

		private readonly Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> _zipper;
		public ZipWith(Func<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> zipper)
		{
			_zipper = zipper;
			InitialAttributes = Attributes.CreateName("ZipWith");
			Shape = new FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut>("ZipWith");
			Out = Shape.Out;
			
			In0 = Shape.In0;
			In1 = Shape.In1;
			In2 = Shape.In2;
			In3 = Shape.In3;
			In4 = Shape.In4;
			In5 = Shape.In5;
			In6 = Shape.In6;
			In7 = Shape.In7;
			In8 = Shape.In8;
		
		}
		
		public Outlet<TOut> Out { get; }

		public Inlet<TIn0> In0 { get; }
		public Inlet<TIn1> In1 { get; }
		public Inlet<TIn2> In2 { get; }
		public Inlet<TIn3> In3 { get; }
		public Inlet<TIn4> In4 { get; }
		public Inlet<TIn5> In5 { get; }
		public Inlet<TIn6> In6 { get; }
		public Inlet<TIn7> In7 { get; }
		public Inlet<TIn8> In8 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanInShape<TIn0, TIn1, TIn2, TIn3, TIn4, TIn5, TIn6, TIn7, TIn8, TOut> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ZipWithStageLogic(Shape, this);
        }
	}
	
}