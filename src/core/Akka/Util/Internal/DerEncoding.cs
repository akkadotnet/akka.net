//-----------------------------------------------------------------------
// <copyright file="DerEncoding.cs" company="Akka.NET Project">
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
using System.Security.Cryptography;
using System.Text;

namespace Akka.Util.Internal
{
    /// <summary>
    /// INTERNAL API
    ///
    /// A strict DER reader for X.509 names and the string types they hold. The certificate hostname matcher in
    /// Akka.Remote and <c>Akka.IO.TlsCertificateSource</c> both read a certificate's common name through it,
    /// so they decode it the same way. The rules follow the dotnet/runtime sources for
    /// <c>X500RelativeDistinguishedName</c> and <c>System.Formats.Asn1.AsnDecoder</c> under DER rules.
    /// </summary>
    internal static class DerEncoding
    {
        // DER tags
        internal const byte SequenceTag = 0x30;
        internal const byte SetTag = 0x31;
        internal const byte ObjectIdentifierTag = 0x06;

        // DER tag numbers of the universal string types
        internal const int Utf8StringTagNumber = 0x0C;
        internal const int NumericStringTagNumber = 0x12;
        internal const int PrintableStringTagNumber = 0x13;
        internal const int T61StringTagNumber = 0x14;
        internal const int Ia5StringTagNumber = 0x16;
        internal const int UniversalStringTagNumber = 0x1C;
        internal const int BmpStringTagNumber = 0x1E;

        // Limits System.Formats.Asn1 puts on an OBJECT IDENTIFIER
        private const int MaxObjectIdentifierArcBits = 128;
        private const int MaxObjectIdentifierArcs = 64;

        internal const string PrintableStringCharacters =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 '()+,-./:=?";

        private static readonly UTF8Encoding StrictUtf8 =
            new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <summary>
        /// Returns the value of the subject's common name when the subject has exactly one, and it is not part
        /// of a multi-valued RDN. Returns <c>null</c> when the subject has no usable common name.
        /// </summary>
        internal static string? ReadSingleCommonName(byte[] rawSubjectName)
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

        internal static void RequirePrimitive(DerValue value)
        {
            // DER forbids the constructed form for strings, and an OBJECT IDENTIFIER is always primitive.
            if (value.IsConstructed)
                throw InvalidEncoding();
        }

        internal static void RequireConstructed(DerValue value)
        {
            // A SEQUENCE, and a value wrapped in an EXPLICIT tag, is always constructed.
            if (!value.IsConstructed)
                throw InvalidEncoding();
        }

        internal static string? DecodeUtf8(byte[] data, DerValue value)
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
        internal static string DecodeRestrictedAscii(byte[] data, DerValue value, string? allowed)
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

        internal static string DecodeBmp(byte[] data, DerValue value)
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
        internal static bool IsValidObjectIdentifier(byte[] data, DerValue oid)
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

        internal static CryptographicException InvalidEncoding()
        {
            return new CryptographicException("ASN.1 encoded value is corrupted or not valid DER.");
        }
    }

    /// <summary>
    /// One DER value: its leading tag byte and where its content sits in the buffer.
    /// </summary>
    internal readonly struct DerValue
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
    internal struct DerReader
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
                        throw DerEncoding.InvalidEncoding(); // padded tag number

                    first = false;
                    tagNumber = (tagNumber << 7) | (uint)(b & 0x7F);
                    if (tagNumber > int.MaxValue)
                        throw DerEncoding.InvalidEncoding();
                } while ((b & 0x80) != 0);

                if (tagNumber < 31)
                    throw DerEncoding.InvalidEncoding(); // must have used the single-byte form
            }

            long length = ReadByte();
            if (length >= 0x80)
            {
                // 0x80 is the indefinite form, which DER forbids. 0xFF is reserved.
                var lengthBytes = (int)length & 0x7F;
                if (lengthBytes == 0 || lengthBytes > 4)
                    throw DerEncoding.InvalidEncoding();

                length = 0;
                for (var i = 0; i < lengthBytes; i++)
                {
                    var b = ReadByte();
                    if (i == 0 && b == 0)
                        throw DerEncoding.InvalidEncoding(); // padded length

                    length = (length << 8) | b;
                }

                // DER requires the short form for lengths under 128.
                if (length < 0x80)
                    throw DerEncoding.InvalidEncoding();
            }

            if (length > _end - _offset)
                throw DerEncoding.InvalidEncoding();

            var value = new DerValue(tag, isHighTagNumber, _offset, (int)length);
            _offset += (int)length;
            return value;
        }

        private byte ReadByte()
        {
            if (_offset >= _end)
                throw DerEncoding.InvalidEncoding();

            return _data[_offset++];
        }
    }
}
