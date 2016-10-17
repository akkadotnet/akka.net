// --- auto generated: 17.10.2016 21:24:59 --- //
//-----------------------------------------------------------------------
// <copyright file="UnzipWith.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
	public interface IUnzipWithCreator<out TIn, in TOut, out T>
	{
		T Create(Func<TIn, TOut> unzipper);
	}	
	
	public abstract class UnzipWithCreator<TIn, TOut0, TOut1> : IUnzipWithCreator<TIn, Tuple<TOut0, TOut1>, UnzipWith<TIn, TOut0, TOut1>>
	{
		public virtual UnzipWith<TIn, TOut0, TOut1> Create(Func<TIn, Tuple<TOut0, TOut1>> unzipper)
		{
			return new UnzipWith<TIn, TOut0, TOut1>(unzipper);
		}
	}
	
	public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2> : IUnzipWithCreator<TIn, Tuple<TOut0, TOut1, TOut2>, UnzipWith<TIn, TOut0, TOut1, TOut2>>
	{
		public virtual UnzipWith<TIn, TOut0, TOut1, TOut2> Create(Func<TIn, Tuple<TOut0, TOut1, TOut2>> unzipper)
		{
			return new UnzipWith<TIn, TOut0, TOut1, TOut2>(unzipper);
		}
	}
	
	public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3> : IUnzipWithCreator<TIn, Tuple<TOut0, TOut1, TOut2, TOut3>, UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3>>
	{
		public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3> Create(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3>> unzipper)
		{
			return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3>(unzipper);
		}
	}
	
	public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> : IUnzipWithCreator<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4>, UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4>>
	{
		public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> Create(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4>> unzipper)
		{
			return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4>(unzipper);
		}
	}
	
	public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> : IUnzipWithCreator<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>, UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>>
	{
		public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> Create(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>> unzipper)
		{
			return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>(unzipper);
		}
	}
	
	public abstract class UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> : IUnzipWithCreator<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>, UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>>
	{
		public virtual UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> Create(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>> unzipper)
		{
			return new UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>(unzipper);
		}
	}
	
	public partial class UnzipWith 
	{
		
		public static UnzipWith<TIn, TOut0, TOut1> Apply<TIn, TOut0, TOut1>(Func<TIn, Tuple<TOut0, TOut1>> unzipper, UnzipWithCreator<TIn, TOut0, TOut1> creator)
		{
			return creator.Create(unzipper);
		}	
		
		public static UnzipWith<TIn, TOut0, TOut1, TOut2> Apply<TIn, TOut0, TOut1, TOut2>(Func<TIn, Tuple<TOut0, TOut1, TOut2>> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2> creator)
		{
			return creator.Create(unzipper);
		}	
		
		public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3> Apply<TIn, TOut0, TOut1, TOut2, TOut3>(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3>> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3> creator)
		{
			return creator.Create(unzipper);
		}	
		
		public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> Apply<TIn, TOut0, TOut1, TOut2, TOut3, TOut4>(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4>> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4> creator)
		{
			return creator.Create(unzipper);
		}	
		
		public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> Apply<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4, TOut5>> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5> creator)
		{
			return creator.Create(unzipper);
		}	
		
		public static UnzipWith<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> Apply<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>(Func<TIn, Tuple<TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6>> unzipper, UnzipWithCreator<TIn, TOut0, TOut1, TOut2, TOut3, TOut4, TOut5, TOut6> creator)
		{
			return creator.Create(unzipper);
		}	
		
	}	
	
	public class UnzipWith<TIn, T0, T1> : GraphStage<FanOutShape<TIn, T0, T1>>
	{
		private sealed class UnzipWithStageLogic : InGraphStageLogic 
		{
			private readonly UnzipWith<TIn, T0, T1> _stage;
			private int _pendingCount = 2;
			private int _downstreamRunning = 2;
			private bool _pending0 = true;
			private bool _pending1 = true;
	
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1> stage) : base(shape)
			{
				_stage = stage;

				SetHandler(stage.In, this);				
				
				SetHandler(stage.Out0, onPull: () => {
					_pendingCount--;
					_pending0 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending0) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					_pendingCount--;
					_pending1 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending1) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
			}

			public override void  OnPush()
			{
				var elements = _stage._unzipper(Grab(_stage.In));
					
				if (!IsClosed(_stage.Out0)) 
				{
					Push(_stage.Out0, elements.Item1);
					_pending0 = true;
				}
				if (!IsClosed(_stage.Out1)) 
				{
					Push(_stage.Out1, elements.Item2);
					_pending1 = true;
				}
				
				_pendingCount = _downstreamRunning;
			}
		}		

		private readonly Func<TIn, Tuple<T0, T1>> _unzipper;
		public UnzipWith(Func<TIn, Tuple<T0, T1>> unzipper)
		{
			_unzipper = unzipper;

			InitialAttributes = Attributes.CreateName("UnzipWith");
			Shape = new FanOutShape<TIn, T0, T1>("UnzipWith");
			In = Shape.In;

			Out0 = Shape.Out0;
			Out1 = Shape.Out1;
			
		}

		public Inlet<TIn> In { get; }

		public Outlet<T0> Out0 { get; }
		public Outlet<T1> Out1 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanOutShape<TIn, T0, T1> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
	}
	
	public class UnzipWith<TIn, T0, T1, T2> : GraphStage<FanOutShape<TIn, T0, T1, T2>>
	{
		private sealed class UnzipWithStageLogic : InGraphStageLogic 
		{
			private readonly UnzipWith<TIn, T0, T1, T2> _stage;
			private int _pendingCount = 3;
			private int _downstreamRunning = 3;
			private bool _pending0 = true;
			private bool _pending1 = true;
			private bool _pending2 = true;
	
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2> stage) : base(shape)
			{
				_stage = stage;

				SetHandler(stage.In, this);				
				
				SetHandler(stage.Out0, onPull: () => {
					_pendingCount--;
					_pending0 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending0) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					_pendingCount--;
					_pending1 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending1) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					_pendingCount--;
					_pending2 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending2) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
			}

			public override void  OnPush()
			{
				var elements = _stage._unzipper(Grab(_stage.In));
					
				if (!IsClosed(_stage.Out0)) 
				{
					Push(_stage.Out0, elements.Item1);
					_pending0 = true;
				}
				if (!IsClosed(_stage.Out1)) 
				{
					Push(_stage.Out1, elements.Item2);
					_pending1 = true;
				}
				if (!IsClosed(_stage.Out2)) 
				{
					Push(_stage.Out2, elements.Item3);
					_pending2 = true;
				}
				
				_pendingCount = _downstreamRunning;
			}
		}		

		private readonly Func<TIn, Tuple<T0, T1, T2>> _unzipper;
		public UnzipWith(Func<TIn, Tuple<T0, T1, T2>> unzipper)
		{
			_unzipper = unzipper;

			InitialAttributes = Attributes.CreateName("UnzipWith");
			Shape = new FanOutShape<TIn, T0, T1, T2>("UnzipWith");
			In = Shape.In;

			Out0 = Shape.Out0;
			Out1 = Shape.Out1;
			Out2 = Shape.Out2;
			
		}

		public Inlet<TIn> In { get; }

		public Outlet<T0> Out0 { get; }
		public Outlet<T1> Out1 { get; }
		public Outlet<T2> Out2 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanOutShape<TIn, T0, T1, T2> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
	}
	
	public class UnzipWith<TIn, T0, T1, T2, T3> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3>>
	{
		private sealed class UnzipWithStageLogic : InGraphStageLogic 
		{
			private readonly UnzipWith<TIn, T0, T1, T2, T3> _stage;
			private int _pendingCount = 4;
			private int _downstreamRunning = 4;
			private bool _pending0 = true;
			private bool _pending1 = true;
			private bool _pending2 = true;
			private bool _pending3 = true;
	
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3> stage) : base(shape)
			{
				_stage = stage;

				SetHandler(stage.In, this);				
				
				SetHandler(stage.Out0, onPull: () => {
					_pendingCount--;
					_pending0 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending0) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					_pendingCount--;
					_pending1 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending1) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					_pendingCount--;
					_pending2 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending2) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					_pendingCount--;
					_pending3 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending3) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
			}

			public override void  OnPush()
			{
				var elements = _stage._unzipper(Grab(_stage.In));
					
				if (!IsClosed(_stage.Out0)) 
				{
					Push(_stage.Out0, elements.Item1);
					_pending0 = true;
				}
				if (!IsClosed(_stage.Out1)) 
				{
					Push(_stage.Out1, elements.Item2);
					_pending1 = true;
				}
				if (!IsClosed(_stage.Out2)) 
				{
					Push(_stage.Out2, elements.Item3);
					_pending2 = true;
				}
				if (!IsClosed(_stage.Out3)) 
				{
					Push(_stage.Out3, elements.Item4);
					_pending3 = true;
				}
				
				_pendingCount = _downstreamRunning;
			}
		}		

		private readonly Func<TIn, Tuple<T0, T1, T2, T3>> _unzipper;
		public UnzipWith(Func<TIn, Tuple<T0, T1, T2, T3>> unzipper)
		{
			_unzipper = unzipper;

			InitialAttributes = Attributes.CreateName("UnzipWith");
			Shape = new FanOutShape<TIn, T0, T1, T2, T3>("UnzipWith");
			In = Shape.In;

			Out0 = Shape.Out0;
			Out1 = Shape.Out1;
			Out2 = Shape.Out2;
			Out3 = Shape.Out3;
			
		}

		public Inlet<TIn> In { get; }

		public Outlet<T0> Out0 { get; }
		public Outlet<T1> Out1 { get; }
		public Outlet<T2> Out2 { get; }
		public Outlet<T3> Out3 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanOutShape<TIn, T0, T1, T2, T3> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
	}
	
	public class UnzipWith<TIn, T0, T1, T2, T3, T4> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3, T4>>
	{
		private sealed class UnzipWithStageLogic : InGraphStageLogic 
		{
			private readonly UnzipWith<TIn, T0, T1, T2, T3, T4> _stage;
			private int _pendingCount = 5;
			private int _downstreamRunning = 5;
			private bool _pending0 = true;
			private bool _pending1 = true;
			private bool _pending2 = true;
			private bool _pending3 = true;
			private bool _pending4 = true;
	
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4> stage) : base(shape)
			{
				_stage = stage;

				SetHandler(stage.In, this);				
				
				SetHandler(stage.Out0, onPull: () => {
					_pendingCount--;
					_pending0 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending0) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					_pendingCount--;
					_pending1 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending1) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					_pendingCount--;
					_pending2 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending2) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					_pendingCount--;
					_pending3 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending3) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out4, onPull: () => {
					_pendingCount--;
					_pending4 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending4) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
			}

			public override void  OnPush()
			{
				var elements = _stage._unzipper(Grab(_stage.In));
					
				if (!IsClosed(_stage.Out0)) 
				{
					Push(_stage.Out0, elements.Item1);
					_pending0 = true;
				}
				if (!IsClosed(_stage.Out1)) 
				{
					Push(_stage.Out1, elements.Item2);
					_pending1 = true;
				}
				if (!IsClosed(_stage.Out2)) 
				{
					Push(_stage.Out2, elements.Item3);
					_pending2 = true;
				}
				if (!IsClosed(_stage.Out3)) 
				{
					Push(_stage.Out3, elements.Item4);
					_pending3 = true;
				}
				if (!IsClosed(_stage.Out4)) 
				{
					Push(_stage.Out4, elements.Item5);
					_pending4 = true;
				}
				
				_pendingCount = _downstreamRunning;
			}
		}		

		private readonly Func<TIn, Tuple<T0, T1, T2, T3, T4>> _unzipper;
		public UnzipWith(Func<TIn, Tuple<T0, T1, T2, T3, T4>> unzipper)
		{
			_unzipper = unzipper;

			InitialAttributes = Attributes.CreateName("UnzipWith");
			Shape = new FanOutShape<TIn, T0, T1, T2, T3, T4>("UnzipWith");
			In = Shape.In;

			Out0 = Shape.Out0;
			Out1 = Shape.Out1;
			Out2 = Shape.Out2;
			Out3 = Shape.Out3;
			Out4 = Shape.Out4;
			
		}

		public Inlet<TIn> In { get; }

		public Outlet<T0> Out0 { get; }
		public Outlet<T1> Out1 { get; }
		public Outlet<T2> Out2 { get; }
		public Outlet<T3> Out3 { get; }
		public Outlet<T4> Out4 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanOutShape<TIn, T0, T1, T2, T3, T4> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
	}
	
	public class UnzipWith<TIn, T0, T1, T2, T3, T4, T5> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3, T4, T5>>
	{
		private sealed class UnzipWithStageLogic : InGraphStageLogic 
		{
			private readonly UnzipWith<TIn, T0, T1, T2, T3, T4, T5> _stage;
			private int _pendingCount = 6;
			private int _downstreamRunning = 6;
			private bool _pending0 = true;
			private bool _pending1 = true;
			private bool _pending2 = true;
			private bool _pending3 = true;
			private bool _pending4 = true;
			private bool _pending5 = true;
	
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4, T5> stage) : base(shape)
			{
				_stage = stage;

				SetHandler(stage.In, this);				
				
				SetHandler(stage.Out0, onPull: () => {
					_pendingCount--;
					_pending0 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending0) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					_pendingCount--;
					_pending1 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending1) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					_pendingCount--;
					_pending2 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending2) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					_pendingCount--;
					_pending3 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending3) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out4, onPull: () => {
					_pendingCount--;
					_pending4 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending4) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out5, onPull: () => {
					_pendingCount--;
					_pending5 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending5) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
			}

			public override void  OnPush()
			{
				var elements = _stage._unzipper(Grab(_stage.In));
					
				if (!IsClosed(_stage.Out0)) 
				{
					Push(_stage.Out0, elements.Item1);
					_pending0 = true;
				}
				if (!IsClosed(_stage.Out1)) 
				{
					Push(_stage.Out1, elements.Item2);
					_pending1 = true;
				}
				if (!IsClosed(_stage.Out2)) 
				{
					Push(_stage.Out2, elements.Item3);
					_pending2 = true;
				}
				if (!IsClosed(_stage.Out3)) 
				{
					Push(_stage.Out3, elements.Item4);
					_pending3 = true;
				}
				if (!IsClosed(_stage.Out4)) 
				{
					Push(_stage.Out4, elements.Item5);
					_pending4 = true;
				}
				if (!IsClosed(_stage.Out5)) 
				{
					Push(_stage.Out5, elements.Item6);
					_pending5 = true;
				}
				
				_pendingCount = _downstreamRunning;
			}
		}		

		private readonly Func<TIn, Tuple<T0, T1, T2, T3, T4, T5>> _unzipper;
		public UnzipWith(Func<TIn, Tuple<T0, T1, T2, T3, T4, T5>> unzipper)
		{
			_unzipper = unzipper;

			InitialAttributes = Attributes.CreateName("UnzipWith");
			Shape = new FanOutShape<TIn, T0, T1, T2, T3, T4, T5>("UnzipWith");
			In = Shape.In;

			Out0 = Shape.Out0;
			Out1 = Shape.Out1;
			Out2 = Shape.Out2;
			Out3 = Shape.Out3;
			Out4 = Shape.Out4;
			Out5 = Shape.Out5;
			
		}

		public Inlet<TIn> In { get; }

		public Outlet<T0> Out0 { get; }
		public Outlet<T1> Out1 { get; }
		public Outlet<T2> Out2 { get; }
		public Outlet<T3> Out3 { get; }
		public Outlet<T4> Out4 { get; }
		public Outlet<T5> Out5 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanOutShape<TIn, T0, T1, T2, T3, T4, T5> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
	}
	
	public class UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> : GraphStage<FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>>
	{
		private sealed class UnzipWithStageLogic : InGraphStageLogic 
		{
			private readonly UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> _stage;
			private int _pendingCount = 7;
			private int _downstreamRunning = 7;
			private bool _pending0 = true;
			private bool _pending1 = true;
			private bool _pending2 = true;
			private bool _pending3 = true;
			private bool _pending4 = true;
			private bool _pending5 = true;
			private bool _pending6 = true;
	
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> stage) : base(shape)
			{
				_stage = stage;

				SetHandler(stage.In, this);				
				
				SetHandler(stage.Out0, onPull: () => {
					_pendingCount--;
					_pending0 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending0) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					_pendingCount--;
					_pending1 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending1) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					_pendingCount--;
					_pending2 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending2) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					_pendingCount--;
					_pending3 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending3) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out4, onPull: () => {
					_pendingCount--;
					_pending4 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending4) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out5, onPull: () => {
					_pendingCount--;
					_pending5 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending5) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out6, onPull: () => {
					_pendingCount--;
					_pending6 = false;
					if (_pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					_downstreamRunning--;
					if (_downstreamRunning == 0) CompleteStage();
					else 
					{
						if (_pending6) _pendingCount--;
						if (_pendingCount == 0 && !HasBeenPulled(stage.In)) Pull(stage.In);
					}
				});
				
			}

			public override void  OnPush()
			{
				var elements = _stage._unzipper(Grab(_stage.In));
					
				if (!IsClosed(_stage.Out0)) 
				{
					Push(_stage.Out0, elements.Item1);
					_pending0 = true;
				}
				if (!IsClosed(_stage.Out1)) 
				{
					Push(_stage.Out1, elements.Item2);
					_pending1 = true;
				}
				if (!IsClosed(_stage.Out2)) 
				{
					Push(_stage.Out2, elements.Item3);
					_pending2 = true;
				}
				if (!IsClosed(_stage.Out3)) 
				{
					Push(_stage.Out3, elements.Item4);
					_pending3 = true;
				}
				if (!IsClosed(_stage.Out4)) 
				{
					Push(_stage.Out4, elements.Item5);
					_pending4 = true;
				}
				if (!IsClosed(_stage.Out5)) 
				{
					Push(_stage.Out5, elements.Item6);
					_pending5 = true;
				}
				if (!IsClosed(_stage.Out6)) 
				{
					Push(_stage.Out6, elements.Item7);
					_pending6 = true;
				}
				
				_pendingCount = _downstreamRunning;
			}
		}		

		private readonly Func<TIn, Tuple<T0, T1, T2, T3, T4, T5, T6>> _unzipper;
		public UnzipWith(Func<TIn, Tuple<T0, T1, T2, T3, T4, T5, T6>> unzipper)
		{
			_unzipper = unzipper;

			InitialAttributes = Attributes.CreateName("UnzipWith");
			Shape = new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>("UnzipWith");
			In = Shape.In;

			Out0 = Shape.Out0;
			Out1 = Shape.Out1;
			Out2 = Shape.Out2;
			Out3 = Shape.Out3;
			Out4 = Shape.Out4;
			Out5 = Shape.Out5;
			Out6 = Shape.Out6;
			
		}

		public Inlet<TIn> In { get; }

		public Outlet<T0> Out0 { get; }
		public Outlet<T1> Out1 { get; }
		public Outlet<T2> Out2 { get; }
		public Outlet<T3> Out3 { get; }
		public Outlet<T4> Out4 { get; }
		public Outlet<T5> Out5 { get; }
		public Outlet<T6> Out6 { get; }
		
        protected sealed override Attributes InitialAttributes { get; }
		public sealed override FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6> Shape { get; }
        protected sealed override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new UnzipWithStageLogic(Shape, this);
        }
	}
	
}