// -----------------------------------------------------------------------
// <copyright file="TlsCertificateSource.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Util.Internal;
using Message = System.ValueTuple<string, string>;

namespace Akka.IO
{
    /// <summary>The job a certificate does on one side of a TLS connection.</summary>
    internal enum TlsCertificateRole
    {
        /// <summary>The certificate authenticates the accepting side.</summary>
        Server,

        /// <summary>The certificate authenticates the connecting side.</summary>
        Client,

        /// <summary>
        /// One certificate does both jobs, and must permit client and server authentication. A node that accepts
        /// and dials connections under mutual TLS uses this role, so it presents one certificate in both directions.
        /// </summary>
        ClientAndServer
    }

    /// <summary>
    /// Supplies the certificate a node presents. Akka.NET asks the source each time it builds a TLS
    /// connection, so a new connection can use a new certificate while existing connections keep theirs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// To rotate a certificate, add the renewed certificate beside the old one, in the certificate store or in
    /// the folder. Nothing is overwritten. A source picks, among the certificates that are in date, allowed for
    /// the role (server or client authentication; and, when the certificate has a key usage extension, digital
    /// signature, or for an RSA server certificate key encipherment) and have a private key this process can open,
    /// the one that was issued last (the latest start date). If two were issued at the same time, it picks the one
    /// that expires last, and then the one with the greater thumbprint, so the choice never depends on the order of
    /// the files or of the store. A certificate that is not valid yet is ignored until its start date, so you can
    /// stage a renewal early.
    /// </para>
    /// <para>
    /// A folder or store source looks in the background. A connection gets the pick of the last look-up without
    /// waiting, while that pick is in date. Once the last look-up is 10 seconds old, the next connection starts a
    /// new one on the thread pool, so a certificate added to the folder or the store is used by the connections
    /// built after that look-up finishes. A connection never waits for a look-up: one with nothing to present fails
    /// at once, and starts a look-up if none is running. Only the startup check waits for the first look-up, for at
    /// most 30 seconds after it started, without blocking a thread. A look-up that runs longer is left to finish;
    /// until it does, the node keeps its certificates and warns. The DotNetty transport also asks a folder or store
    /// source once a minute by itself, so a node that builds no connection still looks, and logs a change of pick
    /// within about a minute. The callback source is called on every connection.
    /// </para>
    /// <para>
    /// If a look-up finds no usable certificate (the folder is gone, the store cannot be opened, every candidate is
    /// rejected, or the callback fails) and the previous pick for that role is still in date, the source keeps
    /// that pick and warns. Each warning repeats at most every 5 minutes while its cause lasts; past 100 different
    /// warnings in 5 minutes, new ones are dropped and one line says so. The source throws only when there is no
    /// earlier pick, or the earlier pick has expired. A store certificate whose private key could not be opened is
    /// read again at the next look-up, so access granted later, or the store entry replaced by one with its key,
    /// counts. A file with such a certificate is read again after 5 minutes, or at once when it changes.
    /// </para>
    /// <para>
    /// Ownership: Akka.NET never disposes a certificate it returned, not even after a newer one replaces it, because
    /// a handshake in flight may still hold it; the runtime releases it when it is no longer used. It disposes only a
    /// certificate it read and found unusable, and never returned. The caller must not dispose a certificate the
    /// source returned. A certificate from <see cref="FromCallback"/> belongs to the caller.
    /// </para>
    /// </remarks>
    public sealed class TlsCertificateSource
    {
        private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";
        private const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
        private const string AnyExtendedKeyUsageOid = "2.5.29.37.0";
        private const string RsaKeyOid = "1.2.840.113549.1.1.1";
        private const string TooLarge = "the file is larger than 256 KB, so it is too large to be a certificate.";
        private static readonly byte[] PemMarker = Encoding.ASCII.GetBytes("-----BEGIN ");

        // Bounds on the work of one look-up. A certificate file is a few KB, and a PEM bundle of a whole CA list
        // stays under 256 KB. A folder holds a handful of files. A file whose content failed waits 5 minutes before
        // it is parsed again, unless its content changes. The PEM files of a folder give at most 100 certificates and
        // 100 private keys in all, so the pairing work and the cache stay small.
        private const long MaxFileBytes = 256 * 1024;
        private const int MaxFiles = 100;
        private const int MaxPemCertificates = 100;
        private const int MaxPemKeys = 100;
        private const int MaxNameLength = 200;
        private const int MaxListed = 10;
        private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(5);

        private readonly Func<X509Certificate2>? _callback;
        private readonly string? _commonName;
        private readonly string? _storeName;
        private readonly StoreLocation _storeLocation;
        private readonly string? _folder;
        private readonly string? _password;
        private readonly X509KeyStorageFlags _keyStorageFlags;

        // The outcome of the last look-up, replaced whole by each refresh and never changed once published; and the
        // refresh that is running, or null. Only one refresh runs at a time.
        private Snapshot? _snapshot;
        private Refresh? _refresh;
        private Task _lastRefresh = Task.CompletedTask;
        private int _scanCount;

        // What refreshes loaded, by content for a folder and by thumbprint for a store. Only the running refresh uses it.
        private readonly Dictionary<string, Loaded> _loaded = new Dictionary<string, Loaded>();

        // Per role, indexed by (int)role: the snapshot whose warnings were logged, the pick the "certificate changed"
        // line named last, and for a callback the last pick and since when the role falls back to it.
        private readonly Snapshot?[] _reported = new Snapshot?[3];
        private readonly Candidate?[] _announced = new Candidate?[3];
        private readonly Outcome?[] _callbackPicks = new Outcome?[3];
        private readonly TlsLogLimiter _warnings = new TlsLogLimiter(TimeSpan.FromMinutes(5));
        private long _callbackCalls;
        private Refresh? _followed; // the last look-up a poll attached a follow-up to

        // The certificates a snapshot handed out, held weakly: a look-up never disposes one of them.
        private readonly ConditionalWeakTable<X509Certificate2, object> _published = new ConditionalWeakTable<X509Certificate2, object>();
        private static readonly object Marker = new object();

        // A failure the source made with its own text, which does not change from call to call while its cause lasts.
        // Key names the kind of failure for a log limiter, when the text holds something that differs between calls.
        private sealed class OwnFailure : InvalidOperationException
        {
            public OwnFailure(string message, string? key = null, Exception? inner = null) : base(message, inner) =>
                Key = key ?? message;

            public string Key { get; }
        }

        /// <summary>Internal seam for tests: how long the outcome of a look-up is served before the next one starts.</summary>
        internal TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Internal seam for tests: how long after a look-up starts the startup check stops waiting for it, and callers
        /// start to warn that it has not finished.
        /// </summary>
        internal TimeSpan RefreshTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Internal seam for tests: the clock, in UTC.</summary>
        internal Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        /// <summary>Internal seam for tests: tells whether the process runs on Windows.</summary>
        internal Func<bool> IsWindows { get; set; } = () => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>Internal seam for tests: opens one file of the folder, after the checks made before it is opened.</summary>
        internal Func<string, Stream> OpenFile { get; set; } =
            path => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        /// <summary>Internal seam for tests: tells whether this process can open the certificate's private key.</summary>
        internal Func<X509Certificate2, bool> CanOpenKey { get; set; } = HasUsablePrivateKey;

        /// <summary>Internal seam for tests: how many look-ups of the folder or the store ran.</summary>
        internal int ScanCount => Volatile.Read(ref _scanCount);

        /// <summary>
        /// Internal seam for tests: runs a look-up that starts after this call (once the one running, if any, has
        /// finished), and completes when it finishes.
        /// </summary>
        internal async Task RefreshAsync()
        {
            if (_callback is null && Volatile.Read(ref _refresh) is { } running)
                await Task.WhenAny(running.Done.Task).ConfigureAwait(false); // its outcome does not matter here
            if (_callback is null)
                await StartRefresh().Done.Task.ConfigureAwait(false);
        }

        /// <summary>Internal seam for tests: the look-up that started last.</summary>
        internal Task LastRefresh => Volatile.Read(ref _lastRefresh);

        private TlsCertificateSource(Func<X509Certificate2>? callback, string? commonName, string? storeName,
            StoreLocation storeLocation, string? folder, string? password,
            X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet)
        {
            _callback = callback;
            _commonName = commonName;
            _storeName = storeName;
            _storeLocation = storeLocation;
            _folder = folder;
            _password = password;
            _keyStorageFlags = keyStorageFlags;
        }

        /// <summary>
        /// Creates a source that picks the valid certificate in a folder that was issued last.
        /// Reads PKCS#12 and PEM files.
        /// </summary>
        /// <remarks>
        /// The source reads the files directly in the folder (not subfolders), at most the first 100 by name, follows
        /// symbolic links, and ignores entries whose name starts with <c>..</c>. A file it cannot read or parse (an
        /// empty file, a half-written one, one larger than 256 KB, one that is not a regular file) is skipped with a
        /// warning; a file whose content failed is parsed again after 5 minutes, or at once when it changes. It decides
        /// the format by content: a file with a <c>-----BEGIN </c> line anywhere (text before the first block is fine)
        /// is PEM, anything else is PKCS#12. A PEM certificate needs its private key, in the same file or in another
        /// file in the folder. PEM support needs .NET 6 or later; on .NET Standard 2.0 targets the source skips PEM
        /// files and logs a warning.
        /// </remarks>
        /// <param name="path">The folder to read.</param>
        /// <param name="password">The password for PKCS#12 files and encrypted PEM keys, or <c>null</c>.</param>
        public static TlsCertificateSource FromFolder(string path, string? password) =>
            FromFolder(path, password, X509KeyStorageFlags.DefaultKeySet);

        /// <summary>
        /// Like <see cref="FromFolder(string, string)"/>, with the key storage flags used to load PKCS#12 files, and
        /// to import a PEM certificate through PKCS#12 on Windows.
        /// </summary>
        /// <remarks>
        /// Use this when the default flags do not work. A Windows service that runs with no loaded user profile
        /// cannot keep a private key in the user key store, so it needs
        /// <see cref="X509KeyStorageFlags.MachineKeySet"/>. The HOCON <c>flags</c> setting does the same job.
        /// </remarks>
        /// <param name="path">The folder to read.</param>
        /// <param name="password">The password for PKCS#12 files and encrypted PEM keys, or <c>null</c>.</param>
        /// <param name="keyStorageFlags">The key storage flags to load certificates with.</param>
        public static TlsCertificateSource FromFolder(string path, string? password,
            X509KeyStorageFlags keyStorageFlags)
        {
            if (path is null) throw new ArgumentNullException(nameof(path));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("The folder path cannot be empty or whitespace.", nameof(path));
            return new TlsCertificateSource(null, null, null, default, path, password, keyStorageFlags);
        }

        /// <summary>
        /// Creates a source that picks the valid certificate in an X.509 store with this common name
        /// that was issued last. The common name must match in full; case is ignored. A certificate matches only when
        /// its subject has exactly one common name attribute, with this value.
        /// </summary>
        /// <param name="commonName">The common name (CN) to match.</param>
        /// <param name="storeName">The store name, such as <c>My</c>.</param>
        /// <param name="storeLocation">The store location.</param>
        public static TlsCertificateSource FromStore(string commonName, string storeName,
            StoreLocation storeLocation)
        {
            if (commonName is null) throw new ArgumentNullException(nameof(commonName));
            if (storeName is null) throw new ArgumentNullException(nameof(storeName));
            if (string.IsNullOrWhiteSpace(commonName))
                throw new ArgumentException("The common name cannot be empty or whitespace.", nameof(commonName));
            if (string.IsNullOrWhiteSpace(storeName))
                throw new ArgumentException("The store name cannot be empty or whitespace.", nameof(storeName));
            return new TlsCertificateSource(null, commonName, storeName, storeLocation, null, null);
        }

        /// <summary>
        /// Creates a source that calls <paramref name="getCertificate"/> each time a connection is built.
        /// The certificate must be in date, allowed for the role and have a private key. If it is not, or the
        /// callback throws, the source returns the last certificate it returned for that role while that one is
        /// still in date; otherwise the connection fails.
        /// </summary>
        /// <remarks>
        /// The callback runs on the connection path: once for every connection the node opens or accepts, before the
        /// peer is authenticated. It must return quickly and must not block or call a network service. Keep the
        /// certificate in a field and refresh it somewhere else.
        /// </remarks>
        /// <param name="getCertificate">Returns a caller-owned certificate with a private key.
        /// Akka.NET never disposes it. The callback may run concurrently.</param>
        public static TlsCertificateSource FromCallback(Func<X509Certificate2> getCertificate)
        {
            if (getCertificate is null) throw new ArgumentNullException(nameof(getCertificate));
            return new TlsCertificateSource(getCertificate, null, null, default, null, null);
        }

        /// <summary>
        /// Internal hook used when a connection is built: the certificate to present for this role. Logs through
        /// <paramref name="log"/> what the last look-up found (skipped files and certificates, a fall-back to the
        /// previous pick) and a change of pick. Never waits for a look-up.
        /// Throws <see cref="InvalidOperationException"/> when no certificate qualifies and there is no earlier pick
        /// that is still in date, or when there is nothing to present while a look-up runs (it starts one if none
        /// runs). The caller must not dispose the result.
        /// </summary>
        internal X509Certificate2 GetCertificate(TlsCertificateRole role, ILoggingAdapter log)
        {
            if (log is null) throw new ArgumentNullException(nameof(log));
            return _callback is not null ? GetFromCallback(role, log) : GetFromSource(role, log);
        }

        /// <summary>
        /// Internal: the current pick for this role, for the public <c>Certificate</c> properties. Like
        /// <see cref="GetCertificate"/>, but writes no log line; the next <see cref="GetCertificate"/> logs what there
        /// is to log. Returns the instance the connections use. The caller must not dispose the result.
        /// </summary>
        internal X509Certificate2 GetCertificateWithoutLogging(TlsCertificateRole role) =>
            _callback is not null ? GetFromCallback(role, null) : GetFromSource(role, null);

        /// <summary>
        /// Internal hook for the startup check: like <see cref="GetCertificate"/>, but a caller with nothing to present
        /// waits for the look-up, until 30 seconds after it started, without blocking a thread.
        /// </summary>
        internal async Task<X509Certificate2> GetCertificateAsync(TlsCertificateRole role, ILoggingAdapter? log)
        {
            if (_callback is not null)
                return GetFromCallback(role, log);
            if (TryGetFromSource(role, log, out var refresh, out _) is { } pick)
                return pick;

            var latest = await AwaitAsync(refresh!, role).ConfigureAwait(false);
            var now = UtcNow();
            if (log is not null)
                Report(latest, role, log, now);
            var outcome = latest.For(role);
            return outcome.Pick is not null && outcome.Pick.IsUsable(now) ? Hand(role, outcome.Pick, log) : throw outcome.Fail();
        }

        /// <summary>Internal: true for a folder or a store, which look in the background; false for a callback.</summary>
        internal bool LooksUp => _callback is null;

        /// <summary>
        /// Internal hook for the transport, which calls it once a minute: starts a look-up when one is due, and logs
        /// through <paramref name="log"/> what the snapshot has to report, then again once that look-up finishes, so a
        /// change of pick is logged without a connection. Throws only a fatal exception. Does nothing for a callback.
        /// </summary>
        internal void Poll(TlsCertificateRole role, ILoggingAdapter log)
        {
            if (_callback is not null)
                return;

            // The look-up this call started, or the one already running. It is the one TryGetFromSource returns, not
            // one read afterwards: a look-up that finishes first would be missed, and its change would wait a minute.
            // One look-up gets one follow-up, however many polls see it: a look-up that hangs must not collect one a minute.
            var refresh = Ask();
            if (refresh is not null && !ReferenceEquals(Interlocked.Exchange(ref _followed, refresh), refresh))
                refresh.Done.Task.ContinueWith(_ => Ask(), CancellationToken.None,
                    TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            Refresh? Ask()
            {
                Refresh? asked = null;
                try
                {
                    TryGetFromSource(role, log, out asked, out _);
                }
                catch (Exception e) when (!IsFatal(e))
                {
                    // Nothing to present, which no connection may be there to report: say so at the limiter's pace.
                    Warn(log, ("poll|" + e.Message, e.Message), UtcNow(), error: true);
                }

                return asked;
            }
        }

        /// <summary>
        /// Internal: what a log limiter should key a failure on, or null for what a callback threw. The source made
        /// the others with their own text, which does not change from call to call while its cause lasts: the key is
        /// that text, or the kind of failure when the text holds something that differs from call to call.
        /// </summary>
        internal static string? OwnFailureKey(Exception exception) => (exception as OwnFailure)?.Key;

        // The callback runs on every call, on the caller's thread, with the same checks and fall-back as a look-up.
        private X509Certificate2 GetFromCallback(TlsCertificateRole role, ILoggingAdapter? log)
        {
            var now = UtcNow();
            var call = Interlocked.Increment(ref _callbackCalls); // taken before the callback runs
            Candidate? candidate = null;
            Exception? failure;
            try
            {
                var certificate = _callback!() ?? throw new OwnFailure("The TLS certificate callback returned null.");
                candidate = new Candidate(certificate, null, null, now, CanOpenKey);
                var reason = candidate.Reason(role, out _);
                failure = reason is null ? null : new OwnFailure(
                    $"The TLS certificate callback returned a certificate {candidate.Text} that cannot be used as a {RoleName(role)}: {reason}.",
                    $"callback returned an unusable certificate|{role}");
            }
            catch (Exception e) when (!IsFatal(e))
            {
                failure = e;
            }

            ref var slot = ref _callbackPicks[(int)role];
            if (failure is null)
            {
                // Stored only over the pick of a call that started earlier: a call that stalled never puts back an
                // older pick over a newer one.
                var picked = new Outcome(candidate, null, null, null, Array.Empty<Message>()) { Call = call };
                var stored = Volatile.Read(ref slot);
                var won = false;
                while (!won && (stored is null || stored.Call < call))
                {
                    won = ReferenceEquals(Interlocked.CompareExchange(ref slot, picked, stored), stored);
                    if (!won)
                        stored = Volatile.Read(ref slot);
                }

                // A call that lost to a newer one still gives its caller its own certificate, but logs no change.
                return won ? Hand(role, candidate!, log) : candidate!.Certificate;
            }

            for (var last = Volatile.Read(ref slot); last?.Pick is not null && last.Pick.IsUsable(now); last = Volatile.Read(ref slot))
            {
                // The first failure after a success starts a new spell of falling back, which warns at once. It is
                // recorded only on the pick it read: a newer pick stored meanwhile is never replaced by an older one.
                var since = last.FallingBackSince ?? now;
                if (last.FallingBackSince is null &&
                    !ReferenceEquals(Interlocked.CompareExchange(ref slot, last with { FallingBackSince = since }, last), last))
                    continue;
                if (log is not null)
                    Warn(log, FallBack(role, last.Pick, failure.Message, since), now);
                return last.Pick.Certificate;
            }

            // Keep the callback's own exception, and its stack, as before.
            ExceptionDispatchInfo.Capture(failure).Throw();
            throw failure;
        }

        // A folder or a store, on the connection path: never waits for a look-up. With nothing to present it fails with
        // the reason of the last look-up, which stays the same from one look-up to the next while its cause lasts.
        private X509Certificate2 GetFromSource(TlsCertificateRole role, ILoggingAdapter? log) =>
            TryGetFromSource(role, log, out _, out var outcome) ?? throw (outcome?.Fail() ?? new OwnFailure(
                $"No TLS certificate is available yet to use as a {RoleName(role)}: the look-up in {Where()} has not finished. " +
                "A connection does not wait for it; connections built after it finishes use its pick."));

        // Served from the last snapshot, without a lock and without touching files or keys. Returns null, with the
        // look-up to wait for and the last outcome for the role, if any, when there is nothing to present; throws when
        // the last look-up, still recent, found none.
        private X509Certificate2? TryGetFromSource(TlsCertificateRole role, ILoggingAdapter? log, out Refresh? refresh,
            out Outcome? last)
        {
            var now = UtcNow();
            var snapshot = Volatile.Read(ref _snapshot);
            refresh = Volatile.Read(ref _refresh);
            if (refresh is null && (snapshot is null || !IsFresh(snapshot, now)))
                refresh = StartRefresh();

            var outcome = last = snapshot?.For(role);
            if (snapshot is not null && outcome is not null)
            {
                if (log is not null)
                {
                    // The warning about a look-up that does not finish goes first, so warnings about files never crowd it out.
                    if (refresh is not null && now - refresh.StartedAt > RefreshTimeout)
                        Warn(log, Note($"TLS certificate look-up in {Where()} has been running since {FormatTime(refresh.StartedAt)} and has not finished, " +
                                       $"so the node keeps using the certificates it has and does not look again until it finishes.{Hint()}"), now);
                    Report(snapshot, role, log, now);
                }

                if (outcome.Pick is not null && outcome.Pick.IsUsable(now))
                    return Hand(role, outcome.Pick, log);

                // Nothing qualified at the last look-up, which is recent: the answer stays the same until it is due.
                if (outcome.Pick is null && refresh is null)
                    throw outcome.Fail();

                // Nothing to present: the pick has expired or was disposed since, or there was none. A look-up is due.
                refresh ??= StartRefresh();
            }

            return null;
        }

        private X509Certificate2 Hand(TlsCertificateRole role, Candidate pick, ILoggingAdapter? log)
        {
            // Logs a change of pick for this role once, by the caller that sees it first.
            ref var slot = ref _announced[(int)role];
            var last = Volatile.Read(ref slot);
            if (log is not null && !string.Equals(last?.Thumbprint, pick.Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                ReferenceEquals(Interlocked.CompareExchange(ref slot, pick, last), last))
                log.Info("TLS {0} certificate changed from {1} to {2}", RoleName(role), last?.Text ?? "none", pick.Text);
            return pick.Certificate;
        }

        private bool IsFresh(Snapshot snapshot, DateTime now) =>
            now >= snapshot.At && now - snapshot.At < ScanInterval;

        // Starts a refresh on the thread pool unless one is running, and returns the running one.
        private Refresh StartRefresh()
        {
            while (true)
            {
                var running = Volatile.Read(ref _refresh);
                if (running is not null)
                    return running;

                var next = new Refresh(UtcNow());
                if (Interlocked.CompareExchange(ref _refresh, next, null) is not null)
                    continue;

                Volatile.Write(ref _lastRefresh, next.Done.Task);
                Task.Run(() =>
                {
                    try
                    {
                        var snapshot = Look(Volatile.Read(ref _snapshot));
                        Volatile.Write(ref _snapshot, snapshot);
                        Interlocked.Exchange(ref _refresh, null);
                        next.Done.TrySetResult(snapshot);
                    }
                    catch (Exception e)
                    {
                        // Only a fatal exception gets here (Look handles the rest), and the waiting callers get it.
                        Interlocked.Exchange(ref _refresh, null);
                        next.Done.TrySetException(e);
                    }
                });
                return next;
            }
        }

        // Waits for the refresh until RefreshTimeout after it started, so callers that come later do not each wait
        // the whole time again. The timeout holds even when the look-up never returns.
        private async Task<Snapshot> AwaitAsync(Refresh refresh, TlsCertificateRole role)
        {
            var remaining = RefreshTimeout - refresh.Elapsed;
            if (remaining > TimeSpan.Zero && !refresh.Done.Task.IsCompleted)
            {
                using var cancel = new CancellationTokenSource(); // stops the delay when the look-up wins
                await Task.WhenAny(refresh.Done.Task, Task.Delay(remaining, cancel.Token)).ConfigureAwait(false);
                cancel.Cancel();
            }

            if (refresh.Done.Task.IsCompleted)
                return await refresh.Done.Task.ConfigureAwait(false);

            throw new InvalidOperationException(
                $"The TLS certificate look-up in {Where()} did not finish in time: it started at {FormatTime(refresh.StartedAt)} and has not " +
                $"finished after {RefreshTimeout.TotalSeconds:0} seconds, so there is no certificate to use as a {RoleName(role)}.{Hint()}");
        }

        private string Hint() => _folder is null ? "" :
            " A file in the folder that cannot be read, such as a file on a network share that does not answer, can cause this.";

        // One look-up: reads the folder or the store and checks every candidate once, then decides every role.
        private Snapshot Look(Snapshot? previous)
        {
            Interlocked.Increment(ref _scanCount);
            var now = UtcNow();
            var found = new Found();
            try
            {
                if (_folder is not null)
                    ReadFolder(found, now);
                else
                    ReadStore(found, now);

                // Forget what the folder or the store no longer holds. Nothing is disposed: a connection may hold it.
                foreach (var key in _loaded.Keys.Where(k => !found.Live.Contains(k)).ToList())
                    _loaded.Remove(key);
            }
            catch (Exception e) when (!IsFatal(e))
            {
                found.Failure = e;
                found.Candidates.Clear();
            }

            var outcomes = new[] { TlsCertificateRole.Server, TlsCertificateRole.Client, TlsCertificateRole.ClientAndServer }
                .Select(role => Decide(role, found, previous?.For(role), now))
                .ToArray();
            var messages = found.Failures.Select(f => Note("TLS certificate skipped: " + f)).ToList();
            if (found.SkippedPem is not null)
                messages.Add(Note($"TLS certificate folder '{_folder}' holds PEM files, such as '{Safe(found.SkippedPem)}'. They are skipped: PEM needs .NET 6 or later."));

            // Release what this look-up read and found unusable, unless a snapshot ever handed it out: a connection may
            // hold that one. Nothing else holds the rest.
            foreach (var pick in outcomes.Select(o => o.Pick?.Certificate).OfType<X509Certificate2>())
                _published.GetValue(pick, _ => Marker);
            foreach (var unusable in found.Unusable.Where(c => !_published.TryGetValue(c, out _)))
                unusable.Dispose();

            return new Snapshot(UtcNow(), outcomes, messages);
        }

        private Outcome Decide(TlsCertificateRole role, Found found, Outcome? previous, DateTime now)
        {
            Candidate? winner = null;
            var rejected = new List<(Candidate Candidate, string Reason, bool Warn)>();
            foreach (var candidate in found.Candidates)
            {
                var reason = candidate.Reason(role, out var warn);
                if (reason is not null)
                    rejected.Add((candidate, reason, warn));
                else if (winner is null || IsBetter(candidate, winner))
                    winner = candidate;
            }

            // Warn about a skipped certificate only when it would have beaten the pick. A certificate that is not
            // valid yet, or expired, is normal and gets no warning.
            var messages = rejected
                .Where(r => r.Warn && (winner is null || IsBetter(r.Candidate, winner)))
                .Select(r => Note($"TLS {RoleName(role)} certificate {r.Candidate.Text}{InFile(r.Candidate)} was skipped: {r.Reason}"))
                .ToList();
            if (winner is not null)
                return new Outcome(winner, null, null, null, messages);

            var failure = found.Failure is null ? NoCertificate(role, found, rejected)
                : found.Failure as InvalidOperationException ??
                  new InvalidOperationException($"The look-up of TLS certificates failed: {found.Failure.Message}", found.Failure);

            // Keep the previous pick while it is in date. One its owner disposed is not usable, so it counts as none.
            if (previous?.Pick is null || !previous.Pick.IsUsable(now))
                return new Outcome(null, failure.Message, failure.InnerException, null, messages);

            var since = previous.FallingBackSince ?? now;
            messages.Insert(0, FallBack(role, previous.Pick, failure.Message, since)); // the most important goes first
            return new Outcome(previous.Pick, failure.Message, failure.InnerException, since, messages);
        }

        // The better of two candidates: the one issued last. A re-issue that expires at the same time, or earlier, must
        // win, so the expiry only breaks a tie on the issue date. The thumbprint breaks a tie on both, so the pick
        // never depends on the order of the files or of the store.
        private static bool IsBetter(Candidate candidate, Candidate other)
        {
            var byIssue = candidate.NotBefore.CompareTo(other.NotBefore);
            if (byIssue != 0)
                return byIssue > 0;

            var byExpiry = candidate.NotAfter.CompareTo(other.NotAfter);
            return byExpiry != 0
                ? byExpiry > 0
                : string.Compare(candidate.Thumbprint, other.Thumbprint, StringComparison.OrdinalIgnoreCase) > 0;
        }

        // The exception for a look-up in which no certificate passed for this role. Lists at most MaxListed reasons.
        private InvalidOperationException NoCertificate(TlsCertificateRole role, Found found,
            List<(Candidate Candidate, string Reason, bool Warn)> rejected)
        {
            var reasons = rejected.Select(r => (Title: "Rejected: ", Text: $"{r.Candidate.Text}{InFile(r.Candidate)}: {r.Reason}"))
                .Concat(found.Failures.Select(f => (Title: "Could not load: ", Text: f)))
                .ToList();
            var parts = reasons.Take(MaxListed)
                .GroupBy(r => r.Title)
                .Select(g => g.Key + string.Join("; ", g.Select(r => r.Text.EndsWith(".", StringComparison.Ordinal) ? r.Text.Substring(0, r.Text.Length - 1) : r.Text)))
                .Select(part => part.EndsWith(".", StringComparison.Ordinal) ? part : part + ".")
                .ToList();
            if (reasons.Count > MaxListed)
                parts.Add($"{reasons.Count - MaxListed} more not listed.");
            parts.AddRange(found.Notes);
            if (parts.Count == 0)
                parts.Add("It holds no certificate.");

            return new InvalidOperationException(
                $"No certificate in {Where()} can be used as a {RoleName(role)}. " + string.Join(" ", parts));
        }

        private string Where() => _folder is not null
            ? $"folder '{_folder}'"
            : $"store '{_storeName}' at '{_storeLocation}' with common name '{_commonName}'";

        private void ReadStore(Found found, DateTime now)
        {
            X509Certificate2Collection all;
            try
            {
                using var store = new X509Store(_storeName!, _storeLocation);
                store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                all = store.Certificates;
            }
            catch (Exception e) when (!IsFatal(e))
            {
                throw new InvalidOperationException(
                    $"Could not open certificate store '{_storeName}' at '{_storeLocation}'.", e);
            }

            foreach (var certificate in all)
            {
                // Keep one instance per certificate. A fresh copy that is not kept was never published.
                var loaded = HasCommonName(certificate, _commonName!)
                    ? Load("store:" + certificate.Thumbprint, () => certificate, found, now)
                    : null;
                if (!ReferenceEquals(loaded?.Value, certificate))
                    certificate.Dispose();
                if (loaded is not null)
                    Add(found, loaded, null, null, now);
            }

            if (found.Candidates.Count == 0)
                found.Notes.Add("The store has no certificate with that common name.");
        }

        private void ReadFolder(Found found, DateTime now)
        {
            string[] files;
            try
            {
                if (!Directory.Exists(_folder!))
                {
                    found.Notes.Add("The folder does not exist.");
                    return;
                }

                // Files only. Symbolic links to files are listed and followed.
                files = Directory.EnumerateFiles(_folder!)
                    .Where(f => !Path.GetFileName(f).StartsWith("..", StringComparison.Ordinal) && !Directory.Exists(f))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception e) when (!IsFatal(e))
            {
                found.Notes.Add($"The folder could not be read: {e.Message}");
                return;
            }

            if (files.Length == 0)
                found.Notes.Add("The folder is empty.");
            if (files.Length > MaxFiles)
                found.Failures.Add($"the folder holds {files.Length} files and only the first {MaxFiles} by name are read, " +
                                   $"so the {files.Length - MaxFiles} files from '{Safe(Path.GetFileName(files[MaxFiles]))}' on are not read.");

            var pemFiles = new List<(string Name, byte[] Bytes)>();
            foreach (var file in files.Take(MaxFiles))
            {
                var name = Path.GetFileName(file);
                byte[] bytes;
                try
                {
                    bytes = ReadBounded(file);
                }
                catch (Exception e) when (!IsFatal(e))
                {
                    found.Failures.Add($"file '{Safe(name)}' {(e is InvalidDataException ? "failed to load" : "could not be read")}: {e.Message}");
                    continue;
                }

                if (bytes.AsSpan().IndexOf(PemMarker) >= 0) // PEM: a "-----BEGIN " line anywhere, as OpenSSL may write text first
                    pemFiles.Add((name, bytes));
                else
                    Add(found, Load("p12:" + Hash(bytes), () => new X509Certificate2(bytes, _password, _keyStorageFlags), found, now), name, null, now);
            }

            if (pemFiles.Count == 0)
                return;
#if NET6_0_OR_GREATER
            ReadPem(pemFiles, found, now);
#else
            found.SkippedPem = pemFiles[0].Name;
            found.Notes.Add("PEM files are skipped on this target: PEM needs .NET 6 or later.");
#endif
        }

        // Reads one file. The file that will be read (for a symbolic link, its final target) is checked before it is
        // opened: an empty file is never a certificate, and neither is anything that reports no size, such as a named
        // pipe, whose opening would wait for a writer. A link that this target cannot resolve (.NET Standard 2.0) is
        // not checked before it is opened, never counts as empty, and only the bounded read applies to it.
        private byte[] ReadBounded(string path)
        {
#if NET6_0_OR_GREATER
            var info = File.ResolveLinkTarget(path, returnFinalTarget: true) as FileInfo ?? new FileInfo(path);
#else
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                info = null;
#endif
            if (info is not null && ((info.Attributes & FileAttributes.Device) != 0 || info.Length == 0))
                throw new InvalidDataException("the file is empty or is not a regular file.");
            if (info is not null && info.Length > MaxFileBytes)
                throw new InvalidDataException(TooLarge);

            using var stream = OpenFile(path);
            return ReadAtMost(stream, info?.Length ?? 0);
        }

        // Reads at most MaxFileBytes, whatever the size checked before: a file may grow after it. The buffer starts at
        // the expected size and grows only when the file holds more.
        private static byte[] ReadAtMost(Stream stream, long expected)
        {
            var bytes = new byte[Math.Min(expected > 0 ? expected : 4096, MaxFileBytes) + 1];
            int read = 0, count;
            while ((count = stream.Read(bytes, read, bytes.Length - read)) > 0)
            {
                read += count;
                if (read > MaxFileBytes)
                    throw new InvalidDataException(TooLarge);
                if (read == bytes.Length)
                    Array.Resize(ref bytes, (int)Math.Min(bytes.Length * 2L, MaxFileBytes + 1));
            }

            if (read == 0)
                throw new InvalidDataException("the file is empty.");
            Array.Resize(ref bytes, read);
            return bytes;
        }

        // Returns what was loaded under this key, and loads it when there is nothing usable: the first time, after a
        // held certificate was disposed by its owner, after a store certificate's key could not be opened, or 5 minutes
        // after a load that failed or a file certificate whose key could not be opened.
        private Loaded Load(string key, Func<object?> load, Found found, DateTime now)
        {
            found.Live.Add(key);
            if (_loaded.TryGetValue(key, out var held) && held.IsReusable(now))
                return held;

            try
            {
                return _loaded[key] = new Loaded(key, load(), null, now);
            }
            catch (Exception e) when (!IsFatal(e))
            {
                return _loaded[key] = new Loaded(key, null, e.Message, now);
            }
        }

        // Adds a loaded certificate as a candidate, or its file as a failure. The instance of a certificate whose private
        // key could not be opened, or that could not be read, is released at the end of the look-up. A store
        // certificate is read afresh at the next look-up: the store may hold it with its key by then, or the process
        // may have been given access to the key. A file is judged again after 5 minutes, or at once when it changes,
        // as a file that failed to load is; until then its candidate stands as it was judged.
        private void Add(Found found, Loaded loaded, string? file, string? keyProblem, DateTime now)
        {
            if (loaded.Error is not null)
            {
                found.Failures.Add($"file '{Safe(file)}' failed to load: {loaded.Error}");
                return;
            }

            if (loaded.Value is Candidate judged)
            {
                found.Add(judged);
                return;
            }

            var certificate = (X509Certificate2)loaded.Value!;
            try
            {
                var candidate = found.Add(new Candidate(certificate, file, keyProblem, now, CanOpenKey));
                if (!candidate.KeyUnusable)
                    return;

                found.Unusable.Add(certificate);
                if (file is null)
                    _loaded.Remove(loaded.Key);
                else
                    _loaded[loaded.Key] = loaded with { Value = candidate, At = now };
            }
            catch (Exception e) when (!IsFatal(e))
            {
                found.Unusable.Add(certificate);
                _loaded.Remove(loaded.Key);
                found.Failures.Add($"{(file is null ? "a certificate in the store" : $"file '{Safe(file)}'")} could not be read: {e.Message}");
            }
        }

        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

#if NET6_0_OR_GREATER
        private sealed record PemBlock(string Label, string Text, string Base64)
        {
            public string Hash { get; } = TlsCertificateSource.Hash(Encoding.UTF8.GetBytes(Text));
        }

        // Reads every PEM block of every PEM file, then pairs each certificate with the key that fits it, whatever
        // the files are called: it tries the keys in turn and the framework rejects a key that does not match.
        private void ReadPem(List<(string Name, byte[] Bytes)> files, Found found, DateTime now)
        {
            var certificates = new List<(string File, PemBlock Block)>();
            var keys = new List<(string File, PemBlock Block, Loaded Loaded)>();
            var seen = new HashSet<string>();
            bool tooManyCertificates = false, tooManyKeys = false;
            foreach (var (name, bytes) in files)
            {
                var parsed = Load("pem:" + Hash(bytes), () => ReadBlocks(bytes), found, now);
                if (parsed.Error is not null)
                    found.Failures.Add($"file '{Safe(name)}' failed to load: {parsed.Error}");
                foreach (var block in (parsed.Value as PemBlock[] ?? Array.Empty<PemBlock>()).Where(b => seen.Add(b.Hash)))
                {
                    // The limits are checked before a key is opened or a block is cached or paired.
                    if (block.Label == "CERTIFICATE")
                    {
                        if (certificates.Count < MaxPemCertificates)
                            certificates.Add((name, block));
                        else
                            tooManyCertificates = true;
                    }
                    else if (keys.Count < MaxPemKeys)
                        keys.Add((name, block, Load("key:" + block.Hash, () => OpenKey(block, _password), found, now)));
                    else
                        tooManyKeys = true;
                }
            }

            if (tooManyCertificates || tooManyKeys)
            {
                var what = tooManyCertificates && tooManyKeys
                    ? $"more than {MaxPemCertificates} certificates and more than {MaxPemKeys} private keys"
                    : tooManyCertificates ? $"more than {MaxPemCertificates} certificates" : $"more than {MaxPemKeys} private keys";
                found.Failures.Add($"the PEM files in the folder hold {what}, and only the first {MaxPemCertificates} certificates " +
                                   $"and the first {MaxPemKeys} private keys in file name order are read, so the others are not read.");
            }

            // A key that cannot be opened (wrong or missing password) is a failed file.
            foreach (var (file, _, loaded) in keys.Where(k => k.Loaded.Error is not null))
                found.Failures.Add($"file '{Safe(file)}' failed to load: {loaded.Error}");

            foreach (var (file, certificate) in certificates)
            {
                // A pair with no certificate and no error means this key does not fit, so try the next one.
                var match = keys.Where(k => k.Loaded.Error is null)
                    .Select(k => Load($"pair:{certificate.Hash}:{k.Block.Hash}", () => Pair(certificate, (AsymmetricAlgorithm)k.Loaded.Value!), found, now))
                    .FirstOrDefault(pair => pair.Value is not null || pair.Error is not null);

                // With no key in the folder, the certificate is still a candidate, so a renewal that arrived without
                // its key is reported when it would have been the pick.
                if (match is null)
                    Add(found, Load("crt:" + certificate.Hash, () => PublicOnly(certificate), found, now), file, "it has no private key in the folder", now);
                else
                    Add(found, match, file, null, now);
            }
        }

        // The certificate and key blocks of a PEM file. Every BEGIN line must be closed by an END line before the
        // next BEGIN. That is checked first, in one pass, so a file of unclosed BEGIN lines costs one pass.
        private static PemBlock[] ReadBlocks(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes).TrimStart('﻿');
            int begins = 0, open = 0;
            for (var at = text.IndexOf("-----", StringComparison.Ordinal); at >= 0 && open < 2; at = text.IndexOf("-----", at + 5, StringComparison.Ordinal))
            {
                if (string.CompareOrdinal(text, at, "-----BEGIN", 0, 10) == 0)
                    (begins, open) = (begins + 1, open + 1);
                else if (string.CompareOrdinal(text, at, "-----END", 0, 8) == 0)
                    open = 0;
            }

            var blocks = new List<PemBlock>();
            var found = 0;
            for (var offset = 0; open == 0 && offset < text.Length && PemEncoding.TryFind(text.AsSpan(offset), out var fields); found++)
            {
                var rest = text.AsSpan(offset);
                var label = rest[fields.Label].ToString();
                if (label == "CERTIFICATE" || label.EndsWith("PRIVATE KEY", StringComparison.Ordinal))
                    blocks.Add(new PemBlock(label, rest[fields.Location].ToString(), rest[fields.Base64Data].ToString()));
                offset += fields.Location.End.Value;
            }

            // TryFind also skips a block it cannot read.
            return open == 0 && found == begins ? blocks.ToArray()
                : throw new FormatException("the PEM text is incomplete (a block has no end line).");
        }

        private static AsymmetricAlgorithm OpenKey(PemBlock key, string? password)
        {
            var encrypted = key.Label == "ENCRYPTED PRIVATE KEY";
            if (encrypted && password is null)
                throw new CryptographicException("the private key is encrypted and no password was given.");

            // The label does not say whether the key is RSA or ECDSA, so try each.
            foreach (var create in new Func<AsymmetricAlgorithm>[] { () => RSA.Create(), () => ECDsa.Create() })
            {
                var algorithm = create();
                try
                {
                    if (encrypted)
                        algorithm.ImportFromEncryptedPem(key.Text, password);
                    else
                        algorithm.ImportFromPem(key.Text);
                    return algorithm;
                }
                catch (Exception e) when (e is CryptographicException || e is ArgumentException)
                {
                    algorithm.Dispose(); // never published
                }
            }

            throw new CryptographicException(encrypted
                ? "the private key could not be opened with the password (or it is not an RSA or ECDSA key)."
                : "the private key could not be read (it is not an RSA or ECDSA key).");
        }

        private static X509Certificate2 PublicOnly(PemBlock certificate)
        {
            try
            {
                return new X509Certificate2(Convert.FromBase64String(certificate.Base64));
            }
            catch (FormatException)
            {
                throw new CryptographicException("the PEM certificate is not valid base64.");
            }
        }

        // Returns the certificate with this key, or null when the key does not fit. Throws when the certificate is bad.
        private X509Certificate2? Pair(PemBlock certificate, AsymmetricAlgorithm key)
        {
            using var publicOnly = PublicOnly(certificate); // a temporary, never published
            X509Certificate2 paired;
            try
            {
                paired = key is RSA rsa ? publicOnly.CopyWithPrivateKey(rsa) : publicOnly.CopyWithPrivateKey((ECDsa)key);
            }
            catch (Exception e) when (e is ArgumentException || e is CryptographicException)
            {
                return null; // the key does not fit this certificate
            }

            if (!IsWindows())
                return paired;

            // On Windows a key attached with CopyWithPrivateKey is not one TLS (Schannel) can use. Export to PKCS#12 and
            // load again, with the flags the source loads PKCS#12 files with, so a PEM certificate gets the same key storage.
            using (paired) // a temporary, never published
            {
                var pkcs12 = paired.Export(X509ContentType.Pkcs12, (string?)null);
                try
                {
                    return new X509Certificate2(pkcs12, (string?)null, _keyStorageFlags);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pkcs12);
                }
            }
        }
#endif

        // Logs the warnings of a snapshot once per role, by the first caller with a logger that sees it. The role's
        // warnings (the fall-back, then the skipped certificates) go before the warnings about files, so the limiter
        // admits them first when there are many.
        private void Report(Snapshot snapshot, TlsCertificateRole role, ILoggingAdapter log, DateTime now)
        {
            ref var slot = ref _reported[(int)role];
            var last = Volatile.Read(ref slot);
            if (ReferenceEquals(last, snapshot) || !ReferenceEquals(Interlocked.CompareExchange(ref slot, snapshot, last), last))
                return;

            foreach (var message in snapshot.For(role).Messages.Concat(snapshot.Messages))
                Warn(log, message, now);
        }

        private void Warn(ILoggingAdapter log, Message message, DateTime now, bool error = false)
        {
            var verdict = _warnings.Admit(message.Item1, now);
            if (verdict == TlsLogLimiter.Verdict.Log && error)
                log.Error("{0}", message.Item2);
            else if (verdict == TlsLogLimiter.Verdict.Log)
                log.Warning("{0}", message.Item2);
            else if (verdict == TlsLogLimiter.Verdict.LogSummary)
                log.Warning("Some TLS certificate warnings were not logged: more than 100 different warnings came within 5 minutes.");
        }

        // A warning whose text is its key.
        private static Message Note(string text) => (text, text);

        // The warning for a role that keeps its previous pick. Its key names the spell of falling back, so one spell
        // warns every 5 minutes whatever the cause says, and a new spell after a recovery warns at once.
        private static Message FallBack(TlsCertificateRole role, Candidate pick, string cause, DateTime since) =>
            ($"fallback|{role}|{since.Ticks}",
                $"TLS {RoleName(role)} certificate look-up failed, so the node keeps using the previous certificate {pick.Text}: {cause}");

        private static bool HasUsablePrivateKey(X509Certificate2 certificate)
        {
            try
            {
                if (!certificate.HasPrivateKey)
                    return false;

                using (var rsa = certificate.GetRSAPrivateKey())
                {
                    if (rsa is not null)
                        return true;
                }

                using (var ecdsa = certificate.GetECDsaPrivateKey())
                    return ecdsa is not null;
            }
            catch (Exception e) when (!IsFatal(e))
            {
                return false;
            }
        }

        // False for a certificate its owner disposed: the source never hands that out again.
        private static bool IsAlive(X509Certificate2 certificate) => certificate.Handle != IntPtr.Zero;

        // The exceptions that mean the process is not fit to go on. A failure of one file or one callback is not.
        internal static bool IsFatal(Exception exception) =>
            exception is OutOfMemoryException || exception is StackOverflowException ||
            exception is AccessViolationException;

        // The common name must match in full, ignoring case: "akka-x" does not match "akka-x-extra". It must be the
        // one common name attribute of the subject, read from the encoded subject with the same reader and the same
        // rule the hostname matcher uses. Reading the formatted subject text is not safe: another attribute whose value
        // holds a new line and then "CN=name" looks like a common name there.
        private static bool HasCommonName(X509Certificate2 certificate, string commonName)
        {
            try
            {
                var value = DerEncoding.ReadSingleCommonName(certificate.SubjectName.RawData);
                return value is not null && string.Equals(value, commonName, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (!IsFatal(e))
            {
                return false; // A subject we cannot read has no common name we can match.
            }
        }

        private static string RoleName(TlsCertificateRole role) => role switch
        {
            TlsCertificateRole.Server => "server",
            TlsCertificateRole.Client => "client",
            _ => "client and server"
        };

        private static string FormatTime(DateTime time) =>
            time.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static string InFile(Candidate candidate) => candidate.File is null ? "" : $" in file '{Safe(candidate.File)}'";

        // A file name goes into a message cut short and without control characters, so a crafted name cannot forge log lines.
        private static string Safe(string? name) =>
            new string((name ?? "").Take(MaxNameLength).Select(c => char.IsControl(c) ? '?' : c).ToArray());

        /// <summary>
        /// A certificate from a look-up or the callback, read and checked once: a snapshot never touches the key
        /// store again. A certificate that cannot be read at all makes the constructor throw.
        /// </summary>
        private sealed class Candidate
        {
            private readonly string? _dateProblem;
            private readonly string? _unreadable;
            private readonly string[]? _ekus;
            private readonly X509KeyUsageFlags? _keyUsages;
            private readonly bool _rsa;
            private readonly string? _keyProblem;

            public Candidate(X509Certificate2 certificate, string? file, string? keyProblem, DateTime now,
                Func<X509Certificate2, bool> canOpenKey)
            {
                Certificate = certificate;
                File = file;
                Thumbprint = certificate.Thumbprint;
                // Compare instants in UTC. A local date is ambiguous when clocks go back.
                NotBefore = certificate.NotBefore.ToUniversalTime();
                NotAfter = certificate.NotAfter.ToUniversalTime();
                // The subject is cut short, without control characters, so a crafted one cannot forge log lines.
                var subject = new string(certificate.Subject.Take(200).Select(c => char.IsControl(c) ? '?' : c).ToArray());
                Text = $"{Thumbprint} (subject '{subject}', expires {FormatTime(NotAfter)})";

                if (NotBefore > now || NotAfter <= now)
                {
                    _dateProblem = NotBefore > now ? $"it is not valid until {FormatTime(NotBefore)}" : $"it expired at {FormatTime(NotAfter)}";
                    return;
                }

                try
                {
                    _ekus = certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault()?
                        .EnhancedKeyUsages.Cast<Oid>().Select(oid => oid.Value ?? "").ToArray();
                    _keyUsages = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault()?.KeyUsages;
                    _rsa = certificate.PublicKey.Oid.Value == RsaKeyOid;
                    KeyUnusable = keyProblem is null && !canOpenKey(certificate);
                    _keyProblem = keyProblem ?? (KeyUnusable ? "it has no private key this process can open" : null);
                }
                catch (Exception e) when (!IsFatal(e))
                {
                    _unreadable = $"it could not be checked: {e.Message}"; // for example an extension that cannot be decoded
                }
            }

            public X509Certificate2 Certificate { get; }
            public string? File { get; }
            public string Thumbprint { get; }
            public DateTime NotBefore { get; }
            public DateTime NotAfter { get; }

            /// <summary>Thumbprint, subject and expiry, for the logs.</summary>
            public string Text { get; }

            /// <summary>True when the private key of this instance could not be opened: a fresh instance may do better.</summary>
            public bool KeyUnusable { get; }

            /// <summary>True when the certificate has no private key that it can use.</summary>
            public bool LacksKey => _keyProblem is not null;

            public bool IsUsable(DateTime now) => NotBefore <= now && NotAfter > now && IsAlive(Certificate);

            /// <summary>
            /// Why the certificate cannot be used in this role, or null when it can. 'warn' is false for the reasons
            /// that are normal and not worth a log line: not valid yet (a pre-staged renewal) and expired.
            /// </summary>
            public string? Reason(TlsCertificateRole role, out bool warn)
            {
                warn = _dateProblem is null;
                if (_dateProblem is not null || _unreadable is not null)
                    return _dateProblem ?? _unreadable;

                bool Allows(string use) => _ekus is null || _ekus.Contains(use) || _ekus.Contains(AnyExtendedKeyUsageOid);
                var lacksServer = role != TlsCertificateRole.Client && !Allows(ServerAuthenticationOid);
                var lacksClient = role != TlsCertificateRole.Server && !Allows(ClientAuthenticationOid);
                var lacks = lacksServer && lacksClient ? "client and server" : lacksServer ? "server" : lacksClient ? "client" : null;
                if (lacks is not null)
                    return $"its enhanced key usage does not permit {lacks} authentication";

                // TLS signs with the key, except that an RSA server may decrypt the key exchange with it instead (TLS 1.2
                // RSA key exchange). A CA certificate that may only sign certificates and revocation lists never qualifies.
                var signs = _keyUsages is not { } usages || (usages & X509KeyUsageFlags.DigitalSignature) != 0 ||
                            role == TlsCertificateRole.Server && _rsa && (usages & X509KeyUsageFlags.KeyEncipherment) != 0;
                return signs ? _keyProblem : "its key usage does not permit digital signature";
            }
        }

        /// <summary>What one look-up found. The refresh drops it once the snapshot is built.</summary>
        private sealed class Found
        {
            private readonly Dictionary<string, int> _byThumbprint = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public List<Candidate> Candidates { get; } = new List<Candidate>();
            public List<string> Failures { get; } = new List<string>();      // files that could not be loaded
            public List<string> Notes { get; } = new List<string>();         // other reasons nothing qualified
            public HashSet<string> Live { get; } = new HashSet<string>();    // the keys of what this look-up loaded
            public Exception? Failure { get; set; }                          // the look-up itself failed
            public string? SkippedPem { get; set; }                          // a PEM file a target without PEM skipped
            public List<X509Certificate2> Unusable { get; } = new List<X509Certificate2>(); // read and found unusable

            // The same certificate in a second file counts once: the copy that has its key, so a copy without it (a
            // .cer beside the .pfx) never hides it, whatever the file names.
            public Candidate Add(Candidate candidate)
            {
                if (!_byThumbprint.TryGetValue(candidate.Thumbprint, out var at))
                {
                    _byThumbprint[candidate.Thumbprint] = Candidates.Count;
                    Candidates.Add(candidate);
                }
                else if (Candidates[at].LacksKey && !candidate.LacksKey)
                    Candidates[at] = candidate;
                return candidate;
            }
        }

        /// <summary>
        /// What a refresh loaded under one key (a certificate, a PEM key, the blocks of a PEM file, or nothing for a
        /// PEM certificate and key that do not fit), or why that failed, and when. A failure, and the candidate of a
        /// file whose private key could not be opened, are kept 5 minutes (a clock that went back ends it); a
        /// certificate is kept until its owner disposes it.
        /// </summary>
        private sealed record Loaded(string Key, object? Value, string? Error, DateTime At)
        {
            public bool IsReusable(DateTime now) => Error is not null || Value is Candidate
                ? now >= At && now - At < RetryFailedAfter
                : Value is not X509Certificate2 certificate || IsAlive(certificate);
        }

        /// <summary>The outcome of one look-up for every role, and its warnings about files. Never changed once published.</summary>
        private sealed record Snapshot(DateTime At, Outcome[] Outcomes, IReadOnlyList<Message> Messages)
        {
            public Outcome For(TlsCertificateRole role) => Outcomes[(int)role];
        }

        /// <summary>
        /// What a look-up (or the callback) gives one role: a pick, or why there is none. When both are set, the pick
        /// is the previous one, kept since <see cref="FallingBackSince"/>. Messages are the role's warnings.
        /// </summary>
        private sealed record Outcome(Candidate? Pick, string? Failure, Exception? Cause, DateTime? FallingBackSince,
            IReadOnlyList<Message> Messages)
        {
            /// <summary>For a callback: the number of the call that gave the pick, taken before the callback ran.</summary>
            public long Call { get; init; }

            // A new exception for each caller: one instance thrown on several threads would share its stack trace.
            public InvalidOperationException Fail() => new OwnFailure(
                Failure ?? $"The TLS certificate {Pick?.Text} is no longer in date or was disposed, and no other was found.", null, Cause);
        }

        private sealed class Refresh(DateTime startedAt)
        {
            private readonly Stopwatch _clock = Stopwatch.StartNew();
            public DateTime StartedAt { get; } = startedAt; // on the source's clock
            public TimeSpan Elapsed => _clock.Elapsed;      // in real time
            public TaskCompletionSource<Snapshot> Done { get; } =
                new TaskCompletionSource<Snapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }
}
