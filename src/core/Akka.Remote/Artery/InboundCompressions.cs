using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Artery.Compress;
using Akka.Remote.Artery.Settings;
using Akka.Remote.Artery.Utils;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Remote.Artery
{
    internal interface IInboundCompressions
    {
        void HitActorRef(long originUid, Address remote, IActorRef @ref, int n);
        Option<IActorRef> DecompressActorRef(long originUid, byte tableVersion, int idx);
        void ConfirmActorRefCompressionAdvertisement(long originUid, byte tableVersion);

        // Triggers compression advertisement via control message
        void RunNextActorRefAdvertisement();

        void HitClassManifest(long originUid, Address remote, string manifest, int n);
        Option<string> DecompressClassManifest(long originUid, byte tableVersion, int idx);
        void ConfirmClassManifestCompressionAdvertisement(long originUid, byte tableVersion);

        // Triggers compression advertisement via control message
        void RunNextClassManifestAdvertisement();

        HashSet<long> CurrentOriginUids { get; }

        // Remove compression and cancel advertisement scheduling for specific origin
        void Close(long originUid);
    }

    internal sealed class InboundCompressionsImpl : IInboundCompressions
    {
        private readonly Dictionary<long, InboundActorRefCompression> _actorRefsIns = new Dictionary<long, InboundActorRefCompression>();
        private readonly ILoggingAdapter _inboundActorRefsLog;

        private readonly Dictionary<long, InboundManifestCompression> _classManifestsIns = new Dictionary<long, InboundManifestCompression>();
        private readonly ILoggingAdapter _inboundManifestLog;

        private readonly ILoggingAdapter _log;

        public ActorSystem System { get; }
        public InboundContext InboundContext { get; }
        public CompressionSettings Settings { get; }
        public RemotingFlightRecorder FlightRecorder { get; }

        public InboundCompressionsImpl(
            ActorSystem system, 
            InboundContext inboundContext, 
            CompressionSettings settings, 
            RemotingFlightRecorder flightRecorder)
        {
            System = system;
            InboundContext = inboundContext;
            Settings = settings;
            FlightRecorder = flightRecorder;

            _inboundActorRefsLog = Logging.GetLogger(System, typeof(InboundActorRefCompression));
            _inboundManifestLog = Logging.GetLogger(System, typeof(InboundManifestCompression));
            _log = Logging.GetLogger(System, this);
        }

        private InboundActorRefCompression CreateInboundActorRefsForOrigin(long originUid)
        {
            var actorRefHitters = new TopHeavyHitters<IActorRef>(Settings.ActorRefs.Max);
            return new InboundActorRefCompression(_inboundActorRefsLog, Settings, originUid, InboundContext, actorRefHitters);
        }

        private InboundActorRefCompression ActorRefsIn(long originUid)
            => _actorRefsIns.ComputeIfAbsent(originUid, CreateInboundActorRefsForOrigin);

        private InboundManifestCompression CreateInboundManifestsForOrigin(long originUid)
        {
            var manifestHitters = new TopHeavyHitters<string>(Settings.Manifests.Max);
            return new InboundManifestCompression(_inboundManifestLog, Settings, originUid, InboundContext, manifestHitters);
        }

        private InboundManifestCompression ClassManifestsIn(long originUid)
            => _classManifestsIns.ComputeIfAbsent(originUid, CreateInboundManifestsForOrigin);

        // actor ref compression ---

        public Option<IActorRef> DecompressActorRef(long originUid, byte tableVersion, int idx)
            => ActorRefsIn(originUid).Decompress(tableVersion, idx);

        public void HitActorRef(long originUid, Address address, IActorRef @ref, int n)
        {
            DebugUtil.PrintLn($"HitActorRef({originUid}, {address}, {@ref}, {n})");
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
                var association = InboundContext.Association(inbound.OriginUid);
                if (association.HasValue)
                {
                    if (association.Value.AssociationState.IsQuarantined(inbound.OriginUid))
                        FlightRecorder.CompressionActorRefAdveertisement(inbound.OriginUid);
                    inbound.RunNextTableAdvertisement();
                }
                else
                {
                    remove.Add(inbound.OriginUid);
                }

                foreach (var uid in remove)
                {
                    Close(uid);
                }
            }
        }

        // class manifest compression ---
        public Option<string> DecompressClassManifest(long originUid, byte tableVersion, int idx)
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
                var association = InboundContext.Association(inbound.OriginUid);
                if (association.HasValue)
                {
                    if (!association.AssociationState.IsQuarantined(inbound.OriginUid))
                    {
                        FlightRecorder.CompressionClassManifestAdvertisement(inbound.OriginUid);
                        inbound.RunNextTableAdvertisement();
                    }
                    else
                    {
                        remove.Add(inbound.OriginUid);
                    }
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
            CompressionSettings settings,
            long originUid,
            InboundContext inboundContext,
            TopHeavyHitters<IActorRef> heavyHitters) :
            base(log, settings, originUid, inboundContext, heavyHitters)
        { }

        public override Option<IActorRef> Decompress(byte tableVersion, int idx)
            => base.DecompressInternal(tableVersion, idx, 0);

        protected override void AdvertiseCompressionTable(
            OutboundContext outboundContext,
            CompressionTable<IActorRef> table)
        {
            Log.Debug(
                $"Advertise {Logging.SimpleName(this.GetType())} compression [{table}] to {outboundContext.RemoteAddress}#{OriginUid}");
            OutboundContext.SendControl(
                CompressionProtocol.ActorRefCompressionAdvertisement(InboundContext.LocalAddress, table));
        }

        protected override Dictionary<IActorRef, int> BuildTableForAdvertisement(IEnumerable<IActorRef> elements)
        {
            var mb = new Dictionary<IActorRef, int>();
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

            return mb;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class InboundManifestCompression : InboundCompression<string>
    {
        public InboundManifestCompression(
            ILoggingAdapter log,
            CompressionSettings settings,
            long originUid,
            InboundContext inboundContext,
            TopHeavyHitters<string> heavyHitters)
        : base(log, settings, originUid, inboundContext, heavyHitters)
        { }

        protected override void AdvertiseCompressionTable(OutboundContext association, CompressionTable<string> table)
        {
            Log.Debug($"Advertise {Logging.SimpleName(GetType())} compression [{table}] to [{OutboundContext.RemoteAddress}#{OriginUid}]");
            OutboundContext.SendControl(
                CompressionProtocol.ClassManifestCompressionAdvertisement(InboundContext.LocalAddress, table));

        }

        public override void Increment(Address remoteAddress, string value, long n)
        {
            if(!string.IsNullOrWhiteSpace(value)) base.Increment(remoteAddress, value, n);
        }

        public override Option<string> Decompress(byte incomingTableVersion, int idx)
        {
            return DecompressInternal(incomingTableVersion, idx, 0);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Handles counting and detecting of heavy-hitters and compressing them via a table lookup.
    ///
    /// Access to this class must be externally synchronized (e.g. by accessing it from only Actors or a GraphStage etc).
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal abstract class InboundCompression<T>
    {
        public static int KeepOldTablesNumber => 3; // TODO: could be configurable

        /// <summary>
        /// Encapsulates the various compression tables that Inbound Compression uses.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public class Tables<T>
        {
            public static Tables<T> Empty => new Tables<T>(
                oldTables: new List<DecompressionTable<T>>( new []{ DecompressionTable<T>.Disabled }), 
                activeTable: DecompressionTable<T>.Empty,
                nextTable: DecompressionTable<T>.Empty.Copy(version: 1),
                advertisementInProgress: Option<CompressionTable<T>>.None, 
                keepOldTables: KeepOldTablesNumber);

            public List<DecompressionTable<T>> OldTables { get; }
            public DecompressionTable<T> ActiveTable { get; }
            public DecompressionTable<T> NextTable { get; }
            public Option<CompressionTable<T>> AdvertisementInProgress { get; }
            public int KeepOldTable { get; }

            /// <summary>
            /// TBD
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
                List<DecompressionTable<T>> oldTables,
                DecompressionTable<T> activeTable,
                DecompressionTable<T> nextTable,
                Option<CompressionTable<T>> advertisementInProgress,
                int keepOldTables)
            {
                OldTables = oldTables;
                ActiveTable = activeTable;
                NextTable = nextTable;
                AdvertisementInProgress = advertisementInProgress;
                KeepOldTable = keepOldTables;
            }

            public Tables<T> Copy(
                List<DecompressionTable<T>> oldTables = null,
                DecompressionTable<T> activeTable = null,
                DecompressionTable<T> nextTable = null,
                Option<CompressionTable<T>>? advertisementInProgress = null,
                int keepOldTables = -1)
                => new Tables<T>(
                        oldTables ?? OldTables,
                        activeTable ?? ActiveTable,
                        nextTable ?? NextTable,
                        advertisementInProgress ?? AdvertisementInProgress,
                        keepOldTables > -1 ? keepOldTables : KeepOldTable );

            public Option<DecompressionTable<T>> SelectTable(int version)
            {
                if (ActiveTable.version == version)
                {
                    DebugUtil.PrintLn($"Found table [version: {version}], was [ACTIVE]{ActiveTable}");
                    return new Option<DecompressionTable<T>>(ActiveTable);
                }

                Option<DecompressionTable<T>> found = Option<DecompressionTable<T>>.None;
                // ARTERY: OldTable needs to be reversed?
                foreach (var table in OldTables)
                {
                    if(table.Version == version)
                    {
                        found = new Option<DecompressionTable<T>>(table);
                        break;
                    }
                }

                if (found.HasValue)
                    DebugUtil.PrintLn($"Found table [version: {version}], was [OLD][{found}], " +
                                      $"old tables: [{string.Join(", ", OldTables.Select<DecompressionTable<T>, int>(ot => ot.Version))}]");
                else
                    DebugUtil.PrintLn($"Did not find table [version: {version}], " +
                                      $"old tables: [{string.Join(", ", OldTables.Select<DecompressionTable<T>, int>(ot => ot.Version))}], " +
                                      $"ActiveTable: {ActiveTable}, " +
                                      $"NextTable: {NextTable}");

                return found;
            }

            public Tables<T> StartUsingNextTable()
            {
                byte IncrementTableVersion(byte version)
                    => (byte)(version == 127 ? 0 : ++version);

                var newOldTables = new List<DecompressionTable<T>>( new []{ ActiveTable });
                newOldTables.AddRange(OldTables);
                newOldTables.Capacity = KeepOldTable;
                newOldTables.TrimExcess();
                return new Tables<T>(
                    oldTables: newOldTables,
                    activeTable: NextTable,
                    nextTable: DecompressionTable<T>.Empty.Copy(version: IncrementTableVersion(NextTable.Version)),
                    advertisementInProgress = Option<CompressionTable<T>>.None,
                    keepOldTables: KeepOldTable);
            }
        }

        public ILoggingAdapter Log { get; }
        public CompressionSettings Settings { get; }
        public long OriginUid { get; }
        public InboundContext InboundContext { get; }
        public TopHeavyHitters<T> HeavyHitters { get; }

        public Tables<T> CompressionTables { get; protected set; } = Tables<T>.Empty;

        // We should not continue sending advertisements to an association that might be dead (not quarantined yet)
        public volatile bool _alive = true;
        public bool Alive => Volatile.Read(ref _alive);

        public int ResendCount { get; set; } = 0;
        public int MaxResendCount { get; set; } = 3;

        public CountMinSketch Cms { get; } = new CountMinSketch(16, 1024, (int)DateTime.Now.TimeOfDay.TotalMilliseconds);

        protected InboundCompression(
            ILoggingAdapter log,
            CompressionSettings settings,
            long originUid,
            InboundContext inboundContext,
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
        public abstract Option<T> Decompress(byte incomingTableVersion, int idx);

        /// <summary>
        /// Decompress given identifier into its original representation.
        /// Passed in tableIds must only ever be in not-decreasing order (as old tables are dropped),
        /// tableIds must not have gaps. If an "old" tableId is received the value will fail to be decompressed.
        /// </summary>
        /// <param name="incomingTableVersion"></param>
        /// <param name="idx"></param>
        /// <param name="attemptCounter"></param>
        /// <returns></returns>
        /// <exception cref="UnknownCompressedIdException">
        /// if given id is not known, this may indicate a bug – such situation should not happen.
        /// </exception>
        public Option<T> DecompressInternal(byte incomingTableVersion, int idx, int attemptCounter)
        {
            // effectively should never loop more than once, to avoid infinite recursion blow up eagerly
            if(attemptCounter > 2)
                throw new IllegalStateException($"Unable to decompress {idx} from table {incomingTableVersion}. Internal tables: {CompressionTables}");

            var current = CompressionTables;
            var activeVersion = current.ActiveTable.Version;

            bool IncomingVersionIsAdvertisementInProgress()
                => current.AdvertisementInProgress.HasValue &&
                   incomingTableVersion == current.AdvertisementInProgress.Value.Version;

            // no compression, bail out early
            if (incomingTableVersion == DecompressionTable.DisabledVersion)
                return Option<T>.None;

            var currentTable = current.SelectTable(version: incomingTableVersion);
            if (currentTable.HasValue)
            {
                var selectedTable = currentTable.Value;
                var value = selectedTable[idx];
                if(value is object)
                    return new Option<T>(value);
                throw new UnknownCompressedIdException(idx);
            }

            if (IncomingVersionIsAdvertisementInProgress())
            {
                Log.Debug($"Received first value from OriginUid [{OriginUid}] compressed using the advertised compression table, " +
                          $"flipping to it (version: {current.NextTable.Version})");
                ConfirmAdvertisement(incomingTableVersion, gaveUp: false);
                return DecompressInternal(incomingTableVersion, idx, ++attemptCounter); // recurse
            }

            // which means that incoming version was > nextTable.version, which likely that
            // it is using a table that was built for previous incarnation of this system
            Log.Warning($"Inbound message from OriginUid{OriginUid} is using unknown compression table version. " +
                        "It may have been sent with compression table built for previous incarnation of this system. " +
                        $"Versions ActiveTable: {activeVersion}, NextTable: {current.NextTable.Version}, IncomingTable: {incomingTableVersion}");
            return Option<T>.None;
        }

        public void ConfirmAdvertisement(byte tableVersion, bool gaveUp)
        {
            if (!CompressionTables.AdvertisementInProgress.HasValue)
                return; // already confirmed

            var inProgress = CompressionTables.AdvertisementInProgress.Value;
            if(tableVersion == inProgress.Version)
                Log.Debug($"{(gaveUp ? "Gave up" : "Confirmed")} compression table version [{tableVersion}] for OriginUid [{OriginUid}]");
            else
                Log.Debug($"{(gaveUp ? "Gave up" : "Confirmed")} compression table version [{tableVersion}] for OriginUid [{OriginUid}] but other version in progress [{inProgress.Version}]");
        }

        /// <summary>
        /// Add `n` occurrence for the given key and call `heavyHittedDetected` if element has become a heavy hitter.
        /// Empty keys are omitted.
        /// </summary>
        /// <param name="remoteAddress"></param>
        /// <param name="value"></param>
        /// <param name="n"></param>
        public virtual void Increment(Address remoteAddress, T value, long n)
        {
            var count = Cms.AddObjectAndEstimateCount(value, n);
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
            DebugUtil.PrintLn($"RunNextTableAdvertisement, tables = {CompressionTables}");

            if (!CompressionTables.AdvertisementInProgress.HasValue)
            {
                var association = InboundContext.Association(OriginUid);
                if (association.HasValue)
                {
                    if (association.Value.IsOrdinaryMessageStreamActive())
                    {
                        if (Volatile.Read(ref _alive))
                        {
                            var table = PrepareCompressionAdvertisement(CompressionTables.NextTable.Version);
                            // TODO expensive, check if building the other way wouldn't be faster?
                            var nextState = CompressionTables.Copy(nextTable: table.Invert(),
                                advertisementInProgress: new Option<CompressionTable<T>>(table));
                            CompressionTables = nextState;
                            Volatile.Write(ref _alive, false); // will be set to true on first incoming message
                            ResendCount = 0;
                            AdvertiseCompressionTable(association.Value, table);
                        }
                        else
                        {
                            Log.Debug($"{Logging.SimpleName(CompressionTables.ActiveTable)} for OriginUid [{OriginUid}] not changed, no need to advertise same.");
                        }
                    }
                    // ARTERY: Original code does not have code for this condition
                }
                else
                {
                    // otherwise it's too early, association not ready yet.
                    // so we don't build the table since we would not be able to send it anyway.
                    Log.Debug($"No Association for OriginUid [{OriginUid}] yet, unable to advertise compression table.");
                }
            }
            else
            {
                ResendCount++;
                var inProgress = CompressionTables.AdvertisementInProgress.Value;
                if (ResendCount <= MaxResendCount)
                {
                    // The ActorRefCompressionAdvertisement message is resent because it can be lost

                    var association = InboundContext.Association(OriginUid);
                    if (association.HasValue)
                    {
                        Log.Debug($"Advertisement in progress for OriginUid [{OriginUid}] version [{inProgress.Version}], resending [{ResendCount}:{MaxResendCount}]");
                        AdvertiseCompressionTable(association, inProgress);
                    }
                    // ARTERY: Original code does not have code for this condition
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
        protected abstract void AdvertiseCompressionTable(OutboundContext association, CompressionTable<T> table);

        private CompressionTable<T> PrepareCompressionAdvertisement(byte nextTableVersion)
        {
            var mappings = BuildTableForAdvertisement(HeavyHitters);
            return new CompressionTable<T>(OriginUid, nextTableVersion, mappings);
        }

        protected virtual Dictionary<T, int> BuildTableForAdvertisement(IEnumerable<T> elements)
        {
            // TODO optimized somewhat, check if still to heavy; could be encoded into simple array
            return elements.ZipWithIndex();
        }

        public override string ToString()
            => $"{Logging.SimpleName(GetType())}(CountMinSketch: {Cms}, HeavyHitters: {HeavyHitters})";
    }

    internal class UnknownCompressedIdException : AkkaException
    {
        public UnknownCompressedIdException(long id) : base(
            $"Attempted de-compress unknown id [{id}]! " +
            $"This could happen if this node has started a new ActorSystem bound to the same address as previously, " +
            $"and previous messages from a remote system were still in flight (using an old compression table). ," +
            $"The remote system is expected to drop the compression table and this system will advertise a new one.")
        { }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Literally, no compression!
    /// </summary>
    internal sealed class NoInboundCompressions : IInboundCompressions
    {
        public void HitActorRef(long originUid, Address remote, IActorRef @ref, int n) { }
        public Option<IActorRef> DecompressActorRef(long originUid, byte tableVersion, int idx)
        {
            if(idx < 0) 
                throw new ArgumentException($"Attempted decompression of illegal compression id: {idx}");
            return Option<IActorRef>.None;
        }
        public void ConfirmActorRefCompressionAdvertisement(long originUid, byte tableVersion) { }
        public void RunNextActorRefAdvertisement() { }

        public void HitClassManifest(long originUid, Address remote, string manifest, int n) { }
        public Option<string> DecompressClassManifest(long originUid, byte tableVersion, int idx)
        {
            if (idx < 0)
                throw new ArgumentException($"Attempted decompression of illegal compression id: {idx}");
            return Option<string>.None;
        }
        public void ConfirmClassManifestCompressionAdvertisement(long originUid, byte tableVersion) { }
        public void RunNextClassManifestAdvertisement() { }

        public HashSet<long> CurrentOriginUids => new HashSet<long>();

        public void Close(long originUid) { }
    }
}
