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
using System.Text;

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

        // DER tags
        private const byte SequenceTag = 0x30;
        private const byte SetTag = 0x31;
        private const byte ObjectIdentifierTag = 0x06;
        private const byte DnsNameTag = 0x82;   // [2] IMPLICIT IA5String, primitive
        private const byte IpAddressTag = 0x87; // [7] IMPLICIT OCTET STRING, primitive

        // DER tag numbers of the universal string types
        private const int Utf8StringTagNumber = 0x0C;
        private const int NumericStringTagNumber = 0x12;
        private const int PrintableStringTagNumber = 0x13;
        private const int T61StringTagNumber = 0x14;
        private const int Ia5StringTagNumber = 0x16;
        private const int UniversalStringTagNumber = 0x1C;
        private const int BmpStringTagNumber = 0x1E;

        // Limits System.Formats.Asn1 puts on an OBJECT IDENTIFIER
        private const int MaxObjectIdentifierArcBits = 128;
        private const int MaxObjectIdentifierArcs = 64;

        private const string PrintableStringCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 '()+,-./:=?";

        private static readonly UTF8Encoding StrictUtf8 =
            new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

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

        /// <summary>
        /// Returns the value of the subject's common name when the subject has exactly one, and it is not part
        /// of a multi-valued RDN. Returns <c>null</c> when the subject has no usable common name.
        /// </summary>
        private static string? ReadSingleCommonName(byte[] rawSubjectName)
        {
            // Name ::= SEQUENCE OF RelativeDistinguishedName
            // RelativeDistinguishedName ::= SET SIZE (1..MAX) OF AttributeTypeAndValue
            // AttributeTypeAndValue ::= SEQUENCE { type OBJECT IDENTIFIER, value ANY }
            var outer = new DerReader(rawSubjectName, 0, rawSubjectName.Length);
            var sequence = outer.ReadValue();
            if (sequence.Tag != SequenceTag || outer.HasData)
                throw InvalidEncoding();

            var commonNameCount = 0;
            var commonNameInMultiValuedRdn = false;
            DerValue commonName = default;

            // Read the whole name before deciding anything, so a malformed name is always rejected.
            var rdns = new DerReader(rawSubjectName, sequence.ContentOffset, sequence.ContentLength);
            while (rdns.HasData)
            {
                var rdn = rdns.ReadValue();
                if (rdn.Tag != SetTag || rdn.ContentLength == 0)
                    throw InvalidEncoding();

                var attributes = new DerReader(rawSubjectName, rdn.ContentOffset, rdn.ContentLength);
                var attributeCount = 0;
                var commonNamesInRdn = 0;
                while (attributes.HasData)
                {
                    var attribute = attributes.ReadValue();
                    if (attribute.Tag != SequenceTag)
                        throw InvalidEncoding();

                    var typeAndValue = new DerReader(rawSubjectName, attribute.ContentOffset, attribute.ContentLength);
                    var type = typeAndValue.ReadValue();
                    if (type.Tag != ObjectIdentifierTag || !IsValidObjectIdentifier(rawSubjectName, type))
                        throw InvalidEncoding();

                    var value = typeAndValue.ReadValue();
                    if (typeAndValue.HasData)
                        throw InvalidEncoding();

                    attributeCount++;
                    if (IsCommonNameOid(rawSubjectName, type))
                    {
                        commonNamesInRdn++;
                        commonName = value;
                    }
                }

                if (commonNamesInRdn == 0)
                    continue;

                if (attributeCount > 1)
                    commonNameInMultiValuedRdn = true;
                else
                    commonNameCount++;
            }

            if (commonNameInMultiValuedRdn || commonNameCount != 1)
                return null;

            return DecodeDirectoryString(rawSubjectName, commonName);
        }

        /// <summary>
        /// Decodes an attribute value as text. Returns <c>null</c> when the value is not one of the text types.
        /// </summary>
        private static string? DecodeDirectoryString(byte[] data, DerValue value)
        {
            // Only universal-class tags name the text types.
            if (!value.IsUniversal)
                return null;

            string decoded;
            switch (value.TagNumber)
            {
                case Utf8StringTagNumber:
                    RequirePrimitive(value);
                    decoded = DecodeUtf8(data, value) ?? throw InvalidEncoding();
                    break;

                case NumericStringTagNumber:
                    RequirePrimitive(value);
                    decoded = DecodeRestrictedAscii(data, value, "0123456789 ");
                    break;

                case PrintableStringTagNumber:
                    RequirePrimitive(value);
                    decoded = DecodeRestrictedAscii(data, value, PrintableStringCharacters);
                    break;

                case T61StringTagNumber:
                    // Read as UTF-8 and fall back to ISO-8859-1, as .NET does.
                    RequirePrimitive(value);
                    decoded = DecodeUtf8(data, value) ?? DecodeLatin1(data, value);
                    break;

                case Ia5StringTagNumber:
                    RequirePrimitive(value);
                    decoded = DecodeRestrictedAscii(data, value, allowed: null);
                    break;

                case BmpStringTagNumber:
                    RequirePrimitive(value);
                    decoded = DecodeBmp(data, value);
                    break;

                default:
                    return null;
            }

            // A trailing NUL embedded in the DER would defeat a length-checked comparison.
            return decoded.TrimEnd('\0');
        }

        private static void RequirePrimitive(DerValue value)
        {
            // DER forbids the constructed form for strings, and an OBJECT IDENTIFIER is always primitive.
            if (value.IsConstructed)
                throw InvalidEncoding();
        }

        private static void RequireConstructed(DerValue value)
        {
            // A SEQUENCE, and a value wrapped in an EXPLICIT tag, is always constructed.
            if (!value.IsConstructed)
                throw InvalidEncoding();
        }

        private static string? DecodeUtf8(byte[] data, DerValue value)
        {
            try
            {
                return StrictUtf8.GetString(data, value.ContentOffset, value.ContentLength);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }

        private static string DecodeLatin1(byte[] data, DerValue value)
        {
            var chars = new char[value.ContentLength];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = (char)data[value.ContentOffset + i];

            return new string(chars);
        }

        /// <summary>
        /// Decodes a string type whose characters are a subset of ASCII.
        /// </summary>
        /// <param name="data">The buffer that holds the value.</param>
        /// <param name="value">The value to decode.</param>
        /// <param name="allowed">The permitted characters, or <c>null</c> to permit all of ASCII (IA5String).</param>
        private static string DecodeRestrictedAscii(byte[] data, DerValue value, string? allowed)
        {
            var chars = new char[value.ContentLength];
            for (var i = 0; i < chars.Length; i++)
            {
                var b = data[value.ContentOffset + i];
                if (b > 0x7F || (allowed != null && allowed.IndexOf((char)b) < 0))
                    throw InvalidEncoding();

                chars[i] = (char)b;
            }

            return new string(chars);
        }

        private static string DecodeBmp(byte[] data, DerValue value)
        {
            // Big-endian UCS-2: two bytes per character, surrogates not allowed.
            if (value.ContentLength % 2 != 0)
                throw InvalidEncoding();

            var chars = new char[value.ContentLength / 2];
            for (var i = 0; i < chars.Length; i++)
            {
                var offset = value.ContentOffset + i * 2;
                var c = (char)((data[offset] << 8) | data[offset + 1]);
                if (char.IsSurrogate(c))
                    throw InvalidEncoding();

                chars[i] = c;
            }

            return new string(chars);
        }

        private static bool IsCommonNameOid(byte[] data, DerValue oid)
        {
            // 2.5.4.3 encodes as 55 04 03. DER gives an OID exactly one encoding, so comparing bytes is exact.
            return oid.ContentLength == 3
                   && data[oid.ContentOffset] == 0x55
                   && data[oid.ContentOffset + 1] == 0x04
                   && data[oid.ContentOffset + 2] == 0x03;
        }

        /// <summary>
        /// Checks the content of an OBJECT IDENTIFIER the way <c>AsnDecoder.ReadObjectIdentifier</c> does.
        /// </summary>
        private static bool IsValidObjectIdentifier(byte[] data, DerValue oid)
        {
            var offset = oid.ContentOffset;
            var end = offset + oid.ContentLength;
            if (offset == end)
                return false;

            // The first sub-identifier holds the first two arcs.
            var remainingArcs = MaxObjectIdentifierArcs - 2;
            var first = true;
            while (offset < end)
            {
                if (!first)
                {
                    if (remainingArcs <= 0)
                        return false;

                    remainingArcs--;
                }

                first = false;

                // A sub-identifier is a base-128 number, most significant digit first. Every byte but the
                // last has its high bit set, and the first byte must not be a padding 0x80.
                var lead = data[offset];
                if (lead == 0x80)
                    return false;

                // Count the significant bits. The leading byte gives between one and seven, each later byte
                // seven more. Start from (bits in the leading byte - 7) so the loop can add 7 for every byte.
                var bits = lead >= 0xC0 ? 0
                    : lead >= 0xA0 ? -1
                    : lead >= 0x90 ? -2
                    : lead >= 0x88 ? -3
                    : lead >= 0x84 ? -4
                    : lead >= 0x82 ? -5
                    : lead >= 0x81 ? -6
                    : 0;

                var complete = false;
                while (offset < end)
                {
                    bits += 7;
                    if (bits > MaxObjectIdentifierArcBits)
                        return false;

                    if ((data[offset++] & 0x80) == 0)
                    {
                        complete = true;
                        break;
                    }
                }

                if (!complete)
                    return false;
            }

            return true;
        }

        private static CryptographicException InvalidEncoding()
        {
            return new CryptographicException("ASN.1 encoded value is corrupted or not valid DER.");
        }

        /// <summary>
        /// One DER value: its leading tag byte and where its content sits in the buffer.
        /// </summary>
        private readonly struct DerValue
        {
            public DerValue(byte tag, bool isHighTagNumber, int contentOffset, int contentLength)
            {
                Tag = tag;
                IsHighTagNumber = isHighTagNumber;
                ContentOffset = contentOffset;
                ContentLength = contentLength;
            }

            /// <summary>
            /// The first tag byte: class in the top two bits, the constructed flag in bit 6,
            /// and the tag number in the low five bits unless <see cref="IsHighTagNumber"/> is set.
            /// </summary>
            public byte Tag { get; }

            /// <summary>
            /// True when the tag number is 31 or more and so is spread over several bytes.
            /// No value this matcher reads uses such a tag.
            /// </summary>
            public bool IsHighTagNumber { get; }

            public int ContentOffset { get; }

            public int ContentLength { get; }

            /// <summary>
            /// The tag number. Only meaningful when <see cref="IsHighTagNumber"/> is false; every tag this
            /// matcher looks for has a number below 31.
            /// </summary>
            public int TagNumber => Tag & 0x1F;

            public bool IsConstructed => (Tag & 0x20) != 0;

            /// <summary>
            /// True for a universal-class tag with a number below 31.
            /// </summary>
            public bool IsUniversal => !IsHighTagNumber && (Tag & 0xC0) == 0x00;

            /// <summary>
            /// True for a context-specific tag with a number below 31.
            /// </summary>
            public bool IsContextSpecific => !IsHighTagNumber && (Tag & 0xC0) == 0x80;
        }

        /// <summary>
        /// A minimal forward-only reader for DER tag-length-value triplets. It checks every bound and
        /// reports any malformed input as a <see cref="CryptographicException"/>.
        /// </summary>
        private struct DerReader
        {
            private readonly byte[] _data;
            private readonly int _end;
            private int _offset;

            public DerReader(byte[] data, int offset, int length)
            {
                _data = data;
                _offset = offset;
                _end = offset + length;
            }

            public bool HasData => _offset < _end;

            public DerValue ReadValue()
            {
                var tag = ReadByte();
                var isHighTagNumber = (tag & 0x1F) == 0x1F;
                if (isHighTagNumber)
                {
                    // High tag number form: base-128 digits, most significant first.
                    long tagNumber = 0;
                    byte b;
                    var first = true;
                    do
                    {
                        b = ReadByte();
                        if (first && b == 0x80)
                            throw InvalidEncoding(); // padded tag number

                        first = false;
                        tagNumber = (tagNumber << 7) | (uint)(b & 0x7F);
                        if (tagNumber > int.MaxValue)
                            throw InvalidEncoding();
                    } while ((b & 0x80) != 0);

                    if (tagNumber < 31)
                        throw InvalidEncoding(); // must have used the single-byte form
                }

                long length = ReadByte();
                if (length >= 0x80)
                {
                    // 0x80 is the indefinite form, which DER forbids. 0xFF is reserved.
                    var lengthBytes = (int)length & 0x7F;
                    if (lengthBytes == 0 || lengthBytes > 4)
                        throw InvalidEncoding();

                    length = 0;
                    for (var i = 0; i < lengthBytes; i++)
                    {
                        var b = ReadByte();
                        if (i == 0 && b == 0)
                            throw InvalidEncoding(); // padded length

                        length = (length << 8) | b;
                    }

                    // DER requires the short form for lengths under 128.
                    if (length < 0x80)
                        throw InvalidEncoding();
                }

                if (length > _end - _offset)
                    throw InvalidEncoding();

                var value = new DerValue(tag, isHighTagNumber, _offset, (int)length);
                _offset += (int)length;
                return value;
            }

            private byte ReadByte()
            {
                if (_offset >= _end)
                    throw InvalidEncoding();

                return _data[_offset++];
            }
        }
    }
}
