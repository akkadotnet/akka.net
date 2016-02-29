using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation
{
    public static class StreamLayout
    {
        //TODO: Materialization order
        //TODO: Special case linear composites
        //TODO: Cycles

#if !DEBUG
        public const bool IsDebug = false;
#endif

#if DEBUG
        public const bool IsDebug = true;
#endif

        #region Materialized Value Node types

        public interface IMaterializedValueNode { }
        public sealed class Combine : IMaterializedValueNode
        {
            public readonly Func<object, object, object> Combinator;
            public readonly IMaterializedValueNode Left;
            public readonly IMaterializedValueNode Right;

            public Combine(Func<object, object, object> combinator, IMaterializedValueNode left, IMaterializedValueNode right)
            {
                Combinator = combinator;
                Left = left;
                Right = right;
            }
        }

        public sealed class Atomic : IMaterializedValueNode
        {
            public readonly IModule Module;

            public Atomic(IModule module)
            {
                Module = module;
            }
        }

        public sealed class Transform : IMaterializedValueNode
        {
            public readonly Func<object, object> Transformator;
            public readonly IMaterializedValueNode Node;

            public Transform(Func<object, object> transformator, IMaterializedValueNode node)
            {
                Transformator = transformator;
                Node = node;
            }
        }

        public sealed class Ignore : IMaterializedValueNode
        {
            public static readonly Ignore Instance = new Ignore();
            private Ignore() { }
        }

        #endregion

        public static void Validate(IModule module, int level = 0, bool shouldPrint = false, IDictionary<object, int> idMap = null)
        {
            idMap = idMap ?? new Dictionary<object, int>();
            var i = 1;
            Func<int> ids = () => i++;
            Func<object, int> id = obj =>
            {
                int x;
                if (!idMap.TryGetValue(obj, out x))
                {
                    x = ids();
                    idMap.Add(obj, x);
                }
                return x;
            };
            Func<InPort, string> inPort = inPorti => inPorti.ToString() + "@" + id(inPorti);
            Func<OutPort, string> outPort = o => o.ToString() + "@" + id(o);
            Func<IEnumerable<InPort>, string> ins = iterables => "[" + string.Join(", ", iterables.Select(inPort)) + "]";
            Func<IEnumerable<OutPort>, string> outs = iterablesOuts => "[" + string.Join(", ", iterablesOuts.Select(outPort)) + "]";
            Func<OutPort, InPort, string> pair = (outPortP, inPortP) => outPort(outPortP) + "->" + inPort(inPortP);
            Func<IEnumerable<KeyValuePair<OutPort, InPort>>, string> pairs = enumerable => "[" + string.Join(", ", enumerable.Select(p => pair(p.Key, p.Value))) + "]";

            var shape = module.Shape;
            var inset = shape.Inlets.ToImmutableHashSet();
            var outset = shape.Outlets.ToImmutableHashSet();
            var inports = module.InPorts.Cast<Inlet>().ToImmutableHashSet();
            var outports = module.OutPorts.Cast<Outlet>().ToImmutableHashSet();
            var problems = new List<string>();

            if (inset.Count != shape.Inlets.Count()) problems.Add("shape has duplicate inlets " + ins(shape.Inlets));
            if (!inset.SetEquals(inports)) problems.Add($"shape has extra {ins(inset.Except(inports))}, module has extra {ins(inports.Except(inset))}");
            var connectedInlets = inset.Intersect(module.Upstreams.Keys).ToArray();
            if (connectedInlets.Any()) problems.Add("found connected inlets " + ins(connectedInlets));
            if (outset.Count != shape.Outlets.Count()) problems.Add("shape has duplicate outlets " + outs(shape.Outlets));
            if (!outset.SetEquals(outports)) problems.Add($"shape has extra {outs(outset.Except(outports))}, module has extra {outs(outports.Except(outset))}");
            var connectedOutlets = outset.Intersect(module.Downstreams.Keys).ToArray();
            if (connectedOutlets.Any()) problems.Add("found connected outlets " + outs(connectedOutlets));

            var ups = module.Upstreams.ToImmutableHashSet();
            var ups2 = ups.Select(x => new KeyValuePair<OutPort, InPort>(x.Value, x.Key)).ToImmutableHashSet();
            var downs = module.Downstreams.ToImmutableHashSet();
            var inter = ups2.Intersect(downs);

            if (!downs.SetEquals(ups2)) problems.Add($"inconsistent maps: ups {pairs(ups2.Except(inter))} downs {pairs(downs.Except(inter))}");

            var allIn = ImmutableHashSet<InPort>.Empty;
            var duplicateIn = ImmutableHashSet<InPort>.Empty;
            var allOut = ImmutableHashSet<OutPort>.Empty;
            var duplicateOut = ImmutableHashSet<OutPort>.Empty;

            foreach (var subModule in module.SubModules)
            {
                allIn = allIn.Union(subModule.InPorts);
                duplicateIn = duplicateIn.Union(allIn.Intersect(subModule.InPorts));
                allOut = allOut.Union(subModule.OutPorts);
                duplicateOut = duplicateOut.Union(allOut.Intersect(subModule.OutPorts));
            }

            if (!duplicateIn.IsEmpty) problems.Add("duplicate ports in submodules " + ins(duplicateIn));
            if (!duplicateOut.IsEmpty) problems.Add("duplicate ports in submodules " + outs(duplicateOut));
            if (!module.IsSealed && inset.Except(allIn).Any()) problems.Add("foreign inlets " + ins(inset.Except(allIn)));
            if (!module.IsSealed && outset.Except(allOut).Any()) problems.Add("foreign outlets " + outs(outset.Except(allOut)));
            var unIn = allIn.Except(inset).ToImmutableHashSet().Except(module.Upstreams.Keys);
            if (unIn.Any() && !module.IsCopied) problems.Add("unconnected inlets " + ins(unIn));
            var unOut = allOut.Except(outset).ToImmutableHashSet().Except(module.Downstreams.Keys);
            if (unOut.Any() && !module.IsCopied) problems.Add("unconnected outlets " + outs(unOut));

            var atomics = Atomics(module.MaterializedValueComputation);
            var graphValues = module.SubModules.SelectMany(m => m is GraphModule ? ((GraphModule)m).SubModules : Enumerable.Empty<IModule>());
            var nonExistent = atomics.Except(module.SubModules).Except(graphValues).Except(new IModule[] { module });
            if (nonExistent.Any()) problems.Add("computation refers to non-existent modules " + string.Join(", ", nonExistent));

            if (shouldPrint || problems.Any())
            {
                //val indent = " " * (level * 2)
                //println(s"$indent${simpleName(this)}($shape): ${ins(inPorts)} ${outs(outPorts)}")
                //downstreams foreach { case (o, i) ⇒ println(s"$indent    ${out(o)} -> ${in(i)}") }
                //problems foreach (p ⇒ println(s"$indent  -!- $p"))
            }

            foreach (var subModule in module.SubModules)
            {
                Validate(subModule, level + 1, shouldPrint, idMap);
            }

            if (problems.Any() && !shouldPrint) throw new IllegalStateException(
                $"module inconsistent, found {problems.Count} problems:\n - {string.Join("\n - ", problems)}");
        }

        private static ImmutableHashSet<IModule> Atomics(IMaterializedValueNode node)
        {
            if (node is Ignore) return ImmutableHashSet<IModule>.Empty;
            if (node is Transform) return Atomics(((Transform)node).Node);
            if (node is Atomic) return ImmutableHashSet.Create(((Atomic)node).Module);
            Combine c;
            if ((c = node as Combine) != null) return Atomics(c.Left).Union(Atomics(c.Right));
            throw new ArgumentException("Couldn't extract atomics for node " + node.GetType());
        }
    }

    public interface IModule : IComparable<IModule>
    {
        Shape Shape { get; }

        /// <summary>
        /// Verify that the given Shape has the same ports and return a new module with that shape.
        /// Concrete implementations may throw UnsupportedOperationException where applicable.
        /// </summary>
        IModule ReplaceShape(Shape shape);

        IImmutableSet<InPort> InPorts { get; }
        IImmutableSet<OutPort> OutPorts { get; }
        bool IsRunnable { get; }

        bool IsSink { get; }
        bool IsSource { get; }
        bool IsFlow { get; }
        bool IsBidiFlow { get; }
        bool IsAtomic { get; }
        bool IsCopied { get; }

        /// <summary>
        /// Fuses this Module to <paramref name="that"/> Module by wiring together <paramref name="from"/> and <paramref name="to"/>,
        /// retaining the materialized value of `this` in the result
        /// </summary>
        /// <param name="that">A module to fuse with</param>
        /// <param name="from">The data source to wire</param>
        /// <param name="to">The data sink to wire</param>
        /// <returns>A module representing fusion of `this` and <paramref name="that"/></returns>
        IModule Fuse(IModule that, OutPort from, InPort to);

        /// <summary>
        /// Fuses this Module to <paramref name="that"/> Module by wiring together <paramref name="from"/> and <paramref name="to"/>,
        /// retaining the materialized value of `this` in the result, using the provided function <paramref name="matFunc"/>.
        /// </summary>
        /// <param name="that">A module to fuse with</param>
        /// <param name="from">The data source to wire</param>
        /// <param name="to">The data sink to wire</param>
        /// <param name="matFunc">The function to apply to the materialized values</param>
        /// <returns>A module representing fusion of `this` and <paramref name="that"/></returns>
        IModule Fuse<T1, T2, T3>(IModule that, OutPort from, InPort to, Func<T1, T2, T3> matFunc);

        /// <summary>
        /// Creates a new Module based on the current Module but with the given OutPort wired to the given InPort.
        /// </summary>
        /// <param name="from">The OutPort to wire.</param>
        /// <param name="to">The InPort to wire.</param>
        /// <returns>A new Module with the ports wired</returns>
        IModule Wire(OutPort from, InPort to);

        IModule TransformMaterializedValue<TMat, TMat2>(Func<TMat, TMat2> mapFunc);

        /// <summary>
        /// Creates a new Module which is `this` Module composed with <paramref name="that"/> Module.
        /// </summary>
        /// <param name="that">A Module to be composed with (cannot be itself)</param>
        /// <returns>A Module that represents the composition of `this` and <paramref name="that"/></returns>
        IModule Compose(IModule that);

        /// <summary>
        /// Creates a new Module which is `this` Module composed with <paramref name="that"/> Module,
        /// using the given function <paramref name="matFunc"/> to compose the materialized value of `this` with
        /// the materialized value of <paramref name="that"/>.
        /// </summary>
        /// <param name="that">A Module to be composed with (cannot be itself)</param>
        /// <param name="matFunc">A function which combines the materialized values</param>
        /// <typeparam name="T1">The type of the materialized value of `this`</typeparam>
        /// <typeparam name="T2">The type of the materialized value of <paramref name="that"/></typeparam>
        /// <typeparam name="T3">The type of the materialized value of the returned Module</typeparam>
        /// <returns>A Module that represents the composition of `this` and <paramref name="that"/></returns>
        IModule Compose<T1, T2, T3>(IModule that, Func<T1, T2, T3> matFunc);

        /// <summary>
        /// Creates a new Module which is `this` Module composed with <paramref name="that"/> Module.
        /// 
        /// The difference to compose(that) is that this version completely ignores the materialized value
        /// computation of <paramref name="that"/> while the normal version executes the computation and discards its result.
        /// This means that this version must not be used for user-provided <paramref name="that"/> modules because users may
        /// transform materialized values only to achieve some side-effect; it can only be
        /// used where we know that there is no meaningful computation to be done (like for
        /// MaterializedValueSource).
        /// </summary>
        /// <param name="that">a Module to be composed with (cannot be itself)</param>
        /// <returns>a Module that represents the composition of `this` and <paramref name="that"/></returns>
        IModule ComposeNoMaterialized(IModule that);

        /// <summary>
        /// Creates a new Module which contains `this` Module
        /// </summary>
        IModule Nest();

        // this cannot be set, since sets are changing ordering of modules
        // which must be kept for fusing to work
        ImmutableArray<IModule> SubModules { get; }
        bool IsSealed { get; }

        IImmutableDictionary<OutPort, InPort> Downstreams { get; }
        IImmutableDictionary<InPort, OutPort> Upstreams { get; }

        StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }
        IModule CarbonCopy();

        Attributes Attributes { get; }
        IModule WithAttributes(Attributes attributes);
    }

    public abstract class Module : IModule
    {
        private readonly Lazy<IImmutableSet<InPort>> _inports;
        private readonly Lazy<IImmutableSet<OutPort>> _outports;

        protected Module()
        {
            _inports = new Lazy<IImmutableSet<InPort>>(() => ImmutableHashSet.CreateRange(Shape.Inlets.Cast<InPort>()));
            _outports = new Lazy<IImmutableSet<OutPort>>(() => ImmutableHashSet.CreateRange(Shape.Outlets.Cast<OutPort>()));

            Downstreams = ImmutableDictionary<OutPort, InPort>.Empty;
            Upstreams = ImmutableDictionary<InPort, OutPort>.Empty;
        }

        public IImmutableSet<InPort> InPorts => _inports.Value;
        public IImmutableSet<OutPort> OutPorts => _outports.Value;
        public virtual bool IsRunnable => InPorts.Count == 0 && OutPorts.Count == 0;
        public virtual bool IsSink => InPorts.Count == 1 && OutPorts.Count == 0;
        public virtual bool IsSource => InPorts.Count == 0 && OutPorts.Count == 1;
        public virtual bool IsFlow => InPorts.Count == 1 && OutPorts.Count == 1;
        public virtual bool IsBidiFlow => InPorts.Count == 2 && OutPorts.Count == 2;
        public virtual bool IsAtomic => !SubModules.Any();
        public virtual bool IsCopied => false;
        public virtual bool IsFused => false;

        public virtual IModule Fuse(IModule other, OutPort @from, InPort to)
        {
            return Fuse<object, object, object>(other, from, to, Keep.Left<object, object, object>);
        }

        public virtual IModule Fuse<T1, T2, T3>(IModule other, OutPort @from, InPort to, Func<T1, T2, T3> matFunc)
        {
            return Compose(other, matFunc).Wire(from, to);
        }

        public virtual IModule Wire(OutPort @from, InPort to)
        {
            if (!OutPorts.Contains(from))
            {
                var message = Downstreams.ContainsKey(from)
                    ? $"The output port [{@from}] is already connected"
                    : $"The output port [{@from}] is not part of underlying graph";
                throw new ArgumentException(message);
            }

            if (!InPorts.Contains(to))
            {
                var message = Upstreams.ContainsKey(to)
                    ? $"The input port [{@from}] is already connected"
                    : $"The input port [{@from}] is not part of underlying graph";
                throw new ArgumentException(message);
            }

            return new CompositeModule(
                subModules: SubModules,
                shape: new AmorphousShape(
                    Shape.Inlets.Where(i => !i.Equals(to)).ToImmutableArray(), 
                    Shape.Outlets.Where(o => !o.Equals(from)).ToImmutableArray()),
                downstreams: Downstreams.SetItem(from, to),
                upstreams: Upstreams.SetItem(to, from),
                materializedValueComputation: MaterializedValueComputation,
                attributes: Attributes);
        }

        public virtual IModule TransformMaterializedValue<TMat, TMat2>(Func<TMat, TMat2> mapFunc)
        {
            return new CompositeModule(
                subModules: IsSealed ? ImmutableArray.Create(this as IModule) : SubModules,
                shape: Shape,
                downstreams: Downstreams,
                upstreams: Upstreams,
                materializedValueComputation: new StreamLayout.Transform(x => mapFunc((TMat)x), IsSealed
                    ? new StreamLayout.Atomic(this)
                    : MaterializedValueComputation),
                attributes: Attributes);
        }

        public virtual IModule Compose(IModule other)
        {
            return Compose<object, object, object>(other, Keep.Left<object, object, object>);
        }

        public virtual IModule Compose<T1, T2, T3>(IModule other, Func<T1, T2, T3> matFunc)
        {
            if (Equals(other, this))
                throw new ArgumentException("A module cannot be added to itself. You should pass a separate instance to compose().");
            if (SubModules.Contains(other))
                throw new ArgumentException("An existing submodule cannot be added again. All contained modules must be unique.");

            var modules1 = this.IsSealed ? ImmutableArray.Create<IModule>(this) : this.SubModules;
            var modules2 = other.IsSealed ? ImmutableArray.Create<IModule>(other) : other.SubModules;

            var matComputation1 = IsSealed ? new StreamLayout.Atomic(this) : MaterializedValueComputation;
            var matComputation2 = IsSealed ? new StreamLayout.Atomic(other) : MaterializedValueComputation;

            return new CompositeModule(
                subModules: modules1.Union(modules2).ToImmutableArray(),
                shape: new AmorphousShape(
                    Shape.Inlets.Union(other.Shape.Inlets).ToImmutableArray(), 
                    Shape.Outlets.Union(other.Shape.Outlets).ToImmutableArray()),
                downstreams: Downstreams.AddRange(other.Downstreams),
                upstreams: Upstreams.AddRange(other.Upstreams),
                materializedValueComputation: new StreamLayout.Combine((x, y) => matFunc((T1)x, (T2)y), matComputation1, matComputation2),
                attributes: Attributes);
        }

        public IModule ComposeNoMaterialized(IModule that)
        {
            if (ReferenceEquals(this, that)) throw new ArgumentException("A module cannot be added to itself. You should pass a separate instance to Compose().");
            if (SubModules.Contains(that)) throw new ArgumentException("An existing submodule cannot be added again. All contained modules must be unique.");

            var module1 = IsSealed ? ImmutableArray.Create <IModule>(this) : SubModules;
            var module2 = that.IsSealed ? ImmutableArray.Create<IModule>(that) : that.SubModules;

            var matComputation = IsSealed ? new StreamLayout.Atomic(this) : MaterializedValueComputation;

            return new CompositeModule(
                subModules: module1.Union(module2).ToImmutableArray(),
                shape: new AmorphousShape(
                    Shape.Inlets.Union(that.Shape.Inlets).ToImmutableArray(), 
                    Shape.Outlets.Union(that.Shape.Outlets).ToImmutableArray()),
                downstreams: Downstreams.AddRange(that.Downstreams),
                upstreams: Upstreams.AddRange(that.Upstreams),
                materializedValueComputation: matComputation,
                attributes: Attributes.None);
        }

        public virtual IModule Nest()
        {
            return new CompositeModule(
                subModules: ImmutableArray.Create(this as IModule),
                shape: Shape,
                /*
                 * Composite modules always maintain the flattened upstreams/downstreams map (i.e. they contain all the
                 * layout information of all the nested modules). Copied modules break the nesting, scoping them to the
                 * copied module. The MaterializerSession will take care of propagating the necessary Publishers and Subscribers
                 * from the enclosed scope to the outer scope.
                 */
                downstreams: Downstreams,
                upstreams: Upstreams,
                /*
                 * Wrapping like this shields the outer module from the details of the
                 * materialized value computation of its submodules.
                 */
                materializedValueComputation: new StreamLayout.Atomic(this),
                attributes: Attributes.None);
        }

        public bool IsSealed => IsAtomic || IsCopied;
        public virtual IImmutableDictionary<OutPort, InPort> Downstreams { get; private set; }
        public virtual IImmutableDictionary<InPort, OutPort> Upstreams { get; private set; }
        public virtual StreamLayout.IMaterializedValueNode MaterializedValueComputation => new StreamLayout.Atomic(this);

        public abstract Shape Shape { get; }
        public abstract IModule ReplaceShape(Shape shape);
        
        public abstract ImmutableArray<IModule> SubModules { get; }
        public abstract IModule CarbonCopy();
        public abstract Attributes Attributes { get; }
        public abstract IModule WithAttributes(Attributes attributes);
        public int CompareTo(IModule other)
        {
            return GetHashCode().CompareTo(other.GetHashCode());
        }
    }

    public sealed class EmptyModule : Module
    {
        public static readonly EmptyModule Instance = new EmptyModule();

        private EmptyModule() { }

        public override Shape Shape => ClosedShape.Instance;
        public override bool IsAtomic => false;
        public override bool IsRunnable => false;
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation => StreamLayout.Ignore.Instance;

        public override IModule ReplaceShape(Shape shape)
        {
            if (shape is ClosedShape) return this;
            else throw new NotSupportedException("Cannot replace the shape of empty module");
        }

        public override IModule Compose(IModule other) => other;

        public override IModule Compose<T1, T2, T3>(IModule other, Func<T1, T2, T3> matFunc)
        {
            throw new NotSupportedException("It is invalid to combine materialized value with EmptyModule");
        }

        public override IModule Nest() => this;

        public override ImmutableArray<IModule> SubModules { get; } = ImmutableArray<IModule>.Empty;

        public override IModule CarbonCopy() => this;

        public override Attributes Attributes => Attributes.None;

        public override IModule WithAttributes(Attributes attributes)
        {
            throw new NotSupportedException("EmptyModule cannot carry attributes");
        }
    }

    public sealed class CopiedModule : Module
    {
        public CopiedModule(Shape shape, Attributes attributes, IModule copyOf)
        {
            Shape = shape;
            Attributes = attributes;
            CopyOf = copyOf;
            SubModules = ImmutableArray.Create(copyOf);
        }

        public override ImmutableArray<IModule> SubModules { get; }
        public override Attributes Attributes { get; }

        public IModule CopyOf { get; }
        public override Shape Shape { get; }

        public override bool IsCopied => true;
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation => new StreamLayout.Atomic(this);

        public override IModule ReplaceShape(Shape shape)
        {
            if (!Shape.HasSamePortsAs(shape))
                throw new ArgumentException("CopiedModule requires shape with same ports to replace", "shape");

            return new CopiedModule(shape, Attributes, CopyOf);
        }

        public override IModule CarbonCopy() => new CopiedModule(Shape, Attributes, CopyOf);

        public override IModule WithAttributes(Attributes attributes) => new CopiedModule(Shape, attributes, CopyOf);
    }

    public sealed class CompositeModule : Module
    {
        private readonly IImmutableDictionary<OutPort, InPort> _downstreams;
        private readonly IImmutableDictionary<InPort, OutPort> _upstreams;

        public CompositeModule(ImmutableArray<IModule> subModules,
            Shape shape,
            IImmutableDictionary<OutPort, InPort> downstreams,
            IImmutableDictionary<InPort, OutPort> upstreams,
            StreamLayout.IMaterializedValueNode materializedValueComputation,
            Attributes attributes)
        {
            SubModules = subModules;
            Shape = shape;
            _downstreams = downstreams;
            _upstreams = upstreams;
            MaterializedValueComputation = materializedValueComputation;
            Attributes = attributes;
        }

        public override Shape Shape { get; }
        public override Attributes Attributes { get; }
        public override ImmutableArray<IModule> SubModules { get; }
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        public override IModule ReplaceShape(Shape shape)
        {
            if (!Shape.HasSamePortsAs(shape))
                throw new ArgumentException("CombinedModule requires shape with same ports to replace", nameof(shape));

            return new CompositeModule(SubModules, shape, Downstreams, Upstreams, MaterializedValueComputation, Attributes);
        }

        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        public override IModule WithAttributes(Attributes attributes) => new CompositeModule(SubModules, Shape.DeepCopy(), Downstreams, Upstreams, MaterializedValueComputation, Attributes);
    }

    public sealed class FusedModule : Module
    {
        private readonly IImmutableDictionary<OutPort, InPort> _downstreams;
        private readonly IImmutableDictionary<InPort, OutPort> _upstreams;

        public readonly Streams.Fusing.StructuralInfo Info;

        public FusedModule(
            ImmutableArray<IModule> subModules,
            Shape shape,
            IImmutableDictionary<OutPort, InPort> downstreams,
            IImmutableDictionary<InPort, OutPort> upstreams,
            StreamLayout.IMaterializedValueNode materializedValueComputation,
            Attributes attributes,
            Streams.Fusing.StructuralInfo info)
        {
            SubModules = subModules;
            Shape = shape;
            _downstreams = downstreams;
            _upstreams = upstreams;
            MaterializedValueComputation = materializedValueComputation;
            Attributes = attributes;
            Info = info;
        }

        public override bool IsFused => true;
        public override IImmutableDictionary<OutPort, InPort> Downstreams => _downstreams;
        public override IImmutableDictionary<InPort, OutPort> Upstreams => _upstreams;
        public override StreamLayout.IMaterializedValueNode MaterializedValueComputation { get; }

        public override Shape Shape { get; }
        public override ImmutableArray<IModule> SubModules { get; }
        public override Attributes Attributes { get; }

        public override IModule ReplaceShape(Shape shape)
        {
            if (!shape.HasSamePortsAndShapeAs(Shape))
                throw new ArgumentException("FusedModule requires shape with the same ports as existing one", nameof(shape));
            return new FusedModule(SubModules, shape, Downstreams, Upstreams, MaterializedValueComputation, Attributes, Info);
        }

        public override IModule CarbonCopy() => new CopiedModule(Shape.DeepCopy(), Attributes, this);

        public override IModule WithAttributes(Attributes attributes) => new FusedModule(SubModules, Shape, Downstreams, Upstreams, MaterializedValueComputation, attributes, Info);
    }

    internal sealed class VirtualProcessor<T> : IProcessor<T, T>
    {
        #region internal classes

        internal interface ITermination { }
        internal struct Allowed : ITermination
        {
            public static readonly Allowed Instance = new Allowed();
        }
        internal struct Completed : ITermination
        {
            public static readonly Completed Instance = new Completed();
        }
        internal struct Failed : ITermination
        {
            public readonly Exception Reason;

            public Failed(Exception reason) : this()
            {
                Reason = reason;
            }
        }

        private sealed class Sub : AtomicCounterLong, ISubscription
        {
            private readonly ISubscription _subscription;
            private readonly AtomicReference<object> _subscriptionStatus;

            public Sub(ISubscription subscription, AtomicReference<object> subscriptionStatus)
            {
                _subscription = subscription;
                _subscriptionStatus = subscriptionStatus;
            }

            public void Request(long n)
            {
                var current = Current;
                while (true)
                {
                    if (current < 0) _subscription.Request(n);
                    else if (CompareAndSet(current, current + n)) ;
                    else continue;
                    break;
                }
            }

            public void Cancel()
            {
                _subscriptionStatus.Value = (object)InnertSubscriber;
                _subscription.Cancel();
            }

            public void CloseLatch()
            {
                var requested = GetAndSet(-1);
                if (requested > 0) _subscription.Request(requested);
            }
        }

        #endregion

        private static readonly CancellingSubscriber<T> InnertSubscriber = new CancellingSubscriber<T>();

        private readonly AtomicReference<object> _subscriptionStatus = new AtomicReference<object>();
        private readonly AtomicReference<ITermination> _terminationStatus = new AtomicReference<ITermination>();

        public void Subscribe(ISubscriber<T> subscriber)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);

            if (_subscriptionStatus.CompareAndSet(null, subscriber))
            {
                /* wait for OnSubscribe */
            }
            else
            {
                var status = _subscriptionStatus.Value;
                if (status is ISubscriber<T>) ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "VirtualProcessor");
                else if (status is Sub)
                {
                    var sub = (Sub)status;
                    try
                    {
                        _subscriptionStatus.Value = subscriber;
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, sub);
                        sub.CloseLatch(); // allow onNext only now
                        var s = _terminationStatus.Value;
                        if (_subscriptionStatus.CompareAndSet(s, Allowed.Instance))
                        {
                            if (s is Completed) ReactiveStreamsCompliance.TryOnComplete(subscriber);
                            else if (s is Failed) ReactiveStreamsCompliance.TryOnError(subscriber, ((Failed)s).Reason);
                        }
                    }
                    catch (Exception)
                    {
                        sub.Cancel();
                    }
                }
            }
        }

        public void OnSubscribe(ISubscription subscription)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);

            var wrapped = new Sub(subscription, _subscriptionStatus);
            if (_subscriptionStatus.CompareAndSet(null, wrapped))
            {
                /* wait for subscriber */
            }
            else
            {
                var value = _subscriptionStatus.Value;
                if (value is ISubscriber<T>)
                {
                    var sub = (ISubscriber<T>)value;
                    var terminationStatus = _terminationStatus.Value;
                    if (terminationStatus is Allowed)
                    {
                        /*
                         * There is a race condition here: if this thread reads the subscriptionStatus after
                         * set set() in subscribe() but then sees the terminationStatus before the getAndSet()
                         * is published then we will rely upon the downstream Subscriber for cancelling this
                         * Subscription. I only mention this because the TCK requires that we handle this here
                         * (since the manualSubscriber used there does not expose this behavior).
                         */
                        subscription.Cancel();
                    }
                    else
                    {
                        ReactiveStreamsCompliance.TryOnSubscribe(sub, wrapped);
                        wrapped.CloseLatch(); // allow OnNext only now
                        _terminationStatus.Value = Allowed.Instance;
                    }
                }
                else if (value is ISubscription)
                    subscription.Cancel(); // reject further Subscriptions
            }
        }

        public void OnNext(T element)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(element);
            ReactiveStreamsCompliance.TryOnNext(_subscriptionStatus.Value as ISubscriber<T>, element);
        }

        public void OnError(Exception cause)
        {
            ReactiveStreamsCompliance.RequireNonNullException(cause);
            if (_terminationStatus.CompareAndSet(null, new Failed(cause))) /* let it be picked up by Subscribe() */ ;
            else ReactiveStreamsCompliance.TryOnError(_subscriptionStatus.Value as ISubscriber<T>, cause);
        }

        public void OnComplete()
        {
            if (_terminationStatus.CompareAndSet(null, Completed.Instance)) /* let it be picked up by Subscribe() */ ;
            else ReactiveStreamsCompliance.TryOnComplete(_subscriptionStatus.Value as ISubscriber<T>);
        }

        void ISubscriber.OnNext(object element)
        {
            OnNext((T)element);
        }

        void IPublisher.Subscribe(ISubscriber subscriber)
        {
            Subscribe((ISubscriber<T>)subscriber);
        }
    }

    internal abstract class MaterializerSession<TMat>
    {
        public class MaterializationPanicException : Exception
        {
            public MaterializationPanicException(Exception innerException) : base("Materialization aborted.", innerException)
            {
            }

            protected MaterializationPanicException(SerializationInfo info, StreamingContext context) : base(info, context)
            {
            }
        }

        protected readonly IModule TopLevel;
        protected readonly Attributes InitialAttributes;

        private readonly LinkedList<IDictionary<InPort, ISubscriber>> _subscribersStack = new LinkedList<IDictionary<InPort, ISubscriber>>();
        private readonly LinkedList<IDictionary<OutPort, IPublisher>> _publishersStack = new LinkedList<IDictionary<OutPort, IPublisher>>();
        private readonly IDictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>> _materializedValueSources =
            new Dictionary<StreamLayout.IMaterializedValueNode, LinkedList<IMaterializedValueSource>>();

        /// <summary>
        /// Please note that this stack keeps track of the scoped modules wrapped in CopiedModule but not the CopiedModule
        /// itself. The reason is that the CopiedModule itself is only needed for the enterScope and exitScope methods but
        /// not elsewhere. For this reason they are just simply passed as parameters to those methods.
        /// 
        /// The reason why the encapsulated (copied) modules are stored as mutable state to save subclasses of this class
        /// from passing the current scope around or even knowing about it.
        /// </summary>
        private readonly LinkedList<IModule> _moduleStack = new LinkedList<IModule>();

        protected MaterializerSession(IModule topLevel, Attributes initialAttributes)
        {
            TopLevel = topLevel;
            InitialAttributes = initialAttributes;
        }

        private IDictionary<InPort, ISubscriber> Subscribers => _subscribersStack.First.Value;
        private IDictionary<OutPort, IPublisher> Publishers => _publishersStack.First.Value;
        private IModule CurrentLayout => _moduleStack.First.Value;

        ///<summary>
        /// Enters a copied module and establishes a scope that prevents internals to leak out and interfere with copies
        /// of the same module.
        /// We don't store the enclosing CopiedModule itself as state since we don't use it anywhere else than exit and enter
        /// </summary>
        private void EnterScope(CopiedModule enclosing)
        {
            _subscribersStack.AddFirst(new Dictionary<InPort, ISubscriber>());
            _publishersStack.AddFirst(new Dictionary<OutPort, IPublisher>());
            _moduleStack.AddFirst(enclosing.CopyOf);
        }

        /// <summary>
        /// Exits the scope of the copied module and propagates Publishers/Subscribers to the enclosing scope assigning
        /// them to the copied ports instead of the original ones (since there might be multiple copies of the same module
        /// leading to port identity collisions)
        /// We don't store the enclosing CopiedModule itself as state since we don't use it anywhere else than exit and enter
        /// </summary>
        private void ExitScope(CopiedModule enclosing)
        {
            var scopeSubscribers = Subscribers;
            var scopePublishers = Publishers;

            _subscribersStack.RemoveFirst();
            _publishersStack.RemoveFirst();
            _moduleStack.RemoveFirst();

            // When we exit the scope of a copied module,  pick up the Subscribers/Publishers belonging to exposed ports of
            // the original module and assign them to the copy ports in the outer scope that we will return to
            var inZip = enclosing.CopyOf.Shape.Inlets.Zip(enclosing.Shape.Inlets, (original, exposed) =>
                new KeyValuePair<Inlet, Inlet>(original, exposed));

            foreach (var kv in inZip)
                AssignPort(kv.Value, scopeSubscribers[kv.Key]);

            var outZip = enclosing.CopyOf.Shape.Outlets.Zip(enclosing.Shape.Outlets, (original, exposed) =>
                new KeyValuePair<Outlet, Outlet>(original, exposed));

            foreach (var kv in outZip)
                AssignPort(kv.Value, scopePublishers[kv.Key]);
        }

        public TMat Materialize()
        {
            if (TopLevel is EmptyModule)
                throw new InvalidOperationException("An empty module cannot be materialized (EmptyModule was given)");

            if (!TopLevel.IsRunnable)
                throw new InvalidOperationException("The top level module cannot be materialized because it has unconnected ports");

            try
            {
                return MaterializeModule(TopLevel, InitialAttributes.And(TopLevel.Attributes));
            }
            catch (Exception cause)
            {
                // PANIC!!! THE END OF THE MATERIALIZATION IS NEAR!
                // Cancels all intermediate Publishers and fails all intermediate Subscribers.
                // (This is an attempt to clean up after an exception during materialization)
                var errorPublisher = new ErrorPublisher<object>(new MaterializationPanicException(cause), string.Empty);
                foreach (var subMap in _subscribersStack)
                    foreach (var subscriber in subMap.Values)
                        ((IPublisher)errorPublisher).Subscribe(subscriber);

                foreach (var pubMap in _publishersStack)
                    foreach (var publisher in pubMap.Values)
                        publisher.Subscribe(new CancellingSubscriber<object>());

                throw;
            }
        }

        protected virtual Attributes MergeAttributes(Attributes parent, Attributes current)
        {
            return parent.And(current);
        }

        protected void RegisterSource(IMaterializedValueSource materializedSource)
        {
            LinkedList<IMaterializedValueSource> sources;
            if (_materializedValueSources.TryGetValue(materializedSource.Computation, out sources))
                sources.AddFirst(materializedSource);
            else _materializedValueSources.Add(materializedSource.Computation, new LinkedList<IMaterializedValueSource>(new[] { materializedSource }));
        }

        protected TMat MaterializeModule(IModule module, Attributes effectiveAttributes)
        {
            var materializedValues = new Dictionary<IModule, object>();

            foreach (var submodule in module.SubModules)
            {
                var subEffectiveAttributes = MergeAttributes(effectiveAttributes, submodule.Attributes);
                GraphStageModule graphStageModule;
                if ((graphStageModule = submodule as GraphStageModule) != null && graphStageModule.MaterializedValueComputation is MaterializedValueSource<TMat>)
                {
                    var copy = ((MaterializedValueSource<TMat>)graphStageModule.MaterializedValueComputation).CopySource();
                    RegisterSource(copy);
                    MaterializeAtomic(copy.Module, subEffectiveAttributes, materializedValues);
                }
                else if (submodule.IsAtomic) MaterializeAtomic(submodule, subEffectiveAttributes, materializedValues);
                else if (submodule is CopiedModule)
                {
                    var copied = submodule as CopiedModule;
                    EnterScope(copied);
                    materializedValues.Add(copied, MaterializeModule(copied, subEffectiveAttributes));
                    ExitScope(copied);
                }
                else materializedValues.Add(submodule, MaterializeComposite(submodule, subEffectiveAttributes));
            }

            var mat = ResolveMaterialized(module.MaterializedValueComputation, materializedValues, string.Empty);
            return (TMat)mat;
        }

        protected virtual object MaterializeComposite(IModule composite, Attributes effectiveAttributes)
        {
            return MaterializeModule(composite, effectiveAttributes);
        }

        protected abstract object MaterializeAtomic(IModule atomic, Attributes effectiveAttributes, IDictionary<IModule, object> materializedValues);

        private object ResolveMaterialized(StreamLayout.IMaterializedValueNode node, IDictionary<IModule, object> values, string indent)
        {
            object result;
            if (node is StreamLayout.Atomic) result = values[((StreamLayout.Atomic)node).Module];
            else if (node is StreamLayout.Combine)
            {
                var combine = node as StreamLayout.Combine;
                result = combine.Combinator(ResolveMaterialized(combine.Left, values, indent + "  "), ResolveMaterialized(combine.Right, values, indent + "  "));
            }
            else if (node is StreamLayout.Transform)
            {
                var transform = node as StreamLayout.Transform;
                result = transform.Transformator(ResolveMaterialized(transform.Node, values, indent + "  "));
            }
            else result = null;

            LinkedList<IMaterializedValueSource> sources;
            if (_materializedValueSources.TryGetValue(node, out sources))
            {
                _materializedValueSources.Remove(node);
                foreach (var source in sources)
                    source.SetValue(result);
            }

            return result;
        }

        protected void AssignPort(InPort inPort, ISubscriber subscriber)
        {
            Subscribers[inPort] = subscriber;
            // Interface (unconnected) ports of the current scope will be wired when exiting the scope
            if (!CurrentLayout.InPorts.Contains(inPort))
            {
                IPublisher publisher;
                if (Publishers.TryGetValue(CurrentLayout.Upstreams[inPort], out publisher))
                    publisher.Subscribe(subscriber);
            }
        }

        protected void AssignPort(OutPort outPort, IPublisher publisher)
        {
            Publishers[outPort] = publisher;
            // Interface (unconnected) ports of the current scope will be wired when exiting the scope
            if (!CurrentLayout.OutPorts.Contains(outPort))
            {
                ISubscriber subscriber;
                if (Subscribers.TryGetValue(CurrentLayout.Downstreams[outPort], out subscriber))
                    publisher.Subscribe(subscriber);
            }
        }
    }
}