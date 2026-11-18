// -----------------------------------------------------------------------
// <copyright file="TlsPeerPolicy.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// An immutable trust decision with optional checks that can only narrow peer acceptance.
    /// </summary>
    public sealed class TlsPeerPolicy
    {
        private readonly TlsCertificateValidationCallback _trust;
        private readonly TlsCertificateValidationCallback[] _checks;

        private TlsPeerPolicy(TlsCertificateValidationCallback trust, TlsCertificateValidationCallback[] checks)
        {
            _trust = trust;
            _checks = checks;
        }

        /// <summary>Uses operating-system certificate-chain trust while ignoring only a name mismatch.</summary>
        public static TlsPeerPolicy SystemTrust() =>
            new(TlsCertificateValidation.ValidateChain(), Array.Empty<TlsCertificateValidationCallback>());

        /// <summary>Trusts a peer when its leaf certificate thumbprint matches one of the supplied pins.</summary>
        public static TlsPeerPolicy PinnedCertificates(params string[] pins) =>
            new(TlsCertificateValidation.PinnedCertificate(pins), Array.Empty<TlsCertificateValidationCallback>());

        /// <summary>Uses an explicit callback as the complete trust decision.</summary>
        public static TlsPeerPolicy CustomTrust(TlsCertificateValidationCallback callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            return new(callback, Array.Empty<TlsCertificateValidationCallback>());
        }

        /// <summary>Returns a policy that accepts only when this trust decision and every added check accept.</summary>
        public TlsPeerPolicy And(params TlsCertificateValidationCallback[] checks)
        {
            ArgumentNullException.ThrowIfNull(checks);
            var combined = new TlsCertificateValidationCallback[_checks.Length + checks.Length];
            Array.Copy(_checks, combined, _checks.Length);
            for (var i = 0; i < checks.Length; i++)
                combined[_checks.Length + i] = checks[i] ?? throw new ArgumentException(
                    $"Certificate validation check at index {i} cannot be null.", nameof(checks));
            return new(_trust, combined);
        }

        /// <summary>Evaluates this policy for a presented peer certificate.</summary>
        /// <returns><c>true</c> when the certificate is trusted and all additional checks accept it.</returns>
        /// <remarks>Callback exceptions propagate to the caller. The TCP transport reports them as handshake failures.</remarks>
        public bool ValidatePeer(X509Certificate2? certificate, X509Chain? chain, string remotePeer,
            SslPolicyErrors errors, ILoggingAdapter log)
        {
            if (certificate is null)
                return false;

            ArgumentNullException.ThrowIfNull(remotePeer);
            ArgumentNullException.ThrowIfNull(log);
            if (!_trust(certificate, chain, remotePeer, errors, log))
                return false;

            foreach (var check in _checks)
            {
                if (!check(certificate, chain, remotePeer, errors, log))
                    return false;
            }

            return true;
        }
    }
}
