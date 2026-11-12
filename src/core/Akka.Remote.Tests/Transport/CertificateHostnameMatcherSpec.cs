//-----------------------------------------------------------------------
// <copyright file="CertificateHostnameMatcherSpec.cs" company="Akka.NET Project">
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
#if NET7_0_OR_GREATER
using System.Formats.Asn1;
#endif
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Akka.Remote.Transport.DotNetty;
using Xunit;
using static Akka.Remote.Tests.Transport.HostnameTestCertificates;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Specs for <see cref="CertificateHostnameMatcher"/>, the v1.5 stand-in for
    /// <c>X509Certificate2.MatchesHostname</c>.
    ///
    /// In the DotNetty transport a failed outbound TLS handshake shuts the ActorSystem down, so a matcher
    /// that wrongly rejects a valid certificate takes a node out. These specs lean toward proving that
    /// valid certificates are accepted. On .NET 7 and later they also run the matcher side by side with
    /// the framework method and require the two to agree, on what they accept and on what they reject.
    /// </summary>
    public class CertificateHostnameMatcherSpec
    {
        private const string ValidCertPath = "Resources/akka-validcert.pfx";
        private const string ClientCertPath = "Resources/akka-client-cert.pfx";
        private const string Password = "password";

        private readonly ITestOutputHelper _output;

        public CertificateHostnameMatcherSpec(ITestOutputHelper output)
        {
            _output = output;
        }

        #region Certificates that must be accepted

        [Theory(DisplayName = "Should_AcceptCertificate_When_ExpectedNameMatchesDnsSubjectAlternativeName")]
        // exact match
        [InlineData("dns:www.example.com", "www.example.com")]
        [InlineData("dns:localhost", "localhost")]
        [InlineData("dns:node-1", "node-1")]
        [InlineData("dns:node-1.cluster.local", "node-1.cluster.local")]
        // case does not matter, on either side
        [InlineData("dns:www.example.com", "WWW.EXAMPLE.COM")]
        [InlineData("dns:WWW.Example.COM", "www.example.com")]
        [InlineData("dns:localhost", "LocalHost")]
        // one trailing dot is ignored, on either side
        [InlineData("dns:www.example.com", "www.example.com.")]
        [InlineData("dns:www.example.com.", "www.example.com")]
        [InlineData("dns:www.example.com.", "www.example.com.")]
        [InlineData("dns:localhost", "localhost.")]
        // any one of several names
        [InlineData("dns:a.example.com,dns:b.example.com,dns:c.example.com", "a.example.com")]
        [InlineData("dns:a.example.com,dns:b.example.com,dns:c.example.com", "b.example.com")]
        [InlineData("dns:a.example.com,dns:b.example.com,dns:c.example.com", "c.example.com")]
        [InlineData("email:ops@example.com,uri:https://example.com/,ip:10.0.0.1,dns:www.example.com", "www.example.com")]
        // an empty name in the list does not get in the way
        [InlineData("dns:,dns:www.example.com", "www.example.com")]
        // punycode names are plain ASCII names
        [InlineData("dns:xn--bcher-kva.example", "xn--bcher-kva.example")]
        [InlineData("dns:xn--bcher-kva.example", "XN--BCHER-KVA.EXAMPLE")]
        public void Should_AcceptCertificate_When_ExpectedNameMatchesDnsSubjectAlternativeName(
            string subjectAlternativeNames, string expectedHostname)
        {
            using var certificate = Create("CN=unrelated", subjectAlternativeNames);

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, expectedHostname));
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_ExpectedNameMatchesWildcardSubjectAlternativeName")]
        [InlineData("dns:*.example.com", "www.example.com")]
        [InlineData("dns:*.example.com", "node-1.example.com")]
        [InlineData("dns:*.example.com", "WWW.Example.Com")]
        [InlineData("dns:*.EXAMPLE.com", "www.example.com")]
        [InlineData("dns:*.example.com", "www.example.com.")]
        [InlineData("dns:*.example.com.", "www.example.com")]
        [InlineData("dns:*.cluster.local", "node-1.cluster.local")]
        [InlineData("dns:*.akka.svc.cluster.local", "node-0.akka.svc.cluster.local")]
        [InlineData("dns:*.local", "node-1.local")]
        // a wildcard next to the bare domain
        [InlineData("dns:*.example.com,dns:example.com", "example.com")]
        [InlineData("dns:*.example.com,dns:example.com", "www.example.com")]
        [InlineData("dns:example.com,dns:*.example.com", "www.example.com")]
        // a wildcard that does not match must not stop a later name from matching
        [InlineData("dns:*.example.org,dns:www.example.com", "www.example.com")]
        [InlineData("dns:*.example.org,dns:*.example.com", "www.example.com")]
        public void Should_AcceptCertificate_When_ExpectedNameMatchesWildcardSubjectAlternativeName(
            string subjectAlternativeNames, string expectedHostname)
        {
            using var certificate = Create("CN=unrelated", subjectAlternativeNames);

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, expectedHostname));
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_ExpectedIpAddressMatchesIpSubjectAlternativeName")]
        [InlineData("ip:127.0.0.1", "127.0.0.1")]
        [InlineData("ip:10.0.0.1,ip:10.0.0.2,ip:10.0.0.3", "10.0.0.1")]
        [InlineData("ip:10.0.0.1,ip:10.0.0.2,ip:10.0.0.3", "10.0.0.3")]
        [InlineData("ip:192.168.1.250", "192.168.1.250")]
        [InlineData("ip:::1", "::1")]
        // the same IPv6 address in other spellings
        [InlineData("ip:::1", "0:0:0:0:0:0:0:1")]
        [InlineData("ip:2001:db8::1", "2001:db8::1")]
        [InlineData("ip:2001:db8::1", "2001:DB8::1")]
        [InlineData("ip:2001:db8::1", "2001:0db8:0000:0000:0000:0000:0000:0001")]
        [InlineData("ip:fe80::1ff:fe23:4567:890a", "fe80::1ff:fe23:4567:890a")]
        [InlineData("ip:::ffff:127.0.0.1", "::ffff:127.0.0.1")]
        // mixed with other kinds of name
        [InlineData("dns:localhost,ip:127.0.0.1,ip:::1", "127.0.0.1")]
        [InlineData("dns:localhost,ip:127.0.0.1,ip:::1", "::1")]
        [InlineData("dns:localhost,ip:127.0.0.1,ip:::1", "localhost")]
        public void Should_AcceptCertificate_When_ExpectedIpAddressMatchesIpSubjectAlternativeName(
            string subjectAlternativeNames, string expectedHostname)
        {
            using var certificate = Create("CN=unrelated", subjectAlternativeNames);

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, expectedHostname));
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_ExpectedNameMatchesCommonNameAndNoSanOfThatKindExists")]
        // no SAN extension at all
        [InlineData("CN=www.example.com", "", "www.example.com")]
        [InlineData("CN=www.example.com", "", "WWW.EXAMPLE.COM")]
        [InlineData("CN=WWW.Example.Com", "", "www.example.com")]
        [InlineData("CN=localhost", "", "localhost")]
        [InlineData("CN=AkkaTestClient", "", "AkkaTestClient")]
        [InlineData("CN=AkkaTestClient", "", "akkatestclient")]
        [InlineData("CN=node-1, OU=Cluster, O=Akka, C=US", "", "node-1")]
        [InlineData("C=US, O=Akka, OU=Cluster, CN=node-1", "", "node-1")]
        [InlineData("CN=127.0.0.1", "", "127.0.0.1")]
        [InlineData("CN=::1", "", "::1")]
        // a SAN extension without DNS names still lets a DNS name fall back to the common name
        [InlineData("CN=www.example.com", "ip:127.0.0.1", "www.example.com")]
        [InlineData("CN=www.example.com", "email:ops@example.com,uri:https://example.com/", "www.example.com")]
        // a SAN extension without IP addresses still lets an IP address fall back to the common name
        [InlineData("CN=127.0.0.1", "dns:www.example.com", "127.0.0.1")]
        public void Should_AcceptCertificate_When_ExpectedNameMatchesCommonNameAndNoSanOfThatKindExists(
            string subject, string subjectAlternativeNames, string expectedHostname)
        {
            using var certificate = Create(subject, subjectAlternativeNames);

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, expectedHostname));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_CommonNameIsNotAscii")]
        public void Should_AcceptCertificate_When_CommonNameIsNotAscii()
        {
            // No IDNA mapping takes place, so the caller must pass the form the certificate carries.
            using var certificate = Create(Name(CommonName("bücher.example")), null);

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, "bücher.example"));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_SubjectAlternativeNameComesFromTheFrameworkBuilder")]
        public void Should_AcceptCertificate_When_SubjectAlternativeNameComesFromTheFrameworkBuilder()
        {
            // Every kind of entry SubjectAlternativeNameBuilder can write, including a UPN otherName as found
            // in Active Directory client certificates. Well-formed entries of other kinds must not get in the way.
            using var certificate = Create("CN=unrelated", builder =>
            {
                builder.AddEmailAddress("ops@example.com");
                builder.AddUserPrincipalName("node-1@example.com");
                builder.AddUri(new Uri("https://example.com/akka"));
                builder.AddDnsName("first.internal");
                builder.AddDnsName("*.wild.internal");
                builder.AddIpAddress(System.Net.IPAddress.Parse("10.1.2.3"));
                builder.AddIpAddress(System.Net.IPAddress.Parse("2001:db8::5"));
                builder.AddDnsName("last.internal");
            });

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, "first.internal"));
            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, "last.internal"));
            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, "node.wild.internal"));
            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, "10.1.2.3"));
            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, "2001:db8::5"));
            Assert.False(CertificateHostnameMatcher.MatchesHostname(certificate, "other.internal"));
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_SubjectAlternativeNameNeedsMultiByteLength")]
        [InlineData(10)]   // extension value over 127 bytes: one length byte after the marker
        [InlineData(40)]   // over 255 bytes: two length bytes after the marker
        [InlineData(400)]  // a large certificate
        public void Should_AcceptCertificate_When_SubjectAlternativeNameNeedsMultiByteLength(int nameCount)
        {
            var names = Enumerable.Range(0, nameCount).Select(i => $"node-{i:D4}.cluster.example.com").ToArray();
            using var certificate = Create("CN=unrelated", builder =>
            {
                foreach (var name in names)
                    builder.AddDnsName(name);
            });

            foreach (var name in names)
                Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, name), name);

            Assert.False(CertificateHostnameMatcher.MatchesHostname(certificate, "node-9999.cluster.example.com"));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_DnsNameIsLongerThan127Characters")]
        public void Should_AcceptCertificate_When_DnsNameIsLongerThan127Characters()
        {
            var label = new string('a', 60);
            var name = $"{label}.{label}.{label}.example.com";
            using var certificate = Create("CN=unrelated", $"dns:{name}");

            Assert.True(CertificateHostnameMatcher.MatchesHostname(certificate, name));
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_CommonNameUsesAnyDirectoryStringType")]
        [InlineData(Utf8StringTag)]
        [InlineData(PrintableStringTag)]
        [InlineData(Ia5StringTag)]
        [InlineData(T61StringTag)]
        public void Should_AcceptCertificate_When_CommonNameUsesAnyDirectoryStringType(byte stringTag)
        {
            var subject = Name(
                Rdn(Attribute(OrganizationOid, Der(PrintableStringTag, Encoding.ASCII.GetBytes("Akka")))),
                CommonName(stringTag, Encoding.ASCII.GetBytes("www.example.com")));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subject));
            Assert.True(CertificateHostnameMatcher.MatchesHostname("WWW.EXAMPLE.COM", null, subject));
            Assert.False(CertificateHostnameMatcher.MatchesHostname("other.example.com", null, subject));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_CommonNameIsBmpString")]
        public void Should_AcceptCertificate_When_CommonNameIsBmpString()
        {
            var subject = Name(CommonName(BmpStringTag, Encoding.BigEndianUnicode.GetBytes("www.example.com")));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subject));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_CommonNameIsT61StringWithLatin1Bytes")]
        public void Should_AcceptCertificate_When_CommonNameIsT61StringWithLatin1Bytes()
        {
            // 0xFC is not valid UTF-8 on its own, so the value is read as ISO-8859-1: "bücher.example"
            var content = new byte[] { (byte)'b', 0xFC }.Concat(Encoding.ASCII.GetBytes("cher.example")).ToArray();
            var subject = Name(CommonName(T61StringTag, content));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("bücher.example", null, subject));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_CommonNameHasTrailingNul")]
        public void Should_AcceptCertificate_When_CommonNameHasTrailingNul()
        {
            var subject = Name(CommonName(Utf8StringTag, Encoding.ASCII.GetBytes("www.example.com\0")));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subject));
        }

        /// <summary>
        /// Subject names that are unusual but decode: each holds the single common name www.example.com.
        /// Only the common name's value is decoded as text; the values of other attributes are not.
        /// </summary>
        public static TheoryData<string, byte[]> UnusualSubjectNames()
        {
            var commonName = CommonName("www.example.com");
            var text = Encoding.ASCII.GetBytes("Akka");
            byte[] WithOrganization(byte[] value) => Name(Rdn(Attribute(OrganizationOid, value)), commonName);

            return new TheoryData<string, byte[]>
            {
                { "another attribute is a PrintableString with a forbidden character", WithOrganization(Der(PrintableStringTag, Encoding.ASCII.GetBytes("Akka_Team"))) },
                { "another attribute is invalid UTF-8", WithOrganization(Der(Utf8StringTag, new byte[] { 0xFF })) },
                { "another attribute is an OCTET STRING", WithOrganization(Der(OctetStringTag, new byte[] { 1, 2, 3 })) },
                { "another attribute is a constructed UTF8String", WithOrganization(Der(0x2C, Der(Utf8StringTag, text))) },
                { "another attribute has an application-class tag", WithOrganization(new byte[] { 0x42, 0x01, (byte)'a' }) },
                { "another attribute has a tag number above 30", WithOrganization(new byte[] { 0x9F, 0x21, 0x01, (byte)'a' }) },
                { "another attribute type has a 128-bit arc", Name(Rdn(Attribute(OidWithArcOfBits(128), Der(Utf8StringTag, text))), commonName) },
                { "another attribute type has 63 sub-identifiers", Name(Rdn(Attribute(OidWithSubIdentifiers(63), Der(Utf8StringTag, text))), commonName) },
                { "a multi-valued RDN without a common name, out of DER order",
                    Name(commonName, Rdn(Attribute(OrganizationalUnitOid, Der(Utf8StringTag, new[] { (byte)'z' })), Attribute(OrganizationOid, Der(Utf8StringTag, new[] { (byte)'a' })))) },
            };
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_SubjectNameIsUnusualButDecodes")]
        [MemberData(nameof(UnusualSubjectNames))]
        public void Should_AcceptCertificate_When_SubjectNameIsUnusualButDecodes(string reason, byte[] subjectName)
        {
            _output.WriteLine(reason);
            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subjectName));
            Assert.False(CertificateHostnameMatcher.MatchesHostname("other.example.com", null, subjectName));
        }

        [Fact(DisplayName = "Should_AcceptCertificate_When_SubjectAlternativeNameHoldsEveryOtherKindOfName")]
        public void Should_AcceptCertificate_When_SubjectAlternativeNameHoldsEveryOtherKindOfName()
        {
            var oid = new byte[] { 0x2A, 0x03, 0x04 }; // 1.2.3.4
            var subjectAlternativeName = Der(0x30,
                // otherName [0]: SEQUENCE { type-id OID, value [0] EXPLICIT UTF8String }
                Der(0xA0, Der(0x06, oid), Der(0xA0, Der(Utf8StringTag, Encoding.ASCII.GetBytes("upn@example.com")))),
                // rfc822Name [1]
                Der(0x81, Encoding.ASCII.GetBytes("ops@example.com")),
                // x400Address [3]
                Der(0xA3, Der(0x30)),
                // directoryName [4]: EXPLICIT Name
                Der(0xA4, Name(CommonName("directory.example.com"))),
                // ediPartyName [5]: SEQUENCE { partyName [1] EXPLICIT UTF8String }
                Der(0xA5, Der(0xA1, Der(Utf8StringTag, Encoding.ASCII.GetBytes("party")))),
                // uniformResourceIdentifier [6]
                Der(0x86, Encoding.ASCII.GetBytes("https://example.com/")),
                // registeredID [8]
                Der(0x88, oid),
                DnsName("www.example.com"),
                IpAddress(10, 0, 0, 1));
            var subject = Name(CommonName("unrelated"));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", subjectAlternativeName, subject));
            Assert.True(CertificateHostnameMatcher.MatchesHostname("10.0.0.1", subjectAlternativeName, subject));
            Assert.False(CertificateHostnameMatcher.MatchesHostname("directory.example.com", subjectAlternativeName, subject));
        }

        /// <summary>
        /// Well-formed SAN entries of every kind the match does not use. Real certificates carry these:
        /// Windows CAs add otherName entries (user principal name, domain controller GUID), and rfc822Name
        /// and URI entries are common. None of them may cost a certificate its match.
        /// </summary>
        public static TheoryData<string, byte[]> WellFormedEntriesOfOtherKinds()
        {
            var oid = new byte[] { 0x2A, 0x03, 0x04 }; // 1.2.3.4
            var text = Encoding.ASCII.GetBytes("party");
            byte[] EdiPartyName(params byte[][] fields) => Der(0xA5, fields);

            return new TheoryData<string, byte[]>
            {
                // otherName [0] ::= SEQUENCE { type-id OBJECT IDENTIFIER, value [0] EXPLICIT ANY }
                { "otherName: user principal name", OtherName(UserPrincipalNameOid, Der(Utf8StringTag, Encoding.ASCII.GetBytes("node-1@example.com"))) },
                { "otherName: domain controller GUID", OtherName(new byte[] { 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x19, 0x01 }, Der(OctetStringTag, Enumerable.Range(1, 16).Select(i => (byte)i).ToArray())) },
                { "otherName: Kerberos principal name",
                    OtherName(new byte[] { 0x2B, 0x06, 0x01, 0x05, 0x02, 0x02 },
                        Der(0x30,
                            Der(0xA0, Der(0x1B, Encoding.ASCII.GetBytes("EXAMPLE.COM"))),
                            Der(0xA1, Der(0x30, Der(0xA0, Der(0x02, new byte[] { 1 })), Der(0xA1, Der(0x30, Der(0x1B, Encoding.ASCII.GetBytes("node-1")))))))) },
                { "otherName: SmtpUTF8Mailbox", OtherName(new byte[] { 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x08, 0x09 }, Der(Utf8StringTag, Encoding.UTF8.GetBytes("b\u00FCcher@example.com"))) },
                { "otherName: IA5String value", OtherName(oid, Der(Ia5StringTag, text)) },
                { "otherName: NULL value", OtherName(oid, new byte[] { 0x05, 0x00 }) },
                { "otherName: empty OCTET STRING value", OtherName(oid, Der(OctetStringTag)) },
                { "otherName: value that is invalid UTF-8", OtherName(oid, Der(Utf8StringTag, new byte[] { 0xFF })) },
                { "otherName: value with a tag number above 30", OtherName(oid, new byte[] { 0x9F, 0x21, 0x01, (byte)'a' }) },
                { "otherName: constructed value whose content is not DER", OtherName(oid, Der(0x30, new byte[] { 0xFF, 0xFF })) },
                { "otherName: type-id with a single byte", OtherName(new byte[] { 0x2A }, Der(Utf8StringTag, text)) },
                { "otherName: type-id with a 128-bit arc", OtherName(OidWithArcOfBits(128), Der(Utf8StringTag, text)) },
                { "otherName: type-id with 63 sub-identifiers", OtherName(OidWithSubIdentifiers(63), Der(Utf8StringTag, text)) },

                // rfc822Name [1] IA5String
                { "rfc822Name: mailbox", Der(0x81, Encoding.ASCII.GetBytes("ops@example.com")) },
                { "rfc822Name: empty", Der(0x81) },
                { "rfc822Name: control characters", Der(0x81, new byte[] { 0x00, 0x7F, (byte)'a' }) },
                { "rfc822Name: 200 characters", Der(0x81, Encoding.ASCII.GetBytes(new string('a', 188) + "@example.com")) },

                // x400Address [3]: read as one opaque value
                { "x400Address: constructed", Der(0xA3, Der(0x30, Der(0x31))) },
                { "x400Address: primitive", Der(0x83, new byte[] { 1, 2, 3 }) },
                { "x400Address: empty", Der(0xA3) },
                { "x400Address: content that is not DER", Der(0xA3, new byte[] { 0xFF, 0xFF }) },

                // directoryName [4] EXPLICIT Name: the wrapper must hold one value, which is not decoded
                { "directoryName: name", Der(0xA4, Name(CommonName("directory.example.com"))) },
                { "directoryName: empty name", Der(0xA4, Der(0x30)) },
                { "directoryName: a value that is not a name", Der(0xA4, Der(OctetStringTag, new byte[] { 1, 2, 3 })) },
                { "directoryName: name whose content is not DER", Der(0xA4, Der(0x30, new byte[] { 0xFF, 0xFF })) },

                // ediPartyName [5] ::= SEQUENCE { nameAssigner [0] EXPLICIT DirectoryString OPTIONAL, partyName [1] EXPLICIT DirectoryString }
                { "ediPartyName: UTF8String party name", EdiPartyName(Der(0xA1, Der(Utf8StringTag, text))) },
                { "ediPartyName: PrintableString party name", EdiPartyName(Der(0xA1, Der(PrintableStringTag, text))) },
                { "ediPartyName: T61String party name with a Latin-1 byte", EdiPartyName(Der(0xA1, Der(T61StringTag, new byte[] { (byte)'b', 0xFC }))) },
                { "ediPartyName: BMPString party name", EdiPartyName(Der(0xA1, Der(BmpStringTag, Encoding.BigEndianUnicode.GetBytes("party")))) },
                { "ediPartyName: UniversalString party name", EdiPartyName(Der(0xA1, Der(UniversalStringTag, new byte[] { 0, 0, 0, (byte)'p' }))) },
                { "ediPartyName: UniversalString party name of odd length", EdiPartyName(Der(0xA1, Der(UniversalStringTag, new byte[] { 1, 2, 3 }))) },
                { "ediPartyName: constructed UniversalString party name", EdiPartyName(Der(0xA1, Der(0x3C, new byte[] { 0xFF, 0xFF }))) },
                { "ediPartyName: empty party name", EdiPartyName(Der(0xA1, Der(Utf8StringTag))) },
                { "ediPartyName: name assigner and party name", EdiPartyName(Der(0xA0, Der(PrintableStringTag, text)), Der(0xA1, Der(Utf8StringTag, text))) },

                // uniformResourceIdentifier [6] IA5String
                { "URI: https", Der(0x86, Encoding.ASCII.GetBytes("https://example.com/akka")) },
                { "URI: Windows security identifier", Der(0x86, Encoding.ASCII.GetBytes("tag:microsoft.com,2022-09-14:sid:S-1-5-21-1004336348-1177238915-682003330-512")) },
                { "URI: SPIFFE identity", Der(0x86, Encoding.ASCII.GetBytes("spiffe://cluster.local/ns/default/sa/akka")) },
                { "URI: empty", Der(0x86) },

                // registeredID [8] OBJECT IDENTIFIER
                { "registeredID: short", Der(0x88, oid) },
                { "registeredID: single byte", Der(0x88, new byte[] { 0x2A }) },
                { "registeredID: 128-bit arc", Der(0x88, OidWithArcOfBits(128)) },
                { "registeredID: 63 sub-identifiers", Der(0x88, OidWithSubIdentifiers(63)) },
            };
        }

        /// <summary>
        /// Malformed SAN entries of every kind the match does not use. <c>X509Certificate2.MatchesHostname</c>
        /// decodes the whole extension and throws on each of these, so the matcher must reject them too.
        /// </summary>
        public static TheoryData<string, byte[]> MalformedEntriesOfOtherKinds()
        {
            var oid = Der(0x06, new byte[] { 0x2A, 0x03, 0x04 }); // 1.2.3.4
            var text = Encoding.ASCII.GetBytes("party");
            var value = Der(Utf8StringTag, text);
            byte[] EdiPartyName(params byte[][] fields) => Der(0xA5, fields);

            return new TheoryData<string, byte[]>
            {
                // otherName
                { "otherName: not constructed", Der(0x80, new byte[] { 1, 2, 3 }) },
                { "otherName: empty", Der(0xA0) },
                { "otherName: no value", Der(0xA0, oid) },
                { "otherName: no type-id", Der(0xA0, Der(0xA0, value)) },
                { "otherName: type-id is not an OID", Der(0xA0, Der(Utf8StringTag, text), Der(0xA0, value)) },
                { "otherName: constructed type-id", Der(0xA0, Der(0x26, oid), Der(0xA0, value)) },
                { "otherName: empty type-id", Der(0xA0, Der(0x06), Der(0xA0, value)) },
                { "otherName: type-id with an unfinished arc", Der(0xA0, Der(0x06, new byte[] { 0x2A, 0x83 }), Der(0xA0, value)) },
                { "otherName: type-id with a padded arc", Der(0xA0, Der(0x06, new byte[] { 0x2A, 0x80, 0x01 }), Der(0xA0, value)) },
                { "otherName: type-id with a 129-bit arc", Der(0xA0, Der(0x06, OidWithArcOfBits(129)), Der(0xA0, value)) },
                { "otherName: type-id with 64 sub-identifiers", Der(0xA0, Der(0x06, OidWithSubIdentifiers(64)), Der(0xA0, value)) },
                { "otherName: value tagged [1]", Der(0xA0, oid, Der(0xA1, value)) },
                { "otherName: value without its [0] wrapper", Der(0xA0, oid, value) },
                { "otherName: primitive [0] wrapper", Der(0xA0, oid, Der(0x80, text)) },
                { "otherName: empty [0] wrapper", Der(0xA0, oid, Der(0xA0)) },
                { "otherName: two values in the [0] wrapper", Der(0xA0, oid, Der(0xA0, value, value)) },
                { "otherName: a field after the value", Der(0xA0, oid, Der(0xA0, value), new byte[] { 0x05, 0x00 }) },
                { "otherName: value before type-id", Der(0xA0, Der(0xA0, value), oid) },
                { "otherName: value longer than its wrapper", Der(0xA0, oid, Der(0xA0, new byte[] { Utf8StringTag, 0x05, (byte)'a' })) },
                { "otherName: value of indefinite length", Der(0xA0, oid, Der(0xA0, new byte[] { 0x30, 0x80, 0x00, 0x00 })) },
                { "otherName: value with a padded length", Der(0xA0, oid, Der(0xA0, new byte[] { Utf8StringTag, 0x81, 0x01, (byte)'a' })) },
                { "otherName: type-id with a padded length", Der(0xA0, new byte[] { 0x06, 0x81, 0x03, 0x2A, 0x03, 0x04 }, Der(0xA0, value)) },

                // rfc822Name
                { "rfc822Name: non-ASCII byte", Der(0x81, new byte[] { (byte)'a', 0xE9 }) },
                { "rfc822Name: constructed", Der(0xA1, Der(Ia5StringTag, text)) },
                { "rfc822Name: padded length", new byte[] { 0x81, 0x81, 0x01, (byte)'a' } },

                // x400Address is opaque to the framework, so only its envelope can be wrong
                { "x400Address: longer than the extension", new byte[] { 0xA3, 0x7F, 0x30 } },

                // directoryName
                { "directoryName: not constructed", Der(0x84, new byte[] { 1, 2, 3 }) },
                { "directoryName: empty", Der(0xA4) },
                { "directoryName: two names", Der(0xA4, Der(0x30), Der(0x30)) },
                { "directoryName: name longer than its wrapper", Der(0xA4, new byte[] { 0x30, 0x05, 0x31 }) },

                // ediPartyName
                { "ediPartyName: not constructed", Der(0x85, new byte[] { 1, 2, 3 }) },
                { "ediPartyName: empty", EdiPartyName() },
                { "ediPartyName: name assigner without a party name", EdiPartyName(Der(0xA0, value)) },
                { "ediPartyName: party name before name assigner", EdiPartyName(Der(0xA1, value), Der(0xA0, value)) },
                { "ediPartyName: two name assigners", EdiPartyName(Der(0xA0, value), Der(0xA0, value), Der(0xA1, value)) },
                { "ediPartyName: a field after the party name", EdiPartyName(Der(0xA1, value), Der(0xA2, value)) },
                { "ediPartyName: unknown field only", EdiPartyName(Der(0xA2, value)) },
                { "ediPartyName: party name without its [1] wrapper", EdiPartyName(value) },
                { "ediPartyName: primitive [1] wrapper", EdiPartyName(Der(0x81, text)) },
                { "ediPartyName: primitive [0] wrapper", EdiPartyName(Der(0x80, text), Der(0xA1, value)) },
                { "ediPartyName: empty [1] wrapper", EdiPartyName(Der(0xA1)) },
                { "ediPartyName: two strings in the [1] wrapper", EdiPartyName(Der(0xA1, value, value)) },
                { "ediPartyName: IA5String party name", EdiPartyName(Der(0xA1, Der(Ia5StringTag, text))) },
                { "ediPartyName: NumericString party name", EdiPartyName(Der(0xA1, Der(NumericStringTag, new[] { (byte)'1' }))) },
                { "ediPartyName: OCTET STRING party name", EdiPartyName(Der(0xA1, Der(OctetStringTag, text))) },
                { "ediPartyName: PrintableString party name with a forbidden character", EdiPartyName(Der(0xA1, Der(PrintableStringTag, Encoding.ASCII.GetBytes("a_b")))) },
                { "ediPartyName: party name that is invalid UTF-8", EdiPartyName(Der(0xA1, Der(Utf8StringTag, new byte[] { 0xFF }))) },
                { "ediPartyName: BMPString party name of odd length", EdiPartyName(Der(0xA1, Der(BmpStringTag, new byte[] { 0x00, (byte)'a', 0x00 }))) },
                { "ediPartyName: BMPString party name with a surrogate", EdiPartyName(Der(0xA1, Der(BmpStringTag, new byte[] { 0xD8, 0x00, 0xDC, 0x00 }))) },
                { "ediPartyName: constructed UTF8String party name", EdiPartyName(Der(0xA1, Der(0x2C, value))) },
                { "ediPartyName: constructed T61String party name", EdiPartyName(Der(0xA1, Der(0x34, Der(T61StringTag, text)))) },
                { "ediPartyName: malformed name assigner", EdiPartyName(Der(0xA0, Der(Ia5StringTag, text)), Der(0xA1, value)) },
                { "ediPartyName: party name with a padded length", EdiPartyName(Der(0xA1, new byte[] { Utf8StringTag, 0x81, 0x01, (byte)'a' })) },
                { "ediPartyName: UniversalString party name longer than its wrapper", EdiPartyName(Der(0xA1, new byte[] { UniversalStringTag, 0x08, 0, 0, 0, (byte)'p' })) },

                // uniformResourceIdentifier
                { "URI: non-ASCII byte", Der(0x86, new byte[] { (byte)'a', 0xE9 }) },
                { "URI: constructed", Der(0xA6, Der(Ia5StringTag, text)) },

                // registeredID
                { "registeredID: empty", Der(0x88) },
                { "registeredID: unfinished arc", Der(0x88, new byte[] { 0x2A, 0x83 }) },
                { "registeredID: padded arc", Der(0x88, new byte[] { 0x2A, 0x80, 0x01 }) },
                { "registeredID: padded first arc", Der(0x88, new byte[] { 0x80, 0x01 }) },
                { "registeredID: constructed", Der(0xA8, oid) },
                { "registeredID: 129-bit arc", Der(0x88, OidWithArcOfBits(129)) },
                { "registeredID: 64 sub-identifiers", Der(0x88, OidWithSubIdentifiers(64)) },
            };
        }

        [Theory(DisplayName = "Should_AcceptCertificate_When_SubjectAlternativeNameHoldsWellFormedEntryOfAnotherKind")]
        [MemberData(nameof(WellFormedEntriesOfOtherKinds))]
        public void Should_AcceptCertificate_When_SubjectAlternativeNameHoldsWellFormedEntryOfAnotherKind(string kind, byte[] entry)
        {
            _output.WriteLine(kind);
            var subject = Name(CommonName("fallback.example.com"));

            foreach (var subjectAlternativeName in new[]
                     {
                         Der(0x30, entry, DnsName("www.example.com"), IpAddress(10, 0, 0, 1)),
                         Der(0x30, DnsName("www.example.com"), IpAddress(10, 0, 0, 1), entry),
                     })
            {
                Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", subjectAlternativeName, subject));
                Assert.True(CertificateHostnameMatcher.MatchesHostname("10.0.0.1", subjectAlternativeName, subject));
                Assert.False(CertificateHostnameMatcher.MatchesHostname("other.example.com", subjectAlternativeName, subject));
                Assert.False(CertificateHostnameMatcher.MatchesHostname("fallback.example.com", subjectAlternativeName, subject));
            }

            // On its own the entry is neither a DNS name nor an IP address, so the common name decides.
            Assert.True(CertificateHostnameMatcher.MatchesHostname("fallback.example.com", Der(0x30, entry), subject));
            Assert.False(CertificateHostnameMatcher.MatchesHostname("www.example.com", Der(0x30, entry), subject));
        }

        [Theory(DisplayName = "Should_RejectCertificate_When_SubjectAlternativeNameHoldsMalformedEntryOfAnotherKind")]
        [MemberData(nameof(MalformedEntriesOfOtherKinds))]
        public void Should_RejectCertificate_When_SubjectAlternativeNameHoldsMalformedEntryOfAnotherKind(string kind, byte[] entry)
        {
            // The certificate names www.example.com and 10.0.0.1 correctly. X509Certificate2.MatchesHostname
            // still rejects it, because it decodes the whole extension first. Akka.NET v1.6 calls that
            // method, and v1.5 must give the same answer.
            _output.WriteLine(kind);
            var subject = Name(CommonName("www.example.com"));

            foreach (var subjectAlternativeName in new[]
                     {
                         Der(0x30, entry, DnsName("www.example.com"), IpAddress(10, 0, 0, 1)),
                         Der(0x30, DnsName("www.example.com"), IpAddress(10, 0, 0, 1), entry),
                         Der(0x30, entry),
                     })
            {
                Assert.ThrowsAny<CryptographicException>(() =>
                    CertificateHostnameMatcher.MatchesHostname("www.example.com", subjectAlternativeName, subject));
                Assert.ThrowsAny<CryptographicException>(() =>
                    CertificateHostnameMatcher.MatchesHostname("10.0.0.1", subjectAlternativeName, subject));
            }
        }

        [Fact(DisplayName = "Should_FallBackToCommonName_When_SubjectAlternativeNameIsEmpty")]
        public void Should_FallBackToCommonName_When_SubjectAlternativeNameIsEmpty()
        {
            var subject = Name(CommonName("www.example.com"));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", Der(0x30), subject));
        }

        [Fact(DisplayName = "Should_AcceptRepositoryTestCertificates_When_ExpectedNameIsTheirOwnName")]
        public void Should_AcceptRepositoryTestCertificates_When_ExpectedNameIsTheirOwnName()
        {
            using var client = CertificateHelper.LoadPkcs12(ClientCertPath, Password);
            using var server = CertificateHelper.LoadPkcs12(ValidCertPath, Password);

            Assert.True(CertificateHostnameMatcher.MatchesHostname(client, "AkkaTestClient"));
            Assert.True(CertificateHostnameMatcher.MatchesHostname(client, "akkatestclient"));
            Assert.False(CertificateHostnameMatcher.MatchesHostname(client, "unrelated.invalid"));
            Assert.False(CertificateHostnameMatcher.MatchesHostname(server, "unrelated.invalid"));
        }

        #endregion

        #region Certificates that must be rejected

        [Theory(DisplayName = "Should_RejectCertificate_When_ExpectedNameDoesNotMatch")]
        // a different name
        [InlineData("CN=unrelated", "dns:www.example.com", "example.com")]
        [InlineData("CN=unrelated", "dns:www.example.com", "other.example.com")]
        [InlineData("CN=unrelated", "dns:www.example.com", "www.example.org")]
        [InlineData("CN=unrelated", "dns:www.example.com", "wwww.example.com")]
        // a wildcard stands for exactly one label, the leftmost one
        [InlineData("CN=unrelated", "dns:*.example.com", "example.com")]
        [InlineData("CN=unrelated", "dns:*.example.com", "a.b.example.com")]
        [InlineData("CN=unrelated", "dns:*.example.com", "www.example.org")]
        [InlineData("CN=unrelated", "dns:*.example.com", "localhost")]
        // anything else with a star in it is an ordinary name, and no hostname equals it
        [InlineData("CN=unrelated", "dns:w*.example.com", "www.example.com")]
        [InlineData("CN=unrelated", "dns:www.*.com", "www.example.com")]
        [InlineData("CN=unrelated", "dns:*", "localhost")]
        [InlineData("CN=unrelated", "dns:*.", "localhost")]
        // the common name is not consulted once the certificate has a SAN entry of the wanted kind
        [InlineData("CN=www.example.com", "dns:other.example.com", "www.example.com")]
        [InlineData("CN=www.example.com", "dns:", "www.example.com")]
        [InlineData("CN=127.0.0.1", "ip:10.0.0.1", "127.0.0.1")]
        // the common name gets no wildcard and no trailing-dot handling
        [InlineData("CN=*.example.com", "", "www.example.com")]
        [InlineData("CN=www.example.com", "", "www.example.com.")]
        [InlineData("CN=www.example.com", "", "example.com")]
        // a subject with two common names, or none, has no usable common name
        [InlineData("CN=www.example.com, CN=other.example.com", "", "www.example.com")]
        [InlineData("O=Akka", "", "www.example.com")]
        [InlineData("", "", "www.example.com")]
        // IP addresses match IP entries only, and exactly
        [InlineData("CN=unrelated", "ip:127.0.0.1", "127.0.0.2")]
        [InlineData("CN=unrelated", "ip:127.0.0.1", "::ffff:127.0.0.1")]
        [InlineData("CN=unrelated", "ip:::1", "127.0.0.1")]
        [InlineData("CN=unrelated", "dns:127.0.0.1", "127.0.0.1")]
        // no IDNA mapping: the Unicode form does not match the punycode form
        [InlineData("CN=unrelated", "dns:xn--bcher-kva.example", "bücher.example")]
        public void Should_RejectCertificate_When_ExpectedNameDoesNotMatch(
            string subject, string subjectAlternativeNames, string expectedHostname)
        {
            using var certificate = Create(subject, subjectAlternativeNames);

            Assert.False(CertificateHostnameMatcher.MatchesHostname(certificate, expectedHostname));
        }

        [Fact(DisplayName = "Should_RejectCertificate_When_CommonNameIsPartOfMultiValuedRdn")]
        public void Should_RejectCertificate_When_CommonNameIsPartOfMultiValuedRdn()
        {
            var subject = Name(Rdn(
                Attribute(CommonNameOid, Der(Utf8StringTag, Encoding.ASCII.GetBytes("www.example.com"))),
                Attribute(OrganizationalUnitOid, Der(Utf8StringTag, Encoding.ASCII.GetBytes("Cluster")))));

            Assert.False(CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subject));
        }

        /// <summary>
        /// Subjects whose only common name has a value that is not one of the six text types the framework
        /// reads. Such a common name matches nothing, and it is not an error.
        /// </summary>
        public static TheoryData<string, byte[]> NonTextCommonNames()
        {
            var text = Encoding.ASCII.GetBytes("www.example.com");
            return new TheoryData<string, byte[]>
            {
                { "OCTET STRING", Name(CommonName(OctetStringTag, text)) },
                { "VisibleString", Name(CommonName(0x1A, text)) },
                { "UniversalString", Name(CommonName(UniversalStringTag, text.Concat(new[] { (byte)'!' }).ToArray())) },
                { "context-specific tag", Name(Rdn(Attribute(CommonNameOid, Der(0x8C, text)))) },
                { "tag number above 30", Name(Rdn(Attribute(CommonNameOid, new byte[] { 0x1F, 0x21, 0x01, (byte)'a' }))) },
            };
        }

        [Theory(DisplayName = "Should_RejectCertificate_When_CommonNameIsNotText")]
        [MemberData(nameof(NonTextCommonNames))]
        public void Should_RejectCertificate_When_CommonNameIsNotText(string valueType, byte[] subjectName)
        {
            _output.WriteLine(valueType);
            Assert.False(CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subjectName));
            Assert.False(CertificateHostnameMatcher.MatchesHostname("a", null, subjectName));
        }

        [Theory(DisplayName = "Should_ThrowArgumentException_When_ExpectedNameIsNotDnsNameOrIpAddress")]
        [InlineData("")]
        [InlineData("not a valid hostname!")]
        [InlineData("exa mple.com")]
        [InlineData("a..b")]
        public void Should_ThrowArgumentException_When_ExpectedNameIsNotDnsNameOrIpAddress(string expectedHostname)
        {
            using var certificate = Create("CN=www.example.com", "dns:www.example.com");

            Assert.Throws<ArgumentException>(() => CertificateHostnameMatcher.MatchesHostname(certificate, expectedHostname));
        }

        [Fact(DisplayName = "Should_ThrowArgumentNullException_When_ArgumentIsNull")]
        public void Should_ThrowArgumentNullException_When_ArgumentIsNull()
        {
            using var certificate = Create("CN=www.example.com", "");

            Assert.Throws<ArgumentNullException>(() => CertificateHostnameMatcher.MatchesHostname(certificate, null!));
            Assert.Throws<ArgumentNullException>(() => CertificateHostnameMatcher.MatchesHostname(null!, "www.example.com"));
        }

        public static TheoryData<string, byte[]> MalformedSubjectAlternativeNames()
        {
            var match = DnsName("www.example.com");
            return new TheoryData<string, byte[]>
            {
                { "empty value", Array.Empty<byte>() },
                { "not a SEQUENCE", Der(0x31, match) },
                { "trailing bytes after the SEQUENCE", Der(0x30, match).Concat(new byte[] { 0x00 }).ToArray() },
                { "truncated", Der(0x30, match).Take(10).ToArray() },
                { "entry longer than the SEQUENCE", new byte[] { 0x30, 0x03, 0x82, 0x05, (byte)'a' } },
                { "indefinite length", new byte[] { 0x30, 0x80, 0x82, 0x01, (byte)'a', 0x00, 0x00 } },
                { "length not in shortest form", new byte[] { 0x30, 0x81, 0x03, 0x82, 0x01, (byte)'a' } },
                { "length with a leading zero", new byte[] { 0x30, 0x82, 0x00, 0x03, 0x82, 0x01, (byte)'a' } },
                { "reserved length byte", new byte[] { 0x30, 0xFF, 0x82, 0x01, (byte)'a' } },
                { "five length bytes", new byte[] { 0x30, 0x85, 0x00, 0x00, 0x00, 0x00, 0x03, 0x82, 0x01, (byte)'a' } },
                { "dNSName with a non-ASCII byte", Der(0x30, match, Der(0x82, new byte[] { (byte)'a', 0xE9 })) },
                { "dNSName in constructed form", Der(0x30, match, Der(0xA2, Der(0x16, new[] { (byte)'a' }))) },
                { "iPAddress of five bytes", Der(0x30, match, IpAddress(10, 0, 0, 0, 1)) },
                { "iPAddress of no bytes", Der(0x30, match, IpAddress()) },
                { "iPAddress in constructed form", Der(0x30, match, Der(0xA7, Der(0x04, new byte[] { 10, 0, 0, 1 }))) },
                { "GeneralName tag [9]", Der(0x30, match, Der(0x89, new[] { (byte)'a' })) },
                { "universal tag where a GeneralName belongs", Der(0x30, match, Der(Ia5StringTag, new[] { (byte)'a' })) },
                { "application tag where a GeneralName belongs", Der(0x30, match, Der(0x42, new[] { (byte)'a' })) },
                { "multi-byte tag where a GeneralName belongs", Der(0x30, match, new byte[] { 0x9F, 0x21, 0x01, (byte)'a' }) },
            };
        }

        [Theory(DisplayName = "Should_ThrowCryptographicException_When_SubjectAlternativeNameIsMalformed")]
        [MemberData(nameof(MalformedSubjectAlternativeNames))]
        public void Should_ThrowCryptographicException_When_SubjectAlternativeNameIsMalformed(string reason, byte[] subjectAlternativeName)
        {
            // Every one of these either holds a matching dNSName or sits beside a matching common name.
            // A malformed extension must reject the certificate all the same.
            var subject = Name(CommonName("www.example.com"));

            _output.WriteLine(reason);
            Assert.ThrowsAny<CryptographicException>(() =>
                CertificateHostnameMatcher.MatchesHostname("www.example.com", subjectAlternativeName, subject));
        }

        public static TheoryData<string, byte[]> MalformedSubjectNames()
        {
            var commonName = CommonName("www.example.com");
            var text = Encoding.ASCII.GetBytes("www.example.com");
            return new TheoryData<string, byte[]>
            {
                { "empty value", Array.Empty<byte>() },
                { "not a SEQUENCE", Der(0x31, commonName) },
                { "trailing bytes after the SEQUENCE", Name(commonName).Concat(new byte[] { 0x00 }).ToArray() },
                { "truncated", Name(commonName).Take(12).ToArray() },
                { "RDN is not a SET", Name(Der(0x30, Attribute(CommonNameOid, Der(Utf8StringTag, text)))) },
                { "empty RDN", Name(commonName, Rdn()) },
                { "attribute is not a SEQUENCE", Name(commonName, Rdn(Der(0x31, Der(0x06, OrganizationOid), Der(Utf8StringTag, text)))) },
                { "attribute type is not an OID", Name(commonName, Rdn(Der(0x30, Der(Utf8StringTag, text), Der(Utf8StringTag, text)))) },
                { "attribute without a value", Name(commonName, Rdn(Der(0x30, Der(0x06, OrganizationOid)))) },
                { "attribute with two values", Name(commonName, Rdn(Der(0x30, Der(0x06, OrganizationOid), Der(Utf8StringTag, text), Der(Utf8StringTag, text)))) },
                { "empty OID", Name(commonName, Rdn(Attribute(Array.Empty<byte>(), Der(Utf8StringTag, text)))) },
                { "OID with an unfinished arc", Name(commonName, Rdn(Attribute(new byte[] { 0x55, 0x04, 0x8A }, Der(Utf8StringTag, text)))) },
                { "OID with a padded arc", Name(commonName, Rdn(Attribute(new byte[] { 0x55, 0x80, 0x04 }, Der(Utf8StringTag, text)))) },
                { "OID with a 129-bit arc", Name(commonName, Rdn(Attribute(OidWithArcOfBits(129), Der(Utf8StringTag, text)))) },
                { "OID with 64 sub-identifiers", Name(commonName, Rdn(Attribute(OidWithSubIdentifiers(64), Der(Utf8StringTag, text)))) },
                { "constructed OID", Name(commonName, Rdn(Der(0x30, Der(0x26, Der(0x06, OrganizationOid)), Der(Utf8StringTag, text)))) },
                { "common name is invalid UTF-8", Name(CommonName(Utf8StringTag, new byte[] { (byte)'a', 0xFF })) },
                { "common name is a PrintableString with a forbidden character", Name(CommonName(PrintableStringTag, Encoding.ASCII.GetBytes("www_1.example.com"))) },
                { "common name is an IA5String with a non-ASCII byte", Name(CommonName(Ia5StringTag, new byte[] { (byte)'a', 0xE9 })) },
                { "common name is a NumericString with a letter", Name(CommonName(NumericStringTag, text)) },
                { "common name is a BMPString of odd length", Name(CommonName(BmpStringTag, new byte[] { 0x00, (byte)'a', 0x00 })) },
                { "common name is a BMPString with a surrogate", Name(CommonName(BmpStringTag, new byte[] { 0xD8, 0x00, 0xDC, 0x00 })) },
                { "common name is a constructed UTF8String", Name(CommonName(0x2C, Der(Utf8StringTag, text))) },
            };
        }

        [Theory(DisplayName = "Should_ThrowCryptographicException_When_SubjectNameIsMalformed")]
        [MemberData(nameof(MalformedSubjectNames))]
        public void Should_ThrowCryptographicException_When_SubjectNameIsMalformed(string reason, byte[] subjectName)
        {
            _output.WriteLine(reason);
            Assert.ThrowsAny<CryptographicException>(() =>
                CertificateHostnameMatcher.MatchesHostname("www.example.com", null, subjectName));
        }

        [Fact(DisplayName = "Should_NotReadSubjectName_When_SubjectAlternativeNameDecidesTheMatch")]
        public void Should_NotReadSubjectName_When_SubjectAlternativeNameDecidesTheMatch()
        {
            // X509Certificate2.MatchesHostname only decodes the subject when it falls back to the common name.
            var malformedSubject = new byte[] { 0x31, 0x00 };
            var subjectAlternativeName = Der(0x30, DnsName("www.example.com"));

            Assert.True(CertificateHostnameMatcher.MatchesHostname("www.example.com", subjectAlternativeName, malformedSubject));
            Assert.False(CertificateHostnameMatcher.MatchesHostname("other.example.com", subjectAlternativeName, malformedSubject));
        }

        #endregion

#if NET7_0_OR_GREATER

        #region Agreement with X509Certificate2.MatchesHostname

        private enum Outcome
        {
            Match,
            NoMatch,
            InvalidHostname,
            InvalidCertificate
        }

        private static Outcome OutcomeOf(Func<bool> match)
        {
            try
            {
                return match() ? Outcome.Match : Outcome.NoMatch;
            }
            catch (ArgumentException)
            {
                return Outcome.InvalidHostname;
            }
            catch (CryptographicException)
            {
                return Outcome.InvalidCertificate;
            }
        }

        /// <summary>
        /// <c>X509Certificate2.MatchesHostname</c> written out over the raw SAN extension value and subject
        /// name, using the framework's own public decoders (<see cref="X509SubjectAlternativeNameExtension"/>,
        /// <see cref="X500DistinguishedName"/>, <see cref="X500RelativeDistinguishedName"/>,
        /// <see cref="AsnReader"/>) for every byte it reads. It follows the dotnet/runtime source of that
        /// method line for line.
        ///
        /// It exists because some platforms refuse to load a certificate with a badly formed subject, so the
        /// real method can never be asked about one. The specs first hold this reference to the real method
        /// on every certificate that does load, then use it to judge the encodings that do not.
        /// </summary>
        private static bool ReferenceMatchesHostname(string hostname, byte[]? rawSubjectAlternativeName, byte[] rawSubjectName)
        {
            ArgumentNullException.ThrowIfNull(hostname);

            if (!System.Net.IPAddress.TryParse(hostname, out var ipAddress))
            {
                if (Uri.CheckHostName(hostname) != UriHostNameType.Dns)
                    throw new ArgumentException("Not a valid DNS hostname or IP address.", nameof(hostname));
            }

            if (rawSubjectAlternativeName is not null)
            {
                var san = new X509SubjectAlternativeNameExtension(rawSubjectAlternativeName);
                var hadAny = false;

                if (ipAddress is not null)
                {
                    foreach (var sanEntry in san.EnumerateIPAddresses())
                    {
                        if (sanEntry.Equals(ipAddress))
                            return true;

                        hadAny = true;
                    }
                }
                else
                {
                    ReadOnlySpan<char> match = hostname;

                    if (hostname.EndsWith('.'))
                    {
                        match = match.Slice(0, match.Length - 1);

                        if (match.IsEmpty)
                            return false;
                    }

                    ReadOnlySpan<char> afterFirstDot = default;
                    var firstDot = match.IndexOf('.');

                    if (firstDot > 0)
                        afterFirstDot = match.Slice(firstDot + 1);

                    foreach (var embedded in san.EnumerateDnsNames())
                    {
                        hadAny = true;

                        if (embedded.Length == 0)
                            continue;

                        ReadOnlySpan<char> embeddedSpan = embedded;

                        if (embedded.EndsWith('.'))
                            embeddedSpan = embeddedSpan.Slice(0, embeddedSpan.Length - 1);

                        if (embeddedSpan.StartsWith("*.", StringComparison.Ordinal) && embeddedSpan.Length > 2)
                        {
                            if (embeddedSpan.Slice(2).Equals(afterFirstDot, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                        else if (embeddedSpan.Equals(match, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                if (hadAny)
                    return false;
            }

            const string commonNameOid = "2.5.4.3";
            X500RelativeDistinguishedName? cn = null;

            foreach (var rdn in new X500DistinguishedName(rawSubjectName).EnumerateRelativeDistinguishedNames())
            {
                if (rdn.HasMultipleElements)
                {
                    var reader = new AsnReader(rdn.RawData, AsnEncodingRules.DER);
                    var set = reader.ReadSetOf(skipSortOrderValidation: true);

                    while (set.HasData)
                    {
                        var attributeTypeAndValue = set.ReadSequence();

                        if (attributeTypeAndValue.ReadObjectIdentifier() == commonNameOid)
                            return false;
                    }
                }
                else if (rdn.GetSingleElementType().Value == commonNameOid)
                {
                    if (cn is null)
                        cn = rdn;
                    else
                        return false;
                }
            }

            if (cn is not null)
                return hostname.Equals(cn.GetSingleElementValue(), StringComparison.OrdinalIgnoreCase);

            return false;
        }

        /// <summary>
        /// Collects comparisons between the framework and the matcher, and every disagreement found.
        /// </summary>
        private sealed class Comparison
        {
            private readonly Dictionary<Outcome, int> _counts = new();

            public List<string> Disagreements { get; } = new();

            /// <summary>Comparisons made through a loaded certificate, against the framework method itself.</summary>
            public int ThroughCertificate { get; private set; }

            /// <summary>Comparisons made over raw encodings, against the framework's decoders.</summary>
            public int OverEncodings { get; private set; }

            public int Count(Outcome outcome) => _counts.TryGetValue(outcome, out var count) ? count : 0;

            /// <summary>
            /// Asks the framework method, the matcher and the reference about a loaded certificate.
            /// Returns what the framework method said.
            /// </summary>
            public Outcome OfCertificate(string description, X509Certificate2 certificate, string hostname)
            {
                var framework = OutcomeOf(() => certificate.MatchesHostname(hostname));
                var matcher = OutcomeOf(() => CertificateHostnameMatcher.MatchesHostname(certificate, hostname));
                ThroughCertificate++;
                _counts[framework] = Count(framework) + 1;

                if (framework != matcher)
                    Disagreements.Add($"{description} / hostname [{Printable(hostname)}]: framework {framework}, matcher {matcher}");

                // Hold the reference to the real method too, so it can be trusted where no certificate loads.
                var subjectAlternativeNames = certificate.Extensions
                    .Where(extension => extension.Oid?.Value == SubjectAltNameOid)
                    .Select(extension => extension.RawData)
                    .ToArray();
                if (subjectAlternativeNames.Length <= 1)
                {
                    var reference = OutcomeOf(() => ReferenceMatchesHostname(hostname, subjectAlternativeNames.SingleOrDefault(), certificate.SubjectName.RawData));
                    if (framework != reference)
                        Disagreements.Add($"{description} / hostname [{Printable(hostname)}]: framework {framework}, the spec's own reference {reference}");
                }

                return framework;
            }

            /// <summary>
            /// Asks the reference and the matcher about encodings that may not load as a certificate.
            /// Returns what the reference said.
            /// </summary>
            public Outcome OfEncodings(string description, string hostname, byte[]? rawSubjectAlternativeName, byte[] rawSubjectName)
            {
                var reference = OutcomeOf(() => ReferenceMatchesHostname(hostname, rawSubjectAlternativeName, rawSubjectName));
                var matcher = OutcomeOf(() => CertificateHostnameMatcher.MatchesHostname(hostname, rawSubjectAlternativeName, rawSubjectName));
                OverEncodings++;

                if (reference != matcher)
                    Disagreements.Add($"{description} / hostname [{Printable(hostname)}]: framework decoders {reference}, matcher {matcher}");

                return reference;
            }

            public override string ToString()
            {
                return $"{ThroughCertificate} comparisons through certificates " +
                       $"({string.Join(", ", _counts.OrderBy(c => c.Key).Select(c => $"{c.Key}={c.Value}"))}), " +
                       $"{OverEncodings} over raw encodings, {Disagreements.Count} disagreements";
            }

            public void AssertNoDisagreements()
            {
                Assert.True(Disagreements.Count == 0, string.Join(Environment.NewLine, Disagreements.Take(100)));
            }
        }

        /// <summary>
        /// Names to try against every certificate: valid, odd and invalid.
        /// </summary>
        private static readonly string[] Hostnames =
        {
            // ordinary DNS names
            "example.com", "EXAMPLE.COM", "Example.Com", "example.com.", "example.com..",
            "www.example.com", "WWW.EXAMPLE.COM", "www.example.com.", "WWW.Example.Com.",
            "a.b.example.com", "foo.example.com", "foo.example.org", "www.example.org", "example.org",
            "com", "com.", "localhost", "LOCALHOST", "localhost.", "a", "A",
            "node-1", "NODE-1", "node-1.cluster.local", "node-1.cluster.local.", "cluster.local",
            "AkkaTestClient", "akkatestclient", "unrelated", "unrelated.invalid",
            "xn--bcher-kva.example", "XN--BCHER-KVA.EXAMPLE",
            // non-ASCII
            "bücher.example", "BÜCHER.example", "例え.テスト", "éxample.com",
            // names with stars, underscores and other doubtful characters
            "*.example.com", "*", "*.", "*.com", "*.*.example.com", "w*.example.com", "www.*.com", "f*o.example.com",
            "node_1", "node_1.example.com", "_service.example.com",
            "-leading.example.com", "trailing-.example.com", "exa mple.com", "example.com:443",
            "http://example.com", "user@example.com", "not a valid hostname!", "example.com/path",
            "www.example.com\0", "\0", "example.com\n", " example.com", "example.com ",
            // structure
            "", " ", ".", "..", "a..b", ".example.com", "a.", ".a",
            // IPv4, in the spellings IPAddress.TryParse accepts and a few it does not
            "127.0.0.1", "127.0.0.2", "127.1", "10.0.0.1", "10.0.0.2", "192.168.1.250", "0.0.0.0", "255.255.255.255",
            "1", "12345", "0x7f.0.0.1", "0177.0.0.1", "01.02.03.04", "256.1.1.1", "1.2.3", "1.2.3.4.5", "127.0.0.1.",
            // IPv6
            "::1", "0:0:0:0:0:0:0:1", "[::1]", "::", "::ffff:127.0.0.1", "::ffff:7f00:1", "fe80::1", "fe80::1%1",
            "2001:db8::1", "2001:DB8::1", "2001:0db8:0000:0000:0000:0000:0000:0001", "2001:db8::2", "::1.", "1::1::1",
            // length limits
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example.com",  // 63-character label
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.example.com", // 64-character label
        };

        private static IEnumerable<(string Description, Func<X509Certificate2> Create)> AgreementCertificates()
        {
            (string, Func<X509Certificate2>) Named(string subject, string san) =>
                ($"subject [{subject}] san [{san}]", () => Create(subject, san));

            // common name only
            yield return Named("CN=example.com", "");
            yield return Named("CN=EXAMPLE.COM", "");
            yield return Named("CN=www.example.com", "");
            yield return Named("CN=example.com.", "");
            yield return Named("CN=*.example.com", "");
            yield return Named("CN=*", "");
            yield return Named("CN=localhost", "");
            yield return Named("CN=node-1, OU=Cluster, O=Akka, C=US", "");
            yield return Named("C=US, O=Akka, OU=Cluster, CN=node-1", "");
            yield return Named("CN=AkkaTestClient", "");
            yield return Named("CN=127.0.0.1", "");
            yield return Named("CN=::1", "");
            yield return Named("CN=12345", "");
            yield return Named("CN=node_1", "");
            yield return Named("CN=example.com, CN=www.example.com", "");
            yield return Named("O=Akka", "");
            yield return Named("", "");

            // DNS names
            yield return Named("CN=unrelated", "dns:www.example.com");
            yield return Named("CN=www.example.com", "dns:www.example.com");
            yield return Named("CN=example.com", "dns:www.example.com");
            yield return Named("CN=unrelated", "dns:example.com,dns:www.example.com,dns:a.b.example.com");
            yield return Named("CN=unrelated", "dns:WWW.EXAMPLE.COM");
            yield return Named("CN=unrelated", "dns:www.example.com.");
            yield return Named("CN=unrelated", "dns:www.example.com..");
            yield return Named("CN=unrelated", "dns:localhost");
            yield return Named("CN=unrelated", "dns:node-1,dns:node-1.cluster.local");
            yield return Named("CN=unrelated", "dns:node_1,dns:node_1.example.com,dns:_service.example.com");
            yield return Named("CN=unrelated", "dns:xn--bcher-kva.example");
            yield return Named("", "dns:www.example.com");
            yield return Named("CN=example.com", "dns:");
            yield return Named("CN=example.com", "dns:,dns:example.com");
            yield return Named("CN=example.com", "dns:.");
            yield return Named("CN=127.0.0.1", "dns:127.0.0.1");
            yield return Named("CN=unrelated", "dns:com");
            yield return Named("CN=unrelated", "dns:a");

            // wildcards
            yield return Named("CN=unrelated", "dns:*.example.com");
            yield return Named("CN=unrelated", "dns:*.EXAMPLE.COM");
            yield return Named("CN=unrelated", "dns:*.example.com.");
            yield return Named("CN=unrelated", "dns:*.example.com,dns:example.com");
            yield return Named("CN=unrelated", "dns:*.cluster.local");
            yield return Named("CN=unrelated", "dns:*.com");
            yield return Named("CN=unrelated", "dns:*.local");
            yield return Named("CN=unrelated", "dns:*");
            yield return Named("CN=unrelated", "dns:*.");
            yield return Named("CN=unrelated", "dns:*..");
            yield return Named("CN=unrelated", "dns:*.*.example.com");
            yield return Named("CN=unrelated", "dns:w*.example.com");
            yield return Named("CN=unrelated", "dns:www.*.com");
            yield return Named("CN=unrelated", "dns:f*o.example.com");
            yield return Named("CN=unrelated", "dns:*.a");
            yield return Named("CN=www.example.com", "dns:*.example.org");

            // IP addresses
            yield return Named("CN=unrelated", "ip:127.0.0.1");
            yield return Named("CN=127.0.0.1", "ip:127.0.0.1");
            yield return Named("CN=127.0.0.1", "ip:10.0.0.1");
            yield return Named("CN=unrelated", "ip:10.0.0.1,ip:10.0.0.2,ip:192.168.1.250");
            yield return Named("CN=unrelated", "ip:::1");
            yield return Named("CN=unrelated", "ip:::");
            yield return Named("CN=unrelated", "ip:0.0.0.0,ip:255.255.255.255");
            yield return Named("CN=unrelated", "ip:0.0.0.1,ip:0.0.48.57");
            yield return Named("CN=unrelated", "ip:2001:db8::1");
            yield return Named("CN=unrelated", "ip:fe80::1");
            yield return Named("CN=unrelated", "ip:::ffff:127.0.0.1");
            yield return Named("CN=unrelated", "dns:localhost,ip:127.0.0.1,ip:::1");

            // SAN without an entry of the wanted kind
            yield return Named("CN=www.example.com", "ip:127.0.0.1");
            yield return Named("CN=127.0.0.1", "dns:www.example.com");
            yield return Named("CN=::1", "dns:www.example.com");
            yield return Named("CN=www.example.com", "email:ops@example.com,uri:https://example.com/");
            yield return Named("CN=127.0.0.1", "email:ops@example.com");

            // encodings written by hand
            (string, Func<X509Certificate2>) Raw(string description, byte[] subject, byte[]? san) =>
                (description, () => Create(subject, san));

            var text = Encoding.ASCII.GetBytes("www.example.com");
            yield return Raw("CN as PrintableString", Name(CommonName(PrintableStringTag, text)), null);
            yield return Raw("CN as IA5String", Name(CommonName(Ia5StringTag, text)), null);
            yield return Raw("CN as T61String", Name(CommonName(T61StringTag, text)), null);
            yield return Raw("CN as T61String with a Latin-1 byte",
                Name(CommonName(T61StringTag, new byte[] { (byte)'b', 0xFC }.Concat(Encoding.ASCII.GetBytes("cher.example")).ToArray())), null);
            yield return Raw("CN as BMPString", Name(CommonName(BmpStringTag, Encoding.BigEndianUnicode.GetBytes("www.example.com"))), null);
            yield return Raw("CN as BMPString, not ASCII", Name(CommonName(BmpStringTag, Encoding.BigEndianUnicode.GetBytes("bücher.example"))), null);
            yield return Raw("CN as NumericString", Name(CommonName(NumericStringTag, Encoding.ASCII.GetBytes("12345"))), null);
            yield return Raw("CN as UTF8String, not ASCII", Name(CommonName("bücher.example")), null);
            yield return Raw("CN as UTF8String, uppercase non-ASCII", Name(CommonName("BÜCHER.EXAMPLE")), null);
            yield return Raw("CN with a trailing NUL", Name(CommonName(Utf8StringTag, Encoding.ASCII.GetBytes("www.example.com\0"))), null);
            yield return Raw("CN with an embedded NUL", Name(CommonName(Utf8StringTag, Encoding.ASCII.GetBytes("www.example.com\0.evil.example"))), null);
            yield return Raw("empty CN", Name(CommonName(string.Empty)), null);
            yield return Raw("CN in a multi-valued RDN",
                Name(Rdn(Attribute(CommonNameOid, Der(Utf8StringTag, text)), Attribute(OrganizationalUnitOid, Der(Utf8StringTag, text)))), null);
            yield return Raw("one CN alone and one in a multi-valued RDN",
                Name(CommonName("www.example.com"),
                    Rdn(Attribute(CommonNameOid, Der(Utf8StringTag, text)), Attribute(OrganizationalUnitOid, Der(Utf8StringTag, text)))), null);
            yield return Raw("multi-valued RDN without a CN, then a CN",
                Name(Rdn(Attribute(OrganizationOid, Der(Utf8StringTag, text)), Attribute(OrganizationalUnitOid, Der(Utf8StringTag, text))),
                    CommonName("www.example.com")), null);
            yield return Raw("two CNs with the same value", Name(CommonName("www.example.com"), CommonName("www.example.com")), null);
            yield return Raw("empty SAN beside a CN", Name(CommonName("www.example.com")), Der(0x30));
            yield return Raw("SAN with a dNSName holding a NUL", Name(CommonName("unrelated")),
                Der(0x30, Der(0x82, Encoding.ASCII.GetBytes("www.example.com\0"))));
            yield return Raw("SAN with otherName, registeredID and directoryName beside a dNSName", Name(CommonName("unrelated")),
                Der(0x30,
                    Der(0xA0, Der(0x06, new byte[] { 0x2A, 0x03, 0x04 }), Der(0xA0, Der(Utf8StringTag, Encoding.ASCII.GetBytes("upn@example.com")))),
                    Der(0xA4, Name(CommonName("directory.example.com"))),
                    Der(0x88, new byte[] { 0x2A, 0x03, 0x04 }),
                    DnsName("www.example.com")));
            yield return Raw("SAN with only a directoryName", Name(CommonName("www.example.com")),
                Der(0x30, Der(0xA4, Name(CommonName("directory.example.com")))));

            // encodings written by the framework
            yield return ("SubjectAlternativeNameBuilder with every kind of entry", () => Create("CN=unrelated", builder =>
            {
                builder.AddEmailAddress("ops@example.com");
                builder.AddUserPrincipalName("node-1@example.com");
                builder.AddUri(new Uri("https://example.com/akka"));
                builder.AddDnsName("www.example.com");
                builder.AddDnsName("*.cluster.local");
                builder.AddIpAddress(System.Net.IPAddress.Parse("10.0.0.1"));
                builder.AddIpAddress(System.Net.IPAddress.Parse("2001:db8::1"));
            }));
            yield return ("SubjectAlternativeNameBuilder with 300 names", () => Create("CN=unrelated", builder =>
            {
                for (var i = 0; i < 300; i++)
                    builder.AddDnsName($"node-{i:D4}.cluster.example.com");
                builder.AddDnsName("www.example.com");
            }));

            // the certificates the other TLS specs use
            yield return (ValidCertPath, () => CertificateHelper.LoadPkcs12(ValidCertPath, Password));
            yield return (ClientCertPath, () => CertificateHelper.LoadPkcs12(ClientCertPath, Password));
        }

        [Fact(DisplayName = "Should_AgreeWithFrameworkMatchesHostname_When_GivenTheSameCertificateAndName")]
        public void Should_AgreeWithFrameworkMatchesHostname_When_GivenTheSameCertificateAndName()
        {
            var comparison = new Comparison();

            foreach (var (description, create) in AgreementCertificates())
            {
                using var certificate = create();
                foreach (var hostname in Hostnames)
                    comparison.OfCertificate(description, certificate, hostname);
            }

            _output.WriteLine(comparison.ToString());
            comparison.AssertNoDisagreements();

            // Guard against a table that proves nothing.
            Assert.True(comparison.Count(Outcome.Match) >= 150, $"Only {comparison.Count(Outcome.Match)} matches in the table");
            Assert.True(comparison.Count(Outcome.NoMatch) >= 1000, $"Only {comparison.Count(Outcome.NoMatch)} mismatches in the table");
            Assert.True(comparison.Count(Outcome.InvalidHostname) >= 100, $"Only {comparison.Count(Outcome.InvalidHostname)} invalid names in the table");
        }

        [Fact(DisplayName = "Should_AgreeWithFrameworkMatchesHostname_When_GivenGeneratedCertificatesAndNames")]
        public void Should_AgreeWithFrameworkMatchesHostname_When_GivenGeneratedCertificatesAndNames()
        {
            // A fixed seed keeps the run repeatable. The label pool is small on purpose so generated
            // names collide with generated certificates often.
            var random = new Random(8465);
            string[] labels = { "a", "b", "www", "WWW", "example", "Example", "com", "local", "node-1", "xn--bcher-kva", "*" };
            string[] addresses = { "127.0.0.1", "10.0.0.1", "10.0.0.2", "::1", "2001:db8::1", "::ffff:127.0.0.1" };

            string RandomName()
            {
                var name = string.Join(".", Enumerable.Range(0, random.Next(1, 5)).Select(_ => labels[random.Next(labels.Length)]));
                switch (random.Next(12))
                {
                    case 0: return name + ".";
                    case 1: return "*." + name;
                    case 2: return "*." + name + ".";
                    case 3: return name.ToUpperInvariant();
                    case 4: return string.Empty;
                    default: return name;
                }
            }

            var comparison = new Comparison();

            for (var i = 0; i < 400; i++)
            {
                // subject
                var commonNames = new List<string>();
                switch (random.Next(8))
                {
                    case 0:
                        break;
                    case 1:
                        commonNames.Add(addresses[random.Next(addresses.Length)]);
                        break;
                    case 2:
                        commonNames.Add(RandomName());
                        commonNames.Add(RandomName());
                        break;
                    default:
                        commonNames.Add(RandomName());
                        break;
                }

                var subject = Name(commonNames.Select(CommonName).ToArray());

                // subject alternative names
                var dnsNames = new List<string>();
                var ipAddresses = new List<string>();
                byte[]? subjectAlternativeName = null;
                if (random.Next(4) != 0)
                {
                    var entries = new List<byte[]>();
                    var entryCount = random.Next(0, 5);
                    for (var j = 0; j < entryCount; j++)
                    {
                        switch (random.Next(5))
                        {
                            case 0:
                                var address = addresses[random.Next(addresses.Length)];
                                ipAddresses.Add(address);
                                entries.Add(IpAddress(System.Net.IPAddress.Parse(address).GetAddressBytes()));
                                break;
                            case 1:
                                entries.Add(Der(0x81, Encoding.ASCII.GetBytes("ops@example.com")));
                                break;
                            default:
                                var dnsName = RandomName();
                                dnsNames.Add(dnsName);
                                entries.Add(DnsName(dnsName));
                                break;
                        }
                    }

                    subjectAlternativeName = Der(0x30, entries.ToArray());
                }

                // names to try: random ones, every address, and names built from the certificate's own entries
                var hostnames = new HashSet<string>(addresses);
                for (var j = 0; j < 30; j++)
                    hostnames.Add(RandomName());

                foreach (var name in dnsNames.Concat(commonNames))
                {
                    hostnames.Add(name);
                    hostnames.Add(name.ToUpperInvariant());
                    hostnames.Add(name.ToLowerInvariant());
                    hostnames.Add(name + ".");
                    hostnames.Add(name.TrimEnd('.'));
                    if (name.StartsWith("*.", StringComparison.Ordinal))
                    {
                        hostnames.Add("host" + name.Substring(1));
                        hostnames.Add("HOST" + name.Substring(1).TrimEnd('.'));
                        hostnames.Add("a.b" + name.Substring(1));
                        hostnames.Add(name.Substring(2));
                    }
                }

                var description =
                    $"CN [{string.Join("; ", commonNames)}] dns [{string.Join("; ", dnsNames)}] ip [{string.Join("; ", ipAddresses)}] " +
                    $"san extension {(subjectAlternativeName is null ? "absent" : "present")}";

                using var certificate = Create(subject, subjectAlternativeName);
                foreach (var hostname in hostnames)
                    comparison.OfCertificate(description, certificate, hostname);
            }

            _output.WriteLine(comparison.ToString());
            comparison.AssertNoDisagreements();

            // Guard against a generator that never produces a match.
            Assert.True(comparison.Count(Outcome.Match) >= 1000, $"Only {comparison.Count(Outcome.Match)} matches generated");
            Assert.True(comparison.Count(Outcome.NoMatch) >= 5000, $"Only {comparison.Count(Outcome.NoMatch)} mismatches generated");
        }

        private static readonly string[] HostnamesForOddEncodings =
        {
            "www.example.com", "WWW.EXAMPLE.COM", "www.example.com.", "other.example.com", "fallback.example.com",
            "a", "10.0.0.1", "10.0.0.2", "::1", "not a valid hostname!"
        };

        /// <summary>
        /// Compares over the raw encodings, and through a certificate as well when the platform will load one.
        /// </summary>
        private void CompareEncodings(Comparison comparison, string description, byte[] subject, byte[]? subjectAlternativeName,
            Action<string, Outcome>? expectation = null)
        {
            X509Certificate2? certificate = null;
            try
            {
                certificate = Create(subject, subjectAlternativeName);
            }
            catch (CryptographicException e)
            {
                // Some platforms refuse to load a certificate this odd, so X509Certificate2.MatchesHostname
                // can never be asked about it. The framework's decoders still can.
                _output.WriteLine($"Will not load as a certificate here, compared over raw encodings only: {description} ({e.Message})");
            }

            using (certificate)
            {
                foreach (var hostname in HostnamesForOddEncodings)
                {
                    var outcome = comparison.OfEncodings(description, hostname, subjectAlternativeName, subject);
                    if (certificate != null)
                        outcome = comparison.OfCertificate(description, certificate, hostname);

                    expectation?.Invoke(hostname, outcome);
                }
            }
        }

        [Fact(DisplayName = "Should_AgreeWithFrameworkMatchesHostname_When_CertificateNamesAreMalformedOrUnusual")]
        public void Should_AgreeWithFrameworkMatchesHostname_When_CertificateNamesAreMalformedOrUnusual()
        {
            var comparison = new Comparison();
            var commonName = Name(CommonName("www.example.com"));

            foreach (var row in MalformedSubjectAlternativeNames())
                CompareEncodings(comparison, $"malformed SAN: {row.Data.Item1}", commonName, row.Data.Item2);
            foreach (var row in MalformedSubjectNames())
                CompareEncodings(comparison, $"malformed subject: {row.Data.Item1}", row.Data.Item2, null);
            foreach (var row in UnusualSubjectNames())
                CompareEncodings(comparison, $"unusual subject: {row.Data.Item1}", row.Data.Item2, null);
            foreach (var row in NonTextCommonNames())
                CompareEncodings(comparison, $"common name that is not text: {row.Data.Item1}", row.Data.Item2, null);

            _output.WriteLine(comparison.ToString());
            comparison.AssertNoDisagreements();
            Assert.True(comparison.Count(Outcome.InvalidCertificate) >= 100,
                $"The framework method rejected only {comparison.Count(Outcome.InvalidCertificate)} lookups as invalid");
        }

        [Fact(DisplayName = "Should_AgreeWithFrameworkMatchesHostname_When_SubjectAlternativeNameHoldsEntriesOfOtherKinds")]
        public void Should_AgreeWithFrameworkMatchesHostname_When_SubjectAlternativeNameHoldsEntriesOfOtherKinds()
        {
            // Both directions matter. A well-formed entry of a kind the match never uses must not cost a
            // certificate its match: a false rejection on the dialing side shuts the node down. A malformed
            // one must be rejected, because the framework method rejects it.
            var comparison = new Comparison();
            var surprises = new List<string>();
            var subject = Name(CommonName("fallback.example.com"));
            var dnsName = DnsName("www.example.com");
            var ipAddress = IpAddress(10, 0, 0, 1);

            void Expect(string description, string hostname, Outcome actual, Outcome expected)
            {
                if (actual != expected && hostname != "not a valid hostname!")
                    surprises.Add($"{description} / hostname [{hostname}]: expected the framework to say {expected}, it said {actual}");
            }

            foreach (var row in WellFormedEntriesOfOtherKinds())
            {
                var (kind, entry) = (row.Data.Item1, row.Data.Item2);

                foreach (var (position, subjectAlternativeName) in new[]
                         {
                             ("before DNS and IP", Der(0x30, entry, dnsName, ipAddress)),
                             ("after DNS and IP", Der(0x30, dnsName, ipAddress, entry)),
                             ("between DNS and IP", Der(0x30, dnsName, entry, ipAddress)),
                         })
                {
                    var description = $"well-formed {kind}, {position}";
                    CompareEncodings(comparison, description, subject, subjectAlternativeName, (hostname, outcome) =>
                    {
                        var matches = hostname is "www.example.com" or "WWW.EXAMPLE.COM" or "www.example.com." or "10.0.0.1";
                        Expect(description, hostname, outcome, matches ? Outcome.Match : Outcome.NoMatch);
                    });
                }

                // On its own the entry is neither a DNS name nor an IP address, so the common name decides.
                var alone = $"well-formed {kind}, alone";
                CompareEncodings(comparison, alone, subject, Der(0x30, entry), (hostname, outcome) =>
                    Expect(alone, hostname, outcome, hostname == "fallback.example.com" ? Outcome.Match : Outcome.NoMatch));
            }

            foreach (var row in MalformedEntriesOfOtherKinds())
            {
                var (kind, entry) = (row.Data.Item1, row.Data.Item2);

                foreach (var (position, subjectAlternativeName) in new[]
                         {
                             ("before DNS and IP", Der(0x30, entry, dnsName, ipAddress)),
                             ("after DNS and IP", Der(0x30, dnsName, ipAddress, entry)),
                             ("alone", Der(0x30, entry)),
                         })
                {
                    var description = $"malformed {kind}, {position}";
                    CompareEncodings(comparison, description, subject, subjectAlternativeName, (hostname, outcome) =>
                        Expect(description, hostname, outcome, Outcome.InvalidCertificate));
                }
            }

            _output.WriteLine(comparison.ToString());
            comparison.AssertNoDisagreements();

            // The lists must be what they claim to be: the framework accepts every well-formed entry and
            // rejects every malformed one.
            Assert.True(surprises.Count == 0, string.Join(Environment.NewLine, surprises.Take(100)));
        }

        [Fact(DisplayName = "Should_AgreeWithFrameworkDecoders_When_EncodingsAreDamagedAtRandom")]
        public void Should_AgreeWithFrameworkDecoders_When_EncodingsAreDamagedAtRandom()
        {
            // Takes every SAN extension and subject name the other specs use, damages a few bytes, and asks
            // the framework's decoders and the matcher about the result. Most damaged encodings would not
            // load as a certificate, so this compares over the raw encodings. A fixed seed keeps it repeatable.
            var random = new Random(8465);
            var subject = Name(CommonName("fallback.example.com"));
            var dnsName = DnsName("www.example.com");
            var ipAddress = IpAddress(10, 0, 0, 1);

            var subjectAlternativeNames = new List<byte[]>();
            foreach (var row in WellFormedEntriesOfOtherKinds())
                subjectAlternativeNames.Add(Der(0x30, dnsName, row.Data.Item2, ipAddress));
            foreach (var row in MalformedEntriesOfOtherKinds())
                subjectAlternativeNames.Add(Der(0x30, dnsName, row.Data.Item2, ipAddress));
            foreach (var row in MalformedSubjectAlternativeNames())
                subjectAlternativeNames.Add(row.Data.Item2);
            subjectAlternativeNames.Add(SubjectAlternativeName("dns:www.example.com,dns:*.example.org,ip:10.0.0.1,ip:::1,email:ops@example.com,uri:https://example.com/"));

            var subjects = new List<byte[]> { subject, Name(CommonName("www.example.com")), Subject("CN=www.example.com, OU=Cluster, O=Akka, C=US") };
            foreach (var row in MalformedSubjectNames())
                subjects.Add(row.Data.Item2);
            foreach (var row in UnusualSubjectNames())
                subjects.Add(row.Data.Item2);
            foreach (var row in NonTextCommonNames())
                subjects.Add(row.Data.Item2);

            byte[] Damage(byte[] original)
            {
                var bytes = original.ToList();
                var changes = random.Next(1, 4);
                for (var i = 0; i < changes; i++)
                {
                    // Favor the values that mean something in DER: tags, short and long lengths, high bits.
                    byte[] interesting = { 0x00, 0x01, 0x06, 0x0C, 0x13, 0x16, 0x1E, 0x1F, 0x30, 0x31, 0x7F, 0x80, 0x81, 0x82, 0x87, 0xA0, 0xA1, 0xFF };
                    var value = random.Next(3) == 0 ? (byte)random.Next(256) : interesting[random.Next(interesting.Length)];
                    switch (bytes.Count == 0 ? 1 : random.Next(4))
                    {
                        case 0:
                            bytes.RemoveAt(random.Next(bytes.Count));
                            break;
                        case 1:
                            bytes.Insert(random.Next(bytes.Count + 1), value);
                            break;
                        default:
                            bytes[random.Next(bytes.Count)] = value;
                            break;
                    }
                }

                return bytes.ToArray();
            }

            var comparison = new Comparison();
            var outcomes = new Dictionary<Outcome, int>();
            void Compare(string description, byte[]? rawSubjectAlternativeName, byte[] rawSubjectName)
            {
                foreach (var hostname in new[] { "www.example.com", "fallback.example.com", "10.0.0.1" })
                {
                    var outcome = comparison.OfEncodings(description, hostname, rawSubjectAlternativeName, rawSubjectName);
                    outcomes[outcome] = outcomes.TryGetValue(outcome, out var count) ? count + 1 : 1;
                }
            }

            for (var i = 0; i < 20000; i++)
            {
                var damaged = Damage(subjectAlternativeNames[random.Next(subjectAlternativeNames.Count)]);
                Compare($"damaged SAN {BitConverter.ToString(damaged)}", damaged, subject);
            }

            for (var i = 0; i < 10000; i++)
            {
                var damaged = Damage(subjects[random.Next(subjects.Count)]);
                Compare($"damaged subject {BitConverter.ToString(damaged)}", null, damaged);
            }

            _output.WriteLine($"{comparison} ({string.Join(", ", outcomes.OrderBy(o => o.Key).Select(o => $"{o.Key}={o.Value}"))})");
            comparison.AssertNoDisagreements();

            // Guard against damage so heavy that nothing ever decodes.
            Assert.True(outcomes[Outcome.Match] >= 1000, $"Only {outcomes[Outcome.Match]} damaged encodings still matched");
            Assert.True(outcomes[Outcome.InvalidCertificate] >= 10000, $"Only {outcomes[Outcome.InvalidCertificate]} damaged encodings were rejected");
        }

        [Fact(DisplayName = "Should_AgreeWithFrameworkMatchesHostname_When_CertificateHasTwoSubjectAlternativeNameExtensions")]
        public void Should_AgreeWithFrameworkMatchesHostname_When_CertificateHasTwoSubjectAlternativeNameExtensions()
        {
            // CertificateRequest refuses to write an extension twice, so these certificates are assembled by hand.
            var subject = Name(CommonName("fallback.example.com"));
            var first = Der(0x30, DnsName("www.example.com"));
            var second = Der(0x30, DnsName("other.example.com"), IpAddress(10, 0, 0, 1));
            var comparison = new Comparison();

            // One extension first, to show that a certificate assembled this way behaves like any other.
            using (var single = LoadCertificate(AssembleCertificate(subject, first)))
            {
                Assert.Equal(Outcome.Match, comparison.OfCertificate("one SAN extension, assembled by hand", single, "www.example.com"));
                Assert.Equal(Outcome.NoMatch, comparison.OfCertificate("one SAN extension, assembled by hand", single, "other.example.com"));
            }

            X509Certificate2 duplicated;
            try
            {
                duplicated = LoadCertificate(AssembleCertificate(subject, first, second));
            }
            catch (CryptographicException e)
            {
                Assert.Skip($"This platform will not load a certificate with two SAN extensions: {e.Message}");
                return;
            }

            using (duplicated)
            {
                foreach (var hostname in HostnamesForOddEncodings)
                {
                    var outcome = comparison.OfCertificate("two SAN extensions", duplicated, hostname);
                    if (hostname != "not a valid hostname!")
                        Assert.Equal(Outcome.InvalidCertificate, outcome);
                }
            }

            _output.WriteLine(comparison.ToString());
            comparison.AssertNoDisagreements();
        }

        /// <summary>
        /// Assembles a signed X.509 v3 certificate whose extensions are the given SAN extension values,
        /// one SAN extension per value.
        /// </summary>
        private static byte[] AssembleCertificate(byte[] subject, params byte[][] subjectAlternativeNames)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var ecdsaWithSha256 = Der(0x30, Der(0x06, new byte[] { 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x04, 0x03, 0x02 }));
            var validity = Der(0x30,
                Der(0x17, Encoding.ASCII.GetBytes("250101000000Z")),
                Der(0x17, Encoding.ASCII.GetBytes("450101000000Z")));
            var extensions = subjectAlternativeNames
                .Select(value => Der(0x30, Der(0x06, new byte[] { 0x55, 0x1D, 0x11 }), Der(OctetStringTag, value)))
                .ToArray();

            var tbsCertificate = Der(0x30,
                Der(0xA0, Der(0x02, new byte[] { 2 })),  // version 3
                Der(0x02, new byte[] { 1 }),              // serial number
                ecdsaWithSha256,
                subject,                                  // issuer: self-signed
                validity,
                subject,
                key.ExportSubjectPublicKeyInfo(),
                Der(0xA3, Der(0x30, extensions)));
            var signature = key.SignData(tbsCertificate, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            return Der(0x30, tbsCertificate, ecdsaWithSha256, Der(0x03, new byte[] { 0 }.Concat(signature).ToArray()));
        }

        private static X509Certificate2 LoadCertificate(byte[] encoded)
        {
#if NET9_0_OR_GREATER
            return X509CertificateLoader.LoadCertificate(encoded);
#else
            return new X509Certificate2(encoded);
#endif
        }

        private static string Printable(string value)
        {
            var builder = new StringBuilder();
            foreach (var c in value)
            {
                if (c < 0x20 || c > 0x7E)
                    builder.Append($"\\u{(int)c:X4}");
                else
                    builder.Append(c);
            }

            return builder.ToString();
        }

        #endregion

#endif
    }
}
