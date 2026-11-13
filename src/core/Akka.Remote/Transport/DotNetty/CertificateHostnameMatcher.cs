//-----------------------------------------------------------------------
// <copyright file="CertificateHostnameMatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
// Portions of this file are ported from the .NET runtime
// (https://github.com/dotnet/runtime), v10.0.0.
// Copyright (c) .NET Foundation and Contributors. Licensed under the MIT license.
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Util.Internal;
using static Akka.Util.Internal.DerEncoding;

namespace Akka.Remote.Transport.DotNetty
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Matches a certificate against an expected hostname or IP address.
    ///
    /// This is a port of <c>X509Certificate2.MatchesHostname(hostname)</c>, called with its default arguments
    /// (wildcards allowed, common-name fallback allowed). That method arrived in .NET 7, and Akka.Remote v1.5
    /// builds for netstandard2.0 and net6.0, so neither target can call it. The rules, in order:
    /// <list type="bullet">
    /// <item>A name that parses as an IP address is matched exactly against the <c>iPAddress</c> entries of the
    /// Subject Alternative Name (SAN) extension. Any other name must be a valid DNS name and is matched against
    /// the <c>dNSName</c> entries.</item>
    /// <item>DNS names are compared without regard to case. One trailing dot is ignored on either side.</item>
    /// <item>A <c>dNSName</c> entry that starts with <c>*.</c> is a wildcard. The wildcard stands for exactly one
    /// label, the leftmost one.</item>
    /// <item>The subject common name (CN) is consulted only when the certificate has no SAN entry of the wanted
    /// kind. The CN must be the only CN in the subject, must not sit in a multi-valued RDN, and is compared
    /// exactly (no wildcards, no trailing-dot handling).</item>
    /// <item>Non-ASCII names are not converted to their IDNA (punycode) form.</item>
    /// <item>The whole SAN extension is decoded before anything is matched, entries the match never uses
    /// included (otherName, rfc822Name, directoryName, URI and so on). A malformed entry of any kind rejects
    /// the certificate, as it does in the framework method.</item>
    /// </list>
    /// The decoding rules follow the dotnet/runtime sources for <c>X509Certificate2.MatchesHostname</c>,
    /// <c>X509SubjectAlternativeNameExtension</c>, <c>GeneralNameAsn</c>, <c>X500RelativeDistinguishedName</c>
    /// and <c>System.Formats.Asn1.AsnDecoder</c> under DER rules, so that a certificate gets the same answer
    /// here as on Akka.NET v1.6, which calls the framework method.
    /// </summary>
    internal static class CertificateHostnameMatcher
    {
        private const string SubjectAltNameOid = "2.5.29.17";

        // DER tags this matcher reads that the shared reader does not
        private const byte DnsNameTag = 0x82;   // [2] IMPLICIT IA5String, primitive
        private const byte IpAddressTag = 0x87; // [7] IMPLICIT OCTET STRING, primitive

        /// <summary>
        /// Checks whether <paramref name="certificate"/> matches <paramref name="hostname"/>.
        /// </summary>
        /// <param name="certificate">The certificate to check.</param>
        /// <param name="hostname">The DNS name or IP address the certificate must match.</param>
        /// <returns><c>true</c> if the certificate matches; otherwise <c>false</c>.</returns>
        /// <exception cref="ArgumentException"><paramref name="hostname"/> is not a valid DNS name or IP address.</exception>
        /// <exception cref="CryptographicException">
        /// The certificate has more than one SAN extension, or its SAN extension or subject name cannot be decoded.
        /// </exception>
        public static bool MatchesHostname(X509Certificate2 certificate, string hostname)
        {
            if (certificate is null)
                throw new ArgumentNullException(nameof(certificate));

            // Validate the name first, as X509Certificate2.MatchesHostname does, so an invalid name is
            // reported as such even when the certificate is also unreadable.
            var ipAddress = ParseHostname(hostname);

            byte[]? rawSubjectAlternativeName = null;
            foreach (X509Extension extension in certificate.Extensions)
            {
                if (extension.Oid?.Value != SubjectAltNameOid)
                    continue;

                if (rawSubjectAlternativeName != null)
                    throw new CryptographicException(
                        "The certificate contains multiple Subject Alternative Name extensions.");

                rawSubjectAlternativeName = extension.RawData;
            }

            return Matches(hostname, ipAddress, rawSubjectAlternativeName, certificate.SubjectName.RawData);
        }

        /// <summary>
        /// Checks <paramref name="hostname"/> against the DER-encoded SAN extension value and subject name of
        /// a certificate. Split out from <see cref="MatchesHostname(X509Certificate2,string)"/> so the matching
        /// rules can be tested against encodings that no platform will load as a certificate.
        /// </summary>
        /// <param name="hostname">The DNS name or IP address the certificate must match.</param>
        /// <param name="rawSubjectAlternativeName">
        /// The value of the SAN extension, or <c>null</c> when the certificate has none.
        /// </param>
        /// <param name="rawSubjectName">The DER-encoded subject distinguished name.</param>
        internal static bool MatchesHostname(string hostname, byte[]? rawSubjectAlternativeName, byte[] rawSubjectName)
        {
            return Matches(hostname, ParseHostname(hostname), rawSubjectAlternativeName, rawSubjectName);
        }

        private static bool Matches(string hostname, IPAddress? ipAddress, byte[]? rawSubjectAlternativeName,
            byte[] rawSubjectName)
        {
            if (rawSubjectAlternativeName != null)
            {
                // Decode the whole extension before matching, so a malformed entry rejects the certificate
                // no matter where in the list it sits.
                DecodeSubjectAlternativeName(rawSubjectAlternativeName, out var dnsNames, out var ipAddresses);
                var hadAny = false;

                if (ipAddress != null)
                {
                    foreach (var entry in ipAddresses)
                    {
                        if (entry.Equals(ipAddress))
                            return true;

                        hadAny = true;
                    }
                }
                else
                {
                    // Treat "something.example.org." as "something.example.org"
                    var match = hostname;
                    if (match[match.Length - 1] == '.')
                    {
                        match = match.Substring(0, match.Length - 1);
                        if (match.Length == 0)
                            return false;
                    }

                    // What a wildcard entry has to equal once its "*." is removed: everything after the
                    // first label of the expected name. Empty when the expected name has a single label,
                    // so a wildcard never matches a single-label name.
                    var firstDot = match.IndexOf('.');
                    var afterFirstDot = firstDot > 0 ? match.Substring(firstDot + 1) : string.Empty;

                    foreach (var embedded in dnsNames)
                    {
                        hadAny = true;

                        if (embedded.Length == 0)
                            continue;

                        // Treat an embedded "something.example.org." as "something.example.org"
                        var name = embedded[embedded.Length - 1] == '.'
                            ? embedded.Substring(0, embedded.Length - 1)
                            : embedded;

                        if (name.Length > 2 && name[0] == '*' && name[1] == '.')
                        {
                            if (name.Length - 2 == afterFirstDot.Length &&
                                string.Compare(name, 2, afterFirstDot, 0, afterFirstDot.Length,
                                    StringComparison.OrdinalIgnoreCase) == 0)
                                return true;
                        }
                        else if (string.Equals(name, match, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                // The certificate names something of the wanted kind, and it is not this name.
                // The common name is not consulted in that case.
                if (hadAny)
                    return false;
            }

            // Common-name fallback. Note that this compares the name exactly as the caller supplied it:
            // no wildcard and no trailing-dot handling.
            var commonName = ReadSingleCommonName(rawSubjectName);
            return commonName != null && string.Equals(hostname, commonName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks that <paramref name="hostname"/> is an IP address or a valid DNS name.
        /// </summary>
        /// <returns>The parsed address when the name is an IP address; otherwise <c>null</c>.</returns>
        private static IPAddress? ParseHostname(string hostname)
        {
            if (hostname is null)
                throw new ArgumentNullException(nameof(hostname));

            if (IPAddress.TryParse(hostname, out var ipAddress))
                return ipAddress;

            if (Uri.CheckHostName(hostname) != UriHostNameType.Dns)
                throw new ArgumentException(
                    "The provided value is not a valid DNS hostname or IP address.", nameof(hostname));

            return null;
        }

        /// <summary>
        /// Decodes <c>SubjectAltName ::= GeneralNames</c> (RFC 5280, section 4.2.1.6) and returns its
        /// <c>dNSName</c> and <c>iPAddress</c> entries. Entries of the other kinds are checked and dropped.
        /// </summary>
        private static void DecodeSubjectAlternativeName(byte[] rawData, out List<string> dnsNames,
            out List<IPAddress> ipAddresses)
        {
            dnsNames = new List<string>();
            ipAddresses = new List<IPAddress>();

            var outer = new DerReader(rawData, 0, rawData.Length);
            var sequence = outer.ReadValue();
            if (sequence.Tag != SequenceTag || outer.HasData)
                throw InvalidEncoding();

            // GeneralName ::= CHOICE {
            //     otherName                 [0] IMPLICIT OtherName,
            //     rfc822Name                [1] IMPLICIT IA5String,
            //     dNSName                   [2] IMPLICIT IA5String,
            //     x400Address               [3] IMPLICIT ORAddress,
            //     directoryName             [4] EXPLICIT Name,
            //     ediPartyName              [5] IMPLICIT EDIPartyName,
            //     uniformResourceIdentifier [6] IMPLICIT IA5String,
            //     iPAddress                 [7] IMPLICIT OCTET STRING,
            //     registeredID              [8] IMPLICIT OBJECT IDENTIFIER }
            var names = new DerReader(rawData, sequence.ContentOffset, sequence.ContentLength);
            while (names.HasData)
            {
                var name = names.ReadValue();
                if (!name.IsContextSpecific)
                    throw InvalidEncoding();

                switch (name.TagNumber)
                {
                    case 0:
                        ValidateOtherName(rawData, name);
                        break;

                    case 1:
                    case 6:
                        // Checked as an IA5String, then dropped.
                        RequirePrimitive(name);
                        DecodeRestrictedAscii(rawData, name, allowed: null);
                        break;

                    case 2:
                        if (name.Tag != DnsNameTag)
                            throw InvalidEncoding();

                        dnsNames.Add(DecodeRestrictedAscii(rawData, name, allowed: null));
                        break;

                    case 3:
                        // The framework reads an x400Address as one opaque value and looks no further.
                        break;

                    case 4:
                        ValidateDirectoryName(rawData, name);
                        break;

                    case 5:
                        ValidateEdiPartyName(rawData, name);
                        break;

                    case 7:
                        if (name.Tag != IpAddressTag || (name.ContentLength != 4 && name.ContentLength != 16))
                            throw InvalidEncoding();

                        var address = new byte[name.ContentLength];
                        Buffer.BlockCopy(rawData, name.ContentOffset, address, 0, address.Length);
                        ipAddresses.Add(new IPAddress(address));
                        break;

                    case 8:
                        RequirePrimitive(name);
                        if (!IsValidObjectIdentifier(rawData, name))
                            throw InvalidEncoding();

                        break;

                    default:
                        throw InvalidEncoding();
                }
            }
        }

        /// <summary>
        /// Checks <c>OtherName ::= SEQUENCE { type-id OBJECT IDENTIFIER, value [0] EXPLICIT ANY }</c>.
        /// </summary>
        private static void ValidateOtherName(byte[] data, DerValue otherName)
        {
            RequireConstructed(otherName);
            var fields = new DerReader(data, otherName.ContentOffset, otherName.ContentLength);

            var typeId = fields.ReadValue();
            if (typeId.Tag != ObjectIdentifierTag || !IsValidObjectIdentifier(data, typeId))
                throw InvalidEncoding();

            var value = fields.ReadValue();
            if (!value.IsContextSpecific || value.TagNumber != 0)
                throw InvalidEncoding();

            RequireConstructed(value);
            RequireSingleValue(data, value);

            if (fields.HasData)
                throw InvalidEncoding();
        }

        /// <summary>
        /// Checks <c>directoryName [4] EXPLICIT Name</c>. The framework requires the wrapper to hold exactly
        /// one value and does not decode the name inside it.
        /// </summary>
        private static void ValidateDirectoryName(byte[] data, DerValue directoryName)
        {
            RequireConstructed(directoryName);
            RequireSingleValue(data, directoryName);
        }

        /// <summary>
        /// Checks <c>EDIPartyName ::= SEQUENCE { nameAssigner [0] EXPLICIT DirectoryString OPTIONAL,
        /// partyName [1] EXPLICIT DirectoryString }</c>.
        /// </summary>
        private static void ValidateEdiPartyName(byte[] data, DerValue ediPartyName)
        {
            RequireConstructed(ediPartyName);
            var fields = new DerReader(data, ediPartyName.ContentOffset, ediPartyName.ContentLength);

            var field = fields.ReadValue();
            if (field.IsContextSpecific && field.TagNumber == 0)
            {
                ValidateExplicitDirectoryString(data, field);
                field = fields.ReadValue();
            }

            if (!field.IsContextSpecific || field.TagNumber != 1)
                throw InvalidEncoding();

            ValidateExplicitDirectoryString(data, field);

            if (fields.HasData)
                throw InvalidEncoding();
        }

        /// <summary>
        /// Checks an explicitly tagged <c>DirectoryString ::= CHOICE { teletexString, printableString,
        /// universalString, utf8String, bmpString }</c>.
        /// </summary>
        private static void ValidateExplicitDirectoryString(byte[] data, DerValue wrapper)
        {
            RequireConstructed(wrapper);
            var content = new DerReader(data, wrapper.ContentOffset, wrapper.ContentLength);
            var value = content.ReadValue();
            if (!value.IsUniversal)
                throw InvalidEncoding();

            switch (value.TagNumber)
            {
                case T61StringTagNumber:
                    // Any bytes decode: as UTF-8 if they can, as ISO-8859-1 otherwise.
                    RequirePrimitive(value);
                    break;

                case PrintableStringTagNumber:
                    RequirePrimitive(value);
                    DecodeRestrictedAscii(data, value, PrintableStringCharacters);
                    break;

                case UniversalStringTagNumber:
                    // The framework reads a UniversalString as one opaque value and looks no further.
                    break;

                case Utf8StringTagNumber:
                    RequirePrimitive(value);
                    if (DecodeUtf8(data, value) is null)
                        throw InvalidEncoding();

                    break;

                case BmpStringTagNumber:
                    RequirePrimitive(value);
                    DecodeBmp(data, value);
                    break;

                default:
                    throw InvalidEncoding();
            }

            if (content.HasData)
                throw InvalidEncoding();
        }

        /// <summary>
        /// Checks that the content of <paramref name="wrapper"/> is exactly one well-formed value.
        /// </summary>
        private static void RequireSingleValue(byte[] data, DerValue wrapper)
        {
            var content = new DerReader(data, wrapper.ContentOffset, wrapper.ContentLength);
            content.ReadValue();
            if (content.HasData)
                throw InvalidEncoding();
        }
    }
}
