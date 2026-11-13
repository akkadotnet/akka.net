//-----------------------------------------------------------------------
// <copyright file="CertificateValidationHelpersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Event;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Unit tests for CertificateValidation helper methods to ensure proper edge case handling
    /// </summary>
    public class CertificateValidationHelpersSpec : AkkaSpec
    {
        private const string ValidCertPath = "Resources/akka-validcert.pfx";
        private const string ClientCertPath = "Resources/akka-client-cert.pfx";
        private const string Password = "password";
        private readonly ILoggingAdapter _log;

        public CertificateValidationHelpersSpec(ITestOutputHelper output) : base(output)
        {
            _log = Logging.GetLogger(Sys, typeof(CertificateValidationHelpersSpec));
        }

        #region PinnedCertificate Tests

        [Fact(DisplayName = "PinnedCertificate should reject null certificate")]
        public void PinnedCertificate_should_reject_null_certificate()
        {
            // Arrange
            var validator = CertificateValidation.PinnedCertificate("ABCD1234");

            // Act & Assert
            EventFilter.Error(contains: "certificate is null").ExpectOne(() =>
            {
                var result = validator(null, null, "test-peer", SslPolicyErrors.None, _log);
                Assert.False(result);
            });
        }

        // Note: X509Certificate2 always has a thumbprint when properly constructed,
        // so we can't test the empty thumbprint case directly. The null check in
        // PinnedCertificate is defensive programming for edge cases.

        [Fact(DisplayName = "PinnedCertificate should throw if no thumbprints provided")]
        public void PinnedCertificate_should_throw_if_no_thumbprints_provided()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => CertificateValidation.PinnedCertificate());
            Assert.Throws<ArgumentException>(() => CertificateValidation.PinnedCertificate(null));
            Assert.Throws<ArgumentException>(() => CertificateValidation.PinnedCertificate(new string[0]));
        }

        [Fact(DisplayName = "PinnedCertificate should throw if only empty/whitespace thumbprints provided")]
        public void PinnedCertificate_should_throw_if_only_empty_thumbprints_provided()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => CertificateValidation.PinnedCertificate(""));
            Assert.Throws<ArgumentException>(() => CertificateValidation.PinnedCertificate("", "  ", null));
            Assert.Throws<ArgumentException>(() => CertificateValidation.PinnedCertificate(" ", "\t", "\n"));
        }

        [Fact(DisplayName = "PinnedCertificate should filter out empty thumbprints and use valid ones")]
        public void PinnedCertificate_should_filter_empty_thumbprints()
        {
            // Arrange
            var cert = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var thumbprint = cert.Thumbprint;

            // Include some empty/null values that should be filtered out
            var validator = CertificateValidation.PinnedCertificate("", thumbprint, null, "  ", thumbprint.ToLower());

            // Act
            var result = validator(cert, null, "test-peer", SslPolicyErrors.None, _log);

            // Assert
            Assert.True(result); // Should accept because valid thumbprint is in the list
        }

        [Fact(DisplayName = "PinnedCertificate should be case-insensitive for thumbprints")]
        public void PinnedCertificate_should_be_case_insensitive()
        {
            // Arrange
            var cert = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var thumbprint = cert.Thumbprint;

            // Test with lowercase thumbprint in allowed list
            var validator = CertificateValidation.PinnedCertificate(thumbprint.ToLower());

            // Act
            var result = validator(cert, null, "test-peer", SslPolicyErrors.None, _log);

            // Assert
            Assert.True(result); // Should accept due to case-insensitive comparison
        }

        [Fact(DisplayName = "PinnedCertificate should accept certificate with matching thumbprint from multiple allowed")]
        public void PinnedCertificate_should_accept_from_multiple_allowed()
        {
            // Arrange
            var cert = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var thumbprint = cert.Thumbprint;

            var validator = CertificateValidation.PinnedCertificate(
                "1111111111111111111111111111111111111111",
                thumbprint,
                "2222222222222222222222222222222222222222");

            // Act
            var result = validator(cert, null, "test-peer", SslPolicyErrors.None, _log);

            // Assert
            Assert.True(result);
        }

        [Fact(DisplayName = "PinnedCertificate should reject certificate with non-matching thumbprint")]
        public void PinnedCertificate_should_reject_non_matching_thumbprint()
        {
            // Arrange
            var cert = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var validator = CertificateValidation.PinnedCertificate(
                "1111111111111111111111111111111111111111",
                "2222222222222222222222222222222222222222");

            // Act & Assert
            EventFilter.Error(contains: "not in allowed list").ExpectOne(() =>
            {
                var result = validator(cert, null, "test-peer", SslPolicyErrors.None, _log);
                Assert.False(result);
            });
        }

        #endregion

        #region ValidateHostname Tests

        [Fact(DisplayName = "ValidateHostname should compare an explicitly supplied expected hostname")]
        public async Task ValidateHostname_should_compare_explicit_expected_hostname()
        {
            using var cert = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            var matchingValidator = CertificateValidation.ValidateHostname("AkkaTestClient");
            var rejectingValidator = CertificateValidation.ValidateHostname("unrelated.invalid");

            Assert.True(matchingValidator(cert, null, "test-peer", SslPolicyErrors.None, _log));
            await EventFilter.Error(contains: "expected 'unrelated.invalid'").ExpectOneAsync(() =>
            {
                Assert.False(rejectingValidator(cert, null, "test-peer", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname should reject a certificate that does not match the expected hostname even when SslStream reports no errors")]
        public async Task ValidateHostname_should_reject_mismatched_certificate_when_SslStream_reports_no_errors()
        {
            // This is the inbound case: SslStream has no target host for a client certificate, so it never
            // sets RemoteCertificateNameMismatch. Up to v1.5.74 the validator accepted any certificate here.
            using var cert = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var validator = CertificateValidation.ValidateHostname("expected.example");

            await EventFilter.Error(contains: "expected 'expected.example'").ExpectAsync(2, () =>
            {
                Assert.False(validator(cert, null, "actual.example", SslPolicyErrors.None, _log));
                Assert.False(validator(cert, null, "actual.example", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname should not warn that the expected hostname is unenforced")]
        public async Task ValidateHostname_should_not_warn_that_expected_hostname_is_unenforced()
        {
            using var cert = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            var validator = CertificateValidation.ValidateHostname("AkkaTestClient");

            await EventFilter.Warning().ExpectAsync(0, () =>
            {
                Assert.True(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname should accept a matching expected hostname even when SslStream reports a name mismatch")]
        public void ValidateHostname_should_accept_matching_expected_hostname_when_SslStream_reports_name_mismatch()
        {
            // The explicit name replaces the SslStream verdict, as it does on dev. A node dialed by IP
            // address whose certificate carries the expected DNS name must be accepted.
            using var cert = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            var validator = CertificateValidation.ValidateHostname("AkkaTestClient");

            Assert.True(validator(cert, null, "127.0.0.1", SslPolicyErrors.RemoteCertificateNameMismatch, _log));
        }

        [Fact(DisplayName = "ValidateHostname should match any DNS subject alternative name")]
        public void ValidateHostname_should_match_any_DNS_subject_alternative_name()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=client.internal", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            var subjectAlternativeNames = new SubjectAlternativeNameBuilder();
            subjectAlternativeNames.AddDnsName("first.internal");
            subjectAlternativeNames.AddDnsName("second.internal");
            request.CertificateExtensions.Add(subjectAlternativeNames.Build());
            using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1),
                DateTimeOffset.UtcNow.AddMinutes(5));
            var validator = CertificateValidation.ValidateHostname("second.internal");

            Assert.True(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
        }

        [Theory(DisplayName = "ValidateHostname should accept certificates that match the expected hostname")]
        [InlineData("CN=unrelated", "dns:node-1.cluster.local", "node-1.cluster.local")]
        [InlineData("CN=unrelated", "dns:node-1.cluster.local", "NODE-1.Cluster.Local")]
        [InlineData("CN=unrelated", "dns:node-1.cluster.local", "node-1.cluster.local.")]
        [InlineData("CN=unrelated", "dns:*.cluster.local", "node-1.cluster.local")]
        [InlineData("CN=unrelated", "dns:a.internal,dns:b.internal,ip:10.0.0.1", "b.internal")]
        [InlineData("CN=unrelated", "ip:10.0.0.1", "10.0.0.1")]
        [InlineData("CN=unrelated", "ip:2001:db8::1", "2001:db8::1")]
        [InlineData("CN=node-1, O=Akka", "", "node-1")]
        [InlineData("CN=node-1, O=Akka", "", "NODE-1")]
        public void ValidateHostname_should_accept_certificates_that_match_expected_hostname(
            string subject, string subjectAlternativeNames, string expectedHostname)
        {
            using var cert = HostnameTestCertificates.Create(subject, subjectAlternativeNames);
            var validator = CertificateValidation.ValidateHostname(expectedHostname);

            Assert.True(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
        }

        [Theory(DisplayName = "ValidateHostname should reject certificates that do not match the expected hostname")]
        [InlineData("CN=unrelated", "dns:node-1.cluster.local", "node-2.cluster.local")]
        [InlineData("CN=unrelated", "dns:*.cluster.local", "cluster.local")]
        [InlineData("CN=unrelated", "dns:*.cluster.local", "a.b.cluster.local")]
        [InlineData("CN=node-1", "dns:other.internal", "node-1")]
        [InlineData("CN=unrelated", "ip:10.0.0.1", "10.0.0.2")]
        [InlineData("CN=*.cluster.local", "", "node-1.cluster.local")]
        public async Task ValidateHostname_should_reject_certificates_that_do_not_match_expected_hostname(
            string subject, string subjectAlternativeNames, string expectedHostname)
        {
            using var cert = HostnameTestCertificates.Create(subject, subjectAlternativeNames);
            var validator = CertificateValidation.ValidateHostname(expectedHostname);

            await EventFilter.Error(contains: $"expected '{expectedHostname}'").ExpectOneAsync(() =>
            {
                Assert.False(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname without an expected hostname should use SslStream policy errors")]
        public async Task ValidateHostname_without_expected_hostname_should_use_policy_errors()
        {
            using var cert = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            var validator = CertificateValidation.ValidateHostname();

            Assert.True(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
            await EventFilter.Error(contains: "Hostname validation failed").ExpectOneAsync(() =>
            {
                Assert.False(validator(cert, null, "test-peer",
                    SslPolicyErrors.RemoteCertificateNameMismatch, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname should reject null certificate")]
        public async Task ValidateHostname_should_reject_null_certificate()
        {
            var withExpectedHostname = CertificateValidation.ValidateHostname("AkkaTestClient");
            var withoutExpectedHostname = CertificateValidation.ValidateHostname();

            await EventFilter.Error(contains: "certificate is null").ExpectAsync(2, () =>
            {
                Assert.False(withExpectedHostname(null, null, "test-peer", SslPolicyErrors.None, _log));
                Assert.False(withoutExpectedHostname(null, null, "test-peer", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname should reject an invalid explicit hostname")]
        public async Task ValidateHostname_should_reject_invalid_explicit_hostname()
        {
            using var cert = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            var validator = CertificateValidation.ValidateHostname("not a valid hostname!");

            await EventFilter.Error(contains: "unable to validate expected hostname").ExpectOneAsync(() =>
            {
                Assert.False(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        [Fact(DisplayName = "ValidateHostname should reject a certificate that can no longer be read")]
        public async Task ValidateHostname_should_reject_certificate_that_can_no_longer_be_read()
        {
            var cert = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            cert.Dispose();
            var validator = CertificateValidation.ValidateHostname("AkkaTestClient");

            await EventFilter.Error(contains: "unable to validate expected hostname").ExpectOneAsync(() =>
            {
                Assert.False(validator(cert, null, "test-peer", SslPolicyErrors.None, _log));
                return Task.CompletedTask;
            });
        }

        #endregion

        #region ValidateSubject Tests

        [Fact(DisplayName = "ValidateSubject should reject null certificate")]
        public void ValidateSubject_should_reject_null_certificate()
        {
            // Arrange
            var validator = CertificateValidation.ValidateSubject("CN=TestSubject");

            // Act & Assert
            EventFilter.Error(contains: "certificate is null").ExpectOne(() =>
            {
                var result = validator(null, null, "test-peer", SslPolicyErrors.None, _log);
                Assert.False(result);
            });
        }

        [Fact(DisplayName = "ValidateSubject should throw if pattern is null or empty")]
        public void ValidateSubject_should_throw_if_pattern_null_or_empty()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => CertificateValidation.ValidateSubject(null));
            Assert.Throws<ArgumentException>(() => CertificateValidation.ValidateSubject(""));
            Assert.Throws<ArgumentException>(() => CertificateValidation.ValidateSubject("  "));
        }

        #endregion

        #region ValidateIssuer Tests

        [Fact(DisplayName = "ValidateIssuer should reject null certificate")]
        public void ValidateIssuer_should_reject_null_certificate()
        {
            // Arrange
            var validator = CertificateValidation.ValidateIssuer("CN=TestIssuer");

            // Act & Assert
            EventFilter.Error(contains: "certificate is null").ExpectOne(() =>
            {
                var result = validator(null, null, "test-peer", SslPolicyErrors.None, _log);
                Assert.False(result);
            });
        }

        [Fact(DisplayName = "ValidateIssuer should throw if pattern is null or empty")]
        public void ValidateIssuer_should_throw_if_pattern_null_or_empty()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => CertificateValidation.ValidateIssuer(null));
            Assert.Throws<ArgumentException>(() => CertificateValidation.ValidateIssuer(""));
            Assert.Throws<ArgumentException>(() => CertificateValidation.ValidateIssuer("  "));
        }

        #endregion

        #region Combine Tests

        [Fact(DisplayName = "Combine should handle null validators array")]
        public void Combine_should_handle_null_validators()
        {
            // Act & Assert - Should throw ArgumentException
            Assert.Throws<ArgumentException>(() => CertificateValidation.Combine(null));
        }

        [Fact(DisplayName = "Combine should handle empty validators array")]
        public void Combine_should_handle_empty_validators()
        {
            // Act & Assert - Should throw ArgumentException
            Assert.Throws<ArgumentException>(() => CertificateValidation.Combine());
            Assert.Throws<ArgumentException>(() => CertificateValidation.Combine(new CertificateValidationCallback[0]));
        }

        [Fact(DisplayName = "Combine should short-circuit on first failure")]
        public void Combine_should_short_circuit_on_first_failure()
        {
            // Arrange
            var callCount = 0;
            CertificateValidationCallback validator1 = (cert, chain, peer, errors, log) =>
            {
                callCount++;
                log.Error("First validator failed");
                return false; // Fail
            };
            CertificateValidationCallback validator2 = (cert, chain, peer, errors, log) =>
            {
                callCount++;
                log.Error("Second validator should never be reached");
                return true; // This should never be called
            };

            var combined = CertificateValidation.Combine(validator1, validator2);
            var cert = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Act & Assert
            EventFilter.Error(contains: "First validator failed").ExpectOne(() =>
            {
                var result = combined(cert, null, "test-peer", SslPolicyErrors.None, _log);
                Assert.False(result);
                Assert.Equal(1, callCount); // Only first validator should be called - short-circuit behavior
            });
        }

        #endregion
    }
}
