using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Artery.Utils;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery.Compress
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Decompress and cause compression advertisements.
    ///
    /// One per inbound message stream thus must demux by originUid to use the right tables.
    /// </summary>
    internal interface IInboundCompressions
    {
        void HitActorRef(long originUid, Address remote, IActorRef @ref, int n);
        IOptionVal<IActorRef> DecompressActorRef(long originUid, byte tableVersion, int idx);
        void ConfirmActorRefCompressionAdvertisement(long originUid, byte tableVersion);

        // Triggers compression advertisement via control message
        void RunNextActorRefAdvertisement();

        void HitClassManifest(long originUid, Address remote, string manifest, int n);
        IOptionVal<string> DecompressClassManifest(long originUid, byte tableVersion, int idx);
        void ConfirmClassManifestCompressionAdvertisement(long originUid, byte tableVersion);

        // Triggers compression advertisement via control message
        void RunNextClassManifestAdvertisement();

        HashSet<long> CurrentOriginUids { get; }

        // Remove compression and cancel advertisement scheduling for specific origin
        void Close(long originUid);
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// One per incoming Aeron stream, actual compression tables are kept per-originUid and created on demand.
    /// All access is via the Decoder stage.
    /// </summary>
    internal sealed class InboundCompressionsImpl : IInboundCompressions
    {
        private readonly Dictionary<long, InboundActorRefCompression> _actorRefsIns;
        private readonly ILoggingAdapter _inboundActorRefsLog;
        private readonly Func<long, InboundActorRefCompression> _createInboundActorRefsForOrigin;

        private readonly Dictionary<long, InboundManifestCompression> _classManifestsIns;
        private readonly ILoggingAdapter _inboundManifestLog;
        private readonly Func<long, InboundManifestCompression> _createInboundManifestsForOrigin;

        public ActorSystem System { get; }
        public IInboundContext InboundContext { get; }
        public ArterySettings.CompressionSettings Settings { get; }

        public InboundCompressionsImpl(
            ActorSystem system,
            IInboundContext inboundContext, 
            ArterySettings.CompressionSettings settings)
        {
            System = system;
            InboundContext = inboundContext;
            Settings = settings;

            _actorRefsIns = new Dictionary<long, InboundActorRefCompression>();
            _inboundActorRefsLog = Logging.GetLogger(System, typeof(InboundActorRefCompression));
            _createInboundActorRefsForOrigin = (originUid) =>
            {
                var actorRefHitters = new TopHeavyHitters<IActorRef>(Settings.ActorRefs.Max);
                return new InboundActorRefCompression(_inboundActorRefsLog, Settings, originUid, InboundContext,
                    actorRefHitters);
            };
            _classManifestsIns = new Dictionary<long, InboundManifestCompression>();
            _inboundManifestLog = Logging.GetLogger(System, typeof(InboundManifestCompression));
            _createInboundManifestsForOrigin = (originUid) =>
            {
                var manifestHitters = new TopHeavyHitters<string>(Settings.Manifests.Max);
                return new InboundManifestCompression(_inboundManifestLog, Settings, originUid, InboundContext,
                    manifestHitters);
            };
        }

        private InboundActorRefCompression ActorRefsIn(long originUid)
            => _actorRefsIns.ComputeIfAbsent(originUid, _createInboundActorRefsForOrigin);

        private InboundManifestCompression ClassManifestsIn(long originUid)
            => _classManifestsIns.ComputeIfAbsent(originUid, _createInboundManifestsForOrigin);

        // actor ref compression ---

        public IOptionVal<IActorRef> DecompressActorRef(long originUid, byte tableVersion, int idx)
            => ActorRefsIn(originUid).Decompress(tableVersion, idx);

        public void HitActorRef(long originUid, Address address, IActorRef @ref, int n)
        {
            DebugUtil.PrintLn($"[compress] HitActorRef({originUid}, {address}, {@ref}, {n})");
            ActorRefsIn(originUid).Increment(address, @ref, n);
        }

        public void ConfirmActorRefCompressionAdvertisement(long originUid, byte tableVersion)
        {
            if (_actorRefsIns.TryGetValue(originUid, out var a))
                a.ConfirmAdvertisement(tableVersion, gaveUp: false);
        }

        // Send compression table advertisement over control stream. Should be called from Decoder.
        public void RunNextActorRefAdvertisement()
        {
            var remove = new List<long>();
            foreach (var inbound in _actorRefsIns.Values)
            {
                switch (InboundContext.Association(inbound.OriginUid))
                {
                    case Some<IOutboundContext> a when !a.Get.AssociationState.IsQuarantined(inbound.OriginUid):
                        inbound.RunNextTableAdvertisement();
                        break;
                    default:
                        remove.Add(inbound.OriginUid);
                        break;
                }
            }
            foreach (var uid in remove)
            {
                Close(uid);
            }
        }

        // class manifest compression ---
        public IOptionVal<string> DecompressClassManifest(long originUid, byte tableVersion, int idx)
            => ClassManifestsIn(originUid).Decompress(tableVersion, idx);

        public void HitClassManifest(long originUid, Address address, string manifest, int n)
        {
            DebugUtil.PrintLn($"HitClassManifest({originUid}, {address}, {manifest}, {n})");
            ClassManifestsIn(originUid).Increment(address, manifest, n);
        }

        public void ConfirmClassManifestCompressionAdvertisement(long originUid, byte tableVersion)
        {
            if (_classManifestsIns.TryGetValue(originUid, out var a))
                a.ConfirmAdvertisement(tableVersion, gaveUp: false);
        }

        public void RunNextClassManifestAdvertisement()
        {
            var remove = new List<long>();
            foreach (var inbound in _classManifestsIns.Values)
            {
                switch (InboundContext.Association(inbound.OriginUid))
                {
                    case Some<IOutboundContext> a when !a.Get.AssociationState.IsQuarantined(inbound.OriginUid):
                        inbound.RunNextTableAdvertisement();
                        break;
                    default:
                        remove.Add(inbound.OriginUid);
                        break;
                }
            }
            foreach (var uid in remove)
            {
                Close(uid);
            }
        }

        public HashSet<long> CurrentOriginUids
        {
            get
            {
                var result = new List<long>();
                result.AddRange(_actorRefsIns.Keys);
                result.AddRange(_classManifestsIns.Keys);
                return new HashSet<long>(result);
            }
        }

        public void Close(long originUid)
        {
            _actorRefsIns.Remove(originUid);
            _classManifestsIns.Remove(originUid);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Dedicated per remote system inbound compression table.
    ///
    /// The outbound context is available by looking it up in the association.
    /// It can be used to advertise a compression table.
    /// If the association is not complete - we simply dont advertise the table, which is fine (handshake not yet complete).
    /// </summary>
    internal sealed class InboundActorRefCompression : InboundCompression<IActorRef>
    {
        public InboundActorRefCompression(
            ILoggingAdapter log,
            ArterySettings.CompressionSettings settings,
            long originUid,
            IInboundContext inboundContext,
            TopHeavyHitters<IActorRef> heavyHitters) :
            base(log, settings, originUid, inboundContext, heavyHitters)
        { }

        public override IOptionVal<IActorRef> Decompress(byte tableVersion, int idx)
            => DecompressInternal(tableVersion, idx);

        protected override void AdvertiseCompressionTable(
            IOutboundContext outboundContext,
            CompressionTable<IActorRef> table)
        {
            Log.Debug(
                $"Advertise {Logging.SimpleName(GetType())} compression [{table}] to {outboundContext.RemoteAddress}#{OriginUid}");
            outboundContext.SendControl(
                new CompressionProtocol.ActorRefCompressionAdvertisement(InboundContext.LocalAddress, table));
        }

        protected override ImmutableDictionary<IActorRef, int> BuildTableForAdvertisement(IEnumerable<IActorRef> elements)
        {
            var mb = ImmutableDictionary.CreateBuilder<IActorRef, int>();
            var idx = 0;
            foreach (var e in elements)
            {
                if (e is IInternalActorRef @ref)
                {
                    var isTemporaryRef = 
                        (@ref.IsLocal && @ref is PromiseActorRef) ||
                        (!@ref.IsLocal && @ref.Path.Elements[0].Equals("temp"));
                    if (!isTemporaryRef)
                    {
                        mb.AddOrSet(@ref, idx);
                        idx++;
                    }
                }
            }

            return mb.ToImmutable();
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class InboundManifestCompression : InboundCompression<string>
    {
        public InboundManifestCompression(
            ILoggingAdapter log,
            ArterySettings.CompressionSettings settings,
            long originUid,
            IInboundContext inboundContext,
            TopHeavyHitters<string> heavyHitters)
        : base(log, settings, originUid, inboundContext, heavyHitters)
        { }

        protected override void AdvertiseCompressionTable(IOutboundContext outboundContext, CompressionTable<string> table)
        {
            Log.Debug($"Advertise {Logging.SimpleName(GetType())} compression [{table}] to [{outboundContext.RemoteAddress}#{OriginUid}]");
            outboundContext.SendControl(
                new CompressionProtocol.ClassManifestCompressionAdvertisement(InboundContext.LocalAddress, table));

        }

        public override void Increment(Address remoteAddress, string value, long n)
        {
            if(!string.IsNullOrWhiteSpace(value)) base.Increment(remoteAddress, value, n);
        }

        public override IOptionVal<string> Decompress(byte incomingTableVersion, int idx)
        {
            return DecompressInternal(incomingTableVersion, idx);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Handles counting and detecting of heavy-hitters and compressing them via a table lookup.
    ///
    /// Access to this class must be externally synchronized (e.g. by accessing it from only Actors or a GraphStage etc).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal abstract class InboundCompression<T> where T: class
    {
        public static int KeepOldTablesNumber => 3; // TODO: could be configurable

        public static class Tables
        {
            public static Tables<TTable> Empty<TTable>() 
                where TTable : class, T => new Tables<TTable>(
                    oldTables: new List<DecompressionTable<TTable>>(new[] { DecompressionTable.Disabled<TTable>() }),
                    activeTable: DecompressionTable.Empty<TTable>(),
                    nextTable: DecompressionTable.Empty<TTable>().Copy(version: 1),
                    advertisementInProgress: Option<CompressionTable<TTable>>.None,
                    keepOldTables: KeepOldTablesNumber);
        }

        /// <summary>
        /// Encapsulates the various compression tables that Inbound Compression uses.
        /// </summary>
        /// <typeparam name="TTable"></typeparam>
        public class Tables<TTable> 
            where TTable: class, T
        {
            public List<DecompressionTable<TTable>> OldTables { get; }
            public DecompressionTable<TTable> ActiveTable { get; }
            public DecompressionTable<TTable> NextTable { get; }
            public Option<CompressionTable<TTable>> AdvertisementInProgress { get; }
            public int KeepOldTable { get; }

            /// <summary>
            /// Encapsulates the various compression tables that Inbound Compression uses.
            /// </summary>
            /// <param name="oldTables">
            /// is guaranteed to always have at-least one and at-most [[keepOldTables]] elements.
            /// It starts with containing only a single "disabled" table (versioned as `DecompressionTable.DisabledVersion`),
            /// and from there on continuously accumulates at most [[keepOldTables]] recently used tables.
            /// </param>
            /// <param name="activeTable"></param>
            /// <param name="nextTable"></param>
            /// <param name="advertisementInProgress"></param>
            /// <param name="keepOldTables"></param>
            public Tables(
                List<DecompressionTable<TTable>> oldTables,
                DecompressionTable<TTable> activeTable,
                DecompressionTable<TTable> nextTable,
                Option<CompressionTable<TTable>> advertisementInProgress,
                int keepOldTables)
            {
                OldTables = oldTables;
                ActiveTable = activeTable;
                NextTable = nextTable;
                AdvertisementInProgress = advertisementInProgress;
                KeepOldTable = keepOldTables;
            }

            public IOptionVal<DecompressionTable<TTable>> SelectTable(int version)
            {
                if (ActiveTable.Version == version)
                {
                    DebugUtil.PrintLn($"Found table [version: {version}], was [ACTIVE]{ActiveTable}");
                    return OptionVal.Some(ActiveTable);
                }

                var found = OptionVal.None<DecompressionTable<TTable>>();
                foreach (var table in OldTables)
                {
                    if(table.Version == version)
                    {
                        found = OptionVal.Some(table);
                        break;
                    }
                }

#if COMPRESS_DEBUG
                switch (found)
                {
                    case Some<DecompressionTable<TTable>> t:
                        DebugUtil.PrintLn($"Found table [version: {version}], was [OLD][{found}], " +
                                          $"old tables: [{string.Join(", ", OldTables.Select<DecompressionTable<TTable>, int>(ot => ot.Version))}]");
                        break;
                    default:
                        DebugUtil.PrintLn($"Did not find table [version: {version}], " +
                                          $"old tables: [{string.Join(", ", OldTables.Select<DecompressionTable<TTable>, int>(ot => ot.Version))}], " +
                                          $"ActiveTable: {ActiveTable}, " +
                                          $"NextTable: {NextTable}");
                        break;
                }
#endif
                return found;
            }

            public Tables<TTable> StartUsingNextTable()
            {
                static byte IncrementTableVersion(byte version)
                    => (byte)(version == 127 ? 0 : ++version);

                var newOldTables = new List<DecompressionTable<TTable>>( new []{ ActiveTable });
                newOldTables.AddRange(OldTables);
                newOldTables.Capacity = KeepOldTable;
                newOldTables.TrimExcess();
                return new Tables<TTable>(
                    oldTables: newOldTables,
                    activeTable: NextTable,
                    nextTable: DecompressionTable.Empty<TTable>().Copy(version: IncrementTableVersion(NextTable.Version)),
                    advertisementInProgress: Option<CompressionTable<TTable>>.None,
                    keepOldTables: KeepOldTable);
            }

            public Tables<TTable> Copy(
                List<DecompressionTable<TTable>> oldTables = null,
                DecompressionTable<TTable> activeTable = null,
                DecompressionTable<TTable> nextTable = null,
                Option<CompressionTable<TTable>>? advertisementInProgress = null,
                int keepOldTables = -1)
                => new Tables<TTable>(
                    oldTables ?? OldTables,
                    activeTable ?? ActiveTable,
                    nextTable ?? NextTable,
                    advertisementInProgress ?? AdvertisementInProgress,
                    keepOldTables > -1 ? keepOldTables : KeepOldTable);
        }

        public ILoggingAdapter Log { get; }
        public ArterySettings.CompressionSettings Settings { get; }
        public long OriginUid { get; }
        public IInboundContext InboundContext { get; }
        public TopHeavyHitters<T> HeavyHitters { get; }

        private Tables<T> _tables = Tables.Empty<T>();

        // We should not continue sending advertisements to an association that might be dead (not quarantined yet)
        private volatile bool _alive = true;

        private int _resendCount = 0;
        private const int MaxResendCount = 3;

        private readonly CountMinSketch _cms = new CountMinSketch(16, 1024, (int)DateTime.Now.TimeOfDay.TotalMilliseconds);

        protected InboundCompression(
            ILoggingAdapter log,
            ArterySettings.CompressionSettings settings,
            long originUid,
            IInboundContext inboundContext,
            TopHeavyHitters<T> heavyHitters)
        {
            Log = log;
            Settings = settings;
            OriginUid = originUid;
            InboundContext = inboundContext;
            HeavyHitters = heavyHitters;

            Log.Debug($"Initializing {Logging.SimpleName(GetType())} for OriginUid {OriginUid}");
        }

        /* ==== COMPRESSION ==== */

        /// <summary>
        /// Override and specialize if needed, for default compression logic delegate to 3-param overload
        /// </summary>
        /// <param name="incomingTableVersion"></param>
        /// <param name="idx"></param>
        /// <returns></returns>
        public abstract IOptionVal<T> Decompress(byte incomingTableVersion, int idx);

        /// <summary>
        /// Decompress given identifier into its original representation.
        /// Passed in tableIds must only ever be in not-decreasing order (as old tables are dropped),
        /// tableIds must not have gaps. If an "old" tableId is received the value will fail to be decompressed.
        /// </summary>
        /// <param name="incomingTableVersion"></param>
        /// <param name="idx"></param>
        /// <returns></returns>
        /// <exception cref="UnknownCompressedIdException">
        /// if given id is not known, this may indicate a bug – such situation should not happen.
        /// </exception>
        public IOptionVal<T> DecompressInternal(byte incomingTableVersion, int idx)
        {
            var attemptCounter = 0;
            while (true)
            {
                // effectively should never loop more than once, to avoid infinite recursion blow up eagerly
                if (attemptCounter > 2) 
                    throw new IllegalStateException($"Unable to decompress {idx} from table {incomingTableVersion}. Internal tables: {_tables}");

                var current = _tables;
                var activeVersion = current.ActiveTable.Version;

                if (incomingTableVersion == DecompressionTable.DisabledVersion)
                {
                    // no compression, bail out early
                    return OptionVal.None<T>();
                }

                var selectedTable = current.SelectTable(version: incomingTableVersion);
                if (selectedTable.IsDefined)
                {
                    var value = selectedTable.Get[idx];
                    if (value != null) return OptionVal.Some(value);
                    throw new UnknownCompressedIdException(idx);
                }

                var incomingVersionIsAdvertisementInProgress =
                    current.AdvertisementInProgress.HasValue
                    && incomingTableVersion == current.AdvertisementInProgress.Value.Version;

                if (incomingVersionIsAdvertisementInProgress)
                {
                    Log.Debug($"Received first value from OriginUid [{OriginUid}] compressed using the advertised compression table, " + $"flipping to it (version: {current.NextTable.Version})");
                    ConfirmAdvertisement(incomingTableVersion, gaveUp: false);
                    attemptCounter++;
                    continue;
                }

                // any other case
                // which means that incoming version was > nextTable.version, which likely that
                // it is using a table that was built for previous incarnation of this system
                Log.Warning($"Inbound message from OriginUid{OriginUid} is using unknown compression table version. " + "It may have been sent with compression table built for previous incarnation of this system. " + $"Versions ActiveTable: {activeVersion}, NextTable: {current.NextTable.Version}, IncomingTable: {incomingTableVersion}");
                return OptionVal.None<T>();
            }
        }

        public void ConfirmAdvertisement(byte tableVersion, bool gaveUp)
        {
            if (!_tables.AdvertisementInProgress.HasValue)
                return; // already confirmed

            var inProgress = _tables.AdvertisementInProgress.Value;
            if(tableVersion == inProgress.Version)
            {
                _tables = _tables.StartUsingNextTable();
                Log.Debug(
                    $"{(gaveUp ? "Gave up" : "Confirmed")} compression table version [{tableVersion}] for OriginUid [{OriginUid}]");
            }
            else
                Log.Debug($"{(gaveUp ? "Gave up" : "Confirmed")} compression table version [{tableVersion}] for OriginUid [{OriginUid}] but other version in progress [{inProgress.Version}]");
        }

        /// <summary>
        /// Add `n` occurrence for the given key and call `heavyHitterDetected` if element has become a heavy hitter.
        /// Empty keys are omitted.
        /// </summary>
        /// <param name="remoteAddress"></param>
        /// <param name="value"></param>
        /// <param name="n"></param>
        public virtual void Increment(Address remoteAddress, T value, long n)
        {
            var count = _cms.AddObjectAndEstimateCount(value, n);
            AddAndCheckIfHeavyHitterDetected(value, count);
            Volatile.Write(ref _alive, true);
        }

        /// <summary>
        /// Mutates heavy hitters
        /// </summary>
        /// <param name="value"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        private bool AddAndCheckIfHeavyHitterDetected(T value, long count)
            => HeavyHitters.Update(value, count);

        /* ==== TABLE ADVERTISEMENT ==== */

        /// <summary>
        /// Entry point to advertising a new compression table.
        ///
        /// [1] First we must *hand the new table over to the Incoming compression side on this system*,
        /// so it will not be used by someone else before "we" know about it in the Decoder.
        /// [2] Then the table must be *advertised to the remote system*, and MAY start using it immediately
        ///
        /// It must be advertised to the other side so it can start using it in its outgoing compression.
        /// Triggers compression table advertisement. May be triggered by schedule or manually, i.e. for testing.
        /// </summary>
        public void RunNextTableAdvertisement()
        {
            DebugUtil.PrintLn($"RunNextTableAdvertisement, tables = {_tables}");

            if (!_tables.AdvertisementInProgress.HasValue)
            {
                switch (InboundContext.Association(OriginUid))
                {
                    case Some<IOutboundContext> association:
                        if (Volatile.Read(ref _alive) && association.Get.IsOrdinaryMessageStreamActive())
                        {
                            var table = PrepareCompressionAdvertisement(_tables.NextTable.Version);
                            // TODO ARTERY expensive, check if building the other way wouldn't be faster?
                            var nextState = _tables.Copy(
                                nextTable: table.Invert(),
                                advertisementInProgress: new Option<CompressionTable<T>>(table));
                            _tables = nextState;
                            Volatile.Write(ref _alive, false); // will be set to true on first incoming message
                            _resendCount = 0;
                            AdvertiseCompressionTable(association.Get, table);
                        } else if (association.Get.IsOrdinaryMessageStreamActive())
                        {
                            Log.Debug($"{Logging.SimpleName(_tables.ActiveTable)} for OriginUid [{OriginUid}] not changed, no need to advertise same.");
                        }
                        break;
                    default:
                        // otherwise it's too early, association not ready yet.
                        // so we don't build the table since we would not be able to send it anyway.
                        Log.Debug($"No Association for OriginUid [{OriginUid}] yet, unable to advertise compression table.");
                        break;
                }
            }
            else
            {
                _resendCount++;
                var inProgress = _tables.AdvertisementInProgress.Value;
                if (_resendCount <= MaxResendCount)
                {
                    // The ActorRefCompressionAdvertisement message is resent because it can be lost
                    switch (InboundContext.Association(OriginUid))
                    {
                        case Some<IOutboundContext> association:
                            Log.Debug($"Advertisement in progress for OriginUid [{OriginUid}] version [{inProgress.Version}], resending [{_resendCount}:{MaxResendCount}]");
                            AdvertiseCompressionTable(association.Get, inProgress); // resend
                            break;
                        default:
                            // no-op
                            break;
                    }
                }
                else
                {
                    // give up, it might be dead
                    Log.Debug($"Advertisement in progress for OriginUid [{OriginUid}] version [{inProgress.Version}] but no confirmation after retries.");
                    ConfirmAdvertisement(inProgress.Version, gaveUp: true);
                }
            }
        }

        /// <summary>
        /// Must be implemented by extending classes in order to send a `ControlMessage`
        /// of appropriate type to the remote system in order to advertise the compression table to it.
        /// </summary>
        /// <param name="association"></param>
        /// <param name="table"></param>
        protected abstract void AdvertiseCompressionTable(IOutboundContext association, CompressionTable<T> table);

        private CompressionTable<T> PrepareCompressionAdvertisement(byte nextTableVersion)
        {
            var mappings = BuildTableForAdvertisement(HeavyHitters);
            return new CompressionTable<T>(OriginUid, nextTableVersion, mappings);
        }

        protected virtual ImmutableDictionary<T, int> BuildTableForAdvertisement(IEnumerable<T> elements)
        {
            // TODO ARTERY optimized somewhat, check if still to heavy; could be encoded into simple array
            var mb = ImmutableDictionary.CreateBuilder<T, int>();
            mb.AddRange(elements.ZipWithIndex());
            return mb.ToImmutable();
        }

        public override string ToString()
            => $"{Logging.SimpleName(GetType())}(CountMinSketch: {_cms}, HeavyHitters: {HeavyHitters})";
    }

    internal class UnknownCompressedIdException : AkkaException
    {
        public UnknownCompressedIdException(long id) : base(
            $"Attempted de-compress unknown id [{id}]! " +
            "This could happen if this node has started a new ActorSystem bound to the same address as previously, " +
            "and previous messages from a remote system were still in flight (using an old compression table). ," +
            "The remote system is expected to drop the compression table and this system will advertise a new one.")
        { }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Literally, no compression!
    /// </summary>
    internal sealed class NoInboundCompressions : IInboundCompressions
    {
        public static readonly NoInboundCompressions Instance = new NoInboundCompressions();

        private NoInboundCompressions() { }

        public void HitActorRef(long originUid, Address remote, IActorRef @ref, int n) { }
        public IOptionVal<IActorRef> DecompressActorRef(long originUid, byte tableVersion, int idx)
        {
            if(idx < 0) 
                throw new ArgumentException($"Attempted decompression of illegal compression id: {idx}");
            return OptionVal.None<IActorRef>();
        }
        public void ConfirmActorRefCompressionAdvertisement(long originUid, byte tableVersion) { }
        public void RunNextActorRefAdvertisement() { }

        public void HitClassManifest(long originUid, Address remote, string manifest, int n) { }
        public IOptionVal<string> DecompressClassManifest(long originUid, byte tableVersion, int idx)
        {
            if (idx < 0)
                throw new ArgumentException($"Attempted decompression of illegal compression id: {idx}");
            return OptionVal.None<string>();
        }
        public void ConfirmClassManifestCompressionAdvertisement(long originUid, byte tableVersion) { }
        public void RunNextClassManifestAdvertisement() { }

        public HashSet<long> CurrentOriginUids => new HashSet<long>();

        public void Close(long originUid) { }
    }
}
