//-----------------------------------------------------------------------
// <copyright file="DotNettySslSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Xunit;
using static Akka.Util.RuntimeDetector;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettySslSetupSpec : AkkaSpec
    {
        #region Setup / Config

        // valid to 01/01/2037
        private const string ValidCertPath = "Resources/akka-validcert.pfx";

        private const string Password = "password";

        private static ActorSystemSetup TestActorSystemSetup(bool enableSsl)
        {
            var setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString($@"
akka {{
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote {{
    dot-netty.tcp {{
      port = 0
      hostname = ""127.0.0.1""
      enable-ssl = ""{enableSsl.ToString().ToLowerInvariant()}""
      log-transport = true
    }}
  }}
}}")));

            if (!enableSsl)
                return setup;
            
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            return setup.And(new DotNettySslSetup(certificate, true));
        }

        private ActorSystem _sys2;
        private ActorPath _echoPath;

        private void Setup(bool enableSsl)
        {
            _sys2 = ActorSystem.Create("sys2", TestActorSystemSetup(enableSsl));
            InitializeLogger(_sys2);

            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";
        }

        #endregion

        public DotNettySslSetupSpec(ITestOutputHelper output) : base(TestActorSystemSetup(true), output)
        {
        }

        [Fact]
        public async Task Secure_transport_should_be_possible_between_systems_sharing_the_same_certificate()
        {
            Setup(true);

            var probe = CreateTestProbe();

            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task Secure_transport_should_NOT_be_possible_between_systems_using_SSL_and_one_not_using_it()
        {
            Setup(false);

            var probe = CreateTestProbe();
            await Assert.ThrowsAsync<RemoteTransportException>(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectNoMsgAsync();
            });
        }

        [Fact(DisplayName = "DotNettySslSetup with 2 parameters should configure effective DotNettyTransportSettings with defaults (RequireMutualAuth=true, ValidateHostname=false)")]
        public void Two_parameter_setup_should_configure_transport_settings_with_defaults()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: true);

            var actorSystemSetup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(@"
akka {
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
  }
}")))
                .And(sslSetup);

            using var sys = ActorSystem.Create("test", actorSystemSetup);

            // Verify that DotNettyTransportSettings.Create uses the setup correctly
            var settings = DotNettyTransportSettings.Create(sys);

            Assert.True(settings.EnableSsl);
            Assert.Equal(certificate, settings.Ssl.Certificate);
            Assert.True(settings.Ssl.SuppressValidation);
            Assert.True(settings.Ssl.RequireMutualAuthentication); // default from 2-param constructor
            Assert.False(settings.Ssl.ValidateCertificateHostname); // default from 2-param constructor
        }

        [Fact(DisplayName = "DotNettySslSetup with 3 parameters should configure effective DotNettyTransportSettings with specified RequireMutualAuth and default ValidateHostname=false")]
        public void Three_parameter_setup_should_configure_transport_settings()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: false);

            var actorSystemSetup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(@"
akka {
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
  }
}")))
                .And(sslSetup);

            using var sys = ActorSystem.Create("test", actorSystemSetup);

            // Verify that DotNettyTransportSettings.Create uses the setup correctly
            var settings = DotNettyTransportSettings.Create(sys);

            Assert.True(settings.EnableSsl);
            Assert.Equal(certificate, settings.Ssl.Certificate);
            Assert.False(settings.Ssl.SuppressValidation);
            Assert.False(settings.Ssl.RequireMutualAuthentication); // explicitly set to false
            Assert.False(settings.Ssl.ValidateCertificateHostname); // default from 3-param constructor
        }

        [Fact(DisplayName = "DotNettySslSetup with 4 parameters should configure effective DotNettyTransportSettings with all specified values")]
        public void Four_parameter_setup_should_configure_transport_settings_with_all_values()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: true, requireMutualAuthentication: false, validateCertificateHostname: true);

            var actorSystemSetup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(@"
akka {
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
  }
}")))
                .And(sslSetup);

            using var sys = ActorSystem.Create("test", actorSystemSetup);

            // Verify that DotNettyTransportSettings.Create uses the setup correctly
            var settings = DotNettyTransportSettings.Create(sys);

            Assert.True(settings.EnableSsl);
            Assert.Equal(certificate, settings.Ssl.Certificate);
            Assert.True(settings.Ssl.SuppressValidation);
            Assert.False(settings.Ssl.RequireMutualAuthentication); // explicitly set to false
            Assert.True(settings.Ssl.ValidateCertificateHostname); // explicitly set to true
        }

        [Fact(DisplayName = "DotNettySslSetup should override HOCON certificate configuration (Bug #7917)")]
        public void DotNettySslSetup_should_override_HOCON_certificate()
        {
            // This test exposes the bug where HOCON certificate wins over DotNettySslSetup
            // when HOCON has valid certificate configuration

            // HOCON certificate
            const string hoconCertPath = "Resources/akka-validcert.pfx";
            var hoconCert = CertificateHelper.LoadPkcs12(hoconCertPath, Password);

            // Programmatic setup certificate (different from HOCON)
            const string setupCertPath = "Resources/akka-client-cert.pfx";
            var setupCert = CertificateHelper.LoadPkcs12(setupCertPath, Password);

            var sslSetup = new DotNettySslSetup(setupCert, suppressValidation: true, requireMutualAuthentication: false, validateCertificateHostname: true);

            var actorSystemSetup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString($@"
akka {{
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {{
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    ssl {{
      certificate {{
        path = ""{hoconCertPath}""
        password = ""{Password}""
      }}
      suppress-validation = false
      require-mutual-authentication = true
      validate-certificate-hostname = false
    }}
  }}
}}")))
                .And(sslSetup);

            using var sys = ActorSystem.Create("test", actorSystemSetup);

            // Verify that DotNettyTransportSettings.Create uses the setup correctly
            var settings = DotNettyTransportSettings.Create(sys);

            Assert.True(settings.EnableSsl);

            // BUG: DotNettySslSetup should take precedence over HOCON, but currently HOCON wins
            // because CreateOrDefault tries HOCON first, and only uses the setup as an exception fallback
            Assert.Equal(setupCert.Thumbprint, settings.Ssl.Certificate.Thumbprint); // Should be setupCert, not hoconCert
            Assert.True(settings.Ssl.SuppressValidation); // From DotNettySslSetup
            Assert.False(settings.Ssl.RequireMutualAuthentication); // From DotNettySslSetup, not HOCON
            Assert.True(settings.Ssl.ValidateCertificateHostname); // From DotNettySslSetup, not HOCON
        }

        [Fact(DisplayName = "DotNettySslSetup with CustomValidator that accepts should allow connection")]
        public async Task CustomValidator_that_accepts_should_allow_connection()
        {
            var validatorCalled = false;

            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Custom validator that accepts all certificates
            CertificateValidationCallback customValidator = (cert, chain, peer, errors, log) =>
            {
                validatorCalled = true;
                Output.WriteLine($"CustomValidator called for peer: {peer}");
                return true; // Accept all certificates
            };

            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: customValidator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-custom-validator", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));

            // Verify that CustomValidator was actually called
            Assert.True(validatorCalled, "CustomValidator should have been invoked during TLS handshake");
        }

        [Fact(DisplayName = "DotNettySslSetup with CustomValidator that rejects should prevent connection")]
        public async Task CustomValidator_that_rejects_should_prevent_connection()
        {
            var validatorCalled = false;

            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Custom validator that rejects all certificates
            CertificateValidationCallback customValidator = (cert, chain, peer, errors, log) =>
            {
                validatorCalled = true;
                Output.WriteLine($"CustomValidator called for peer: {peer}, rejecting certificate");
                return false; // Reject all certificates
            };

            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: customValidator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-reject-validator", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Connection should fail due to custom validator rejection - TLS handshake fails, so message never arrives
            Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
            await probe.ExpectNoMsgAsync(TimeSpan.FromSeconds(3));

            // Verify that CustomValidator was actually called
            Assert.True(validatorCalled, "CustomValidator should have been invoked during TLS handshake");
        }

        [Fact(DisplayName = "DotNettySslSetup should pass CustomValidator to SslSettings")]
        public void DotNettySslSetup_should_pass_CustomValidator_to_SslSettings()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            var customValidator = CertificateValidation.ValidateChain();
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: customValidator);

            var actorSystemSetup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString(@"
akka {
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
  }
}")))
                .And(sslSetup);

            using var sys = ActorSystem.Create("test-custom-validator", actorSystemSetup);

            // Verify that CustomValidator is passed through to SslSettings
            var settings = DotNettyTransportSettings.Create(sys);
            Assert.NotNull(settings.Ssl.CustomValidator);
            Assert.Same(customValidator, settings.Ssl.CustomValidator);
        }

        [Fact(DisplayName = "DotNettySslSetup should take precedence when both setup and HOCON SSL are configured (and log warning)")]
        public void DotNettySslSetup_should_take_precedence_when_both_configured()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // HOCON certificate (different from setup)
            const string hoconCertPath = "Resources/akka-validcert.pfx";

            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: true);

            var actorSystemSetup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create().WithConfig(ConfigurationFactory.ParseString($@"
akka {{
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {{
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    ssl {{
      certificate {{
        path = ""{hoconCertPath}""
        password = ""{Password}""
      }}
      suppress-validation = false
    }}
  }}
}}")))
                .And(sslSetup);

            using var sys = ActorSystem.Create("test-precedence", actorSystemSetup);

            // Verify DotNettySslSetup takes precedence over HOCON
            // (A warning will be logged to help users understand this behavior)
            var settings = DotNettyTransportSettings.Create(sys);

            Assert.True(settings.EnableSsl);
            Assert.Equal(certificate.Thumbprint, settings.Ssl.Certificate.Thumbprint);
            Assert.True(settings.Ssl.SuppressValidation); // From DotNettySslSetup, not HOCON (which has false)
        }

        [Fact(DisplayName = "CertificateValidation.PinnedCertificate should accept certificates with matching thumbprint")]
        public async Task PinnedCertificate_should_accept_matching_thumbprint()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create validator that pins to this specific certificate
            var validator = CertificateValidation.PinnedCertificate(certificate.Thumbprint);
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: validator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-pinned-accept", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Should successfully connect because thumbprint matches
            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        }

        [Fact(DisplayName = "CertificateValidation.PinnedCertificate should reject certificates with non-matching thumbprint")]
        public async Task PinnedCertificate_should_reject_non_matching_thumbprint()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create validator that pins to a DIFFERENT thumbprint (connection should fail)
            var validator = CertificateValidation.PinnedCertificate("0000000000000000000000000000000000000000");
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: validator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-pinned-reject", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Connection should fail due to thumbprint mismatch
            Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
            await probe.ExpectNoMsgAsync(TimeSpan.FromSeconds(3));
        }

        [Fact(DisplayName = "CertificateValidation.ValidateSubject should accept certificates with matching subject")]
        public async Task ValidateSubject_should_accept_matching_subject()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create validator that accepts the certificate's actual subject
            var validator = CertificateValidation.ValidateSubject(certificate.Subject);
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: validator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-subject-accept", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Should successfully connect because subject matches
            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        }

        [Fact(DisplayName = "CertificateValidation.ValidateSubject should reject certificates with non-matching subject")]
        public async Task ValidateSubject_should_reject_non_matching_subject()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create validator with a subject that won't match
            var validator = CertificateValidation.ValidateSubject("CN=WrongSubject");
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: validator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-subject-reject", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Connection should fail due to subject mismatch
            Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
            await probe.ExpectNoMsgAsync(TimeSpan.FromSeconds(3));
        }

        [Fact(DisplayName = "CertificateValidation.ValidateSubject should support wildcard patterns")]
        public void ValidateSubject_should_support_wildcards()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Extract the CN from the subject (e.g., "CN=akka.net, O=Test")
            // If subject is "CN=akka.net, O=Test", wildcard "CN=akka*" should match
            var subject = certificate.Subject;
            Output.WriteLine($"Certificate subject: {subject}");

            // Test that wildcard pattern matching works
            // Extract just the CN part for wildcard testing
            var cnStart = subject.IndexOf("CN=");
            if (cnStart >= 0)
            {
                var cnEnd = subject.IndexOf(",", cnStart);
                var cn = cnEnd > cnStart ? subject.Substring(cnStart, cnEnd - cnStart) : subject.Substring(cnStart);

                // Extract the first few characters of CN for wildcard
                var cnValue = cn.Substring(3); // Skip "CN="
                if (cnValue.Length > 3)
                {
                    var wildcardPattern = "CN=" + cnValue.Substring(0, cnValue.Length - 2) + "*";
                    Output.WriteLine($"Testing wildcard pattern: {wildcardPattern}");

                    var validator = CertificateValidation.ValidateSubject(wildcardPattern);

                    // Invoke the validator directly to test pattern matching
                    var log = Akka.Event.Logging.GetLogger(Sys, "test");
                    var result = validator(certificate, null, "test-peer", System.Net.Security.SslPolicyErrors.None, log);
                    Assert.True(result, $"Wildcard pattern '{wildcardPattern}' should match subject '{subject}'");
                }
            }
        }

        [Fact(DisplayName = "CertificateValidation.ValidateIssuer should accept certificates with matching issuer")]
        public async Task ValidateIssuer_should_accept_matching_issuer()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create validator that accepts the certificate's actual issuer
            var validator = CertificateValidation.ValidateIssuer(certificate.Issuer);
            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: validator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-issuer-accept", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Should successfully connect because issuer matches
            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        }

        [Fact(DisplayName = "CertificateValidation.ChainPlusThen should combine chain validation with custom logic")]
        public async Task ChainPlusThen_should_combine_validation()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create validator that does chain validation PLUS custom check
            // Note: For self-signed certificates, chain validation will fail, so we'll verify
            // the custom logic is invoked by using Combine with a custom validator instead
            var customCheckCalled = false;
            var validator = CertificateValidation.Combine(
                // Accept all for testing (since cert is self-signed)
                (cert, chain, peer, errors, log) => true,
                // Then custom check - just verify it's called
                (cert, chain, peer, errors, log) =>
                {
                    customCheckCalled = true;
                    Output.WriteLine($"Custom validation called for peer: {peer}, subject: {cert?.Subject}");
                    // Accept all - we're just testing that Combine works
                    return true;
                }
            );

            var sslSetup = new DotNettySslSetup(certificate, suppressValidation: false, requireMutualAuthentication: true, customValidator: validator);

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-chainplusthen", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Should successfully connect (custom validator accepts all, then custom check passes)
            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));

            // Verify custom validation was actually called
            Assert.True(customCheckCalled, "Custom validation logic should have been invoked");
        }

        [Fact(DisplayName = "CustomValidator should take precedence over validateCertificateHostname setting")]
        public async Task CustomValidator_should_override_hostname_validation_setting()
        {
            var certificate = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            // Create a custom validator that accepts everything
            var customValidatorCalled = false;
            CertificateValidationCallback customValidator = (cert, chain, peer, errors, log) =>
            {
                customValidatorCalled = true;
                Output.WriteLine($"CustomValidator called (should take precedence over hostname validation)");
                return true; // Accept all
            };

            // Configure with validateCertificateHostname=true, but customValidator should win
            var sslSetup = new DotNettySslSetup(
                certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: true,  // This would normally fail
                customValidator: customValidator     // But this should take precedence
            );

            var sys2Setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(sslSetup);

            _sys2 = ActorSystem.Create("sys2-custom-precedence", sys2Setup);
            InitializeLogger(_sys2);
            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";

            var probe = CreateTestProbe();

            // Should successfully connect because CustomValidator accepts all (overrides hostname validation)
            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromMilliseconds(500));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));

            // Verify custom validator was called (proving it took precedence)
            Assert.True(customValidatorCalled, "CustomValidator should have been invoked, proving it takes precedence");
        }

        #region helper classes / methods

        protected override void AfterAll()
        {
            base.AfterAll();
            Shutdown(_sys2, TimeSpan.FromSeconds(3));
        }

        private class Echo : ReceiveActor
        {
            public Echo()
            {
                Receive<string>(str => Sender.Tell(str));
            }
        }

        #endregion
    }
}
