//-----------------------------------------------------------------------
// <copyright file="Shape.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Implementation;

namespace Akka.Streams
{
    /// <summary>
    /// An input port of a <see cref="IModule"/>. This type logically belongs
    /// into the impl package but must live here due to how sealed works.
    /// It is also used in the Java DSL for "untyped Inlets" as a work-around
    /// for otherwise unreasonable existential types.
    /// </summary>
    public abstract class InPort
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal int Id = -1;
    }

    /// <summary>
    /// An output port of a StreamLayout.Module. This type logically belongs
    /// into the impl package but must live here due to how sealed works.
    /// It is also used in the Java DSL for "untyped Outlets" as a work-around
    /// for otherwise unreasonable existential types.
    /// </summary>
    public abstract class OutPort 
    {
        /// <summary>
        /// TBD
        /// </summary>
        internal int Id = -1;
    }

    /// <summary>
    /// An Inlet is a typed input to a Shape. Its partner in the Module view 
    /// is the InPort(which does not bear an element type because Modules only 
    /// express the internal structural hierarchy of stream topologies).
    /// </summary>
    public abstract class Inlet : InPort
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        public static Inlet<T> Create<T>(Inlet inlet) => inlet as Inlet<T> ?? new Inlet<T>(inlet.Name);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="name"/> is undefined.
        /// </exception>
        protected Inlet(string name) => Name = name ?? throw new ArgumentException("Inlet name must be defined");

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract Inlet CarbonCopy();

        /// <inheritdoc/>
        public sealed override string ToString() => Name;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class Inlet<T> : Inlet
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public Inlet(string name) : base(name) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOther">TBD</typeparam>
        /// <returns>TBD</returns>
        internal Inlet<TOther> As<TOther>() => Create<TOther>(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Inlet CarbonCopy() => new Inlet<T>(Name);
    }

    /// <summary>
    /// An Outlet is a typed output to a Shape. Its partner in the Module view
    /// is the OutPort(which does not bear an element type because Modules only
    /// express the internal structural hierarchy of stream topologies).
    /// </summary>
    public abstract class Outlet : OutPort
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <returns>TBD</returns>
        public static Outlet<T> Create<T>(Outlet outlet) => outlet as Outlet<T> ?? new Outlet<T>(outlet.Name);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        protected Outlet(string name) => Name = name;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract Outlet CarbonCopy();

        /// <inheritdoc/>
        public sealed override string ToString() => Name;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class Outlet<T> : Outlet
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        public Outlet(string name) : base(name) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOther">TBD</typeparam>
        /// <returns>TBD</returns>
        internal Outlet<TOther> As<TOther>() => Create<TOther>(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Outlet CarbonCopy() => new Outlet<T>(Name);
    }

    /// <summary>
    /// A Shape describes the inlets and outlets of a <see cref="IGraph{TShape}"/>. In keeping with the
    /// philosophy that a Graph is a freely reusable blueprint, everything that
    /// matters from the outside are the connections that can be made with it,
    /// otherwise it is just a black box.
    /// </summary>
    public abstract class Shape
#if CLONEABLE
     : ICloneable
#endif
    {
        /// <summary>
        /// Gets list of all input ports.
        /// </summary>
        public abstract ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// Gets list of all output ports.
        /// </summary>
        public abstract ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// Create a copy of this Shape object, returning the same type as the
        /// original; this constraint can unfortunately not be expressed in the
        /// type system.
        /// </summary>
        /// <returns>TBD</returns>
        public abstract Shape DeepCopy();

        /// <summary>
        /// Create a copy of this Shape object, returning the same type as the
        /// original but containing the ports given within the passed-in Shape.
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <returns>TBD</returns>
        public abstract Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets);

        /// <summary>
        /// Compare this to another shape and determine whether the set of ports is the same (ignoring their ordering).
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        public bool HasSamePortsAs(Shape shape)
        {
            var inlets = new HashSet<Inlet>(Inlets);
            var outlets = new HashSet<Outlet>(Outlets);

            return inlets.SetEquals(shape.Inlets) && outlets.SetEquals(shape.Outlets);
        }

        /// <summary>
        /// Compare this to another shape and determine whether the arrangement of ports is the same (including their ordering).
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        public bool HasSamePortsAndShapeAs(Shape shape) => Inlets.Equals(shape.Inlets) && Outlets.Equals(shape.Outlets);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public object Clone() => DeepCopy();

        /// <inheritdoc/>
        public sealed override string ToString() => $"{GetType().Name}([{string.Join(", ", Inlets)}] [{string.Join(", ", Outlets)}])";
    }

    /// <summary>
    /// This <see cref="Shape"/> is used for graphs that have neither open inputs nor open
    /// outputs. Only such a <see cref="IGraph{TShape,TMaterializer}"/> can be materialized by a <see cref="IMaterializer"/>.
    /// </summary>
    public class ClosedShape : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly ClosedShape Instance = new ClosedShape();
        
        private ClosedShape() { }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets => ImmutableArray<Inlet>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets => ImmutableArray<Outlet>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy() => this;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the size of the specified <paramref name="inlets"/> array is zero
        /// or the size of the specified <paramref name="outlets"/> array is zero.
        /// </exception>
        /// <returns>TBD</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Any())
                throw new ArgumentException("Proposed inlets do not fit ClosedShape", nameof(inlets));
            if (outlets.Any())
                throw new ArgumentException("Proposed outlets do not fit ClosedShape", nameof(outlets));

            return this;
        }
    }

    /// <summary>
    /// This type of <see cref="Shape"/> can express any number of inputs and outputs at the
    /// expense of forgetting about their specific types. It is used mainly in the
    /// implementation of the <see cref="IGraph{TShape,TMaterializer}"/> builders and typically replaced by a more
    /// meaningful type of Shape when the building is finished.
    /// </summary>
    public class AmorphousShape : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        public AmorphousShape(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            Inlets = inlets;
            Outlets = outlets;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy()
            => new AmorphousShape(Inlets.Select(i => i.CarbonCopy()).ToImmutableArray(),Outlets.Select(o => o.CarbonCopy()).ToImmutableArray());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <returns>TBD</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
            => new AmorphousShape(inlets, outlets);
    }

    /// <summary>
    /// A Source <see cref="Shape"/> has exactly one output and no inputs, it models a source of data.
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    public sealed class SourceShape<TOut> : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <exception cref="ArgumentNullException">TBD</exception>
        public SourceShape(Outlet<TOut> outlet)
        {
            Outlet = outlet ?? throw new ArgumentNullException(nameof(outlet));
            Outlets = ImmutableArray.Create<Outlet>(outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TOut> Outlet;

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets => ImmutableArray<Inlet>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy() => new SourceShape<TOut>((Outlet<TOut>) Outlet.CarbonCopy());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the size of the specified <paramref name="inlets"/> array is zero
        /// or the size of the specified <paramref name="outlets"/> array is one.
        /// </exception>
        /// <returns>TBD</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 0)
                throw new ArgumentException("Proposed inlets do not fit SourceShape", nameof(inlets));
            if (outlets.Length != 1)
                throw new ArgumentException("Proposed outlets do not fit SourceShape", nameof(outlets));

            return new SourceShape<TOut>(outlets[0] as Outlet<TOut>);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;

            return obj is SourceShape<TOut> shape && Equals(shape);
        }

        /// <inheritdoc/>
        private bool Equals(SourceShape<TOut> other) => Outlet.Equals(other.Outlet);

        /// <inheritdoc/>
        public override int GetHashCode() => Outlet.GetHashCode();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IFlowShape
    {
        /// <summary>
        /// TBD
        /// </summary>
        Inlet Inlet { get; }
        /// <summary>
        /// TBD
        /// </summary>
        Outlet Outlet { get; }
    }

    /// <summary>
    /// A Flow <see cref="Shape"/> has exactly one input and one output, it looks from the
    /// outside like a pipe (but it can be a complex topology of streams within of course).
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public sealed class FlowShape<TIn, TOut> : Shape, IFlowShape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="outlet">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when either the specified <paramref name="inlet"/> or <paramref name="outlet"/> is undefined.
        /// </exception>
        public FlowShape(Inlet<TIn> inlet, Outlet<TOut> outlet)
        {
            Inlet = inlet ?? throw new ArgumentNullException(nameof(inlet), "FlowShape expected non-null inlet");
            Outlet = outlet ?? throw new ArgumentNullException(nameof(outlet), "FlowShape expected non-null outlet");
            Inlets = ImmutableArray.Create<Inlet>(inlet);
            Outlets = ImmutableArray.Create<Outlet>(outlet);
        }

        Inlet IFlowShape.Inlet => Inlet;

        Outlet IFlowShape.Outlet => Outlet;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> Inlet { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Outlet { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy()
            => new FlowShape<TIn, TOut>((Inlet<TIn>) Inlet.CarbonCopy(), (Outlet<TOut>) Outlet.CarbonCopy());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the size of the specified <paramref name="inlets"/> array is one
        /// or the size of the specified <paramref name="outlets"/> array is one.
        /// </exception>
        /// <returns>TBD</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 1)
                throw new ArgumentException("Proposed inlets do not fit FlowShape", nameof(inlets));
            if (outlets.Length != 1)
                throw new ArgumentException("Proposed outlets do not fit FlowShape", nameof(outlets));

            return new FlowShape<TIn, TOut>(inlets[0] as Inlet<TIn>, outlets[0] as Outlet<TOut>);
        }
    }

    /// <summary>
    /// A Sink <see cref="Shape"/> has exactly one input and no outputs, it models a data sink.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    public sealed class SinkShape<TIn> : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TIn> Inlet;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="inlet"/> is undefined.
        /// </exception>
        public SinkShape(Inlet<TIn> inlet)
        {
            Inlet = inlet ?? throw new ArgumentNullException(nameof(inlet), "SinkShape expected non-null inlet");
            Inlets = ImmutableArray.Create<Inlet>(inlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets => ImmutableArray<Outlet>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy() => new SinkShape<TIn>((Inlet<TIn>) Inlet.CarbonCopy());

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the size of the specified <paramref name="inlets"/> array is zero
        /// or the size of the specified <paramref name="outlets"/> array is one.
        /// </exception>
        /// <returns>TBD</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (outlets.Length != 0)
                throw new ArgumentException("Proposed outlets do not fit SinkShape", nameof(outlets));
            if (inlets.Length != 1)
                throw new ArgumentException("Proposed inlets do not fit SinkShape", nameof(inlets));

            return new SinkShape<TIn>(inlets[0] as Inlet<TIn>);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;

            return obj is SinkShape<TIn> shape && Equals(shape);
        }

        private bool Equals(SinkShape<TIn> other) => Equals(Inlet, other.Inlet);

        /// <inheritdoc/>
        public override int GetHashCode() => Inlet.GetHashCode();
    }

    /// <summary>
    /// A bidirectional flow of elements that consequently has two inputs and two outputs.
    /// </summary>
    /// <typeparam name="TIn1">TBD</typeparam>
    /// <typeparam name="TOut1">TBD</typeparam>
    /// <typeparam name="TIn2">TBD</typeparam>
    /// <typeparam name="TOut2">TBD</typeparam>
    public sealed class BidiShape<TIn1, TOut1, TIn2, TOut2> : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TIn1> Inlet1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<TIn2> Inlet2;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TOut1> Outlet1;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<TOut2> Outlet2;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="in1">TBD</param>
        /// <param name="out1">TBD</param>
        /// <param name="in2">TBD</param>
        /// <param name="out2">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when either the specified <paramref name="in1"/>, <paramref name="out1"/>,
        /// <paramref name="in2"/>, or <paramref name="out2"/> is undefined.
        /// </exception>
        public BidiShape(Inlet<TIn1> in1, Outlet<TOut1> out1, Inlet<TIn2> in2, Outlet<TOut2> out2)
        {
            Inlet1 = in1 ?? throw new ArgumentNullException(nameof(in1));
            Inlet2 = in2 ?? throw new ArgumentNullException(nameof(in2));
            Outlet1 = out1 ?? throw new ArgumentNullException(nameof(out1));
            Outlet2 = out2 ?? throw new ArgumentNullException(nameof(out2));

            Inlets = ImmutableArray.Create<Inlet>(Inlet1, Inlet2);
            Outlets = ImmutableArray.Create<Outlet>(Outlet1, Outlet2);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="top">TBD</param>
        /// <param name="bottom">TBD</param>
        public BidiShape(FlowShape<TIn1, TOut1> top, FlowShape<TIn2, TOut2> bottom)
            : this(top.Inlet, top.Outlet, bottom.Inlet, bottom.Outlet)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Inlet> Inlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override ImmutableArray<Outlet> Outlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Shape DeepCopy()
        {
            return new BidiShape<TIn1, TOut1, TIn2, TOut2>(
                (Inlet<TIn1>) Inlet1.CarbonCopy(),
                (Outlet<TOut1>) Outlet1.CarbonCopy(),
                (Inlet<TIn2>) Inlet2.CarbonCopy(),
                (Outlet<TOut2>) Outlet2.CarbonCopy());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inlets">TBD</param>
        /// <param name="outlets">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the size of the specified <paramref name="inlets"/> array is two
        /// or the size of the specified <paramref name="outlets"/> array is two.
        /// </exception>
        /// <returns>TBD</returns>
        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 2) throw new ArgumentException($"Proposed inlets [{string.Join(", ", inlets)}] don't fit BidiShape");
            if (outlets.Length != 2) throw new ArgumentException($"Proposed outlets [{string.Join(", ", outlets)}] don't fit BidiShape");

            return new BidiShape<TIn1, TOut1, TIn2, TOut2>((Inlet<TIn1>)inlets[0], (Outlet<TOut1>)outlets[0], (Inlet<TIn2>)inlets[1], (Outlet<TOut2>)outlets[1]);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public Shape Reversed() => new BidiShape<TIn2, TOut2, TIn1, TOut1>(Inlet2, Outlet2, Inlet1, Outlet1);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class BidiShape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn1">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="top">TBD</param>
        /// <param name="bottom">TBD</param>
        /// <returns>TBD</returns>
        public static BidiShape<TIn1, TOut1, TIn2, TOut2> FromFlows<TIn1, TOut1, TIn2, TOut2>(
            FlowShape<TIn1, TOut1> top, FlowShape<TIn2, TOut2> bottom)
            => new BidiShape<TIn1, TOut1, TIn2, TOut2>(top.Inlet, top.Outlet, bottom.Inlet, bottom.Outlet);
    }
}
