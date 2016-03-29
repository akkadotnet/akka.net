using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Dispatch;
using Akka.Pattern;
using Akka.Streams.Stage;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.Fusing
{
    internal static class Fusing
    {

#if !DEBUG
        public const bool IsDebug = false;
#else
        public const bool IsDebug = true;
#endif

        public static FusedGraph<TShape, TMat> Aggressive<TShape, TMat>(IGraph<TShape, TMat> graph)
            where TShape : Shape
        {
            if (graph is FusedGraph<TShape, TMat>) return graph as FusedGraph<TShape, TMat>;
            var graphType = graph.GetType();
            if (graphType.IsGenericType && graphType.GetGenericTypeDefinition() == typeof(FusedGraph<,>)) return new FusedGraph<TShape, TMat>((FusedModule)graph.Module, graph.Shape);

            return DoAggressive(graph);
        }

        private static FusedGraph<TShape, TMat> DoAggressive<TShape, TMat>(IGraph<TShape, TMat> graph) where TShape : Shape
        {
            var structInfo = new BuildStructuralInfo();

            // First perform normalization by descending the module tree and recording information in the BuildStructuralInfo instance.
            var materializedValue = Descend<TMat>(graph.Module, Attributes.None, structInfo,
                structInfo.CreateGroup(string.Empty), string.Empty);

            // Then create a copy of the original Shape with the new copied ports.
            var shape = graph.Shape.CopyFromPorts(structInfo.NewInlets(graph.Shape.Inlets),
                structInfo.NewOutlets(graph.Shape.Outlets));

            // Extract the full topological information from the builder before removing assembly -internal (fused) wirings in the next step.
            var info = structInfo.ToInfo();

            // Perform the fusing of `structInfo.groups` into GraphModules (leaving them as they are for non - fusable modules).
            structInfo.RemoveInternalWires();
            structInfo.BreakUpGroupsByDispatcher();
            var modules = Fuse<TMat>(structInfo);

            // Now we have everything ready for a FusedModule.
            var module = new FusedModule(
                subModules: modules,
                shape: shape,
                downstreams: ImmutableDictionary.CreateRange(structInfo.Downstreams),
                upstreams: ImmutableDictionary.CreateRange(structInfo.Upstreams),
                materializedValueComputation: materializedValue.First().Value,
                attributes: Attributes.None,
                info: info);

            if (StreamLayout.IsDebug) StreamLayout.Validate(module);

            return new FusedGraph<TShape, TMat>(module, (TShape) shape);
        }

        /// <summary>
        /// Take the fusable islands identified by <see cref="Descend"/> in the `groups` list 
        /// and execute their fusion; only fusable islands will have multiple modules in their set.
        /// </summary>
        private static ImmutableArray<IModule> Fuse<T>(BuildStructuralInfo info)
        {
            return info.Groups.SelectMany(group =>
            {
                if (group.Count == 0) return Enumerable.Empty<IModule>();
                if (group.Count == 1) return new[] {group.First()};
                return new[] {FuseGroup<T>(info, group)};
            }).Distinct().ToImmutableArray();
        }

        /// <summary>
        /// Transform a set of GraphStageModules into a single GraphModule. This is done
        /// by performing a traversal of all their Inlets, sorting them into those without
        /// internal connections(the exposed inlets) and those with internal connections
        ///(where the corresponding Outlet is recorded in a map so that it will be wired
        /// to the same slot number in the GraphAssembly). Then all Outlets are traversed,
        /// completing internal connections using the aforementioned maps and appending
        /// the others to the list of exposed Outlets.
        /// </summary>
        private static GraphModule FuseGroup<T>(BuildStructuralInfo info, ISet<IModule> group)
        {
            var stages = new IGraphStageWithMaterializedValue[group.Count];
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

                    var cinlets = copy.Shape.Inlets.GetEnumerator();
                    var orig = graphStageModule.Shape.Inlets.GetEnumerator();
                    while (cinlets.MoveNext() && orig.MoveNext())
                    {
                        OutPort outport;
                        ISet<IModule> g;
                        if (ups.TryGetValue(cinlets.Current, out outport) && outGroup.TryGetValue(outport, out g) && g == group)
                        {
                            ups.Remove(cinlets.Current);
                            downs.Remove(outport);
                            outConns.Add(outport, insB2.Count);
                            insB2.Add(orig.Current);
                            inOwnersB2.Add(pos);
                        }
                        else
                        {
                            insB1.Add(orig.Current);
                            inOwnersB1.Add(pos);
                            inlets.Add(cinlets.Current);
                        }
                    }
                }
                pos++;
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
                    var outEnumerator = copy.Shape.Outlets.GetEnumerator();
                    var gsmEnumerator = graphStageModule.Shape.Outlets.GetEnumerator();
                    while (outEnumerator.MoveNext() && gsmEnumerator.MoveNext())
                    {
                        int idx;
                        if (outConns.TryGetValue(outEnumerator.Current, out idx))
                        {
                            outConns.Remove(outEnumerator.Current);
                            outsB2[idx] = gsmEnumerator.Current;
                            outOwnersB2[idx] = pos;
                        }
                        else
                        {
                            outsB3.Add(gsmEnumerator.Current);
                            outOwnersB3.Add(pos);
                            outlets.Add(outEnumerator.Current);
                        }
                    }
                }
                pos++;
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
            var asyncAttrs = IsAsync(firstModule) ? new Attributes(Attributes.AsyncBoundary.Instance) : Attributes.None;
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
        /// “inlined”, resulting in only one big computation tree for the whole
        /// normalized overall module. The contained MaterializedValueSource stages
        /// are also rewritten to point to the copied MaterializedValueNodes. This
        /// correspondence is then used during materialization to trigger these sources
        /// when “their” node has received its value.
        /// </summary>
        private static IEnumerable<KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>> Descend<T>(
            IModule module,
            Attributes inheritedAttributes,
            BuildStructuralInfo structInfo,
            ISet<IModule> openGroup,
            string indent)
        {
            var isAsync = IsAsync<T>(module);
            var localGroup = isAsync ? structInfo.CreateGroup(indent) : openGroup;

            if (module.IsAtomic)
            {
                GraphModule graphModule;
                if ((graphModule = module as GraphModule) != null)
                {
                    if (!isAsync)
                    {
                        // need to dissolve previously fused GraphStages to allow further fusion
                        var attributes = inheritedAttributes.And(module.Attributes);
                        return graphModule.MaterializedValueIds.SelectMany(sub => Descend<T>(sub, attributes, structInfo, localGroup, indent + "    "));
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

                        var oldShape = graphModule.Shape;
                        var mvids = graphModule.MaterializedValueIds;
                        // storing the old Shape in arrays for in-place updating as we clone the contained GraphStages
                        var oldIns = oldShape.Inlets.ToArray();
                        var oldOuts = oldShape.Outlets.ToArray();
                        var newIds = mvids.Where(x => x is CopiedModule).Cast<CopiedModule>().Select(x =>
                        {
                            var newShape = x.Shape.DeepCopy();
                            IModule copy = new CopiedModule(newShape, x.Attributes, x.CopyOf);

                            // rewrite shape: first the inlets
                            var oldIn = x.Shape.Inlets.GetEnumerator();
                            var newIn = newShape.Inlets.GetEnumerator();
                            while (oldIn.MoveNext() && newIn.MoveNext())
                            {
                                var idx = Array.IndexOf(oldIns, oldIn.Current);
                                if (idx != -1) oldIns[idx] = newIn.Current;
                            }

                            // ... then the outlets
                            var oldOut = x.Shape.Outlets.GetEnumerator();
                            var newOut = newShape.Outlets.GetEnumerator();
                            while (oldOut.MoveNext() && newOut.MoveNext())
                            {
                                var idx = Array.IndexOf(oldOuts, oldOut.Current);
                                if (idx != -1) oldOuts[idx] = newOut.Current;
                            }

                            // need to add the module so that the structural (internal) wirings can be rewritten as well
                            // but these modules must not be added to any of the groups
                            structInfo.AddModule(copy, new HashSet<IModule>(), inheritedAttributes, indent, x.Shape);
                            structInfo.RegisterInternal(newShape, indent);
                            return copy;
                        }).ToArray();
                        
                        var newGraphModule = new GraphModule(graphModule.Assembly, oldShape.CopyFromPorts(oldIns.ToImmutableArray(), oldOuts.ToImmutableArray()), graphModule.Attributes, newIds);
                        // make sure to add all the port mappings from old GraphModule Shape to new shape
                        structInfo.AddModule(newGraphModule, localGroup, inheritedAttributes, indent, oldShape);
                        // now compute the list of all materialized value computation updates
                        var result = new List<KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>>(mvids.Length + 1);
                        for (int i = 0; i < mvids.Length; i++)
                        {
                            result.Add(new KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>(mvids[i], new StreamLayout.Atomic(newIds[i])));
                        }
                        result.Add(new KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>(module, new StreamLayout.Atomic(newGraphModule)));
                        return result;
                    }
                }
                else
                {
                    return new[]
                    {
                        new KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>(module, structInfo.AddModule(module, localGroup, inheritedAttributes, indent))
                    };
                }
            }
            else
            {
                var attributes = inheritedAttributes.And(module.Attributes);
                CopiedModule copied;
                if ((copied = module as CopiedModule) != null)
                {
                    var result = Descend<T>(copied.CopyOf, attributes, structInfo, localGroup, indent + "    ").ToArray();
                    if(result.Length == 0) 
                        throw new IllegalStateException("Descend returned empty result from CopiedModule");
                    else
                        result[0] = new KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>(module, result[0].Value);

                    structInfo.Rewire(copied.CopyOf.Shape, copied.Shape, indent);
                    return result;
                }
                else
                {
                    // we need to keep track of all MaterializedValueSource nodes that get pushed into the current
                    // computation context (i.e. that need the same value).
                    structInfo.EnterMaterializationContext();
                    
                    // now descend into submodules and collect their computations (plus updates to `struct`)
                    var subMatBuilder = ImmutableDictionary.CreateBuilder<IModule, StreamLayout.IMaterializedValueNode>();
                    foreach (var sub in module.SubModules)
                    {
                        var res = Descend<T>(sub, attributes, structInfo, localGroup, indent + "    ");
                        subMatBuilder.AddRange(res);
                    }
                    var subMat = subMatBuilder.ToImmutable();

                    // we need to remove all wirings that this module copied from nested modules so that we don’t do wirings twice
                    var oldDownstreams = 
                        (module as FusedModule)?.Info.Downstreams.ToImmutableHashSet()
                        ?? module.Downstreams.ToImmutableHashSet();

                    var down = module.SubModules.Aggregate(oldDownstreams, (set, m) => set.Except(m.Downstreams));
                    foreach (var entry in down)
                    {
                        structInfo.Wire(entry.Key, entry.Value, indent);
                    }

                    // now rewrite the materialized value computation based on the copied modules and their computation nodes
                    var matNodeMapping = new Dictionary<StreamLayout.IMaterializedValueNode, StreamLayout.IMaterializedValueNode>();
                    var newMat = RewriteMaterializer(subMat, module.MaterializedValueComputation, matNodeMapping);
                    // and finally rewire all MaterializedValueSources to their new computation nodes
                    var materializedSources = structInfo.ExitMaterializationContext();
                    foreach (var c in materializedSources)
                    {
                        var ms = (IMaterializedValueSource)((GraphStageModule)c.CopyOf).Stage;
                        var mapped = ms.Computation is StreamLayout.Atomic
                            ? subMat[((StreamLayout.Atomic) ms.Computation).Module]
                            : matNodeMapping[ms.Computation];
                        
                        var newSrc = new MaterializedValueSource<T>(mapped, Outlet.Create<T>(ms.Outlet));
                        var replacement = new CopiedModule(c.Shape, c.Attributes, newSrc.Module);
                        structInfo.Replace(c, replacement, localGroup);
                    }

                    // the result for each level is the materialized value computation
                    return new[] {new KeyValuePair<IModule, StreamLayout.IMaterializedValueNode>(module, newMat)};
                }
            }
        }

        private static bool IsAsync<T>(IModule module)
        {
            return module is GraphStageModule || module is GraphModule
                ? module.Attributes.Contains<Attributes.AsyncBoundary>()
                : module.IsAtomic || module.Attributes.Contains<Attributes.AsyncBoundary>();
        }

        private static StreamLayout.IMaterializedValueNode RewriteMaterializer(IDictionary<IModule, StreamLayout.IMaterializedValueNode> subMat, StreamLayout.IMaterializedValueNode mat, Dictionary<StreamLayout.IMaterializedValueNode, StreamLayout.IMaterializedValueNode> mapping)
        {
            if (mat is StreamLayout.Atomic)
            {
                var atomic = (StreamLayout.Atomic) mat;
                var result = subMat[atomic.Module];
                mapping.Put(mat, result);
                return result;
            }
            if (mat is StreamLayout.Combine)
            {
                var combine = (StreamLayout.Combine) mat;
                var result = new StreamLayout.Combine(combine.Combinator, RewriteMaterializer(subMat, combine.Left, mapping), RewriteMaterializer(subMat, combine.Right, mapping));
                mapping.Put(mat, result);
                return result;
            }
            if (mat is StreamLayout.Transform)
            {
                var transform = (StreamLayout.Transform) mat;
                var result = new StreamLayout.Transform(transform.Transformator, RewriteMaterializer(subMat, transform.Node, mapping));
                mapping.Put(mat, result);
                return result;
            }

            return mat;
        }

        internal static bool IsAsync(IModule module)
        {
            CopiedModule copied;
            if ((copied = module as CopiedModule) != null)
            {
                var attrs = copied.Attributes.And(copied.CopyOf.Attributes);
                return attrs.GetAttribute<Attributes.AsyncBoundary>(null) != null;
            }

            return module.Attributes.GetAttribute<Attributes.AsyncBoundary>(null) != null;
        }

        /// <summary>
        /// Figure out the dispatcher setting of a module.
        /// </summary>
        internal static ActorAttributes.Dispatcher GetDispatcher(IModule module)
        {
            CopiedModule copied;
            if ((copied = module as CopiedModule) != null)
            {
                var attrs = copied.Attributes.And(copied.CopyOf.Attributes);
                return attrs.GetAttribute<ActorAttributes.Dispatcher>(null);
            }
            else
            {
                return module.Attributes.GetAttribute<ActorAttributes.Dispatcher>(null);
            }
        }

        /// <summary>
        /// See through copied modules to the “real” module.
        /// </summary>
        internal static IModule GetRealModule(IModule module)
        {
            return module is CopiedModule
                ? GetRealModule(((CopiedModule)module).CopyOf)
                : module;
        }
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

        public void EnterMaterializationContext()
        {
            _materializedSources.AddFirst(new LinkedList<CopiedModule>());
        }

        public IImmutableList<CopiedModule> ExitMaterializationContext()
        {
            if (_materializedSources.Count == 0) throw new ArgumentException("ExitMaterializationContext with empty stack");
            var x = _materializedSources.First.Value;
            _materializedSources.RemoveFirst();
            return x.ToImmutableList();
        }

        public void PushMaterializationSource(CopiedModule module)
        {
            if (_materializedSources.Count == 0) throw new ArgumentException("PushMaterializationSource without context");
            _materializedSources.First.Value.AddFirst(module);
        }

        public Streams.Fusing.StructuralInfo ToInfo()
        {
            return new Streams.Fusing.StructuralInfo(
                upstreams: ImmutableDictionary.CreateRange(Upstreams),
                downstreams: ImmutableDictionary.CreateRange(Downstreams),
                inOwners: ImmutableDictionary.CreateRange(InOwners),
                outOwners: ImmutableDictionary.CreateRange(OutOwners),
                allModules: ImmutableHashSet.CreateRange(Modules));
        }

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
                    if (subgroups.Count() > 1)
                    {
                        group.Clear();
                        foreach (var subgroup in subgroups)
                        {
                            var values = new HashSet<IModule>(subgroup);
                            newGroups.AddLast(values);
                        }
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
        public void RegisterInternal(Shape shape, string indent)
        {
            foreach (var outlet in shape.Outlets) InternalOuts.Add(outlet);
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
                InPort inport;
                if (Downstreams.TryGetValue(outport, out inport))
                {
                    Downstreams.Remove(outport);
                    Upstreams.Remove(inport);
                }
            }
        }

        /// <summary>
        /// Create and return a new grouping (i.e. an AsyncBoundary-delimited context)
        /// </summary>
        public ISet<IModule> CreateGroup(string indent)
        {
            var group = new HashSet<IModule>();
            Groups.AddLast(group);
            return group;
        }

        /// <summary>
        /// Add a module to the given group, performing normalization (i.e. giving it a unique port identity).
        /// </summary>
        public StreamLayout.Atomic AddModule(IModule module, ISet<IModule> group, Attributes inheritedAttributes, string indent, Shape oldShape = null)
        {
            var copy = oldShape == null
                ? new CopiedModule(module.Shape.DeepCopy(), inheritedAttributes, Fusing.GetRealModule(module))
                : module;
            oldShape = oldShape ?? module.Shape;
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
                InOwners.Add(mapd, copy);
            }

            var orig2 = oldShape.Outlets.GetEnumerator();
            var mapd2 = copy.Shape.Outlets.GetEnumerator();
            while (orig2.MoveNext() && mapd2.MoveNext())
            {
                var orig = orig2.Current;
                var mapd = mapd2.Current;
                AddMapping(orig, mapd, NewOutputs);
                OutOwners.Add(mapd, copy);
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
            {
                PushMaterializationSource((CopiedModule)copy);
            }
            else if (copy is GraphModule)
            {
                var mvids = ((GraphModule)copy).MaterializedValueIds;
                for (int i = 0; i < mvids.Length; i++)
                {
                    if (IsCopiedModuleWithGraphStageAndMaterializedValue(mvids[i]))
                    {
                        PushMaterializationSource((CopiedModule)mvids[i]);
                    }
                }
            }

            return new StreamLayout.Atomic(copy);
        }

        /// <summary>
        /// Record a wiring between two copied ports, using (and reducing) the port mappings.
        /// </summary>
        public void Wire(OutPort outPort, InPort inPort, string indent)
        {
            var newOut = RemoveMapping(outPort, NewOutputs);
            if (newOut == null) throw new ArgumentException($"wiring {outPort} -> {inPort}", nameof(outPort));
            var newIn = RemoveMapping(inPort, NewInputs);
            if (newIn == null) throw new ArgumentException($"wiring {outPort} -> {inPort}", nameof(inPort));
            Downstreams.Add(newOut, newIn);
            Upstreams.Add(newIn, newOut);
        }

        /// <summary>
        /// Replace all mappings for a given shape with its new (copied) form.
        /// </summary>
        public void Rewire(Shape oldShape, Shape newShape, string indent)
        {
            {
                var oins = oldShape.Inlets.GetEnumerator();
                var nins = newShape.Inlets.GetEnumerator();
                while (oins.MoveNext() && nins.MoveNext())
                {
                    var x = RemoveMapping(oins.Current, NewInputs);
                    if (x == null)
                        throw new ArgumentException($"rewiring {oins.Current} -> {nins.Current}", nameof(oldShape));
                    AddMapping(nins.Current, x, NewInputs);
                }
            }
            {
                var oouts = oldShape.Outlets.GetEnumerator();
                var nouts = newShape.Outlets.GetEnumerator();
                while (oouts.MoveNext() && nouts.MoveNext())
                {
                    var x = RemoveMapping(oouts.Current, NewOutputs);
                    if (x == null)
                        throw new ArgumentException($"rewiring {oouts.Current} -> {nouts.Current}", nameof(oldShape));
                    AddMapping(nouts.Current, x, NewOutputs);
                }
            }
        }

        /// <summary>
        /// Transform original into copied Inlets.
        /// </summary>
        public ImmutableArray<Inlet> NewInlets(IEnumerable<Inlet> old)
        {
            return old.Select(i => (Inlet)NewInputs[i].First.Value).ToImmutableArray();
        }

        /// <summary>
        /// Transform original into copied Outlets.
        /// </summary>
        public ImmutableArray<Outlet> NewOutlets(IEnumerable<Outlet> old)
        {
            return old.Select(o => (Outlet)NewOutputs[o].First.Value).ToImmutableArray();
        }

        private bool IsCopiedModuleWithGraphStageAndMaterializedValue(IModule module)
        {
            var copiedModule = module as CopiedModule;
            GraphStageModule graphStage;
            Type mvcType;
            return copiedModule != null
                && (graphStage = copiedModule.CopyOf as GraphStageModule) != null
                && (mvcType = graphStage.MaterializedValueComputation.GetType()).IsGenericType
                && mvcType.GetGenericTypeDefinition() == typeof(MaterializedValueSource<>);
        }

        private void AddMapping<T>(T orig, T mapd, IDictionary<T, LinkedList<T>> map)
        {
            LinkedList<T> values;
            if (map.TryGetValue(orig, out values)) values.AddLast(mapd);
            else map.Add(orig, new LinkedList<T>(new[] { mapd }));
        }

        private T RemoveMapping<T>(T orig, IDictionary<T, LinkedList<T>> map) where T : class
        {
            LinkedList<T> values;
            if (map.TryGetValue(orig, out values))
            {
                if (values.Count == 0) map.Remove(orig);
                else
                {
                    var x = values.First.Value;
                    values.RemoveFirst();
                    return x;
                }
            }
            return null;
        }
    }

    /// <summary>
    /// A fused graph of the right shape, containing a <see cref="FusedModule"/> which holds more information 
    /// on the operation structure of the contained stream topology for convenient graph traversal.
    /// </summary>
    internal sealed class FusedGraph<TShape, TMat> : IGraph<TShape, TMat> where TShape : Shape
    {
        public FusedGraph(FusedModule module, TShape shape)
        {
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (shape == null) throw new ArgumentNullException(nameof(shape));

            Module = module;
            Shape = shape;
        }

        public TShape Shape { get; }
        public IModule Module { get; }
        public IGraph<TShape, TMat> WithAttributes(Attributes attributes)
        {
            return new FusedGraph<TShape, TMat>(Module.WithAttributes(attributes) as FusedModule, Shape);
        }

        public IGraph<TShape, TMat> AddAttributes(Attributes attributes)
        {
            return WithAttributes(Module.Attributes.And(attributes));
        }

        public IGraph<TShape, TMat> Named(string name)
        {
            return AddAttributes(Attributes.CreateName(name));
        }

        public IGraph<TShape, TMat> Async()
        {
            return AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }
    }
}