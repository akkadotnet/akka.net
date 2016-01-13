// --- auto generated: 2016-01-13 11:28:18 --- //
using System;
using System.Linq;

namespace Akka.Streams
{
	public class FanInShapeN<T0, T1, TOut> : FanInShape<TOut>
	{
		public readonly int N;
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1>[] In1s;
		public FanInShapeN(int n, IInit init) : base(init)
		{
			N = n;
			In0 = NewInlet<T0>("in0");
			In1s = new Inlet<T1>[n];
			for (int i = 0; i < n; i++) In1s[i] = new Inlet<T1>("in" + i);
		}

        public FanInShapeN(int n) : this(n, new InitName("FanInShape1N")) { }
        public FanInShapeN(int n, string name) : this(n, new InitName(name)) { }
        public FanInShapeN(Outlet<TOut> outlet, Inlet<T0> in0, params Inlet<T1>[] inlets) : this(inlets.Length, new InitPorts(outlet, new Inlet[]{in0}.Union(inlets))) { }

		public Inlet<T1> In(int n)
		{
			if (n <= 0) throw new ArgumentException("n must be > 0", "n");
			return In1s[n-1];
		}
		
        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShapeN<T0, T1, TOut>(N, init);
        }
	}

		   
	public class FanInShape<T0, T1, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, T3, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		public readonly Inlet<T3> In3;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
			In3 = NewInlet<T3>("in3");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, T3, T4, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		public readonly Inlet<T3> In3;
		public readonly Inlet<T4> In4;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
			In3 = NewInlet<T3>("in3");
			In4 = NewInlet<T4>("in4");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, T3, T4, T5, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		public readonly Inlet<T3> In3;
		public readonly Inlet<T4> In4;
		public readonly Inlet<T5> In5;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
			In3 = NewInlet<T3>("in3");
			In4 = NewInlet<T4>("in4");
			In5 = NewInlet<T5>("in5");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, T3, T4, T5, T6, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		public readonly Inlet<T3> In3;
		public readonly Inlet<T4> In4;
		public readonly Inlet<T5> In5;
		public readonly Inlet<T6> In6;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
			In3 = NewInlet<T3>("in3");
			In4 = NewInlet<T4>("in4");
			In5 = NewInlet<T5>("in5");
			In6 = NewInlet<T6>("in6");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		public readonly Inlet<T3> In3;
		public readonly Inlet<T4> In4;
		public readonly Inlet<T5> In5;
		public readonly Inlet<T6> In6;
		public readonly Inlet<T7> In7;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
			In3 = NewInlet<T3>("in3");
			In4 = NewInlet<T4>("in4");
			In5 = NewInlet<T5>("in5");
			In6 = NewInlet<T6>("in6");
			In7 = NewInlet<T7>("in7");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6, Inlet<T7> in7) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6, in7 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, TOut>(init);
        }
	}
		   
	public class FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, T8, TOut> : FanInShape<TOut>
	{
		public readonly Inlet<T0> In0;
		public readonly Inlet<T1> In1;
		public readonly Inlet<T2> In2;
		public readonly Inlet<T3> In3;
		public readonly Inlet<T4> In4;
		public readonly Inlet<T5> In5;
		public readonly Inlet<T6> In6;
		public readonly Inlet<T7> In7;
		public readonly Inlet<T8> In8;
		
		public FanInShape(IInit init) : base(init)
		{
			In0 = NewInlet<T0>("in0");
			In1 = NewInlet<T1>("in1");
			In2 = NewInlet<T2>("in2");
			In3 = NewInlet<T3>("in3");
			In4 = NewInlet<T4>("in4");
			In5 = NewInlet<T5>("in5");
			In6 = NewInlet<T6>("in6");
			In7 = NewInlet<T7>("in7");
			In8 = NewInlet<T8>("in8");
		}

        public FanInShape(string name) : this(new InitName(name)) { }
        public FanInShape(Outlet<TOut> outlet, Inlet<T0> in0, Inlet<T1> in1, Inlet<T2> in2, Inlet<T3> in3, Inlet<T4> in4, Inlet<T5> in5, Inlet<T6> in6, Inlet<T7> in7, Inlet<T8> in8) 
			: this(new InitPorts(outlet, new Inlet[] { in0, in1, in2, in3, in4, in5, in6, in7, in8 })) { }

        protected override FanInShape<TOut> Construct(IInit init)
        {
            return new FanInShape<T0, T1, T2, T3, T4, T5, T6, T7, T8, TOut>(init);
        }
	}
	
}