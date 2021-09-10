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
        public sealed class Surrogate : ISurrogate, IEquatable<Surrogate>, IEquatable<ActorPath>
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
                return TryParse(Path, out var path) ? path : null;
            }

            #region Equality

            /// <inheritdoc/>
            public bool Equals(Surrogate other)
            {
                if (other is null) return false;
                return ReferenceEquals(this, other) || StringComparer.Ordinal.Equals(Path, other.Path);
            }

            /// <inheritdoc/>
            public bool Equals(ActorPath other)
            {
                if (other is null) return false;
                return Equals(other.ToSurrogate(null)); //TODO: not so sure if this is OK
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (obj is null) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj is ActorPath actorPath) return Equals(actorPath);
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
            return !string.IsNullOrEmpty(s) && !s.StartsWith("$") && Validate(s);
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
                    pos += 1;
                }
                else if (chars[pos] == '%' && pos + 2 < len && IsHexChar(chars[pos + 1]) && IsHexChar(chars[pos + 2]))
                {
                    pos += 3;
                }
                else
                {
                    return false;
                }
            }
            return true;
        }

        private readonly Address _address;
        private readonly ActorPath _parent;
        private readonly int _depth;

        private readonly string _name;
        private readonly long _uid;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class as root.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <param name="name"> The name. </param>
        protected ActorPath(Address address, string name)
        {
            _address = address;
            _parent = null;
            _depth = 0;
            _name = name;
            _uid = ActorCell.UndefinedUid;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class as child.
        /// </summary>
        /// <param name="parentPath"> The parentPath. </param>
        /// <param name="name"> The name. </param>
        /// <param name="uid"> The uid. </param>
        protected ActorPath(ActorPath parentPath, string name, long uid)
        {
            _parent = parentPath;
            _address = parentPath.Address;
            _depth = parentPath._depth + 1;
            _name = name;
            _uid = uid;
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value> The name. </value>
        public string Name => _name;

        /// <summary>
        /// The Address under which this path can be reached; walks up the tree to
        /// the RootActorPath.
        /// </summary>
        /// <value> The address. </value>
        public Address Address => _address;

        /// <summary>
        /// Gets the uid.
        /// </summary>
        /// <value> The uid. </value>
        public long Uid => _uid;

        /// <summary>
        /// The path of the parent to this actor.
        /// </summary>
        public ActorPath Parent => _parent;

        /// <summary>
        /// The the depth of the actor.
        /// </summary>
        public int Depth => _depth;

        /// <summary>
        /// Gets the elements.
        /// </summary>
        /// <value> The elements. </value>
        public IReadOnlyList<string> Elements
        {
            get
            {
                if (_depth == 0)
                    return ImmutableArray<string>.Empty;

                var b = ImmutableArray.CreateBuilder<string>(_depth);
                b.Count = _depth;
                var p = this;
                for (var i = 0; i < _depth; i++)
                {
                    b[_depth - i - 1] = p.Name;
                    p = p._parent;
                }
                return b.ToImmutable();
            }
        }

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
                if (_depth == 0)
                    return ImmutableArray<string>.Empty;

                var b = ImmutableArray.CreateBuilder<string>(_depth);
                b.Count = _depth;
                var p = this;
                for (var i = 0; i < _depth; i++)
                {
                    b[_depth - i - 1] = i > 0 ? p._name : AppendUidFragment(p._name);
                    p = p._parent;
                }
                return b.ToImmutable();
            }
        }

        /// <summary>
        /// The root actor path.
        /// </summary>
        [JsonIgnore]
        public ActorPath Root
        {
            get
            {
                var current = this;
                while (current._depth > 0)
                    current = current.Parent;
                return current;
            }
        }

        /// <inheritdoc/>
        public bool Equals(ActorPath other)
        {
            if (other == null)
                return false;

            if (!Address.Equals(other.Address))
                return false;

            ActorPath a = this;
            ActorPath b = other;
            while (true)
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
        public int CompareTo(ActorPath other)
        {
            if (_depth == 0)
            {
                if (other is ChildActorPath) return 1;
                return StringComparer.Ordinal.Compare(ToString(), other?.ToString());
            }
            return InternalCompareTo(this, other);
        }

        private int InternalCompareTo(ActorPath left, ActorPath right)
        {
            if (ReferenceEquals(left, right))
                return 0;

            if (left._depth == 0)
                return left.CompareTo(right);

            if (right._depth == 0)
                return -right.CompareTo(left);

            var nameCompareResult = StringComparer.Ordinal.Compare(left._name, right._name);
            if (nameCompareResult != 0)
                return nameCompareResult;

            return InternalCompareTo(left._parent, right._parent);
        }

        /// <summary>
        /// Creates a copy of the given ActorPath and applies a new Uid
        /// </summary>
        /// <param name="uid"> The uid. </param>
        /// <returns> ActorPath. </returns>
        public ActorPath WithUid(long uid)
        {
            if (_depth == 0)
            {
                if (uid != 0) throw new NotSupportedException("RootActorPath must have undefined Uid");
                return this;
            }

            return uid != Uid ? new ChildActorPath(_parent, Name, uid) : this;
        }

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
            if (!TryParse(path, out var actorPath))
                throw new UriFormatException($"Can not parse an ActorPath: {path}");

            return actorPath;
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
            //todo lookup address and/or root in cache

            if (!TryParseAddress(path, out var address, out var spanified))
            {
                actorPath = null;
                return false;
            }

            // check for Uri fragment here
            int nextSlash;

            actorPath = new RootActorPath(address);

            do
            {
                nextSlash = spanified.IndexOf('/');
                if (nextSlash > 0)
                {
                    var name = spanified.Slice(0, nextSlash).ToString();
                    actorPath = new ChildActorPath(actorPath, name, ActorCell.UndefinedUid);
                }
                else if (nextSlash < 0 && spanified.Length > 0) // final segment
                {
                    var fragLoc = spanified.IndexOf('#');
                    if (fragLoc > -1)
                    {
                        var fragment = spanified.Slice(fragLoc + 1);
                        var fragValue = SpanHacks.Parse(fragment);
                        spanified = spanified.Slice(0, fragLoc);
                        actorPath = new ChildActorPath(actorPath, spanified.ToString(), fragValue);
                    }
                    else
                    {
                        actorPath = new ChildActorPath(actorPath, spanified.ToString(), ActorCell.UndefinedUid);
                    }

                }

                spanified = spanified.Slice(nextSlash + 1);
            }
            while (nextSlash >= 0);

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
            string sysName;

            if (firstAtPos == -1)
            {
                // dealing with an absolute local Uri
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
            string host;

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
        /// <param name="prefix">the address or empty</param>
        /// <returns> System.String. </returns>
        private string Join(ReadOnlySpan<char> prefix)
        {
            if (_depth == 0)
            {
                Span<char> buffer = stackalloc char[prefix.Length+1];
                prefix.CopyTo(buffer);
                buffer[buffer.Length-1] = '/';
                return buffer.ToString();
            }
            else
            {
                // Resolve length of final string
                var totalLength = prefix.Length;
                var p = this;
                while (p._depth > 0)
                {
                    totalLength += p._name.Length + 1;
                    p = p.Parent;
                }

                // Concatenate segments (in reverse order) into buffer with '/' prefixes
                Span<char> buffer = stackalloc char[totalLength];
                prefix.CopyTo(buffer);

                var offset = buffer.Length;
                ReadOnlySpan<char> name;
                p = this;
                while (p._depth > 0)
                {
                    name = p._name.AsSpan();
                    offset -= name.Length + 1;
                    buffer[offset] = '/';
                    name.CopyTo(buffer.Slice(offset + 1, name.Length));
                    p = p.Parent;
                }
                return buffer.ToString();
            }            
        }

        /// <summary>
        /// String representation of the path elements, excluding the address
        /// information. The elements are separated with "/" and starts with "/",
        /// e.g. "/user/a/b".
        /// </summary>
        /// <returns> System.String. </returns>
        public string ToStringWithoutAddress()
        {
            return Join(ReadOnlySpan<char>.Empty);
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return Join(_address.ToString().AsSpan());
        }

        /// <summary>
        /// Returns a string representation of this instance including uid.
        /// </summary>
        /// <returns>TBD</returns>
        public string ToStringWithUid()
        {
            return _uid != ActorCell.UndefinedUid ? $"{ToStringWithAddress()}#{_uid}" : ToStringWithAddress();
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

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = (hash * 23) ^ Address.GetHashCode();
                for (var p = this; !(p is null); p = p.Parent)
                    hash = (hash * 23) ^ p.Name.GetHashCode();
                return hash;
            }
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return Equals(obj as ActorPath);
        }

        /// <summary>
        /// Compares two specified actor paths for equality.
        /// </summary>
        /// <param name="left">The first actor path used for comparison</param>
        /// <param name="right">The second actor path used for comparison</param>
        /// <returns><c>true</c> if both actor paths are equal; otherwise <c>false</c></returns>
        public static bool operator ==(ActorPath left, ActorPath right)
        {
            return left?.Equals(right) ?? right is null;
        }

        /// <summary>
        /// Compares two specified actor paths for inequality.
        /// </summary>
        /// <param name="left">The first actor path used for comparison</param>
        /// <param name="right">The second actor path used for comparison</param>
        /// <returns><c>true</c> if both actor paths are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(ActorPath left, ActorPath right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Generate String representation, with the address in the RootActorPath.
        /// </summary>
        /// <returns> System.String. </returns>
        public string ToStringWithAddress()
        {
            return ToStringWithAddress(_address);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public string ToSerializationFormat()
        {
            return AppendUidFragment(ToStringWithAddress());
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
            var withAddress = ToStringWithAddress(address);
            var result = AppendUidFragment(withAddress);
            return result;
        }

        private string AppendUidFragment(string withAddress)
        {
            return _uid != ActorCell.UndefinedUid ? $"{withAddress}#{_uid}" : withAddress;
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
            if (_address.Host != null && _address.Port.HasValue)
                return Join(_address.ToString().AsSpan());

            return Join(address.ToString().AsSpan());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="pathElements">TBD</param>
        /// <returns>TBD</returns>
        public static string FormatPathElements(IEnumerable<string> pathElements)
        {
            return string.Join("/", pathElements);
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
    public sealed class RootActorPath : ActorPath
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

    }

    /// <summary>
    /// Actor paths for child actors, which is to say any non-guardian actor.
    /// </summary>
    public sealed class ChildActorPath : ActorPath
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ChildActorPath" /> class.
        /// </summary>
        /// <param name="parentPath"> The parent path. </param>
        /// <param name="name"> The name. </param>
        /// <param name="uid"> The uid. </param>
        public ChildActorPath(ActorPath parentPath, string name, long uid)
            : base(parentPath, name, uid)
        {
        }
    }
}
