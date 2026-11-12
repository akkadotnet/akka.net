//-----------------------------------------------------------------------
// <copyright file="HostnameTestCertificates.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Builds short-lived self-signed certificates, and the DER pieces they are made of, for the
    /// hostname-matching specs.
    /// </summary>
    internal static class HostnameTestCertificates
    {
        public const string SubjectAltNameOid = "2.5.29.17";

        public static readonly byte[] CommonNameOid = { 0x55, 0x04, 0x03 };
        public static readonly byte[] OrganizationOid = { 0x55, 0x04, 0x0A };
        public static readonly byte[] OrganizationalUnitOid = { 0x55, 0x04, 0x0B };

        public const byte Utf8StringTag = 0x0C;
        public const byte NumericStringTag = 0x12;
        public const byte PrintableStringTag = 0x13;
        public const byte T61StringTag = 0x14;
        public const byte Ia5StringTag = 0x16;
        public const byte UniversalStringTag = 0x1C;
        public const byte BmpStringTag = 0x1E;
        public const byte OctetStringTag = 0x04;

        /// <summary>
        /// 1.3.6.1.4.1.311.20.2.3, the otherName type Windows CAs use for a user principal name.
        /// </summary>
        public static readonly byte[] UserPrincipalNameOid = { 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x14, 0x02, 0x03 };

        /// <summary>
        /// Creates a certificate from a subject such as <c>CN=node-1, O=Akka</c> (empty for an empty subject)
        /// and a comma-separated list of SAN entries such as <c>dns:*.example.com,ip:127.0.0.1</c>.
        /// An empty <paramref name="subjectAlternativeNames"/> leaves the SAN extension out.
        /// </summary>
        public static X509Certificate2 Create(string subject, string subjectAlternativeNames)
        {
            return Create(Subject(subject),
                subjectAlternativeNames.Length == 0 ? null : SubjectAlternativeName(subjectAlternativeNames));
        }

        /// <summary>
        /// Creates a certificate from an already encoded subject name and SAN extension value.
        /// </summary>
        public static X509Certificate2 Create(byte[] rawSubjectName, byte[]? rawSubjectAlternativeName)
        {
            return Create(new X500DistinguishedName(rawSubjectName),
                rawSubjectAlternativeName is null
                    ? null
                    : new X509Extension(SubjectAltNameOid, rawSubjectAlternativeName, critical: false));
        }

        /// <summary>
        /// Creates a certificate whose SAN extension is produced by the framework's own
        /// <see cref="SubjectAlternativeNameBuilder"/>.
        /// </summary>
        public static X509Certificate2 Create(string subject, Action<SubjectAlternativeNameBuilder> configure)
        {
            var builder = new SubjectAlternativeNameBuilder();
            configure(builder);
            return Create(new X500DistinguishedName(Subject(subject)), builder.Build());
        }

        private static X509Certificate2 Create(X500DistinguishedName subject, X509Extension? subjectAlternativeName)
        {
            // ECDSA keeps key generation cheap; the specs create hundreds of certificates.
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
            if (subjectAlternativeName != null)
                request.CertificateExtensions.Add(subjectAlternativeName);

            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        }

        /// <summary>
        /// Encodes a subject given as a distinguished-name string. An empty string gives an empty name.
        /// </summary>
        public static byte[] Subject(string subject)
        {
            return subject.Length == 0 ? Der(0x30) : new X500DistinguishedName(subject).RawData;
        }

        /// <summary>
        /// Encodes a SAN extension value from entries prefixed with <c>dns:</c>, <c>ip:</c>, <c>email:</c>
        /// or <c>uri:</c>. Names are written as given, so wildcards, empty names and trailing dots survive.
        /// </summary>
        public static byte[] SubjectAlternativeName(string entries)
        {
            var encoded = new List<byte[]>();
            foreach (var entry in entries.Split(','))
            {
                var separator = entry.IndexOf(':');
                var kind = entry.Substring(0, separator);
                var value = entry.Substring(separator + 1);
                switch (kind)
                {
                    case "dns":
                        encoded.Add(DnsName(value));
                        break;
                    case "ip":
                        encoded.Add(IpAddress(IPAddress.Parse(value).GetAddressBytes()));
                        break;
                    case "email":
                        encoded.Add(Der(0x81, Encoding.ASCII.GetBytes(value)));
                        break;
                    case "uri":
                        encoded.Add(Der(0x86, Encoding.ASCII.GetBytes(value)));
                        break;
                    default:
                        throw new ArgumentException($"Unknown SAN entry kind [{kind}]", nameof(entries));
                }
            }

            return Der(0x30, encoded.ToArray());
        }

        public static byte[] DnsName(string name) => Der(0x82, Encoding.ASCII.GetBytes(name));

        public static byte[] IpAddress(params byte[] address) => Der(0x87, address);

        /// <summary>
        /// Encodes an otherName SAN entry: <c>[0] SEQUENCE { type-id OBJECT IDENTIFIER, value [0] EXPLICIT ANY }</c>.
        /// </summary>
        /// <param name="typeId">The content bytes of the type-id OID.</param>
        /// <param name="value">The encoded value to wrap in the explicit [0] tag.</param>
        public static byte[] OtherName(byte[] typeId, byte[] value) => Der(0xA0, Der(0x06, typeId), Der(0xA0, value));

        /// <summary>
        /// The content bytes of the OID 2.25.x, where x takes exactly <paramref name="bits"/> significant bits.
        /// System.Formats.Asn1 accepts an arc of up to 128 bits (a UUID) and rejects anything larger.
        /// </summary>
        public static byte[] OidWithArcOfBits(int bits)
        {
            // Base 128, most significant digit first: seven bits per byte, fewer in the leading byte.
            var length = (bits + 6) / 7;
            var leadingBits = bits - 7 * (length - 1);
            var arc = Enumerable.Repeat((byte)0xFF, length).ToArray();
            arc[0] = (byte)(0x80 | (1 << (leadingBits - 1)));
            arc[length - 1] = 0x7F;

            return new byte[] { 0x69 }.Concat(arc).ToArray();
        }

        /// <summary>
        /// The content bytes of an OID with <paramref name="count"/> one-byte sub-identifiers.
        /// The first sub-identifier holds two arcs, and System.Formats.Asn1 accepts at most 64 arcs,
        /// so 63 sub-identifiers decode and 64 do not.
        /// </summary>
        public static byte[] OidWithSubIdentifiers(int count) => Enumerable.Repeat((byte)0x01, count).ToArray();

        /// <summary>
        /// Encodes a name from its relative distinguished names.
        /// </summary>
        public static byte[] Name(params byte[][] relativeDistinguishedNames) => Der(0x30, relativeDistinguishedNames);

        /// <summary>
        /// Encodes one relative distinguished name from its attributes. More than one makes it multi-valued.
        /// </summary>
        public static byte[] Rdn(params byte[][] attributes) => Der(0x31, attributes);

        public static byte[] Attribute(byte[] oid, byte[] value) => Der(0x30, Der(0x06, oid), value);

        /// <summary>
        /// Encodes a single-valued common-name RDN holding <paramref name="value"/> as a UTF8String.
        /// </summary>
        public static byte[] CommonName(string value) =>
            CommonName(Utf8StringTag, Encoding.UTF8.GetBytes(value));

        /// <summary>
        /// Encodes a single-valued common-name RDN whose value has the given tag and content bytes.
        /// </summary>
        public static byte[] CommonName(byte tag, byte[] content) =>
            Rdn(Attribute(CommonNameOid, Der(tag, content)));

        /// <summary>
        /// Encodes one DER value. Lengths up to 65535 are supported.
        /// </summary>
        public static byte[] Der(byte tag, params byte[][] contents)
        {
            var length = contents.Sum(c => c.Length);
            var buffer = new List<byte>(length + 4) { tag };
            if (length < 0x80)
            {
                buffer.Add((byte)length);
            }
            else if (length <= 0xFF)
            {
                buffer.Add(0x81);
                buffer.Add((byte)length);
            }
            else if (length <= 0xFFFF)
            {
                buffer.Add(0x82);
                buffer.Add((byte)(length >> 8));
                buffer.Add((byte)length);
            }
            else
            {
                throw new ArgumentException("Value too long for this test helper", nameof(contents));
            }

            foreach (var content in contents)
                buffer.AddRange(content);

            return buffer.ToArray();
        }
    }
}
