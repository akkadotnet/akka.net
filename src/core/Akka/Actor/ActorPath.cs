//-----------------------------------------------------------------------
// <copyright file="ActorPath.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Util;
using Newtonsoft.Json;
using static System.String;

namespace Akka.Actor
{
    /// <summary>
    /// Actor path is a unique path to an actor that shows the creation path
    /// up through the actor tree to the root actor.
    /// ActorPath defines a natural ordering (so that ActorRefs can be put into
    /// collections with this requirement); this ordering is intended to be as fast
    /// as possible, which owing to the bottom-up recursive nature of ActorPath
    /// is sorted by path elements FROM RIGHT TO LEFT, where RootActorPath >
    /// ChildActorPath in case the number of elements is different.
    /// Two actor paths are compared equal when they have the same name and parent
    /// elements, including the root address information. That does not necessarily
    /// mean that they point to the same incarnation of the actor if the actor is
    /// re-created with the same path. In other words, in contrast to how actor
    /// references are compared the unique id of the actor is not taken into account
    /// when comparing actor paths.
    /// </summary>
    public abstract class ActorPath : IEquatable<ActorPath>, IComparable<ActorPath>, ISurrogated
    {
        /// <summary>
        /// This class represents a surrogate of an <see cref="ActorPath"/>.
        /// Its main use is to help during the serialization process.
        /// </summary>
        public class Surrogate : ISurrogate, IEquatable<Surrogate>, IEquatable<ActorPath>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="Surrogate"/> class.
            /// </summary>
            /// <param name="path">The string representation of the actor path.</param>
            public Surrogate(string path)
            {
                Path = path;
            }

            /// <summary>
            /// The string representation of the actor path
            /// </summary>
            public string Path { get; }

            /// <summary>
            /// Creates an <see cref="ActorPath"/> encapsulated by this surrogate.
            /// </summary>
            /// <param name="system">The actor system that contains this actor path.</param>
            /// <returns>The <see cref="ActorPath"/> encapsulated by this surrogate.</returns>
            public ISurrogated FromSurrogate(ActorSystem system)
            {
                if (TryParse(Path, out var path))
                {
                    return path;
                }

                return null;
            }

            #region Equality

            /// <inheritdoc/>
            public bool Equals(Surrogate other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return string.Equals(Path, other.Path);
            }

            /// <inheritdoc/>
            public bool Equals(ActorPath other)
            {
                if (other == null) return false;
                return Equals(other.ToSurrogate(null)); //TODO: not so sure if this is OK
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                var actorPath = obj as ActorPath;
                if (actorPath != null) return Equals(actorPath);
                return Equals(obj as Surrogate);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Path.GetHashCode();
            }

            #endregion
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal static readonly char[] ValidSymbols = @"""-_.*$+:@&=,!~';""()".ToCharArray();

        /// <summary>
        /// Method that checks if actor name conforms to RFC 2396, http://www.ietf.org/rfc/rfc2396.txt
        /// Note that AKKA JVM does not allow parenthesis ( ) but, according to RFC 2396 those are allowed, and
        /// since we use URL Encode to create valid actor names, we must allow them.
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public static bool IsValidPathElement(string s)
        {
            if (IsNullOrEmpty(s))
            {
                return false;
            }
            return !s.StartsWith("$") && Validate(s);
        }

        private static bool IsValidChar(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                                                   (c >= '0' && c <= '9') || ValidSymbols.Contains(c);

        private static bool IsHexChar(char c) => (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') ||
                                                 (c >= '0' && c <= '9');

        private static bool Validate(string chars)
        {
            int len = chars.Length;
            var pos = 0;
            while (pos < len)
            {
                if (IsValidChar(chars[pos]))
                {
                    pos = pos + 1;
                }
                else if (chars[pos] == '%' && pos + 2 < len && IsHexChar(chars[pos + 1]) && IsHexChar(chars[pos + 2]))
                {
                    pos = pos + 3;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <param name="name"> The name. </param>
        protected ActorPath(Address address, string name)
        {
            Name = name;
            Address = address;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class.
        /// </summary>
        /// <param name="parentPath"> The parent path. </param>
        /// <param name="name"> The name. </param>
        /// <param name="uid"> The uid. </param>
        protected ActorPath(ActorPath parentPath, string name, long uid)
        {
            Address = parentPath.Address;
            Uid = uid;
            Name = name;
        }

        /// <summary>
        /// Gets the uid.
        /// </summary>
        /// <value> The uid. </value>
        public long Uid { get; }

        internal static readonly string[] EmptyElements = { };

        /// <summary>
        /// Gets the elements.
        /// </summary>
        /// <value> The elements. </value>
        public abstract IReadOnlyList<string> Elements { get; }

        /// <summary>
        /// INTERNAL API.
        ///
        /// Used in Akka.Remote - when resolving deserialized local actor references
        /// we need to be able to include the UID at the tail end of the elements.
        ///
        /// It's implemented in this class because we don't have an ActorPathExtractor equivalent.
        /// </summary>
        internal IReadOnlyList<string> ElementsWithUid
        {
            get
            {
                if (this is RootActorPath) return EmptyElements;
                var elements = (List<string>)Elements;
                elements[elements.Count - 1] = AppendUidFragment(Name);
                return elements;
            }
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value> The name. </value>
        public string Name { get; }

        /// <summary>
        /// The Address under which this path can be reached; walks up the tree to
        /// the RootActorPath.
        /// </summary>
        /// <value> The address. </value>
        public Address Address { get; }

        /// <summary>
        /// The root actor path.
        /// </summary>
        public abstract ActorPath Root { get; }

        /// <summary>
        /// The path of the parent to this actor.
        /// </summary>
        public abstract ActorPath Parent { get; }

        /// <inheritdoc/>
        public bool Equals(ActorPath other)
        {
            if (other == null)
                return false;

            if (!Address.Equals(other.Address))
                return false;

            ActorPath a = this;
            ActorPath b = other;
            for (; ; )
            {
                if (ReferenceEquals(a, b))
                    return true;
                else if (a == null || b == null)
                    return false;
                else if (a.Name != b.Name)
                    return false;

                a = a.Parent;
                b = b.Parent;
            }
        }

        /// <inheritdoc/>
        public abstract int CompareTo(ActorPath other);

        /// <summary>
        /// Withes the uid.
        /// </summary>
        /// <param name="uid"> The uid. </param>
        /// <returns> ActorPath. </returns>
        public abstract ActorPath WithUid(long uid);

        /// <summary>
        /// Creates a new <see cref="ChildActorPath"/> with the specified parent <paramref name="path"/>
        /// and the specified <paramref name="name"/>.
        /// </summary>
        /// <param name="path">The parent path of the newly created actor path</param>
        /// <param name="name">The name of child actor path</param>
        /// <returns>A newly created <see cref="ChildActorPath"/></returns>
        public static ActorPath operator /(ActorPath path, string name)
        {
            var (s, uid) = ActorCell.GetNameAndUid(name);
            return new ChildActorPath(path, s, uid);
        }

        /// <summary>
        /// Creates a new <see cref="ActorPath"/> by appending all the names in <paramref name="name"/>
        /// to the specified <paramref name="path"/>.
        /// </summary>
        /// <param name="path">The base path of the newly created actor path.</param>
        /// <param name="name">The names being appended to the specified <paramref name="path"/>.</param>
        /// <returns>A newly created <see cref="ActorPath"/></returns>
        public static ActorPath operator /(ActorPath path, IEnumerable<string> name)
        {
            var a = path;
            foreach (string element in name)
            {
                if (!string.IsNullOrEmpty(element))
                    a = a / element;
            }
            return a;
        }

        /// <summary>
        /// Creates an <see cref="ActorPath"/> from the specified <paramref name="path"/>.
        /// </summary>
        /// <param name="path">The string representing a possible <see cref="ActorPath"/></param>
        /// <exception cref="UriFormatException">
        /// This exception is thrown if the given <paramref name="path"/> cannot be parsed into an <see cref="ActorPath"/>.
        /// </exception>
        /// <returns>A newly created <see cref="ActorPath"/></returns>
        public static ActorPath Parse(string path)
        {
            ActorPath actorPath;
            if (TryParse(path, out actorPath))
            {
                return actorPath;
            }
            throw new UriFormatException($"Can not parse an ActorPath: {path}");
        }

        /// <summary>
        /// Tries to parse the uri, which should be a full uri, i.e containing protocol.
        /// For example "akka://System/user/my-actor"
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="actorPath">TBD</param>
        /// <returns>TBD</returns>
        public static bool TryParse(string path, out ActorPath actorPath)
        {
            actorPath = null;

            if (!TryParseAddress(path, out var address, out var absoluteUri)) return false;
            var spanified = absoluteUri;

            // check for Uri fragment here
            var nextSlash = 0;

            actorPath = new RootActorPath(address);

            do
            {
                nextSlash = spanified.IndexOf('/');
                if (nextSlash > 0)
                {
                    actorPath /= spanified.Slice(0, nextSlash).ToString();
                }
                else if (nextSlash < 0 && spanified.Length > 0) // final segment
                {
                    var fragLoc = spanified.IndexOf('#');
                    if (fragLoc > -1)
                    {
                        var fragment = spanified.Slice(fragLoc+1);
                        var fragValue = SpanHacks.Parse(fragment);
                        spanified = spanified.Slice(0, fragLoc);
                        actorPath = new ChildActorPath(actorPath, spanified.ToString(), fragValue);
                    }
                    else
                    {
                        actorPath /= spanified.ToString();
                    }
                    
                }

                spanified = spanified.Slice(nextSlash + 1);
            } while (nextSlash >= 0);

            return true;
        }

        /// <summary>
        /// Attempts to parse an <see cref="Address"/> from a stringified <see cref="ActorPath"/>.
        /// </summary>
        /// <param name="path">The string representation of the <see cref="ActorPath"/>.</param>
        /// <param name="address">If <c>true</c>, the parsed <see cref="Address"/>. Otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if the <see cref="Address"/> could be parsed, <c>false</c> otherwise.</returns>
        public static bool TryParseAddress(string path, out Address address)
        {
            return TryParseAddress(path, out address, out var _);
        }

        /// <summary>
        /// Attempts to parse an <see cref="Address"/> from a stringified <see cref="ActorPath"/>.
        /// </summary>
        /// <param name="path">The string representation of the <see cref="ActorPath"/>.</param>
        /// <param name="address">If <c>true</c>, the parsed <see cref="Address"/>. Otherwise <c>null</c>.</param>
        /// <param name="absoluteUri">A <see cref="ReadOnlySpan{T}"/> containing the path following the address.</param>
        /// <returns><c>true</c> if the <see cref="Address"/> could be parsed, <c>false</c> otherwise.</returns>
        private static bool TryParseAddress(string path, out Address address, out ReadOnlySpan<char> absoluteUri)
        {
            address = null;

            var spanified = path.AsSpan();
            absoluteUri = spanified;

            var firstColonPos = spanified.IndexOf(':');

            if (firstColonPos == -1) // not an absolute Uri
                return false;

            var fullScheme = SpanHacks.ToLowerInvariant(spanified.Slice(0, firstColonPos));
            if (!fullScheme.StartsWith("akka"))
                return false;

            spanified = spanified.Slice(firstColonPos + 1);
            if (spanified.Length < 2 || !(spanified[0] == '/' && spanified[1] == '/'))
                return false;

            spanified = spanified.Slice(2); // move past the double //
            var firstAtPos = spanified.IndexOf('@');
            var sysName = string.Empty;

            if (firstAtPos == -1)
            { // dealing with an absolute local Uri
                var nextSlash = spanified.IndexOf('/');

                if (nextSlash == -1)
                {
                    sysName = spanified.ToString();
                    absoluteUri = "/".AsSpan(); // RELY ON THE JIT
                }
                else
                {
                    sysName = spanified.Slice(0, nextSlash).ToString();
                    absoluteUri = spanified.Slice(nextSlash);
                }

                address = new Address(fullScheme, sysName);
                return true;
            }

            // dealing with a remote Uri
            sysName = spanified.Slice(0, firstAtPos).ToString();
            spanified = spanified.Slice(firstAtPos + 1);

            /*
             * Need to check for:
             * - IPV4 / hostnames
             * - IPV6 (must be surrounded by '[]') according to spec.
             */
            var host = string.Empty;

            // check for IPV6 first
            var openBracket = spanified.IndexOf('[');
            var closeBracket = spanified.IndexOf(']');
            if (openBracket > -1 && closeBracket > openBracket)
            {
                // found an IPV6 address
                host = spanified.Slice(openBracket, closeBracket - openBracket + 1).ToString();
                spanified = spanified.Slice(closeBracket + 1); // advance past the address
                
                // need to check for trailing colon
                var secondColonPos = spanified.IndexOf(':');
                if (secondColonPos == -1)
                    return false;

                spanified = spanified.Slice(secondColonPos + 1);
            }
            else
            {
                var secondColonPos = spanified.IndexOf(':');
                if (secondColonPos == -1)
                    return false;

                host = spanified.Slice(0, secondColonPos).ToString();

                // move past the host
                spanified = spanified.Slice(secondColonPos + 1);
            }

            var actorPathSlash = spanified.IndexOf('/');
            ReadOnlySpan<char> strPort;
            if (actorPathSlash == -1)
            {
                strPort = spanified;
            }
            else
            {
                strPort = spanified.Slice(0, actorPathSlash);
            }

            if (SpanHacks.TryParse(strPort, out var port))
            {
                address = new Address(fullScheme, sysName, host, port);

                // need to compute the absolute path after the Address
                if (actorPathSlash == -1)
                {
                    absoluteUri = "/".AsSpan();
                }
                else
                {
                    absoluteUri = spanified.Slice(actorPathSlash);
                }

                return true;
            }

            return false;
        }


        /// <summary>
        /// Joins this instance.
        /// </summary>
        /// <returns> System.String. </returns>
        private string Join(Address addrOption = null, long? uidOption = null)
        {
            if (this is RootActorPath && addrOption == null)
                return "/";

            // Resolve length of final string
            var totalPathLength = 0;
            var p = this;
            if (p is RootActorPath)
            {
                totalPathLength = 1; // "/"
            }
            
            while (!(p is RootActorPath))
            {
                totalPathLength += p.Name.Length + 1;
                p = p.Parent;
            }

            var remote = !string.IsNullOrWhiteSpace(addrOption?.Host) && addrOption.Port.HasValue;
            var addrLength = addrOption == null ? 0 : (remote
                ? addrOption.Protocol.Length + 3 + addrOption.System.Length + 1 + addrOption.Host.Length + 1 + 11 // 11 MAX characters for port number
                : addrOption.Protocol.Length + 3 + addrOption.System.Length);
            Span<char> writeSpan = stackalloc char[addrLength];
            if (addrOption != null)
            {
               
                var curPos = 0;

                var protSpan = addrOption.Protocol.AsSpan();
                curPos += SpanHacks.CopySpans(protSpan, writeSpan, curPos);

                writeSpan[curPos++] = ':';
                writeSpan[curPos++] = '/';
                writeSpan[curPos++] = '/';

                var sysSpan = addrOption.System.AsSpan();
                curPos += SpanHacks.CopySpans(sysSpan, writeSpan, curPos);

                if (remote)
                {
                    writeSpan[curPos++] = '@';
                    var hostSpan = addrOption.Host.AsSpan();
                    curPos += SpanHacks.CopySpans(hostSpan, writeSpan, curPos);
                    writeSpan[curPos++] = ':';
                    Span<char> portSpan = stackalloc char[11];
                    var length = addrOption.Port.Value.AsCharSpan(portSpan);
                    curPos += SpanHacks.CopySpans(portSpan.Slice(0, length), writeSpan, curPos);
                }

                addrLength = curPos;
            }

            // 20 characters is the max for a long integer
           
            Span<char> uidSpan = stackalloc char[20];
            var intLength = 0;
            var adjustedUidLength = 0;
            if (uidOption.HasValue && uidOption != ActorCell.UndefinedUid)
            {
                intLength = uidOption.Value.AsCharSpan(uidSpan);
                adjustedUidLength = intLength + 1; // need 1 extra for '#'
            }

            // Concatenate segments (in reverse order) into buffer with '/' prefixes
            Span<char> buffer = stackalloc char[addrLength + totalPathLength + adjustedUidLength];

            // copy address
            writeSpan.Slice(0, addrLength).CopyTo(buffer);

            // need to start after address but before uid
            var offset = buffer.Length - adjustedUidLength;
            p = this; // need to reset local var after previous traversal
            if (totalPathLength == 1) // RootActorPath
            {
                buffer[offset-1] = '/';
            }

            while (!(p is RootActorPath))
            {
                offset -= p.Name.Length + 1;
                buffer[offset] = '/';

                var spanified = p.Name.AsSpan();
                var writeOffset = offset;
                for (var i = 0; i < spanified.Length; i++)
                {
                    buffer[++writeOffset] = spanified[i];
                }

                p = p.Parent;
            }

            if (adjustedUidLength > 0)
            {
                var uidOffset = buffer.Length - adjustedUidLength;
                buffer[offset++] = '#';
                SpanHacks.CopySpans(uidSpan.Slice(0, intLength), buffer, offset);
            }

            return buffer.ToString();
        }

        /// <summary>
        /// String representation of the path elements, excluding the address
        /// information. The elements are separated with "/" and starts with "/",
        /// e.g. "/user/a/b".
        /// </summary>
        /// <returns> System.String. </returns>
        public string ToStringWithoutAddress()
        {
            return Join();
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Join(Address);
        }

        /// <summary>
        /// Returns a string representation of this instance including uid.
        /// </summary>
        /// <returns>TBD</returns>
        public string ToStringWithUid()
        {
            var uid = Uid;
            if (uid == ActorCell.UndefinedUid)
                return ToStringWithAddress();
            return Join(Address, Uid);
            //ToStringWithAddress() + "#" + uid;
        }

        /// <summary>
        /// Creates a child with the specified name
        /// </summary>
        /// <param name="childName"> Name of the child. </param>
        /// <returns> ActorPath. </returns>
        public ActorPath Child(string childName)
        {
            return this / childName;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 23) ^ Address.GetHashCode();
                foreach (var e in Elements)
                    hash = (hash * 23) ^ e.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            var other = obj as ActorPath;
            return Equals(other);
        }

        /// <summary>
        /// Compares two specified actor paths for equality.
        /// </summary>
        /// <param name="left">The first actor path used for comparison</param>
        /// <param name="right">The second actor path used for comparison</param>
        /// <returns><c>true</c> if both actor paths are equal; otherwise <c>false</c></returns>
        public static bool operator ==(ActorPath left, ActorPath right)
        {
            return Equals(left, right);
        }

        /// <summary>
        /// Compares two specified actor paths for inequality.
        /// </summary>
        /// <param name="left">The first actor path used for comparison</param>
        /// <param name="right">The second actor path used for comparison</param>
        /// <returns><c>true</c> if both actor paths are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(ActorPath left, ActorPath right)
        {
            return !Equals(left, right);
        }

        /// <summary>
        /// Generate String representation, with the address in the RootActorPath.
        /// </summary>
        /// <returns> System.String. </returns>
        public string ToStringWithAddress()
        {
            return ToStringWithAddress(Address);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public string ToSerializationFormat()
        {
            if (Uid == ActorCell.UndefinedUid)
                return ToStringWithAddress();
            return Join(Address, Uid);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <returns>TBD</returns>
        public string ToSerializationFormatWithAddress(Address address)
        {
            if (IgnoreActorRef.IsIgnoreRefPath(this))
            {
                // we never change address for IgnoreActorRef
                return ToString();
            }
            //var withAddress = ToStringWithAddress(address);
            //var result = AppendUidFragment(withAddress);
            return Join(address, Uid);
        }

        private string AppendUidFragment(string withAddress)
        {
            if (Uid == ActorCell.UndefinedUid)
                return withAddress;

            return String.Concat(withAddress, "#", Uid.ToString());
        }

        /// <summary>
        /// Generate String representation, replacing the Address in the RootActorPath
        /// with the given one unless this path’s address includes host and port
        /// information.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <returns> System.String. </returns>
        public string ToStringWithAddress(Address address)
        {
            if (IgnoreActorRef.IsIgnoreRefPath(this))
            {
                // we never change address for IgnoreActorRef
                return ToString();
            }
            if (Address.Host != null && Address.Port.HasValue)
                return Join(Address);

            return Join(address);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pathElements">TBD</param>
        /// <returns>TBD</returns>
        public static string FormatPathElements(IEnumerable<string> pathElements)
        {
            return String.Join("/", pathElements);
        }

        /// <summary>
        /// Creates a surrogate representation of the current <see cref="ActorPath"/>.
        /// </summary>
        /// <param name="system">The actor system that references this actor path.</param>
        /// <returns>The surrogate representation of the current <see cref="ActorPath"/>.</returns>
        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new Surrogate(ToSerializationFormat());
        }
    }

    /// <summary>
    /// Actor paths for root guardians, such as "/user" and "/system"
    /// </summary>
    public class RootActorPath : ActorPath
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RootActorPath" /> class.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <param name="name"> The name. </param>
        public RootActorPath(Address address, string name = "")
            : base(address, name)
        {
        }

        /// <inheritdoc/>
        public override ActorPath Parent => null;

        public override IReadOnlyList<string> Elements => EmptyElements;

        /// <inheritdoc/>
        [JsonIgnore]
        public override ActorPath Root => this;

        /// <inheritdoc/>
        public override ActorPath WithUid(long uid)
        {
            if (uid == 0)
                return this;
            throw new NotSupportedException("RootActorPath must have undefined Uid");
        }

        /// <inheritdoc/>
        public override int CompareTo(ActorPath other)
        {
            if (other is ChildActorPath) return 1;
            return Compare(ToString(), other.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Actor paths for child actors, which is to say any non-guardian actor.
    /// </summary>
    public class ChildActorPath : ActorPath
    {
        private readonly string _name;
        private readonly ActorPath _parent;

        /// <summary>
        /// Initializes a new instance of the <see cref="ChildActorPath" /> class.
        /// </summary>
        /// <param name="parentPath"> The parent path. </param>
        /// <param name="name"> The name. </param>
        /// <param name="uid"> The uid. </param>
        public ChildActorPath(ActorPath parentPath, string name, long uid)
            : base(parentPath, name, uid)
        {
            _name = name;
            _parent = parentPath;
        }

        /// <inheritdoc/>
        public override ActorPath Parent => _parent;

        public override IReadOnlyList<string> Elements
        {
            get
            {
                ActorPath p = this;
                var acc = new Stack<string>();
                while (true)
                {
                    if (p is RootActorPath)
                        return acc.ToList();
                    acc.Push(p.Name);
                    p = p.Parent;
                }
            }
        }

        /// <inheritdoc/>
        public override ActorPath Root
        {
            get
            {
                var current = _parent;
                while (current is ChildActorPath child)
                {
                    current = child._parent;
                }
                return current.Root;
            }
        }

        /// <summary>
        /// Creates a copy of the given ActorPath and applies a new Uid
        /// </summary>
        /// <param name="uid"> The uid. </param>
        /// <returns> ActorPath. </returns>
        public override ActorPath WithUid(long uid)
        {
            if (uid == Uid)
                return this;
            return new ChildActorPath(_parent, _name, uid);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 23) ^ Address.GetHashCode();
                for (ActorPath p = this; p != null; p = p.Parent)
                    hash = (hash * 23) ^ p.Name.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc/>
        public override int CompareTo(ActorPath other)
        {
            return InternalCompareTo(this, other);
        }

        private int InternalCompareTo(ActorPath left, ActorPath right)
        {
            if (ReferenceEquals(left, right)) return 0;
            var leftRoot = left as RootActorPath;
            if (leftRoot != null)
                return leftRoot.CompareTo(right);
            var rightRoot = right as RootActorPath;
            if (rightRoot != null)
                return -rightRoot.CompareTo(left);
            var nameCompareResult = Compare(left.Name, right.Name, StringComparison.Ordinal);
            if (nameCompareResult != 0)
                return nameCompareResult;
            return InternalCompareTo(left.Parent, right.Parent);
        }
    }
}
