using Akka.Actor;
using Akka.Cluster;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    internal sealed class DataEnvelope
    {
        static internal DataEnvelope DeletedEnvelope
        {
            get { return new DataEnvelope(DeletedData.Instance); }
        }

        readonly IReplicatedData _data;
        readonly IImmutableDictionary<UniqueAddress, PruningState> _pruning;

        internal IReplicatedData Data
        {
            get { return _data; }
        }

        internal IImmutableDictionary<UniqueAddress, PruningState> Pruning
        {
            get { return _pruning; }
        }

        internal DataEnvelope(IReplicatedData data)
            : this(data, ImmutableDictionary<UniqueAddress, PruningState>.Empty)
        { }

        internal DataEnvelope(IReplicatedData data, IImmutableDictionary<UniqueAddress, PruningState> pruning)
        {
            _data = data;
            _pruning = pruning;
        }

        internal bool NeedPruningFrom(UniqueAddress removedNode)
        {
            var r = Data as IRemovedNodePruning;
            if(r != null)
            {
                return r.NeedPruningFrom(removedNode);
            }
            return false;
        }

        internal DataEnvelope InitRemovedNodePruning(UniqueAddress removed, UniqueAddress owner)
        {
            return new DataEnvelope(Data, Pruning.Add(removed, new PruningState(owner, new PruningInitialized(ImmutableHashSet<Address>.Empty))));
        }

        internal DataEnvelope Prune(UniqueAddress from)
        {
            var r = Data as IRemovedNodePruning;
            if(r != null)
            {
                if(!_pruning.ContainsKey(from))
                {
                    throw new ArgumentException(String.Format("Can't prune {0} since it's not there", from));
                }
                var to = _pruning[from].Owner;
                var prunedData = r.Prune(from, to);
                return new DataEnvelope(prunedData, _pruning.SetItem(from, new PruningState(to, PruningPerformed.Instance)));
            }
            return this;
        }

        internal DataEnvelope Merge(DataEnvelope other)
        {
            if(other.Data == DeletedData.Instance)
            {
                return DeletedEnvelope;
            }
            else
            {
                var mergedRemovedNodePruning = other.Pruning;
                foreach(var kvp in Pruning)
                {
                    PruningState value;
                    var contains = mergedRemovedNodePruning.TryGetValue(kvp.Key, out value);
                    if(!contains)
                    {
                        mergedRemovedNodePruning = mergedRemovedNodePruning.SetItem(kvp.Key, kvp.Value);
                    }
                    else
                    {
                        mergedRemovedNodePruning = mergedRemovedNodePruning.SetItem(kvp.Key, value.Merge(kvp.Value));
                    }
                }
                var envelope = new DataEnvelope(Cleaned(Data, mergedRemovedNodePruning), mergedRemovedNodePruning);
                return envelope.Merge(other.Data);
            }
        }

        internal DataEnvelope Merge(IReplicatedData otherData)
        {
            if(otherData is DeletedData)
            {
                return DeletedEnvelope;
            }
            var data = (IReplicatedData)Data.Merge(Cleaned(otherData, _pruning));
            return new DataEnvelope(data, _pruning);
        }

        private IReplicatedData Cleaned(IReplicatedData c, IImmutableDictionary<UniqueAddress, PruningState> p)
        {
            return p.Aggregate(c, (state, kvp) =>
                {
                    if(c is IRemovedNodePruning)
                    {
                        if(kvp.Value.Phase is PruningPerformed)
                        {
                            if(((IRemovedNodePruning)c).NeedPruningFrom(kvp.Key))
                            {
                                return ((IRemovedNodePruning)c).PruningCleanup(kvp.Key);
                            }
                        }
                    }
                    return c;
                });
        }

        internal DataEnvelope AddSeen(Address node)
        {
            var changed = false;
            var newRemovedNodePruning = _pruning.Select(kvp =>
                {
                    var newPruningState = kvp.Value.AddSeen(node);
                    changed = (newPruningState != kvp.Value) || changed;
                    return new KeyValuePair<UniqueAddress, PruningState>(kvp.Key, newPruningState);
                }).ToImmutableDictionary();
            if(changed)
            {
                return new DataEnvelope(Data, newRemovedNodePruning);
            }
            return this;
        }

        public override bool Equals(object obj)
        {
            var other = obj as DataEnvelope;
            if(other == null)
            {
                return false;
            }
            var pruningCountsEqual = Pruning.Count == other.Pruning.Count;
            var elementsEqual = !Pruning.Except(other.Pruning).Any();
            return Data.Equals(other.Data) && pruningCountsEqual && elementsEqual;
        }
    }
}
