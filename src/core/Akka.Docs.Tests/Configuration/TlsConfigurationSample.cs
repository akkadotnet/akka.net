//-----------------------------------------------------------------------
// <copyright file="TlsConfigurationSample.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;

namespace Akka.Docs.Tests.Configuration
{
    /// <summary>
    /// TLS configuration examples for Akka.Remote documentation
    /// </summary>
    public class TlsConfigurationSample
    {
        #region MutualTlsConfig
        public static Config MutualTlsConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = false
                    require-mutual-authentication = true  # Both client and server authenticate
                    certificate {
                        path = ""path/to/certificate.pfx""
                        password = ""certificate-password""
                    }
                }
            }
        ");
        #endregion

        #region StandardTlsConfig
        public static Config StandardTlsConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = false
                    require-mutual-authentication = false  # Server authentication only
                    certificate {
                        path = ""path/to/certificate.pfx""
                        password = ""certificate-password""
                    }
                }
            }
        ");
        #endregion

        #region WindowsCertStoreConfig
        public static Config WindowsCertificateStoreConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = false
                    require-mutual-authentication = true
                    certificate {
                        use-thumbprint-over-file = true
                        thumbprint = ""2531c78c51e5041d02564697a88af8bc7a7ce3e3""
                        store-name = ""My""
                        store-location = ""local-machine""  # or ""current-user""
                    }
                }
            }
        ");
        #endregion

        #region DevTlsConfig
        // WARNING: Development only - never use suppress-validation = true in production!
        public static Config DevelopmentTlsConfiguration = ConfigurationFactory.ParseString(@"
            akka.remote.dot-netty.tcp {
                enable-ssl = true
                ssl {
                    suppress-validation = true  # INSECURE: Accepts any certificate
                    require-mutual-authentication = false
                    certificate {
                        path = ""self-signed-dev-cert.pfx""
                        password = ""password""
                    }
                }
            }
        ");
        #endregion

        #region ProgrammaticMutualTlsSetup
        /// <summary>
        /// Example of programmatic mutual TLS setup using DotNettySslSetup with custom validation.
        /// This allows full programmatic control over certificate validation logic.
        /// </summary>
        public static BootstrapSetup ProgrammaticMutualTlsSetup()
        {
            // Load or obtain your certificate
            var certificate = new X509Certificate2("path/to/certificate.pfx", "password");

            // Create custom validator combining multiple validation strategies
            var customValidator = CertificateValidation.Combine(
                // Validate the certificate chain
                CertificateValidation.ValidateChain(null),
                // Also pin against known thumbprints for additional security
                CertificateValidation.PinnedCertificate(new[] { certificate.Thumbprint }, null)
            );

            // Setup SSL with custom validator taking precedence over HOCON config
            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: customValidator
            );

            return new BootstrapSetup().And(sslSetup);
        }
        #endregion

        #region CertificatePinningExample
        /// <summary>
        /// Example of certificate pinning - only accept certificates with specific thumbprints.
        /// Useful for preventing man-in-the-middle attacks with compromised CAs.
        /// </summary>
        public static BootstrapSetup CertificatePinningSetup()
        {
            var certificate = new X509Certificate2("path/to/certificate.pfx", "password");

            // Allow only specific certificates by thumbprint
            var allowedThumbprints = new[]
            {
                "2531c78c51e5041d02564697a88af8bc7a7ce3e3",  // Production cert
                "abc123def456789ghi012jkl345mno678pqr901stu"  // Backup cert
            };

            var validator = CertificateValidation.PinnedCertificate(allowedThumbprints, null);

            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: validator
            );

            return new BootstrapSetup().And(sslSetup);
        }
        #endregion

        #region CustomValidationLogicExample
        /// <summary>
        /// Example of custom certificate validation logic combined with standard validation.
        /// Allows complete control over what certificates are accepted.
        /// </summary>
        public static BootstrapSetup CustomValidationLogicSetup()
        {
            var certificate = new X509Certificate2("path/to/certificate.pfx", "password");

            // Start with standard chain validation, then add custom logic
            var validator = CertificateValidation.ChainPlusThen(
                // First, validate the certificate chain
                (cert, chain, peer, log) =>
                {
                    // Then apply custom logic - e.g., check certificate attributes
                    if (cert?.Subject != null && cert.Subject.Contains("CN=authorized-peer"))
                    {
                        return true;  // Accept this certificate
                    }
                    return false;  // Reject all others
                },
                null
            );

            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: validator
            );

            return new BootstrapSetup().And(sslSetup);
        }
        #endregion

        #region HostnameValidationExample
        /// <summary>
        /// Example of enabling traditional hostname validation for client-server architectures.
        /// Use when all nodes share the same certificate with matching CN/SAN.
        /// </summary>
        public static BootstrapSetup HostnameValidationSetup()
        {
            var certificate = new X509Certificate2("path/to/certificate.pfx", "password");

            // Enable both chain validation and hostname validation
            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                validateCertificateHostname: true  // Enable traditional TLS hostname validation
            );

            return new BootstrapSetup().And(sslSetup);
        }
        #endregion

        #region SubjectValidationExample
        /// <summary>
        /// Example of subject DN validation - only accept certificates with specific subject names.
        /// Useful for verifying peer identity based on certificate subject.
        /// </summary>
        public static BootstrapSetup SubjectValidationSetup()
        {
            var certificate = new X509Certificate2("path/to/certificate.pfx", "password");

            // Only accept certificates with specific subject names
            var subjectPatterns = new[]
            {
                "CN=node1.example.com",
                "CN=node2.example.com",
                "CN=node3.example.com"
            };

            var validator = CertificateValidation.ValidateSubject(subjectPatterns, null);

            var sslSetup = new DotNettySslSetup(
                certificate: certificate,
                suppressValidation: false,
                requireMutualAuthentication: true,
                customValidator: validator
            );

            return new BootstrapSetup().And(sslSetup);
        }
        #endregion
    }
}