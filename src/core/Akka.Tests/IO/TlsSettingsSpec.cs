// -----------------------------------------------------------------------
// <copyright file="TlsSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.IO;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.IO
{
    public class TlsSettingsSpec : AkkaSpec
    {
        public TlsSettingsSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Fact(DisplayName = "Should_Create_Fresh_Options_And_Not_Select_Certificates_Without_Mutual_Authentication")]
        public void Should_Create_Fresh_Options_And_Not_Select_Certificates_Without_Mutual_Authentication()
        {
            using var certificate = CreateCertificate("localhost", server: false);
            var settings = new TlsClientSettings(certificate) { RequireMutualAuthentication = false };
            var first = settings.CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log);
            var second = settings.CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log);

            Assert.NotSame(first, second);
            Assert.Null(first.ClientCertificates);
            Assert.Null(first.LocalCertificateSelectionCallback);
            Assert.Equal("server.example", first.TargetHost);
        }

        [Fact(DisplayName = "Should_Require_A_Client_Certificate_When_Mutual_Authentication_Is_Enabled")]
        public void Should_Require_A_Client_Certificate_When_Mutual_Authentication_Is_Enabled()
        {
            var settings = new TlsClientSettings();

            Assert.Throws<InvalidOperationException>(() =>
                settings.CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log));
        }

        [Fact(DisplayName = "Should_Ignore_Chain_Errors_Independently_From_Hostname_Errors")]
        public void Should_Ignore_Chain_Errors_Independently_From_Hostname_Errors()
        {
            using var serverCertificate = CreateCertificate("server.example", server: true);
            var settings = new TlsClientSettings
            {
                RequireMutualAuthentication = false,
                SuppressValidation = true,
                ValidateCertificateHostname = true
            };
            var validation = settings.CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log)
                .RemoteCertificateValidationCallback!;

            Assert.True(validation(Sys, serverCertificate, null,
                SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(validation(Sys, serverCertificate, null,
                SslPolicyErrors.RemoteCertificateNameMismatch));
            Assert.False(validation(Sys, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));

            var hostnameDisabled = new TlsClientSettings { RequireMutualAuthentication = false, SuppressValidation = true }
                .CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log).RemoteCertificateValidationCallback!;
            Assert.True(hostnameDisabled(Sys, serverCertificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        }

        [Fact(DisplayName = "Should_Allow_Custom_Validation_To_Override_Built_In_Policy_But_Reject_Missing_Mutual_Certificate_First")]
        public void Should_Allow_Custom_Validation_To_Override_Built_In_Policy_But_Reject_Missing_Mutual_Certificate_First()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var customCalls = 0;
            var settings = new TlsServerSettings(serverCertificate)
            {
                CustomValidator = (_, _, _, _, _) =>
                {
                    customCalls++;
                    return true;
                }
            };
            var validation = settings.CreateAuthenticationOptions("127.0.0.1:1234", Sys.Log)
                .RemoteCertificateValidationCallback!;

            Assert.False(validation(Sys, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
            Assert.Equal(0, customCalls);
            Assert.True(validation(Sys, serverCertificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.Equal(1, customCalls);
        }

        [Fact(DisplayName = "Should_Allow_Custom_Client_Validation_To_Override_Chain_And_Hostname_Flags")]
        public void Should_allow_custom_client_validation_to_override_chain_and_hostname_flags()
        {
            using var serverCertificate = CreateCertificate("localhost", server: true);
            var customCalls = 0;
            var validation = new TlsClientSettings
            {
                RequireMutualAuthentication = false,
                SuppressValidation = true,
                ValidateCertificateHostname = true,
                CustomValidator = (_, _, _, errors, _) =>
                {
                    customCalls++;
                    return errors == (SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch);
                }
            }
                .CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log)
                .RemoteCertificateValidationCallback!;

            Assert.True(validation(Sys, serverCertificate, null,
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
            Assert.Equal(1, customCalls);
        }

        [Fact(DisplayName = "Should_Reject_Nonpositive_Handshake_Timeouts")]
        public void Should_reject_nonpositive_handshake_timeouts()
        {
            var settings = new TlsClientSettings { RequireMutualAuthentication = false, HandshakeTimeout = TimeSpan.Zero };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                settings.CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log));
        }

        [Fact(DisplayName = "Should_Reject_Handshake_Timeouts_Beyond_Cancellation_Timer_Range")]
        public void Should_reject_handshake_timeouts_beyond_cancellation_timer_range()
        {
            var settings = new TlsClientSettings
            {
                RequireMutualAuthentication = false,
                HandshakeTimeout = TimeSpan.FromMilliseconds(uint.MaxValue)
            };

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                settings.CreateAuthenticationOptions("server.example", "127.0.0.1:1234", Sys.Log));
        }

        [Fact(DisplayName = "Should_Reject_Server_Certificates_Without_A_Private_Key")]
        public void Should_reject_server_certificates_without_a_private_key()
        {
            using var certificateWithKey = CreateCertificate("localhost", server: true);
            using var publicCertificate = X509CertificateLoader.LoadCertificate(certificateWithKey.RawData);
            var settings = new TlsServerSettings(publicCertificate);

            Assert.Throws<ArgumentException>(() => settings.CreateAuthenticationOptions("127.0.0.1:1234", Sys.Log));
        }

        private static X509Certificate2 CreateCertificate(string host, bool server)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest($"CN={host}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName(host);
            request.CertificateExtensions.Add(san.Build());
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            var usages = server ? X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment : X509KeyUsageFlags.DigitalSignature;
            request.CertificateExtensions.Add(new X509KeyUsageExtension(usages, true));
            var eku = new OidCollection
            {
                new Oid(server ? "1.3.6.1.5.5.7.3.1" : "1.3.6.1.5.5.7.3.2")
            };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(eku, true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
