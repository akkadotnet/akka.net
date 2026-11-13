// -----------------------------------------------------------------------
// <copyright file="TlsCertificateSourceFixtures.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Akka.Event;
using Akka.IO;
using Xunit;

namespace Akka.Tests.IO
{
    /// <summary>
    /// One way to hold certificates (a folder of PKCS#12 files, a folder of PEM files, a store, a callback).
    /// The specs write each behaviour once and run it against every fixture.
    /// </summary>
    internal abstract class SourceFixture : IDisposable
    {
        public const string Password = "pw";
        private readonly List<X509Certificate2> _made = new List<X509Certificate2>();

        public static readonly string[] AllKindNames = { "pkcs12-folder", "pem-folder", "store", "callback" };

        /// <summary>Every kind but the callback: the kinds that skip an unusable certificate and pick another.</summary>
        public static readonly string[] SkippingKindNames = { "pkcs12-folder", "pem-folder", "store" };

        public static TheoryData<string> AllKinds => new TheoryData<string>(AllKindNames);

        public static TheoryData<string> SkippingKinds => new TheoryData<string>(SkippingKindNames);

        public static TheoryData<string> FolderKinds => new TheoryData<string> { "pkcs12-folder", "pem-folder" };

        /// <summary>Creates the fixture, or skips the running test when this platform cannot host it.</summary>
        public static SourceFixture Open(string kind)
        {
            var fixture = Create(kind);
            if (fixture.SkipReason is not null)
            {
                var reason = fixture.SkipReason;
                fixture.Dispose();
                Assert.Skip(reason);
            }

            return fixture;
        }

        public static SourceFixture Create(string kind) => kind switch
        {
            "pkcs12-folder" => new Pkcs12FolderFixture(),
            "pem-folder" => new PemFolderFixture(),
            "store" => new StoreFixture(),
            "callback" => new CallbackFixture(),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown fixture kind.")
        };

        /// <summary>The common name every certificate made by <see cref="Make"/> gets. Only a store uses it.</summary>
        public string CommonName { get; } = "akka-tls-" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// The source. It keeps the real look-up interval: a spec that changes the certificates calls
        /// <see cref="RefreshAsync"/> before it expects a call to see the change.
        /// </summary>
        public abstract TlsCertificateSource Source { get; }

        /// <summary>Runs a look-up of the source now and waits for it. Does nothing for a callback.</summary>
        public System.Threading.Tasks.Task RefreshAsync() => Source.RefreshAsync();

        /// <summary>Why the specs cannot run on this platform or target, or <c>null</c>.</summary>
        public virtual string? SkipReason => null;

        /// <summary>
        /// A callback hands over one certificate and cannot fall back to another, so an unusable certificate makes it
        /// throw. Folders and stores skip the certificate and pick another.
        /// </summary>
        public virtual bool FailsOnUnusableCertificate => false;

        public X509Certificate2 Make(int notBeforeMinutes, int notAfterMinutes, bool rsa = false, params string[] ekus) =>
            Make(CommonName, notBeforeMinutes, notAfterMinutes, rsa, ekus);

        public X509Certificate2 Make(string commonName, int notBeforeMinutes, int notAfterMinutes, bool rsa,
            params string[] ekus)
        {
            var certificate = TlsTestCertificates.Create(commonName, notBeforeMinutes, notAfterMinutes, rsa, ekus);
            _made.Add(certificate);
            return certificate;
        }

        /// <summary>Makes a certificate with this key usage (<c>null</c>: none), as a CA when <paramref name="authority"/> is true.</summary>
        public X509Certificate2 Make(int notBeforeMinutes, int notAfterMinutes, X509KeyUsageFlags? keyUsage, bool authority,
            params string[] ekus) =>
            Keep(TlsTestCertificates.Create(CommonName, notBeforeMinutes, notAfterMinutes, false, keyUsage, authority, ekus));

        /// <summary>Makes an RSA or ECDSA certificate with this key usage, not a CA.</summary>
        public X509Certificate2 MakeWithKeyUsage(int notBeforeMinutes, int notAfterMinutes, bool rsa,
            X509KeyUsageFlags keyUsage, params string[] ekus) =>
            Keep(TlsTestCertificates.Create(CommonName, notBeforeMinutes, notAfterMinutes, rsa, keyUsage, false, ekus));

        /// <summary>Makes a certificate whose enhanced key usage extension cannot be decoded.</summary>
        public X509Certificate2 MakeWithUndecodableEku(int notBeforeMinutes, int notAfterMinutes, bool rsa = false) =>
            Keep(TlsTestCertificates.CreateWithUndecodableEku(CommonName, notBeforeMinutes, notAfterMinutes, rsa));

        /// <summary>Like <see cref="Make(int,int,bool,string[])"/>, with exact validity times.</summary>
        public X509Certificate2 Make(DateTimeOffset notBefore, DateTimeOffset notAfter, bool rsa = false,
            params string[] ekus) => Keep(TlsTestCertificates.Create(CommonName, notBefore, notAfter, rsa, ekus));

        /// <summary>Makes a certificate with any subject, valid from an hour ago for an hour.</summary>
        public X509Certificate2 Make(X500DistinguishedName subject, bool rsa = false, params string[] ekus) =>
            Keep(TlsTestCertificates.Create(subject, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1),
                rsa, ekus));

        private X509Certificate2 Keep(X509Certificate2 certificate)
        {
            _made.Add(certificate);
            return certificate;
        }

        public abstract void Add(X509Certificate2 certificate, bool withKey = true);

        public abstract void Remove(X509Certificate2 certificate);

        /// <summary>Adds something the source cannot load, such as a half-written file.</summary>
        public virtual void AddBroken() => throw new NotSupportedException();

        public virtual void Dispose()
        {
            foreach (var certificate in _made)
                certificate.Dispose();
        }
    }

    internal abstract class FolderFixture : SourceFixture
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "akka-tls-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<X509Certificate2, List<string>> _files =
            new Dictionary<X509Certificate2, List<string>>();
        private readonly Lazy<TlsCertificateSource> _source;

        protected FolderFixture()
        {
            Folder = Path.Combine(_root, "folder");
            Targets = Path.Combine(_root, "targets");
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Targets);
            _source = new Lazy<TlsCertificateSource>(() => TlsCertificateSource.FromFolder(Folder, FolderPassword));
        }

        public string Folder { get; }

        /// <summary>A directory next to the folder, for files that the folder links to.</summary>
        public string Targets { get; }

        public override TlsCertificateSource Source => _source.Value;

        protected abstract string? FolderPassword { get; }

        /// <summary>The files that hold this certificate: a kind (used to build the file name) and the bytes.</summary>
        protected abstract IEnumerable<(string Kind, byte[] Bytes)> Render(X509Certificate2 certificate, bool withKey);

        protected abstract byte[] RenderBroken();

        /// <summary>The files that hold this certificate with its key, by kind (such as "pfx", "crt" or "key").</summary>
        public IEnumerable<(string Kind, byte[] Bytes)> FilesOf(X509Certificate2 certificate) => Render(certificate, true);

        public override void Add(X509Certificate2 certificate, bool withKey = true) => Add(certificate, withKey, "");

        /// <summary>
        /// Writes the certificate's files with random names, so a certificate file never has a name that
        /// corresponds to its key file. <paramref name="prefix"/> is put in front of every name.
        /// </summary>
        public void Add(X509Certificate2 certificate, bool withKey, string prefix)
        {
            if (!_files.TryGetValue(certificate, out var paths))
                _files[certificate] = paths = new List<string>();

            foreach (var (kind, bytes) in Render(certificate, withKey))
            {
                var path = Path.Combine(Folder, $"{prefix}{kind}-{Guid.NewGuid():N}");
                File.WriteAllBytes(path, bytes);
                paths.Add(path);
            }
        }

        public override void Remove(X509Certificate2 certificate)
        {
            foreach (var path in _files[certificate])
                File.Delete(path);
            _files.Remove(certificate);
        }

        public override void AddBroken() =>
            File.WriteAllBytes(Path.Combine(Folder, "broken-" + Guid.NewGuid().ToString("N")), RenderBroken());

        /// <summary>
        /// Makes <c>{linkName}-{kind}</c> in the folder a symbolic link to the certificate's files, which live
        /// outside the folder. Calling it again with another certificate swaps the link.
        /// </summary>
        public void Link(string linkName, X509Certificate2 certificate)
        {
#if NET6_0_OR_GREATER
            foreach (var (kind, bytes) in Render(certificate, true))
            {
                var target = Path.Combine(Targets, $"{kind}-{Guid.NewGuid():N}");
                File.WriteAllBytes(target, bytes);
                var link = Path.Combine(Folder, $"{linkName}-{kind}");
                if (File.Exists(link))
                    File.Delete(link);
                File.CreateSymbolicLink(link, target);
            }
#else
            throw new PlatformNotSupportedException("Symbolic links need .NET 6 or later.");
#endif
        }

#if NET6_0_OR_GREATER
        /// <summary>
        /// Lays the folder out the way a Kubernetes secret mount does: the files live in a timestamped folder named
        /// <paramref name="stamp"/>, <c>..data</c> is a relative link to it, and each name in the folder is a
        /// relative link through <c>..data</c>. Calling it again with another stamp swaps <c>..data</c>.
        /// </summary>
        public void PublishSecret(string stamp, params (string Name, byte[] Bytes)[] files)
        {
            var dated = Path.Combine(Folder, stamp);
            Directory.CreateDirectory(dated);
            foreach (var (name, bytes) in files)
                File.WriteAllBytes(Path.Combine(dated, name), bytes);

            var data = Path.Combine(Folder, "..data");
            if (Directory.Exists(data))
                Directory.Delete(data); // removes the link, not its target
            Directory.CreateSymbolicLink(data, stamp);
            foreach (var (name, _) in files)
            {
                var link = Path.Combine(Folder, name);
                if (!File.Exists(link))
                    File.CreateSymbolicLink(link, Path.Combine("..data", name));
            }
        }
#endif

        public override void Dispose()
        {
            base.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    internal sealed class Pkcs12FolderFixture : FolderFixture
    {
        protected override string? FolderPassword => Password;

        protected override IEnumerable<(string Kind, byte[] Bytes)> Render(X509Certificate2 certificate, bool withKey)
        {
            if (withKey)
            {
                yield return ("pfx", certificate.Export(X509ContentType.Pkcs12, Password));
            }
            else
            {
                using var publicOnly = TlsTestCertificates.WithoutKey(certificate);
                yield return ("pfx", publicOnly.Export(X509ContentType.Pkcs12, Password));
            }
        }

        protected override byte[] RenderBroken()
        {
            using var certificate = TlsTestCertificates.Create(CommonName, -1, 1, false);
            var bytes = certificate.Export(X509ContentType.Pkcs12, Password);
            return bytes.Take(bytes.Length / 2).ToArray();
        }
    }

    internal sealed class PemFolderFixture : FolderFixture
    {
        private bool SameFile { get; }
        private bool TraditionalKeys { get; }

        /// <summary>Text written before the first block of every file, as OpenSSL does ("Bag Attributes").</summary>
        private string LeadingText { get; }

        public PemFolderFixture(string? password = null, bool sameFile = false, bool traditionalKeys = false,
            string leadingText = "")
        {
            FolderPassword = password;
            SameFile = sameFile;
            TraditionalKeys = traditionalKeys;
            LeadingText = leadingText;
        }

        protected override string? FolderPassword { get; }

#if NET6_0_OR_GREATER
        protected override IEnumerable<(string Kind, byte[] Bytes)> Render(X509Certificate2 certificate, bool withKey)
        {
            var certificatePem = PemEncoding.WriteString("CERTIFICATE", certificate.RawData);
            if (!withKey)
            {
                yield return ("crt", Encoding.ASCII.GetBytes(LeadingText + certificatePem + "\n"));
                yield break;
            }

            var keyPem = KeyPem(certificate);
            if (SameFile)
                yield return ("pem", Encoding.ASCII.GetBytes(LeadingText + certificatePem + "\n" + keyPem + "\n"));
            else
            {
                yield return ("crt", Encoding.ASCII.GetBytes(LeadingText + certificatePem + "\n"));
                yield return ("key", Encoding.ASCII.GetBytes(LeadingText + keyPem + "\n"));
            }
        }

        private string KeyPem(X509Certificate2 certificate)
        {
            using var rsa = certificate.GetRSAPrivateKey();
            using var ecdsa = rsa is null ? certificate.GetECDsaPrivateKey() : null;
            if (FolderPassword is not null)
            {
                var parameters = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 2048);
                var encrypted = rsa is not null
                    ? rsa.ExportEncryptedPkcs8PrivateKey(FolderPassword, parameters)
                    : ecdsa!.ExportEncryptedPkcs8PrivateKey(FolderPassword, parameters);
                return PemEncoding.WriteString("ENCRYPTED PRIVATE KEY", encrypted);
            }

            if (TraditionalKeys)
                return rsa is not null
                    ? PemEncoding.WriteString("RSA PRIVATE KEY", rsa.ExportRSAPrivateKey())
                    : PemEncoding.WriteString("EC PRIVATE KEY", ecdsa!.ExportECPrivateKey());

            return PemEncoding.WriteString("PRIVATE KEY",
                rsa is not null ? rsa.ExportPkcs8PrivateKey() : ecdsa!.ExportPkcs8PrivateKey());
        }

        // A certificate block that starts and never ends.
        protected override byte[] RenderBroken()
        {
            using var certificate = TlsTestCertificates.Create(CommonName, -1, 1, false);
            var pem = PemEncoding.WriteString("CERTIFICATE", certificate.RawData);
            return Encoding.ASCII.GetBytes(pem.Substring(0, pem.Length / 2));
        }
#else
        public override string? SkipReason => "PEM support needs .NET 6 or later.";

        protected override IEnumerable<(string Kind, byte[] Bytes)> Render(X509Certificate2 certificate, bool withKey) =>
            throw new PlatformNotSupportedException(SkipReason);

        protected override byte[] RenderBroken() => throw new PlatformNotSupportedException(SkipReason);
#endif
    }

    internal sealed class StoreFixture : SourceFixture
    {
        private readonly List<string> _thumbprints = new List<string>();
        private readonly TlsCertificateSource _source;
        private readonly string? _skipReason;

        public StoreFixture()
        {
            _source = TlsCertificateSource.FromStore(CommonName, "My", StoreLocation.CurrentUser);
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadWrite);
            }
            catch (Exception e) when (e is CryptographicException || e is PlatformNotSupportedException ||
                                      e is UnauthorizedAccessException || e is IOException)
            {
                _skipReason = $"This OS does not give a writable current-user certificate store: {e.Message}";
            }
        }

        public override TlsCertificateSource Source => _source;

        public override string? SkipReason => _skipReason;

        public override void Add(X509Certificate2 certificate, bool withKey = true)
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            if (withKey)
            {
                store.Add(certificate);
            }
            else
            {
                using var publicOnly = TlsTestCertificates.WithoutKey(certificate);
                store.Add(publicOnly);
            }

            _thumbprints.Add(certificate.Thumbprint);
        }

        public override void Remove(X509Certificate2 certificate) => RemoveByThumbprint(certificate.Thumbprint);

        private void RemoveByThumbprint(string thumbprint)
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            foreach (var stored in store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false))
            {
                store.Remove(stored);
                stored.Dispose();
            }

            _thumbprints.Remove(thumbprint);
        }

        public override void Dispose()
        {
            if (_skipReason is null)
            {
                foreach (var thumbprint in _thumbprints.ToArray())
                    RemoveByThumbprint(thumbprint);
            }

            base.Dispose();
        }
    }

    /// <summary>
    /// Stands for a read that does not return: the thread that calls <see cref="Block{T}"/> stays there until the spec
    /// calls <see cref="Release"/>, or the spec ends. A source seam calls it on the source's own look-up thread.
    /// </summary>
    internal sealed class BlockingGate : IDisposable
    {
        private readonly System.Threading.SemaphoreSlim _entered = new System.Threading.SemaphoreSlim(0);
        private readonly System.Threading.ManualResetEventSlim _released = new System.Threading.ManualResetEventSlim();

        public T Block<T>(T result)
        {
            _entered.Release();
            _released.Wait(TimeSpan.FromSeconds(60));
            return result;
        }

        /// <summary>Completes when a thread is blocked in <see cref="Block{T}"/>; fails after 10 seconds.</summary>
        public async System.Threading.Tasks.Task EnteredAsync()
        {
            if (!await _entered.WaitAsync(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Nothing reached the gate.");
        }

        public void Release() => _released.Set();

        // Lets a blocked thread go. The events are left to the collector, as a thread may still be leaving them.
        public void Dispose() => _released.Set();
    }

    internal sealed class CallbackFixture : SourceFixture
    {
        private readonly List<X509Certificate2> _held = new List<X509Certificate2>();
        private readonly List<X509Certificate2> _copies = new List<X509Certificate2>();
        private readonly TlsCertificateSource _source;

        public CallbackFixture()
        {
            // The application's own logic: hand over the certificate issued last, usable or not.
            _source = TlsCertificateSource.FromCallback(() =>
            {
                lock (_held)
                    return _held.OrderByDescending(c => c.NotBefore).ThenByDescending(c => c.NotAfter).First();
            });
        }

        public override TlsCertificateSource Source => _source;

        public override bool FailsOnUnusableCertificate => true;

        public override void Add(X509Certificate2 certificate, bool withKey = true)
        {
            var toHold = withKey ? certificate : TlsTestCertificates.WithoutKey(certificate);
            if (!withKey)
                _copies.Add(toHold);
            lock (_held)
                _held.Add(toHold);
        }

        public override void Remove(X509Certificate2 certificate)
        {
            lock (_held)
                _held.RemoveAll(c => c.Thumbprint == certificate.Thumbprint);
        }

        public override void Dispose()
        {
            foreach (var copy in _copies)
                copy.Dispose();
            base.Dispose();
        }
    }

    /// <summary>A logger that keeps what it is given, in order, so a spec can count lines without timing.</summary>
    internal sealed class RecordingLog : LoggingAdapterBase
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Text)> _lines =
            new System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Text)>();

        public RecordingLog() : base(DefaultLogMessageFormatter.Instance)
        {
        }

        public override bool IsDebugEnabled => true;
        public override bool IsErrorEnabled => true;
        public override bool IsInfoEnabled => true;
        public override bool IsWarningEnabled => true;

        public System.Collections.Generic.List<string> Warnings =>
            _lines.Where(l => l.Level == LogLevel.WarningLevel).Select(l => l.Text).ToList();

        public System.Collections.Generic.List<string> Errors =>
            _lines.Where(l => l.Level == LogLevel.ErrorLevel).Select(l => l.Text).ToList();

        public System.Collections.Generic.List<string> Infos =>
            _lines.Where(l => l.Level == LogLevel.InfoLevel).Select(l => l.Text).ToList();

        /// <summary>Runs on the logging thread for each line, after it is kept. A spec uses it to hold a caller still.</summary>
        public Action<LogLevel, string>? OnLine { get; set; }

        protected override void NotifyLog(LogLevel logLevel, object message, Exception? cause = null)
        {
            var text = message?.ToString() ?? "";
            _lines.Enqueue((logLevel, text));
            OnLine?.Invoke(logLevel, text);
        }
    }
}
