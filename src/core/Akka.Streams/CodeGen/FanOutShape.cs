//-----------------------------------------------------------------------
// <copyright file="FanOutShape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T3> Out3;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        /// <param name="out3">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T4> Out4;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        /// <param name="out3">TBD</param>
        /// <param name="out4">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T5> Out5;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        public FanOutShape(IInit init) : base(init)
        {
            Out0 = NewOutlet<T0>("out0");
            Out1 = NewOutlet<T1>("out1");
            Out2 = NewOutlet<T2>("out2");
            Out3 = NewOutlet<T3>("out3");
            Out4 = NewOutlet<T4>("out4");
            Out5 = NewOutlet<T5>("out5");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        /// <param name="out3">TBD</param>
        /// <param name="out4">TBD</param>
        /// <param name="out5">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T5> Out5;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T6> Out6;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        /// <param name="out3">TBD</param>
        /// <param name="out4">TBD</param>
        /// <param name="out5">TBD</param>
        /// <param name="out6">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T5> Out5;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T6> Out6;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T7> Out7;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        /// <param name="out3">TBD</param>
        /// <param name="out4">TBD</param>
        /// <param name="out5">TBD</param>
        /// <param name="out6">TBD</param>
        /// <param name="out7">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6, Outlet<T7> out7) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6, out7 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7>(init);
        }
    }
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="T0">TBD</typeparam>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    /// <typeparam name="T3">TBD</typeparam>
    /// <typeparam name="T4">TBD</typeparam>
    /// <typeparam name="T5">TBD</typeparam>
    /// <typeparam name="T6">TBD</typeparam>
    /// <typeparam name="T7">TBD</typeparam>
    /// <typeparam name="T8">TBD</typeparam>
    public class FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7, T8> : FanOutShape<TIn>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T0> Out0;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T1> Out1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T2> Out2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T3> Out3;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T4> Out4;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T5> Out5;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T6> Out6;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T7> Out7;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T8> Out8;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public FanOutShape(string name) : this(new InitName(name)) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="out0">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="out2">TBD</param>
        /// <param name="out3">TBD</param>
        /// <param name="out4">TBD</param>
        /// <param name="out5">TBD</param>
        /// <param name="out6">TBD</param>
        /// <param name="out7">TBD</param>
        /// <param name="out8">TBD</param>
        public FanOutShape(Inlet<TIn> inlet, Outlet<T0> out0, Outlet<T1> out1, Outlet<T2> out2, Outlet<T3> out3, Outlet<T4> out4, Outlet<T5> out5, Outlet<T6> out6, Outlet<T7> out7, Outlet<T8> out8) 
            : this(new InitPorts(inlet, new Outlet[] { out0, out1, out2, out3, out4, out5, out6, out7, out8 })) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="init">TBD</param>
        /// <returns>TBD</returns>
        protected override FanOutShape<TIn> Construct(IInit init)
        {
            return new FanOutShape<TIn, T0, T1, T2, T3, T4, T5, T6, T7, T8>(init);
        }
    }
}
