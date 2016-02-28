using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Implementation;

namespace Akka.Streams
{
    /// <summary>
    /// An input port of a <see cref="IModule"/>. This type logically belongs
    /// into the impl package but must live here due to how `sealed` works.
    /// It is also used in the Java DSL for “untyped Inlets” as a work-around
    /// for otherwise unreasonable existential types.
    /// </summary>
    public abstract class InPort
    {
        internal int Id = -1;
    }

    /// <summary>
    /// An output port of a StreamLayout.Module. This type logically belongs
    /// into the impl package but must live here due to how `sealed` works.
    /// It is also used in the Java DSL for “untyped Outlets” as a work-around
    /// for otherwise unreasonable existential types.
    /// </summary>
    public abstract class OutPort 
    {
        internal int Id = -1;
    }

    /// <summary>
    /// An Inlet is a typed input to a Shape. Its partner in the Module view 
    /// is the InPort(which does not bear an element type because Modules only 
    /// express the internal structural hierarchy of stream topologies).
    /// </summary>
    public abstract class Inlet : InPort
    {
        public static Inlet<T> Create<T>(Inlet inlet)
        {
            return inlet as Inlet<T> ?? new Inlet<T>(inlet.Name);
        }

        public readonly string Name;
        public abstract Inlet CarbonCopy();

        protected Inlet(string name)
        {
            if (name == null) throw new ArgumentException("Inlet name must be defined");
            Name = name;
        }

        public sealed override string ToString()
        {
            return Name;
        }
    }

    public sealed class Inlet<T> : Inlet
    {
        public Inlet(string name) : base(name) { }

        internal Inlet<TOther> As<TOther>()
        {
            return Inlet.Create<TOther>(this);
        }

        public override Inlet CarbonCopy()
        {
            return new Inlet<T>(Name);
        }
    }

    /// <summary>
    /// An Outlet is a typed output to a Shape. Its partner in the Module view
    /// is the OutPort(which does not bear an element type because Modules only
    /// express the internal structural hierarchy of stream topologies).
    /// </summary>
    public abstract class Outlet : OutPort
    {
        public static Outlet<T> Create<T>(Outlet outlet)
        {
            return outlet as Outlet<T> ?? new Outlet<T>(outlet.Name);
        }

        public readonly string Name;
        public abstract Outlet CarbonCopy();

        protected Outlet(string name)
        {
            Name = name;
        }

        public sealed override string ToString()
        {
            return Name;
        }
    }

    public sealed class Outlet<T> : Outlet
    {
        public Outlet(string name) : base(name) { }

        internal Outlet<TOther> As<TOther>()
        {
            return Outlet.Create<TOther>(this);
        }

        public override Outlet CarbonCopy()
        {
            return new Outlet<T>(Name);
        }
    }

    /// <summary>
    /// A Shape describes the inlets and outlets of a [[Graph]]. In keeping with the
    /// philosophy that a Graph is a freely reusable blueprint, everything that
    /// matters from the outside are the connections that can be made with it,
    /// otherwise it is just a black box.
    /// </summary>
    public abstract class Shape : ICloneable
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
        public abstract Shape DeepCopy();

        /// <summary>
        /// Create a copy of this Shape object, returning the same type as the
        /// original but containing the ports given within the passed-in Shape.
        /// </summary>
        public abstract Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets);

        /// <summary>
        /// Compare this to another shape and determine whether the set of ports is the same (ignoring their ordering).
        /// </summary>
        public bool HasSamePortsAs(Shape shape)
        {
            var inlets = new HashSet<Inlet>(Inlets);
            var outlets = new HashSet<Outlet>(Outlets);

            return inlets.SetEquals(shape.Inlets) && outlets.SetEquals(shape.Outlets);
        }

        /// <summary>
        /// Compare this to another shape and determine whether the arrangement of ports is the same (including their ordering).
        /// </summary>
        public bool HasSamePortsAndShapeAs(Shape shape)
        {
            return Inlets.Equals(shape.Inlets) && Outlets.Equals(shape.Outlets);
        }

        public object Clone()
        {
            return DeepCopy();
        }

        public sealed override string ToString()
        {
            return $"{GetType().Name}([{string.Join(", ", Inlets)}] [{string.Join(", ", Outlets)}])";
        }
    }

    /// <summary>
    /// This <see cref="Shape"/> is used for graphs that have neither open inputs nor open
    /// outputs. Only such a <see cref="IGraph{TShape,TMaterializer}"/> can be materialized by a <see cref="IMaterializer"/>.
    /// </summary>
    public class ClosedShape : Shape
    {
        public static readonly ClosedShape Instance = new ClosedShape();
        
        private ClosedShape() { }

        public override ImmutableArray<Inlet> Inlets => ImmutableArray<Inlet>.Empty;
        public override ImmutableArray<Outlet> Outlets => ImmutableArray<Outlet>.Empty;
        public override Shape DeepCopy()
        {
            return this;
        }

        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Any()) throw new ArgumentException("Proposed inlets do not fit ClosedShape", "inlets");
            if (outlets.Any()) throw new ArgumentException("Proposed outlets do not fit ClosedShape", "outlets");

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
        public AmorphousShape(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            Inlets = inlets;
            Outlets = outlets;
        }

        public override ImmutableArray<Inlet> Inlets { get; }
        public override ImmutableArray<Outlet> Outlets { get; }

        public override Shape DeepCopy()
        {
            return new AmorphousShape(Inlets.Select(i => i.CarbonCopy()).ToImmutableArray(), Outlets.Select(o => o.CarbonCopy()).ToImmutableArray());
        }

        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            return new AmorphousShape(inlets, outlets);
        }
    }

    /// <summary>
    /// A Source <see cref="Shape"/> has exactly one output and no inputs, it models a source of data.
    /// </summary>
    public sealed class SourceShape<TOut> : Shape
    {
        private readonly ImmutableArray<Outlet> _outlets;
        public readonly Outlet<TOut> Outlet;

        public SourceShape(Outlet<TOut> outlet)
        {
            if (outlet == null) throw new ArgumentNullException("outlet");
            Outlet = outlet;
            Outlets = ImmutableArray.Create<Outlet>(outlet);
        }

        public override ImmutableArray<Inlet> Inlets => ImmutableArray<Inlet>.Empty;
        public override ImmutableArray<Outlet> Outlets { get; }

        public override Shape DeepCopy()
        {
            return new SourceShape<TOut>(Outlet);
        }

        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 0) throw new ArgumentException("Proposed inlets do not fit SourceShape", "inlets");
            if (outlets.Length != 1) throw new ArgumentException("Proposed outlets do not fit SourceShape", "outlets");

            return new SourceShape<TOut>(outlets[0] as Outlet<TOut>);
        }
    }

    /// <summary>
    /// A Flow <see cref="Shape"/> has exactly one input and one output, it looks from the
    /// outside like a pipe (but it can be a complex topology of streams within of course).
    /// </summary>
    public sealed class FlowShape<TIn, TOut> : Shape
    {
        public readonly Inlet<TIn> Inlet;
        public readonly Outlet<TOut> Outlet;

        public FlowShape(Inlet<TIn> inlet, Outlet<TOut> outlet)
        {
            if (inlet == null) throw new ArgumentNullException("inlet", "FlowShape expected non-null inlet");
            if (outlet == null) throw new ArgumentNullException("outlet", "FlowShape expected non-null outlet");

            Inlet = inlet;
            Outlet = outlet;
            Inlets = ImmutableArray.Create<Inlet>(inlet);
            Outlets = ImmutableArray.Create<Outlet>(outlet);
        }

        public override ImmutableArray<Inlet> Inlets { get; }
        public override ImmutableArray<Outlet> Outlets { get; }

        public override Shape DeepCopy()
        {
            return new FlowShape<TIn, TOut>(Inlet, Outlet);
        }

        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (inlets.Length != 1) throw new ArgumentException("Proposed inlets do not fit FlowShape", "inlets");
            if (outlets.Length != 1) throw new ArgumentException("Proposed outlets do not fit FlowShape", "outlets");

            return new FlowShape<TIn, TOut>(inlets[0] as Inlet<TIn>, outlets[0] as Outlet<TOut>);
        }
    }

    /// <summary>
    /// A Sink <see cref="Shape"/> has exactly one input and no outputs, it models a data sink.
    /// </summary>
    public sealed class SinkShape<TIn> : Shape
    {
        public readonly Inlet<TIn> Inlet;

        public SinkShape(Inlet<TIn> inlet)
        {
            if (inlet == null) throw new ArgumentNullException("inlet");
            Inlet = inlet;
            Inlets = ImmutableArray.Create<Inlet>(inlet);
        }

        public override ImmutableArray<Inlet> Inlets { get; }
        public override ImmutableArray<Outlet> Outlets => ImmutableArray<Outlet>.Empty;

        public override Shape DeepCopy()
        {
            return new SinkShape<TIn>(Inlet);
        }

        public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
        {
            if (outlets.Length != 0) throw new ArgumentException("Proposed outlets do not fit SinkShape", "outlets");
            if (inlets.Length != 1) throw new ArgumentException("Proposed inlets do not fit SinkShape", "inlets");

            return new SinkShape<TIn>(inlets[0] as Inlet<TIn>);
        }
    }

    /// <summary>
    /// A bidirectional flow of elements that consequently has two inputs and two outputs.
    /// </summary>
    public sealed class BidiShape<TIn1, TOut1, TIn2, TOut2> : Shape
    {
        public readonly Inlet<TIn1> Inlet1;
        public readonly Inlet<TIn2> Inlet2;
        public readonly Outlet<TOut1> Outlet1;
        public readonly Outlet<TOut2> Outlet2;

        public BidiShape(Inlet<TIn1> in1, Outlet<TOut1> out1, Inlet<TIn2> in2, Outlet<TOut2> out2)
        {
            if (in1 == null) throw new ArgumentNullException("in1");
            if (out1 == null) throw new ArgumentNullException("out1");
            if (in2 == null) throw new ArgumentNullException("in2");
            if (out2 == null) throw new ArgumentNullException("out2");

            Inlet1 = in1;
            Inlet2 = in2;
            Outlet1 = out1;
            Outlet2 = out2;

            Inlets = ImmutableArray.Create<Inlet>(Inlet1, Inlet2);
            Outlets = ImmutableArray.Create<Outlet>(Outlet1, Outlet2);
        }

        public BidiShape(FlowShape<TIn1, TOut1> top, FlowShape<TIn2, TOut2> bottom)
            : this(top.Inlet, top.Outlet, bottom.Inlet, bottom.Outlet)
        {
        }

        public override ImmutableArray<Inlet> Inlets { get; }

        public override ImmutableArray<Outlet> Outlets { get; }

        public override Shape DeepCopy()
        {
            return new BidiShape<TIn1, TOut1, TIn2, TOut2>(Inlet1, Outlet1, Inlet2, Outlet2);
        }

        public override Shape CopyFromPorts(ImmutableArray<Inlet> i, ImmutableArray<Outlet> o)
        {
            if (i.Length != 2) throw new ArgumentException(string.Format("Proposed inlets [{0}] don't fit BidiShape", string.Join(", ", (IEnumerable<Inlet>)i)));
            if (o.Length != 2) throw new ArgumentException(string.Format("Proposed outlets [{0}] don't fit BidiShape", string.Join(", ", (IEnumerable<Outlet>)o)));

            return new BidiShape<TIn1, TOut1, TIn2, TOut2>((Inlet<TIn1>)i[0], (Outlet<TOut1>)o[0], (Inlet<TIn2>)i[1], (Outlet<TOut2>)o[1]);
        }

        public Shape Reversed()
        {
            return new BidiShape<TIn2, TOut2, TIn1, TOut1>(Inlet2, Outlet2, Inlet1, Outlet1);
        }
    }
}