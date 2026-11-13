//-----------------------------------------------------------------------
// <copyright file="DotNettySslSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor.Setup;
using Akka.IO;

namespace Akka.Remote.Transport.DotNetty;

/// <summary>
/// Programmatic setup for DotNetty SSL/TLS configuration.
/// Provides a fluent API alternative to HOCON configuration.
/// </summary>
public sealed class DotNettySslSetup : Setup
{
    /// <summary>
    /// Constructor for backward compatibility - defaults to RequireMutualAuthentication = true, ValidateCertificateHostname = false
    /// </summary>
    /// <param name="certificate">X509 certificate used to establish SSL/TLS</param>
    /// <param name="suppressValidation">When true, suppresses certificate chain validation (use only for development/testing)</param>
    public DotNettySslSetup(X509Certificate2 certificate, bool suppressValidation)
        : this(certificate, suppressValidation, requireMutualAuthentication: true, validateCertificateHostname: false, customValidator: null)
    {
    }

    /// <summary>
    /// Constructor for backward compatibility - defaults to ValidateCertificateHostname = false
    /// </summary>
    /// <param name="certificate">X509 certificate used to establish SSL/TLS</param>
    /// <param name="suppressValidation">When true, suppresses certificate chain validation (use only for development/testing)</param>
    /// <param name="requireMutualAuthentication">When true, requires mutual TLS authentication (both client and server present certificates)</param>
    public DotNettySslSetup(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication)
        : this(certificate, suppressValidation, requireMutualAuthentication, validateCertificateHostname: false, customValidator: null)
    {
    }

    /// <summary>
    /// Full constructor with all SSL/TLS configuration options
    /// </summary>
    /// <param name="certificate">X509 certificate used to establish SSL/TLS</param>
    /// <param name="suppressValidation">When true, suppresses certificate chain validation (use only for development/testing)</param>
    /// <param name="requireMutualAuthentication">When true, requires mutual TLS authentication (both client and server present certificates)</param>
    /// <param name="validateCertificateHostname">When true, enables outbound hostname validation (server certificate CN/SAN must match target hostname)</param>
    public DotNettySslSetup(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname)
        : this(certificate, suppressValidation, requireMutualAuthentication, validateCertificateHostname, customValidator: null)
    {
    }

    /// <summary>
    /// Constructor with custom certificate validation callback
    /// </summary>
    /// <param name="certificate">X509 certificate used to establish SSL/TLS</param>
    /// <param name="suppressValidation">When true, suppresses certificate chain validation (use only for development/testing)</param>
    /// <param name="requireMutualAuthentication">When true, requires mutual TLS authentication (both client and server present certificates)</param>
    /// <param name="customValidator">Custom certificate validation callback (overrides config-based validation when provided)</param>
    public DotNettySslSetup(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication, CertificateValidationCallback? customValidator)
        : this(certificate, suppressValidation, requireMutualAuthentication, validateCertificateHostname: false, customValidator)
    {
    }

    /// <summary>
    /// Full constructor with all SSL/TLS configuration options including custom validation
    /// </summary>
    /// <param name="certificate">X509 certificate used to establish SSL/TLS</param>
    /// <param name="suppressValidation">When true, suppresses certificate chain validation (use only for development/testing)</param>
    /// <param name="requireMutualAuthentication">When true, requires mutual TLS authentication (both client and server present certificates)</param>
    /// <param name="validateCertificateHostname">When true, enables outbound hostname validation (server certificate CN/SAN must match target hostname)</param>
    /// <param name="customValidator">Custom certificate validation callback (overrides config-based validation when provided)</param>
    public DotNettySslSetup(X509Certificate2 certificate, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname, CertificateValidationCallback? customValidator)
    {
        _certificate = certificate;
        SuppressValidation = suppressValidation;
        RequireMutualAuthentication = requireMutualAuthentication;
        ValidateCertificateHostname = validateCertificateHostname;
        CustomValidator = customValidator;
    }

    private DotNettySslSetup(TlsCertificateSource source, bool suppressValidation, bool requireMutualAuthentication, bool validateCertificateHostname, CertificateValidationCallback? customValidator)
    {
        _source = source;
        SuppressValidation = suppressValidation;
        RequireMutualAuthentication = requireMutualAuthentication;
        ValidateCertificateHostname = validateCertificateHostname;
        CustomValidator = customValidator;
    }

    private readonly X509Certificate2? _certificate;
    private readonly TlsCertificateSource? _source;

    /// <summary>
    /// Creates a setup whose certificate comes from a <see cref="TlsCertificateSource"/>. The node asks the
    /// source each time it builds a TLS connection, so a certificate added to the source is used by new
    /// connections without a restart. The other arguments mean what they mean on the full constructor.
    /// A source only uses a certificate that is allowed for the job: with
    /// <paramref name="requireMutualAuthentication"/> true, both server and client authentication. The
    /// constructors that take a certificate do not check this.
    /// </summary>
    /// <param name="source">Supplies the certificate the node presents.</param>
    /// <param name="suppressValidation">When true, suppresses certificate chain validation (use only for development/testing)</param>
    /// <param name="requireMutualAuthentication">When true, requires mutual TLS authentication (both client and server present certificates)</param>
    /// <param name="validateCertificateHostname">When true, enables outbound hostname validation (server certificate CN/SAN must match target hostname)</param>
    /// <param name="customValidator">Custom certificate validation callback (overrides config-based validation when provided)</param>
    public static DotNettySslSetup FromSource(
        TlsCertificateSource source,
        bool suppressValidation,
        bool requireMutualAuthentication,
        bool validateCertificateHostname,
        CertificateValidationCallback? customValidator)
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));

        return new DotNettySslSetup(source, suppressValidation, requireMutualAuthentication, validateCertificateHostname, customValidator);
    }

    /// <summary>
    /// X509 certificate used to establish Secure Socket Layer (SSL) between two remote endpoints.
    /// For a setup made with <see cref="FromSource"/>, this is the source's current pick: a certificate that allows
    /// both client and server authentication when mutual TLS is on, or one that allows server authentication when it is off.
    /// Reading it throws <see cref="InvalidOperationException"/> when the source has no usable certificate, or has
    /// none yet because its look-up is still running: reading never waits for a look-up, and starts one when it is due.
    /// The caller must not dispose the certificate it returns: the node may still use it for connections.
    /// </summary>
    public X509Certificate2 Certificate => _source is null
        ? _certificate!
        : _source.GetCertificateWithoutLogging(
            RequireMutualAuthentication ? TlsCertificateRole.ClientAndServer : TlsCertificateRole.Server);

    /// <summary>
    /// Flag used to suppress certificate validation - use true only when on dev machine or for testing.
    /// </summary>
    public bool SuppressValidation { get; }

    /// <summary>
    /// When true, requires mutual TLS authentication where both client and server
    /// must present valid certificates with accessible private keys during the TLS handshake.
    /// Provides defense-in-depth security by ensuring symmetric authentication.
    /// </summary>
    public bool RequireMutualAuthentication { get; }

    /// <summary>
    /// When true, enables traditional outbound TLS hostname validation
    /// (the server certificate CN/SAN must match the connection target hostname).
    /// When false, only validates certificate chain against CA, ignores hostname mismatches.
    /// Default is false for backward compatibility and to support mutual TLS scenarios with per-node certificates,
    /// IP-based connections, or dynamic service discovery.
    /// </summary>
    public bool ValidateCertificateHostname { get; }

    /// <summary>
    /// Custom certificate validation callback for advanced validation scenarios.
    /// When provided, this callback takes precedence over config-based validation.
    /// Use with CertificateValidation helper factory to combine multiple validation strategies.
    /// Example: CertificateValidation.Combine(ValidateChain(log), PinnedCertificate(thumbprints))
    /// </summary>
    public CertificateValidationCallback? CustomValidator { get; }

    internal SslSettings Settings => _source is null
        ? new SslSettings(_certificate!, SuppressValidation, RequireMutualAuthentication, ValidateCertificateHostname, CustomValidator)
        : SslSettings.FromSource(_source, SuppressValidation, RequireMutualAuthentication, ValidateCertificateHostname, CustomValidator);
}
