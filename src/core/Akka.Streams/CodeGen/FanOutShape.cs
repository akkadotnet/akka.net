// --- auto generated: 21.04.2016 07:48:36 --- //
//-----------------------------------------------------------------------
// <copyright file="FanOutShape.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Linq;

namespace Akka.Streams
{
		   
	public class FanOutShape<TIn, T0, T1> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2, T3> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		public readonly Outlet<T3> Out3;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
			Out3 = NewOutlet<T3>("out3");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2, T3, T4> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		public readonly Outlet<T3> Out3;
		public readonly Outlet<T4> Out4;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
			Out3 = NewOutlet<T3>("out3");
			Out4 = NewOutlet<T4>("out4");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		public readonly Outlet<T3> Out3;
		public readonly Outlet<T4> Out4;
		public readonly Outlet<T5> Out5;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
			Out3 = NewOutlet<T3>("out3");
			Out4 = NewOutlet<T4>("out4");
			Out5 = NewOutlet<T5>("out5");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		public readonly Outlet<T3> Out3;
		public readonly Outlet<T4> Out4;
		public readonly Outlet<T5> Out5;
		public readonly Outlet<T6> Out6;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
			Out3 = NewOutlet<T3>("out3");
			Out4 = NewOutlet<T4>("out4");
			Out5 = NewOutlet<T5>("out5");
			Out6 = NewOutlet<T6>("out6");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		public readonly Outlet<T3> Out3;
		public readonly Outlet<T4> Out4;
		public readonly Outlet<T5> Out5;
		public readonly Outlet<T6> Out6;
		public readonly Outlet<T7> Out7;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
			Out3 = NewOutlet<T3>("out3");
			Out4 = NewOutlet<T4>("out4");
			Out5 = NewOutlet<T5>("out5");
			Out6 = NewOutlet<T6>("out6");
			Out7 = NewOutlet<T7>("out7");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6, Outlet<T7> out7) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6, out7 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7>(init);
        }
	}
		   
	public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7, T8> : FanOutShape<TIn>
	{
		public readonly Outlet<T0> Out0;
		public readonly Outlet<T1> Out1;
		public readonly Outlet<T2> Out2;
		public readonly Outlet<T3> Out3;
		public readonly Outlet<T4> Out4;
		public readonly Outlet<T5> Out5;
		public readonly Outlet<T6> Out6;
		public readonly Outlet<T7> Out7;
		public readonly Outlet<T8> Out8;
		
		public FanOutShape(IInit init) : base(init)
		{
			Out0 = NewOutlet<T0>("out0");
			Out1 = NewOutlet<T1>("out1");
			Out2 = NewOutlet<T2>("out2");
			Out3 = NewOutlet<T3>("out3");
			Out4 = NewOutlet<T4>("out4");
			Out5 = NewOutlet<T5>("out5");
			Out6 = NewOutlet<T6>("out6");
			Out7 = NewOutlet<T7>("out7");
			Out8 = NewOutlet<T8>("out8");
		}

        public FanOutShape(string name) : this(new InitName(name)) { }
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6, Outlet<T7> out7, Outlet<T8> out8) 
			: this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6, out7, out8 })) { }

        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7, T8>(init);
        }
	}
	
}