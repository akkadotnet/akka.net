//-----------------------------------------------------------------------
// <copyright file="TlsCertificateValidation.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Akka.Event;

namespace Akka.IO
{
    /// <summary>
    /// Creates certificate validation callbacks for common TLS trust policies.
    /// </summary>
    public static class TlsCertificateValidation
    {
        private const int MaximumDistinguishedNamePatternLength = 4096;
        private static readonly TimeSpan PatternMatchTimeout = TimeSpan.FromMilliseconds(250);

        /// <summary>
        /// Creates a callback that accepts certificates whose chain has no policy errors.
        /// A name mismatch is ignored so this callback can be combined with an explicit hostname policy.
        /// </summary>
        /// <param name="log">Optional logger used for validation failures. The callback logger is used when omitted.</param>
        /// <returns>A certificate validation callback.</returns>
        public static TlsCertificateValidationCallback ValidateChain(ILoggingAdapter? log = null)
        {
            return (certificate, chain, remotePeer, errors, callbackLog) =>
            {
                if (certificate is null)
                {
                    (log ?? callbackLog).Error("TLS certificate chain validation failed for {0}: no certificate was presented", remotePeer);
                    return false;
                }

                var chainErrors = errors & ~SslPolicyErrors.RemoteCertificateNameMismatch;
                if (chainErrors == SslPolicyErrors.None)
                    return true;

                (log ?? callbackLog).Error("TLS certificate chain validation failed for {0}:\n{1}",
                    remotePeer, TlsDiagnostics.BuildSslPolicyErrorMessage(chainErrors, certificate, chain));
                return false;
            };
        }

        /// <summary>
        /// Creates a callback that validates the hostname reported by TLS or an explicitly supplied hostname.
        /// </summary>
        /// <param name="expectedHostname">Optional host name to match against the certificate SAN or subject.</param>
        /// <param name="log">Optional logger used for validation failures. The callback logger is used when omitted.</param>
        /// <returns>A certificate validation callback.</returns>
        public static TlsCertificateValidationCallback ValidateHostname(
            string? expectedHostname = null,
            ILoggingAdapter? log = null)
        {
            if (expectedHostname is not null)
            {
                if (string.IsNullOrWhiteSpace(expectedHostname))
                    throw new ArgumentException("The expected hostname cannot be empty or whitespace.", nameof(expectedHostname));
                if (expectedHostname.Contains('*') || Uri.CheckHostName(expectedHostname) == UriHostNameType.Unknown)
                    throw new ArgumentException("The expected hostname must be a concrete DNS name or IP address.", nameof(expectedHostname));
            }

            return (certificate, _, remotePeer, errors, callbackLog) =>
            {
                if (certificate is null)
                {
                    (log ?? callbackLog).Error("TLS hostname validation failed for {0}: no certificate was presented", remotePeer);
                    return false;
                }

                if (expectedHostname is null)
                {
                    if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) == 0)
                        return true;

                    var actualName = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
                    (log ?? callbackLog).Error("TLS hostname validation failed for {0}: certificate name is '{1}'",
                        remotePeer, actualName);
                    return false;
                }

                try
                {
                    if (certificate.MatchesHostname(expectedHostname))
                        return true;
                }
                catch (Exception exception) when (exception is ArgumentException or CryptographicException)
                {
                    (log ?? callbackLog).Error(exception,
                        "TLS hostname validation failed for {0}: unable to compare certificate with expected host '{1}'",
                        remotePeer, expectedHostname);
                    return false;
                }

                var name = certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false);
                (log ?? callbackLog).Error("TLS hostname validation failed for {0}: expected '{1}', certificate name is '{2}'",
                    remotePeer, expectedHostname, name);
                return false;
            };
        }

        /// <summary>
        /// Creates a callback that accepts only certificates with a thumbprint in the supplied list.
        /// Thumbprints are compared case-insensitively after removing display whitespace.
        /// </summary>
        /// <param name="allowedThumbprints">Allowed certificate thumbprints.</param>
        /// <returns>A certificate validation callback.</returns>
        public static TlsCertificateValidationCallback PinnedCertificate(params string[] allowedThumbprints)
        {
            ArgumentNullException.ThrowIfNull(allowedThumbprints);
            if (allowedThumbprints.Length == 0)
                throw new ArgumentException("At least one certificate thumbprint is required.", nameof(allowedThumbprints));

            var normalizedThumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < allowedThumbprints.Length; i++)
            {
                var thumbprint = allowedThumbprints[i];
                if (string.IsNullOrWhiteSpace(thumbprint))
                    throw new ArgumentException($"Certificate thumbprint at index {i} cannot be null or whitespace.", nameof(allowedThumbprints));

                normalizedThumbprints.Add(NormalizeThumbprint(thumbprint));
            }

            return (certificate, _, remotePeer, _, log) =>
            {
                var thumbprint = certificate is null ? null : NormalizeThumbprint(certificate.Thumbprint ?? string.Empty);
                if (string.IsNullOrEmpty(thumbprint) || !normalizedThumbprints.Contains(thumbprint))
                {
                    log.Error("TLS certificate pin validation failed for {0}: thumbprint '{1}' is not allowed",
                        remotePeer, thumbprint ?? "<no certificate>");
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Creates a callback that matches the certificate subject distinguished name against a case-sensitive pattern.
        /// An asterisk matches zero or more characters; the match always covers the complete distinguished name.
        /// </summary>
        /// <param name="expectedSubjectPattern">Case-sensitive subject pattern, such as <c>CN=worker-*</c>.</param>
        /// <param name="log">Optional logger used for validation failures. The callback logger is used when omitted.</param>
        /// <returns>A certificate validation callback.</returns>
        public static TlsCertificateValidationCallback ValidateSubject(
            string expectedSubjectPattern,
            ILoggingAdapter? log = null)
        {
            var matcher = CreateDistinguishedNameMatcher(expectedSubjectPattern, nameof(expectedSubjectPattern));
            return (certificate, _, remotePeer, _, callbackLog) =>
            {
                var subject = certificate?.Subject;
                if (string.IsNullOrEmpty(subject) || !matcher.IsMatch(subject))
                {
                    (log ?? callbackLog).Error("TLS subject validation failed for {0}: subject '{1}' does not match '{2}'",
                        remotePeer, subject ?? "<no certificate>", expectedSubjectPattern);
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Creates a callback that matches the certificate issuer distinguished name against a case-sensitive pattern.
        /// An asterisk matches zero or more characters; the match always covers the complete distinguished name.
        /// </summary>
        /// <param name="expectedIssuerPattern">Case-sensitive issuer pattern, such as <c>CN=Example CA*</c>.</param>
        /// <param name="log">Optional logger used for validation failures. The callback logger is used when omitted.</param>
        /// <returns>A certificate validation callback.</returns>
        public static TlsCertificateValidationCallback ValidateIssuer(
            string expectedIssuerPattern,
            ILoggingAdapter? log = null)
        {
            var matcher = CreateDistinguishedNameMatcher(expectedIssuerPattern, nameof(expectedIssuerPattern));
            return (certificate, _, remotePeer, _, callbackLog) =>
            {
                var issuer = certificate?.Issuer;
                if (string.IsNullOrEmpty(issuer) || !matcher.IsMatch(issuer))
                {
                    (log ?? callbackLog).Error("TLS issuer validation failed for {0}: issuer '{1}' does not match '{2}'",
                        remotePeer, issuer ?? "<no certificate>", expectedIssuerPattern);
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Creates a callback that runs validators in order and accepts only when every validator succeeds.
        /// </summary>
        /// <param name="validators">Callbacks to invoke in order.</param>
        /// <returns>A composed certificate validation callback.</returns>
        public static TlsCertificateValidationCallback Combine(params TlsCertificateValidationCallback[] validators)
        {
            ArgumentNullException.ThrowIfNull(validators);
            if (validators.Length == 0)
                throw new ArgumentException("At least one certificate validator is required.", nameof(validators));

            var validatorSnapshot = new TlsCertificateValidationCallback[validators.Length];
            for (var i = 0; i < validators.Length; i++)
            {
                validatorSnapshot[i] = validators[i] ?? throw new ArgumentException(
                    $"Certificate validator at index {i} cannot be null.", nameof(validators));
            }

            return (certificate, chain, remotePeer, errors, log) =>
            {
                foreach (var validator in validatorSnapshot)
                {
                    if (!validator(certificate, chain, remotePeer, errors, log))
                        return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Creates a callback that validates the chain first, then applies an additional application check.
        /// </summary>
        /// <param name="customCheck">Application-specific certificate check.</param>
        /// <param name="log">Optional logger used for validation failures. The callback logger is used when omitted.</param>
        /// <returns>A certificate validation callback.</returns>
        public static TlsCertificateValidationCallback ChainPlusThen(
            Func<X509Certificate2?, X509Chain?, string, bool> customCheck,
            ILoggingAdapter? log = null)
        {
            ArgumentNullException.ThrowIfNull(customCheck);
            var chainValidator = ValidateChain(log);
            return (certificate, chain, remotePeer, errors, callbackLog) =>
            {
                if (!chainValidator(certificate, chain, remotePeer, errors, callbackLog))
                    return false;

                if (!customCheck(certificate, chain, remotePeer))
                {
                    (log ?? callbackLog).Error("Application TLS certificate validation failed for {0}", remotePeer);
                    return false;
                }

                return true;
            };
        }

        private static Regex CreateDistinguishedNameMatcher(string pattern, string parameterName)
        {
            ArgumentNullException.ThrowIfNull(pattern);
            if (string.IsNullOrWhiteSpace(pattern))
                throw new ArgumentException("A non-empty distinguished name pattern is required.", parameterName);
            if (pattern.Length > MaximumDistinguishedNamePatternLength)
                throw new ArgumentOutOfRangeException(parameterName, pattern.Length,
                    $"Distinguished name patterns cannot exceed {MaximumDistinguishedNamePatternLength} characters.");

            var expression = "\\A" + Regex.Escape(pattern).Replace("\\*", ".*") + "\\z";
            return new Regex(expression, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, PatternMatchTimeout);
        }

        private static string NormalizeThumbprint(string thumbprint)
        {
            var builder = new StringBuilder(thumbprint.Length);
            foreach (var character in thumbprint)
            {
                if (!char.IsWhiteSpace(character))
                    builder.Append(char.ToUpperInvariant(character));
            }

            return builder.ToString();
        }
    }

    internal static class TlsDiagnostics
    {
        internal static string BuildSslPolicyErrorMessage(
            SslPolicyErrors errors,
            X509Certificate2? certificate,
            X509Chain? chain)
        {
            var message = new StringBuilder("TLS certificate validation failed:");
            if ((errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
                message.AppendLine().Append(" - The peer did not provide a certificate.");
            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
                message.AppendLine().Append(" - The certificate name does not match the connection target. Check that its CN or SAN entries include the target host.");
            if ((errors & SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            {
                message.AppendLine().Append(" - The certificate chain could not be validated.");
                if (chain is null || chain.ChainStatus.Length == 0)
                    message.AppendLine().Append("   Check that required intermediate certificates are available.");
                else
                {
                    foreach (var status in chain.ChainStatus)
                    {
                        if (status.Status == X509ChainStatusFlags.NoError)
                            continue;

                        message.AppendLine().Append("   - ").Append(status.Status).Append(": ")
                            .Append(status.StatusInformation.Trim()).Append(". ")
                            .Append(GetChainSuggestion(status.Status));
                    }
                }
            }

            if (certificate is not null)
            {
                message.AppendLine().Append(" - Subject: ").Append(certificate.Subject)
                    .AppendLine().Append(" - Issuer: ").Append(certificate.Issuer)
                    .AppendLine().Append(" - Thumbprint: ").Append(certificate.Thumbprint)
                    .AppendLine().Append(" - Has private key: ").Append(certificate.HasPrivateKey)
                    .AppendLine().Append(" - Valid: ").Append(certificate.NotBefore.ToUniversalTime().ToString("O"))
                    .Append(" to ").Append(certificate.NotAfter.ToUniversalTime().ToString("O"));
            }

            return message.ToString();
        }

        private static string GetChainSuggestion(X509ChainStatusFlags status)
        {
            var suggestions = new StringBuilder();
            foreach (var flag in IndividualChainStatusFlags)
            {
                if ((status & flag) != flag)
                    continue;

                if (suggestions.Length > 0)
                    suggestions.Append(' ');
                suggestions.Append(GetSingleChainSuggestion(flag));
            }

            return suggestions.Length == 0
                ? "Inspect the chain status and certificate extensions."
                : suggestions.ToString();
        }

        private static readonly X509ChainStatusFlags[] IndividualChainStatusFlags =
        {
            X509ChainStatusFlags.NotTimeValid,
            X509ChainStatusFlags.NotTimeNested,
            X509ChainStatusFlags.Revoked,
            X509ChainStatusFlags.NotSignatureValid,
            X509ChainStatusFlags.NotValidForUsage,
            X509ChainStatusFlags.UntrustedRoot,
            X509ChainStatusFlags.RevocationStatusUnknown,
            X509ChainStatusFlags.Cyclic,
            X509ChainStatusFlags.InvalidExtension,
            X509ChainStatusFlags.InvalidPolicyConstraints,
            X509ChainStatusFlags.InvalidBasicConstraints,
            X509ChainStatusFlags.InvalidNameConstraints,
            X509ChainStatusFlags.HasNotSupportedNameConstraint,
            X509ChainStatusFlags.HasNotDefinedNameConstraint,
            X509ChainStatusFlags.HasNotPermittedNameConstraint,
            X509ChainStatusFlags.HasExcludedNameConstraint,
            X509ChainStatusFlags.PartialChain,
            X509ChainStatusFlags.CtlNotTimeValid,
            X509ChainStatusFlags.CtlNotSignatureValid,
            X509ChainStatusFlags.CtlNotValidForUsage,
            X509ChainStatusFlags.OfflineRevocation,
            X509ChainStatusFlags.NoIssuanceChainPolicy,
            X509ChainStatusFlags.ExplicitDistrust,
            X509ChainStatusFlags.HasNotSupportedCriticalExtension,
            X509ChainStatusFlags.HasWeakSignature
        };

        private static string GetSingleChainSuggestion(X509ChainStatusFlags status)
        {
            return status switch
            {
                X509ChainStatusFlags.NotTimeValid => "Check the system clock and certificate validity dates.",
                X509ChainStatusFlags.NotTimeNested => "Ensure each certificate validity period is contained within its issuer's period.",
                X509ChainStatusFlags.Revoked => "Replace the revoked certificate with a valid one or contact its issuer.",
                X509ChainStatusFlags.NotSignatureValid => "Check that the certificate is intact and was signed by the expected issuer.",
                X509ChainStatusFlags.NotValidForUsage => "Check the certificate's extended key usage for this connection role.",
                X509ChainStatusFlags.UntrustedRoot => "Install or configure the trusted root certificate.",
                X509ChainStatusFlags.RevocationStatusUnknown => "Check network access to the configured CRL or OCSP service.",
                X509ChainStatusFlags.Cyclic => "Correct the issuer relationships so the certificate chain has no cycle.",
                X509ChainStatusFlags.InvalidExtension => "Correct the malformed certificate extension.",
                X509ChainStatusFlags.InvalidPolicyConstraints => "Correct the certificate policy constraints in the chain.",
                X509ChainStatusFlags.InvalidBasicConstraints => "Correct the certificate basic constraints, including CA status where required.",
                X509ChainStatusFlags.InvalidNameConstraints => "Correct the name constraints in the certificate chain.",
                X509ChainStatusFlags.HasNotSupportedNameConstraint => "Use a chain with name constraints supported by this runtime.",
                X509ChainStatusFlags.HasNotDefinedNameConstraint => "Define the name constraints required by the certificate chain.",
                X509ChainStatusFlags.HasNotPermittedNameConstraint => "Change the certificate name to one permitted by the chain constraints.",
                X509ChainStatusFlags.HasExcludedNameConstraint => "Change the certificate name because the chain explicitly excludes it.",
                X509ChainStatusFlags.PartialChain => "Install or provide the missing intermediate certificates.",
                X509ChainStatusFlags.CtlNotTimeValid => "Check the system clock and the certificate trust list validity period.",
                X509ChainStatusFlags.CtlNotSignatureValid => "Provide a certificate trust list with a valid signature.",
                X509ChainStatusFlags.CtlNotValidForUsage => "Use a certificate trust list valid for the requested purpose.",
                X509ChainStatusFlags.OfflineRevocation => "Restore access to the revocation service or configure an appropriate revocation policy.",
                X509ChainStatusFlags.NoIssuanceChainPolicy => "Configure an issuance chain policy for this certificate.",
                X509ChainStatusFlags.ExplicitDistrust => "Remove the certificate from the applicable distrust list if it should be trusted.",
                X509ChainStatusFlags.HasNotSupportedCriticalExtension => "Use a certificate without unsupported critical extensions or upgrade the validating runtime.",
                X509ChainStatusFlags.HasWeakSignature => "Replace the certificate with one signed using a currently accepted algorithm.",
                _ => "Inspect the chain status and certificate extensions."
            };
        }
    }
}
