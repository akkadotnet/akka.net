// --- auto generated: 2016-02-02 11:11:01 --- //
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
		private sealed class UnzipWithStageLogic : GraphStageLogic 
		{
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1> stage) : base(shape)
			{
				var pendingCount = 1;
				var downstreamRunning = 1;
				var pending0 = true;
				var pending1 = true;
				
				SetHandler(stage.In, onPush: () => {
					var elements = stage._unzipper(Grab(stage.In));
					
					if (!IsClosed(stage.Out0)) Push(stage.Out0, elements.Item1);
					if (!IsClosed(stage.Out1)) Push(stage.Out1, elements.Item2);
					
				});				
				
				SetHandler(stage.Out0, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending0) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending1) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
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
		private sealed class UnzipWithStageLogic : GraphStageLogic 
		{
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2> stage) : base(shape)
			{
				var pendingCount = 1;
				var downstreamRunning = 1;
				var pending0 = true;
				var pending1 = true;
				var pending2 = true;
				
				SetHandler(stage.In, onPush: () => {
					var elements = stage._unzipper(Grab(stage.In));
					
					if (!IsClosed(stage.Out0)) Push(stage.Out0, elements.Item1);
					if (!IsClosed(stage.Out1)) Push(stage.Out1, elements.Item2);
					if (!IsClosed(stage.Out2)) Push(stage.Out2, elements.Item3);
					
				});				
				
				SetHandler(stage.Out0, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending0) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending1) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending2) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
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
		private sealed class UnzipWithStageLogic : GraphStageLogic 
		{
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3> stage) : base(shape)
			{
				var pendingCount = 1;
				var downstreamRunning = 1;
				var pending0 = true;
				var pending1 = true;
				var pending2 = true;
				var pending3 = true;
				
				SetHandler(stage.In, onPush: () => {
					var elements = stage._unzipper(Grab(stage.In));
					
					if (!IsClosed(stage.Out0)) Push(stage.Out0, elements.Item1);
					if (!IsClosed(stage.Out1)) Push(stage.Out1, elements.Item2);
					if (!IsClosed(stage.Out2)) Push(stage.Out2, elements.Item3);
					if (!IsClosed(stage.Out3)) Push(stage.Out3, elements.Item4);
					
				});				
				
				SetHandler(stage.Out0, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending0) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending1) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending2) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending3) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
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
		private sealed class UnzipWithStageLogic : GraphStageLogic 
		{
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4> stage) : base(shape)
			{
				var pendingCount = 1;
				var downstreamRunning = 1;
				var pending0 = true;
				var pending1 = true;
				var pending2 = true;
				var pending3 = true;
				var pending4 = true;
				
				SetHandler(stage.In, onPush: () => {
					var elements = stage._unzipper(Grab(stage.In));
					
					if (!IsClosed(stage.Out0)) Push(stage.Out0, elements.Item1);
					if (!IsClosed(stage.Out1)) Push(stage.Out1, elements.Item2);
					if (!IsClosed(stage.Out2)) Push(stage.Out2, elements.Item3);
					if (!IsClosed(stage.Out3)) Push(stage.Out3, elements.Item4);
					if (!IsClosed(stage.Out4)) Push(stage.Out4, elements.Item5);
					
				});				
				
				SetHandler(stage.Out0, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending0) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending1) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending2) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending3) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out4, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending4) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
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
		private sealed class UnzipWithStageLogic : GraphStageLogic 
		{
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4, T5> stage) : base(shape)
			{
				var pendingCount = 1;
				var downstreamRunning = 1;
				var pending0 = true;
				var pending1 = true;
				var pending2 = true;
				var pending3 = true;
				var pending4 = true;
				var pending5 = true;
				
				SetHandler(stage.In, onPush: () => {
					var elements = stage._unzipper(Grab(stage.In));
					
					if (!IsClosed(stage.Out0)) Push(stage.Out0, elements.Item1);
					if (!IsClosed(stage.Out1)) Push(stage.Out1, elements.Item2);
					if (!IsClosed(stage.Out2)) Push(stage.Out2, elements.Item3);
					if (!IsClosed(stage.Out3)) Push(stage.Out3, elements.Item4);
					if (!IsClosed(stage.Out4)) Push(stage.Out4, elements.Item5);
					if (!IsClosed(stage.Out5)) Push(stage.Out5, elements.Item6);
					
				});				
				
				SetHandler(stage.Out0, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending0) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending1) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending2) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending3) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out4, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending4) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out5, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending5) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
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
		private sealed class UnzipWithStageLogic : GraphStageLogic 
		{
			public UnzipWithStageLogic(Shape shape, UnzipWith<TIn, T0, T1, T2, T3, T4, T5, T6> stage) : base(shape)
			{
				var pendingCount = 1;
				var downstreamRunning = 1;
				var pending0 = true;
				var pending1 = true;
				var pending2 = true;
				var pending3 = true;
				var pending4 = true;
				var pending5 = true;
				var pending6 = true;
				
				SetHandler(stage.In, onPush: () => {
					var elements = stage._unzipper(Grab(stage.In));
					
					if (!IsClosed(stage.Out0)) Push(stage.Out0, elements.Item1);
					if (!IsClosed(stage.Out1)) Push(stage.Out1, elements.Item2);
					if (!IsClosed(stage.Out2)) Push(stage.Out2, elements.Item3);
					if (!IsClosed(stage.Out3)) Push(stage.Out3, elements.Item4);
					if (!IsClosed(stage.Out4)) Push(stage.Out4, elements.Item5);
					if (!IsClosed(stage.Out5)) Push(stage.Out5, elements.Item6);
					if (!IsClosed(stage.Out6)) Push(stage.Out6, elements.Item7);
					
				});				
				
				SetHandler(stage.Out0, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending0) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out1, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending1) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out2, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending2) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out3, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending3) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out4, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending4) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out5, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending5) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
				SetHandler(stage.Out6, onPull: () => {
					pendingCount--;
					if (pendingCount == 0) Pull(stage.In);
				},
				onDownstreamFinish: () => {
					downstreamRunning--;
					if (downstreamRunning == 0) CompleteStage();
					else 
					{
						if (pending6) pendingCount--;
						if (pendingCount == 0) Pull(stage.In);
					}
				});
				
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