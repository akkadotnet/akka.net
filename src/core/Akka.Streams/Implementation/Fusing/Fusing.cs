//-----------------------------------------------------------------------
// <copyright file="Fusing.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Akka.Pattern;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;
using Atomic = Akka.Streams.Implementation.StreamLayout.Atomic;
using Combine = Akka.Streams.Implementation.StreamLayout.Combine;
using IMaterializedValueNode = Akka.Streams.Implementation.StreamLayout.IMaterializedValueNode;
using Transform = Akka.Streams.Implementation.StreamLayout.Transform;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class Fusing
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly bool IsDebug = false;

        /// <summary>
        /// Fuse everything that is not forbidden via AsyncBoundary attribute.
        /// </summary>
        /// <typeparam name="TShape">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static Streams.Fusing.FusedGraph<TShape, TMat> Aggressive<TShape, TMat>(IGraph<TShape, TMat> graph)
            where TShape : Shape
        {
            if (graph is Streams.Fusing.FusedGraph<TShape, TMat> fusedGraph)
                return fusedGraph;

            var structInfo = new BuildStructuralInfo();

            // First perform normalization by descending the module tree and recording information in the BuildStructuralInfo instance.
            LinkedList<KeyValuePair<IModule, IMaterializedValueNode>> materializedValue;

            try
            {
                materializedValue = Descend<TMat>(graph.Module, Attributes.None, structInfo,
                    structInfo.CreateGroup(0), 0);
            }
            catch
            {
                if (IsDebug)
                    structInfo.Dump();
                throw;
            }

            // Then create a copy of the original Shape with the new copied ports.
            var shape = graph.Shape.CopyFromPorts(structInfo.NewInlets(graph.Shape.Inlets),
                structInfo.NewOutlets(graph.Shape.Outlets));

            // Extract the full topological information from the builder before removing assembly-internal (fused) wirings in the next step.
            var info = structInfo.ToInfo(shape,
                materializedValue.Select(pair => (pair.Key, pair.Value)).ToList());

            // Perform the fusing of `structInfo.groups` into GraphModules (leaving them as they are for non - fusable modules).
            structInfo.RemoveInternalWires();
            structInfo.BreakUpGroupsByDispatcher();
            var modules = Fuse(structInfo);

            // Now we have everything ready for a FusedModule.
            var module = new FusedModule(
                modules,
                shape,
                ImmutableDictionary.CreateRange(structInfo.Downstreams),
                ImmutableDictionary.CreateRange(structInfo.Upstreams),
                materializedValue.First().Value,
                Attributes.None,
                info);

            if (StreamLayout.IsDebug) StreamLayout.Validate(module);
            if (IsDebug) Console.WriteLine(module.ToString());

            return new Streams.Fusing.FusedGraph<TShape, TMat>(module, (TShape) shape);
        }

        /// <summary>
        /// Return the <see cref="StructuralInfoModule"/> for this Graph without any fusing
        /// </summary>
        /// <typeparam name="TShape">TBD</typeparam>
        /// <typeparam name="TMat"></typeparam>
        /// <param name="graph"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        public static StructuralInfoModule StructuralInfo<TShape, TMat>(IGraph<TShape, TMat> graph, Attributes attributes) where TShape : Shape
        {
            var structuralInfo = new BuildStructuralInfo();

            // First perform normalization by descending the module tree and recording
            // information in the BuildStructuralInfo instance.

            try
            {
                var materializedValue = Descend<TMat>(graph.Module, Attributes.None, structuralInfo,
                    structuralInfo.CreateGroup(0), 0);

                // Then create a copy of the original Shape with the new copied ports.
                var shape = graph.Shape.CopyFromPorts(structuralInfo.NewInlets(graph.Shape.Inlets),
                    structuralInfo.NewOutlets(graph.Shape.Outlets));

                // Extract the full topological information from the builder
                return structuralInfo.ToInfo(shape, materializedValue.Select(pair=> (pair.Key, pair.Value)).ToList(), attributes);
            }
            catch (Exception)
            {
                if(IsDebug)
                    structuralInfo.Dump();

                throw;
            }
        }

        /// <summary>
        /// Take the fusable islands identified by <see cref="Descend{T}"/> in the <see cref="BuildStructuralInfo.Groups"/> list 
        /// and execute their fusion; only fusable islands will have multiple modules in their set.
        /// </summary>
        private static ImmutableArray<IModule> Fuse(BuildStructuralInfo info)
        {
            return info.Groups.SelectMany(group =>
            {
                if (group.Count == 0) return Enumerable.Empty<IModule>();
                if (group.Count == 1) return new[] {group.First()};
                return new[] {FuseGroup(info, group)};
            }).ToImmutableArray();
        }

        /// <summary>
        /// Transform a set of GraphStageModules into a single GraphModule. This is done
        /// by performing a traversal of all their Inlets, sorting them into those without
        /// internal connections(the exposed inlets) and those with internal connections
        /// (where the corresponding Outlet is recorded in a map so that it will be wired
        /// to the same slot number in the GraphAssembly). Then all Outlets are traversed,
        /// completing internal connections using the aforementioned maps and appending
        /// the others to the list of exposed Outlets.
        /// </summary>
        private static GraphModule FuseGroup(BuildStructuralInfo info, ISet<IModule> group)
        {
            var stages = new IGraphStageWithMaterializedValue<Shape, object>[group.Count];
            var materializedValueIds = new IModule[group.Count];
            var attributes = new Attributes[group.Count];

            /*
             * The overall GraphAssembly arrays are constructed in three parts:
             * - 1) exposed inputs (ins)
             * - 2) connections (ins and outs)
             * - 3) exposed outputs (outs)
             */
            var insB1 = new List<Inlet>();
            var insB2 = new List<Inlet>();
            var outsB3 = new List<Outlet>();
            var inOwnersB1 = new List<int>();
            var inOwnersB2 = new List<int>();
            var outOwnersB3 = new List<int>();

            // for the shape of the GraphModule
            var inlets = ImmutableArray.CreateBuilder<Inlet>(2);
            var outlets = ImmutableArray.CreateBuilder<Outlet>(2);

            // connection slots are allocated from the inputs side, outs find their place by this map
            var outConns = new Dictionary<OutPort, int>();

            /*
             * First traverse all Inlets and sort them into exposed and internal,
             * taking note of their partner Outlets where appropriate.
             */
            var pos = 0;
            var enumerator = group.GetEnumerator();
            var ups = info.Upstreams;
            var downs = info.Downstreams;
            var outGroup = info.OutGroups;
            while (enumerator.MoveNext())
            {
                CopiedModule copy;
                GraphStageModule graphStageModule;
                if ((copy = enumerator.Current as CopiedModule) != null && (graphStageModule = copy.CopyOf as GraphStageModule) != null)
                {
                    stages[pos] = graphStageModule.Stage;
                    materializedValueIds[pos] = copy;
                    attributes[pos] = copy.Attributes.And(graphStageModule.Attributes);

                    var copyInlets = copy.Shape.Inlets.GetEnumerator();
                    var originalInlets = graphStageModule.Shape.Inlets.GetEnumerator();
                    while (copyInlets.MoveNext() && originalInlets.MoveNext())
                    {
                        var copyInlet = copyInlets.Current;
                        var originalInlet = originalInlets.Current;
                        var isInternal = ups.TryGetValue(copyInlet, out var outport) 
                            && outGroup.TryGetValue(outport, out var g) 
                            && g == group;
                        if (isInternal)
                        {
                            ups.Remove(copyInlet);
                            downs.Remove(outport);
                            outConns[outport] = insB2.Count;
                            insB2.Add(originalInlet);
                            inOwnersB2.Add(pos);
                        }
                        else
                        {
                            insB1.Add(originalInlet);
                            inOwnersB1.Add(pos);
                            inlets.Add(copyInlet);
                        }
                    }
                    pos++;
                }
                else
                    throw new ArgumentException("unexpected module structure");
            }

            var outsB2 = new Outlet[insB2.Count];
            var outOwnersB2 = new int[insB2.Count];

            /*
             * Then traverse all Outlets and complete connections.
             */
            pos = 0;
            enumerator = group.GetEnumerator();
            while (enumerator.MoveNext())
            {
                CopiedModule copy;
                GraphStageModule graphStageModule;
                if ((copy = enumerator.Current as CopiedModule) != null && (graphStageModule = copy.CopyOf as GraphStageModule) != null)
                {
                    var copyOutlets = copy.Shape.Outlets.GetEnumerator();
                    var originalOutlets = graphStageModule.Shape.Outlets.GetEnumerator();
                    while (copyOutlets.MoveNext() && originalOutlets.MoveNext())
                    {
                        var copyOutlet = copyOutlets.Current;
                        var originalOutlet = originalOutlets.Current;
                        if (outConns.TryGetValue(copyOutlet, out int idx))
                        {
                            outConns.Remove(copyOutlet);
                            outsB2[idx] = originalOutlet;
                            outOwnersB2[idx] = pos;
                        }
                        else
                        {
                            outsB3.Add(originalOutlet);
                            outOwnersB3.Add(pos);
                            outlets.Add(copyOutlet);
                        }
                    }
                    pos++;
                }
                else
                    throw new ArgumentException("unexpected module structure");
            }

            /*
             * Now mechanically gather together the GraphAssembly arrays from their various pieces.
             */
            var shape = new AmorphousShape(inlets.ToImmutable(), outlets.ToImmutable());
            var connStart = insB1.Count;
            var conns = insB2.Count;
            var outStart = connStart + conns;
            var count = outStart + outsB3.Count;

            var ins = new Inlet[count];
            insB1.CopyTo(ins, 0);
            insB2.CopyTo(ins, insB1.Count);

            var inOwners = new int[count];
            inOwnersB1.CopyTo(inOwners, 0);
            inOwnersB2.CopyTo(inOwners, inOwnersB1.Count);
            for (int i = inOwnersB1.Count + inOwnersB2.Count; i < inOwners.Length; i++) inOwners[i] = -1;

            var outs = new Outlet[count];
            Array.Copy(outsB2, 0, outs, connStart, conns);
            outsB3.CopyTo(outs, outStart);

            var outOwners = new int[count];
            for (int i = 0; i < connStart; i++) outOwners[i] = -1;
            Array.Copy(outOwnersB2, 0, outOwners, connStart, conns);
            outOwnersB3.CopyTo(outOwners, outStart);

            var firstModule = group.First();
            if(!(firstModule is CopiedModule))
                throw new ArgumentException("unexpected module structure");
            var asyncAttrs = IsAsync((CopiedModule) firstModule) ? new Attributes(Attributes.AsyncBoundary.Instance) : Attributes.None;
            var dispatcher = GetDispatcher(firstModule);
            var dispatcherAttrs = dispatcher == null ? Attributes.None : new Attributes(dispatcher);
            var attr = asyncAttrs.And(dispatcherAttrs);

            return new GraphModule(new GraphAssembly(stages, attributes, ins, inOwners, outs, outOwners), shape, attr, materializedValueIds);
        }

        /// <summary>
        /// This is a normalization step for the graph that also collects the needed
        /// information for later fusing. The goal is to transform an arbitrarily deep
        /// module tree into one that has exactly two levels: all direct submodules are
        /// CopiedModules where each contains exactly one atomic module. This way all
        /// modules have their own identity and all necessary port copies have been
        /// made. The upstreams/downstreams in the BuildStructuralInfo are rewritten
        /// to point to the shapes of the copied modules.
        /// 
        /// The materialized value computation is rewritten as well in that all
        /// leaf nodes point to the copied modules and all nested computations are
        /// "inlined", resulting in only one big computation tree for the whole
        /// normalized overall module. The contained MaterializedValueSource stages
        /// are also rewritten to point to the copied MaterializedValueNodes. This
        /// correspondence is then used during materialization to trigger these sources
        /// when "their" node has received its value.
        /// </summary>
        private static LinkedList<KeyValuePair<IModule, IMaterializedValueNode>> Descend<T>(
            IModule module,
            Attributes inheritedAttributes,
            BuildStructuralInfo structInfo,
            ISet<IModule> openGroup,
            int indent)
        {

            var isAsync = module is GraphStageModule || module is GraphModule
                ? module.Attributes.Contains(Attributes.AsyncBoundary.Instance)
                : module.IsAtomic || module.Attributes.Contains(Attributes.AsyncBoundary.Instance);
            if (IsDebug)
                Log(indent,
                    $"entering {module.GetType().Name} (hash={module.GetHashCode()}, async={isAsync}, name={module.Attributes.GetNameLifted()}, dispatcher={GetDispatcher(module)})");
            var localGroup = isAsync ? structInfo.CreateGroup(indent) : openGroup;

            if (module.IsAtomic)
            {
                GraphModule graphModule;
                if ((graphModule = module as GraphModule) != null)
                {
                    if (!isAsync)
                    {
                        if (IsDebug)
                            Log(indent,
                                $"dissolving graph module {module.ToString().Replace("\n", $"\n{string.Empty.PadLeft(indent*2)}")}");
                        // need to dissolve previously fused GraphStages to allow further fusion
                        var attributes = inheritedAttributes.And(module.Attributes);
                        return new LinkedList<KeyValuePair<IModule, IMaterializedValueNode>>(
                            graphModule.MaterializedValueIds.SelectMany(
                                sub => Descend<T>(sub, attributes, structInfo, localGroup, indent + 1)));
                    }
                    else
                    {
                        /*
                         * Importing a GraphModule that has an AsyncBoundary attribute is a little more work:
                         *
                         *  - we need to copy all the CopiedModules that are in matValIDs
                         *  - we need to rewrite the corresponding MaterializedValueNodes
                         *  - we need to match up the new (copied) GraphModule shape with the individual Shape copies
                         *  - we need to register the contained modules but take care to not include the internal
                         *    wirings into the final result, see also `struct.removeInternalWires()`
                         */
                        if (IsDebug)
                            Log(indent,
                                $"graph module {module.ToString().Replace("\n", $"\n{string.Empty.PadLeft(indent*2)}")}");

                        var oldShape = graphModule.Shape;
                        var mvids = graphModule.MaterializedValueIds;

                        // storing the old Shape in arrays for in-place updating as we clone the contained GraphStages
                        var oldIns = oldShape.Inlets.ToArray();
                        var oldOuts = oldShape.Outlets.ToArray();

                        var newIds = mvids.OfType<CopiedModule>().Select(x =>
                        {
                            var newShape = x.Shape.DeepCopy();
                            IModule copy = new CopiedModule(newShape, x.Attributes, x.CopyOf);

                            // rewrite shape: first the inlets
                            var oldIn = x.Shape.Inlets.GetEnumerator();
                            var newIn = newShape.Inlets.GetEnumerator();
                            while (oldIn.MoveNext() && newIn.MoveNext())
                            {
                                var idx = Array.IndexOf(oldIns, oldIn.Current);
                                if (idx >= 0) oldIns[idx] = newIn.Current;
                            }

                            // ... then the outlets
                            var oldOut = x.Shape.Outlets.GetEnumerator();
                            var newOut = newShape.Outlets.GetEnumerator();
                            while (oldOut.MoveNext() && newOut.MoveNext())
                            {
                                var idx = Array.IndexOf(oldOuts, oldOut.Current);
                                if (idx >= 0) oldOuts[idx] = newOut.Current;
                            }

                            // need to add the module so that the structural (internal) wirings can be rewritten as well
                            // but these modules must not be added to any of the groups
                            structInfo.AddModule(copy, new HashSet<IModule>(), inheritedAttributes, indent, x.Shape);
                            structInfo.RegisterInternals(newShape, indent);
                            return copy;
                        }).ToArray();

                        var newGraphModule = new GraphModule(graphModule.Assembly,
                            oldShape.CopyFromPorts(oldIns.ToImmutableArray(), oldOuts.ToImmutableArray()),
                            graphModule.Attributes, newIds);
                        // make sure to add all the port mappings from old GraphModule Shape to new shape
                        structInfo.AddModule(newGraphModule, localGroup, inheritedAttributes, indent, oldShape);
                        // now compute the list of all materialized value computation updates
                        var result = new LinkedList<KeyValuePair<IModule, IMaterializedValueNode>>(
                            mvids.Select(
                                (t, i) =>
                                    new KeyValuePair<IModule, IMaterializedValueNode>(t,
                                        new Atomic(newIds[i]))));
                        result.AddLast(new KeyValuePair<IModule, IMaterializedValueNode>(module,
                            new Atomic(newGraphModule)));
                        return result;
                    }
                }
                else
                {
                    if (IsDebug) Log(indent, $"atomic module {module}");
                    var result = new LinkedList<KeyValuePair<IModule, IMaterializedValueNode>>();
                    result.AddFirst(
                        new KeyValuePair<IModule, IMaterializedValueNode>(module,
                            structInfo.AddModule(module, localGroup, inheritedAttributes, indent))
                        );
                    return result;
                }
            }
            else
            {
                var allAttributes = inheritedAttributes.And(module.Attributes);
                if (module is CopiedModule copied)
                {
                    var result = Descend<T>(copied.CopyOf, allAttributes, structInfo, localGroup, indent + 1);
                    if (result.Count == 0)
                        throw new IllegalStateException("Descend returned empty result from CopiedModule");

                    result.AddFirst(new KeyValuePair<IModule, IMaterializedValueNode>(copied, result.First.Value.Value));

                    structInfo.Rewire(copied.CopyOf.Shape, copied.Shape, indent);
                    return result;
                }
                else
                {
                    // we need to keep track of all MaterializedValueSource nodes that get pushed into the current
                    // computation context (i.e. that need the same value).
                    structInfo.EnterMaterializationContext();

                    // now descend into submodules and collect their computations (plus updates to `struct`)
                    var subMatBuilder = ImmutableDictionary.CreateBuilder<IModule, IMaterializedValueNode>();
                    foreach (var sub in module.SubModules)
                    {
                        var res = Descend<T>(sub, allAttributes, structInfo, localGroup, indent + 1);
                        foreach (var r in res)
                        {
                            // key may already be in builder, we overwrite
                            subMatBuilder[r.Key] = r.Value;
                        }
                    }
                    var subMat = subMatBuilder.ToImmutable();
                    if (IsDebug)
                        Log(indent,
                            $"subMat\n  {string.Empty.PadLeft(indent*2)}{string.Join("\n  " + string.Empty.PadLeft(indent*2), subMat.Select(p => $"{p.Key.GetType().Name}[{p.Key.GetHashCode()}] -> {p.Value}"))}");

                    // we need to remove all wirings that this module copied from nested modules so that we donâ€™t do wirings twice
                    var oldDownstreams =
                        (module as FusedModule)?.Info.Downstreams.ToImmutableHashSet()
                        ?? module.Downstreams.ToImmutableHashSet();

                    var down = module.SubModules.Aggregate(oldDownstreams, (set, m) => set.Except(m.Downstreams));
                    foreach (var entry in down)
                    {
                        structInfo.Wire(entry.Key, entry.Value, indent);
                    }

                    // now rewrite the materialized value computation based on the copied modules and their computation nodes
                    var matNodeMapping = new Dictionary<IMaterializedValueNode, IMaterializedValueNode>();
                    var newMat = RewriteMaterializer(subMat, module.MaterializedValueComputation, matNodeMapping);
                    // and finally rewire all MaterializedValueSources to their new computation nodes
                    var materializedSources = structInfo.ExitMaterializationContext();
                    foreach (var c in materializedSources)
                    {
                        if (IsDebug) Log(indent, $"materialized value source: {structInfo.Hash(c)}");
                        var ms = (IMaterializedValueSource) ((GraphStageModule) c.CopyOf).Stage;

                        IMaterializedValueNode mapped;
                        if (ms.Computation is Atomic atomic)
                            mapped = subMat[atomic.Module];
                        else if (ms.Computation == StreamLayout.Ignore.Instance)
                            mapped = ms.Computation;
                        else
                            mapped = matNodeMapping[ms.Computation];

                        var outputType = ms.Outlet.GetType().GetGenericArguments().First();
                        var materializedValueSourceType = typeof(MaterializedValueSource<>).MakeGenericType(outputType);
                        var newSrc = (IMaterializedValueSource) Activator.CreateInstance(materializedValueSourceType, mapped, ms.Outlet);
                        var replacement = new CopiedModule(c.Shape, c.Attributes, newSrc.Module);
                        structInfo.Replace(c, replacement, localGroup);
                    }

                    // the result for each level is the materialized value computation
                    var result = new LinkedList<KeyValuePair<IModule, IMaterializedValueNode>>();
                    result.AddFirst(new KeyValuePair<IModule, IMaterializedValueNode>(module, newMat));
                    return result;
                }
            }
        }

        /// <summary>
        /// Given a mapping from old modules to new MaterializedValueNode, rewrite the given
        /// computation while also populating a mapping from old computation nodes to new ones.
        /// That mapping is needed to rewrite the MaterializedValueSource stages later-on in
        /// descend().
        /// </summary>
        private static IMaterializedValueNode RewriteMaterializer(IDictionary<IModule, IMaterializedValueNode> subMat, IMaterializedValueNode mat, Dictionary<IMaterializedValueNode, IMaterializedValueNode> mapping)
        {
            if (mat is Atomic atomic)
            {
                var result = subMat[atomic.Module];
                mapping.Put(atomic, result);
                return result;
            }
            if (mat is Combine combine)
            {
                var result = new Combine(combine.Combinator, RewriteMaterializer(subMat, combine.Left, mapping), RewriteMaterializer(subMat, combine.Right, mapping));
                mapping.Put(combine, result);
                return result;
            }
            if (mat is Transform transform)
            {
                var result = new Transform(transform.Transformator, RewriteMaterializer(subMat, transform.Node, mapping));
                mapping.Put(transform, result);
                return result;
            }

            return mat;
        }

        private static bool IsAsync(CopiedModule module)
        {
            var attrs = module.Attributes.And(module.CopyOf.Attributes);
            return attrs.Contains(Attributes.AsyncBoundary.Instance);
        }

        /// <summary>
        /// Figure out the dispatcher setting of a module.
        /// </summary>
        /// <param name="module">TBD</param>
        /// <returns>TBD</returns>
        internal static ActorAttributes.Dispatcher GetDispatcher(IModule module)
        {
            CopiedModule copied;
            if ((copied = module as CopiedModule) != null)
            {
                var attrs = copied.Attributes.And(copied.CopyOf.Attributes);
                return attrs.GetAttribute<ActorAttributes.Dispatcher>(null);
            }
            return module.Attributes.GetAttribute<ActorAttributes.Dispatcher>(null);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="indent">TBD</param>
        /// <param name="msg">TBD</param>
        internal static void Log(int indent, string msg) => Console.WriteLine("{0}{1}", string.Empty.PadLeft(indent*2), msg);
    }

    /// <summary>
    /// Collect structural information about a module tree while descending into it and performing normalization.
    /// </summary>
    internal sealed class BuildStructuralInfo
    {
        /// <summary>
        /// The set of all contained modules.
        /// </summary>
        public readonly ISet<IModule> Modules = new HashSet<IModule>();

        /// <summary>
        /// The list of all groups of modules that are within each async boundary.
        /// </summary>
        public readonly LinkedList<ISet<IModule>> Groups = new LinkedList<ISet<IModule>>();

        /// <summary>
        /// A mapping from OutPort to its containing group, needed when determining whether an upstream connection is internal or not.
        /// </summary>
        public readonly IDictionary<OutPort, ISet<IModule>> OutGroups = new Dictionary<OutPort, ISet<IModule>>();

        /// <summary>
        /// A stack of mappings for a given non-copied InPort.
        /// </summary>
        public readonly IDictionary<InPort, LinkedList<InPort>> NewInputs = new Dictionary<InPort, LinkedList<InPort>>();

        /// <summary>
        /// A stack of mappings for a given non-copied OutPort.
        /// </summary>
        public readonly IDictionary<OutPort, LinkedList<OutPort>> NewOutputs = new Dictionary<OutPort, LinkedList<OutPort>>();

        /// <summary>
        /// The downstreams relationships of the original module rewritten in terms of the copied ports.
        /// </summary>
        public readonly IDictionary<OutPort, InPort> Downstreams = new Dictionary<OutPort, InPort>();

        /// <summary>
        /// The upstreams relationships of the original module rewritten in terms of the copied ports.
        /// </summary>
        public readonly IDictionary<InPort, OutPort> Upstreams = new Dictionary<InPort, OutPort>();

        /// <summary>
        /// The owner mapping for the copied InPorts.
        /// </summary>
        public readonly IDictionary<InPort, IModule> InOwners = new Dictionary<InPort, IModule>();

        /// <summary>
        /// The owner mapping for the copied OutPorts.
        /// </summary>
        public readonly IDictionary<OutPort, IModule> OutOwners = new Dictionary<OutPort, IModule>();

        /// <summary>
        /// List of internal wirings of GraphModules that were incorporated.
        /// </summary>
        public readonly ISet<OutPort> InternalOuts = new HashSet<OutPort>();

        /// <summary>
        /// A stack of materialized value sources, grouped by materialized computation context.
        /// </summary>
        private readonly LinkedList<LinkedList<CopiedModule>> _materializedSources = new LinkedList<LinkedList<CopiedModule>>();

        /// <summary>
        /// TBD
        /// </summary>
        public void EnterMaterializationContext() => _materializedSources.AddFirst(new LinkedList<CopiedModule>());

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the stack of materialized value sources is empty.
        /// </exception>
        /// <returns>TBD</returns>
        public IImmutableList<CopiedModule> ExitMaterializationContext()
        {
            if (_materializedSources.Count == 0) throw new ArgumentException("ExitMaterializationContext with empty stack");
            var x = _materializedSources.First.Value;
            _materializedSources.RemoveFirst();
            return x.ToImmutableList();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the stack of materialized value sources is empty.
        /// </exception>
        public void PushMaterializationSource(CopiedModule module)
        {
            if (_materializedSources.Count == 0) throw new ArgumentException("PushMaterializationSource without context");
            _materializedSources.First.Value.AddFirst(module);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public StructuralInfoModule ToInfo<TShape>(TShape shape, IList<(IModule, IMaterializedValueNode)> materializedValues ,Attributes attributes = null) where TShape : Shape
        {
            attributes = attributes ?? Attributes.None;

            return new StructuralInfoModule(Modules.ToImmutableArray(), shape, 
                Downstreams.ToImmutableDictionary(),
                Upstreams.ToImmutableDictionary(), 
                InOwners.ToImmutableDictionary(), 
                OutOwners.ToImmutableDictionary(),
                materializedValues.ToImmutableList(), 
                materializedValues.First().Item2, 
                attributes);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="oldModule">TBD</param>
        /// <param name="newModule">TBD</param>
        /// <param name="localGroup">TBD</param>
        public void Replace(IModule oldModule, IModule newModule, ISet<IModule> localGroup)
        {
            Modules.Remove(oldModule);
            Modules.Add(newModule);
            localGroup.Remove(oldModule);
            localGroup.Add(newModule);
        }

        /// <summary>
        /// Fusable groups may contain modules with differing dispatchers, in which case the group needs to be broken up.
        /// </summary>
        public void BreakUpGroupsByDispatcher()
        {
            var newGroups = new LinkedList<ISet<IModule>>();
            var it = Groups.GetEnumerator();
            while (it.MoveNext())
            {
                var group = it.Current;
                if (group.Count > 1)
                {
                    var subgroups = group.GroupBy(Fusing.GetDispatcher).ToArray();
                    if (subgroups.Length > 1)
                    {
                        group.Clear();
                        foreach (var subgroup in subgroups)
                            newGroups.AddLast(new HashSet<IModule>(subgroup));
                    }
                }
            }

            foreach (var group in newGroups)
                Groups.AddLast(group);
        }

        /// <summary>
        /// Register the outlets of the given Shape as sources for internal connections within imported 
        /// (and not dissolved) GraphModules. See also the comment in addModule where this is partially undone.
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <param name="indent">TBD</param>
        public void RegisterInternals(Shape shape, int indent)
        {
            if (Fusing.IsDebug) Fusing.Log(indent, $"registerInternals({string.Join(",", shape.Outlets.Select(Hash))}");
            foreach (var outlet in shape.Outlets)
                InternalOuts.Add(outlet);
        }

        /// <summary>
        /// Remove wirings that belong to the fused stages contained in GraphModules that were incorporated in this fusing run.
        /// </summary>
        public void RemoveInternalWires()
        {
            var enumerator = InternalOuts.GetEnumerator();
            while (enumerator.MoveNext())
            {
                var outport = enumerator.Current;
                if (Downstreams.TryGetValue(outport, out var inport))
                {
                    Downstreams.Remove(outport);
                    Upstreams.Remove(inport);
                }
            }
        }

        /// <summary>
        /// Create and return a new grouping (i.e. an AsyncBoundary-delimited context)
        /// </summary>
        /// <param name="indent">TBD</param>
        /// <returns>TBD</returns>
        public ISet<IModule> CreateGroup(int indent)
        {
            var group = new HashSet<IModule>();
            if (Fusing.IsDebug) Fusing.Log(indent, $"creating new group {Hash(group)}");
            Groups.AddLast(group);
            return group;
        }

        /// <summary>
        /// Add a module to the given group, performing normalization (i.e. giving it a unique port identity).
        /// </summary>
        /// <param name="module">TBD</param>
        /// <param name="group">TBD</param>
        /// <param name="inheritedAttributes">TBD</param>
        /// <param name="indent">TBD</param>
        /// <param name="oldShape">TBD</param>
        /// <returns>TBD</returns>
        public Atomic AddModule(IModule module, ISet<IModule> group, Attributes inheritedAttributes, int indent, Shape oldShape = null)
        {
            var copy = oldShape == null
                ? new CopiedModule(module.Shape.DeepCopy(), inheritedAttributes, GetRealModule(module))
                : module;
            oldShape = oldShape ?? module.Shape;
            if (Fusing.IsDebug) Fusing.Log(indent, $"adding copy {Hash(copy)} {PrintShape(copy.Shape)} of {PrintShape(oldShape)}");
            group.Add(copy);
            Modules.Add(copy);

            foreach (var outlet in copy.Shape.Outlets)
                OutGroups.Add(outlet, group);

            var orig1 = oldShape.Inlets.GetEnumerator();
            var mapd1 = copy.Shape.Inlets.GetEnumerator();
            while (orig1.MoveNext() && mapd1.MoveNext())
            {
                var orig = orig1.Current;
                var mapd = mapd1.Current;
                AddMapping(orig, mapd, NewInputs);
                InOwners[mapd] = copy;
            }

            var orig2 = oldShape.Outlets.GetEnumerator();
            var mapd2 = copy.Shape.Outlets.GetEnumerator();
            while (orig2.MoveNext() && mapd2.MoveNext())
            {
                var orig = orig2.Current;
                var mapd = mapd2.Current;
                AddMapping(orig, mapd, NewOutputs);
                OutOwners[mapd] = copy;
            }

            /*
             * In Descend() we add internalOuts entries for all shapes that belong to stages that
             * are part of a GraphModule that is not dissolved. This includes the exposed Outlets,
             * which of course are external and thus need to be removed again from the internalOuts
             * set.
             */
            if (module is GraphModule)
                foreach (var outlet in module.Shape.Outlets)
                    InternalOuts.Remove(outlet);

            if (IsCopiedModuleWithGraphStageAndMaterializedValue(copy))
                PushMaterializationSource((CopiedModule) copy);
            else if (copy is GraphModule)
            {
                var mvids = ((GraphModule) copy).MaterializedValueIds;
                foreach (IModule mvid in mvids)
                {
                    if (IsCopiedModuleWithGraphStageAndMaterializedValue(mvid))
                        PushMaterializationSource((CopiedModule) mvid);
                }
            }

            return new Atomic(copy);
        }

        /// <summary>
        /// Record a wiring between two copied ports, using (and reducing) the port mappings.
        /// </summary>
        /// <param name="outPort">TBD</param>
        /// <param name="inPort">TBD</param>
        /// <param name="indent">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        public void Wire(OutPort outPort, InPort inPort, int indent)
        {
            if (Fusing.IsDebug) Fusing.Log(indent, $"wiring {outPort} ({Hash(outPort)}) -> {inPort} ({Hash(inPort)})");
            var newOut = RemoveMapping(outPort, NewOutputs);
            if (!newOut.HasValue) throw new ArgumentException($"wiring {outPort} -> {inPort}", nameof(outPort));
            var newIn = RemoveMapping(inPort, NewInputs);
            if (!newIn.HasValue) throw new ArgumentException($"wiring {outPort} -> {inPort}", nameof(inPort));
            Downstreams.Add(newOut.Value, newIn.Value);
            Upstreams.Add(newIn.Value, newOut.Value);
        }

        /// <summary>
        /// Replace all mappings for a given shape with its new (copied) form.
        /// </summary>
        /// <param name="oldShape">TBD</param>
        /// <param name="newShape">TBD</param>
        /// <param name="indent">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        public void Rewire(Shape oldShape, Shape newShape, int indent)
        {
            if (Fusing.IsDebug) Fusing.Log(indent, $"rewiring {PrintShape(oldShape)} -> {PrintShape(newShape)}");
            {
                var oins = oldShape.Inlets.GetEnumerator();
                var nins = newShape.Inlets.GetEnumerator();
                while (oins.MoveNext() && nins.MoveNext())
                {
                    var x = RemoveMapping(oins.Current, NewInputs);
                    if (!x.HasValue)
                        throw new ArgumentException($"rewiring {oins.Current} -> {nins.Current}", nameof(oldShape));
                    AddMapping(nins.Current, x.Value, NewInputs);
                }
            }
            {
                var oouts = oldShape.Outlets.GetEnumerator();
                var nouts = newShape.Outlets.GetEnumerator();
                while (oouts.MoveNext() && nouts.MoveNext())
                {
                    var x = RemoveMapping(oouts.Current, NewOutputs);
                    if (!x.HasValue)
                        throw new ArgumentException($"rewiring {oouts.Current} -> {nouts.Current}", nameof(oldShape));
                    AddMapping(nouts.Current, x.Value, NewOutputs);
                }
            }
        }

        /// <summary>
        /// Transform original into copied Inlets.
        /// </summary>
        /// <param name="old">TBD</param>
        /// <returns>TBD</returns>
        public ImmutableArray<Inlet> NewInlets(IEnumerable<Inlet> old) => old.Select(i => (Inlet)NewInputs[i].First.Value).ToImmutableArray();

        /// <summary>
        /// Transform original into copied Outlets.
        /// </summary>
        /// <param name="old">TBD</param>
        /// <returns>TBD</returns>
        public ImmutableArray<Outlet> NewOutlets(IEnumerable<Outlet> old) => old.Select(o => (Outlet)NewOutputs[o].First.Value).ToImmutableArray();

        private bool IsCopiedModuleWithGraphStageAndMaterializedValue(IModule module)
        {
            var copiedModule = module as CopiedModule;
            GraphStageModule graphStageModule;
            Type stageType;
            return copiedModule != null
                && (graphStageModule = copiedModule.CopyOf as GraphStageModule) != null
                && (stageType = graphStageModule.Stage.GetType()).GetTypeInfo().IsGenericType
                && stageType.GetGenericTypeDefinition() == typeof(MaterializedValueSource<>);
        }

        private void AddMapping<T>(T orig, T mapd, IDictionary<T, LinkedList<T>> map)
        {
            if (map.TryGetValue(orig, out var values))
                values.AddLast(mapd);
            else
                map.Add(orig, new LinkedList<T>(new[] { mapd }));
        }

        private Option<T> RemoveMapping<T>(T orig, IDictionary<T, LinkedList<T>> map)
        {
            if (map.TryGetValue(orig, out var values))
            {
                if (values.Count == 0)
                    map.Remove(orig);
                else
                {
                    var x = values.First.Value;
                    values.RemoveFirst();
                    return x;
                }
            }
            return Option<T>.None;
        }

        /// <summary>
        /// See through copied modules to the "real" module.
        /// </summary>
        private static IModule GetRealModule(IModule module)
        {
            return module is CopiedModule copiedModule
                ? GetRealModule(copiedModule.CopyOf)
                : module;
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal void Dump()
        {
            Console.WriteLine("StructuralInfo:");
            Console.WriteLine("  newIns:");
            NewInputs.ForEach(kvp => Console.WriteLine($"    {kvp.Key} ({Hash(kvp.Key)}) -> {string.Join(",", kvp.Value.Select(Hash))}"));
            Console.WriteLine("  newOuts:");
            NewInputs.ForEach(kvp => Console.WriteLine($"    {kvp.Key} ({Hash(kvp.Key)}) -> {string.Join(",", kvp.Value.Select(Hash))}"));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        internal string Hash(object obj) => obj.GetHashCode().ToString("x");

        private string PrintShape(Shape shape) =>
            $"{shape.GetType().Name}(ins={string.Join(",", shape.Inlets.Select(Hash))} outs={string.Join(",", shape.Outlets.Select(Hash))}";
    }
}
