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
                TryParse(Path, out var path);
                return path;
            }

            #region Equality

            public bool Equals(Surrogate other)
            {
                if (other is null) return false;
                return ReferenceEquals(this, other) || StringComparer.Ordinal.Equals(Path, other.Path);
            }

            public bool Equals(ActorPath other)
            {
                if (other is null) return false;
                return StringComparer.Ordinal.Equals(Path, other.ToSerializationFormat());
            }

            public override bool Equals(object obj)
            {
                if (obj is null) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj is ActorPath actorPath) return Equals(actorPath);
                return Equals(obj as Surrogate);
            }

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
            var len = chars.Length;
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
            _address = parentPath._address;
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
                    b[_depth - i - 1] = p._name;
                    p = p._parent;
                }
                return b.MoveToImmutable();
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
                return b.MoveToImmutable();
            }
        }

        /// <summary>
        /// The root actor path.
        /// </summary>
        [JsonIgnore]
        public ActorPath Root => ParentOf(0);

        public bool Equals(ActorPath other)
        {
            if (other is null || _depth != other._depth)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            if (!Address.Equals(other.Address))
                return false;

            var a = this;
            var b = other;
            while (true)
            {
                if (ReferenceEquals(a, b))
                    return true;
                else if (a is null || b is null)
                    return false;
                else if (a._name != b._name)
                    return false;

                a = a._parent;
                b = b._parent;
            }
        }

        public int CompareTo(ActorPath other)
        {
            if (_depth == 0)
            {
                if (other is null || other._depth > 0) return 1;
                return StringComparer.Ordinal.Compare(ToString(), other.ToString());
            }
            return InternalCompareTo(this, other);
        }

        private int InternalCompareTo(ActorPath left, ActorPath right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (right is null)
                return 1;
            if (left is null)
                return -1;

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

            return uid != _uid ? new ChildActorPath(_parent, Name, uid) : this;
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
            foreach (var element in name)
            {
                if (!string.IsNullOrEmpty(element))
                    a /= element;
            }
            return a;
        }

        /// <summary>
        /// Returns a parent of depth
        /// 0: Root, 1: Guardian, ..., -1: Parent, -2: GrandParent
        /// </summary>
        /// <param name="depth">The parent depth, negative depth for reverse lookup</param>
        public ActorPath ParentOf(int depth)
        {
            var current = this;
            if (depth >= 0)
            {
                while (current._depth > depth)
                    current = current._parent;
            }
            else
            {
                for (var i = depth; i < 0 && current._depth > 0; i++)
                    current = current._parent;
            }
            return current;
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
            return TryParse(path, out var actorPath)
                ? actorPath
                : throw new UriFormatException($"Can not parse an ActorPath: {path}");
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
            if (!TryParseAddress(path, out var address, out var absoluteUri))
            {
                actorPath = null;
                return false;
            }

            return TryParse(new RootActorPath(address), absoluteUri, out actorPath);
        }

        /// <summary>
        /// Tries to parse the uri, which should be a uri not containing protocol.
        /// For example "/user/my-actor"
        /// </summary>
        /// <param name="basePath">the base path, normaly a root path</param>
        /// <param name="absoluteUri">TBD</param>
        /// <param name="actorPath">TBD</param>
        /// <returns>TBD</returns>
        public static bool TryParse(ActorPath basePath, string absoluteUri, out ActorPath actorPath)
        {
            return TryParse(basePath, absoluteUri.AsSpan(), out actorPath);
        }

        /// <summary>
        /// Tries to parse the uri, which should be a uri not containing protocol.
        /// For example "/user/my-actor"
        /// </summary>
        /// <param name="basePath">the base path, normaly a root path</param>
        /// <param name="absoluteUri">TBD</param>
        /// <param name="actorPath">TBD</param>
        /// <returns>TBD</returns>
        public static bool TryParse(ActorPath basePath, ReadOnlySpan<char> absoluteUri, out ActorPath actorPath)
        {
            actorPath = basePath;

            // check for Uri fragment here
            int nextSlash;

            do
            {
                nextSlash = absoluteUri.IndexOf('/');
                if (nextSlash > 0)
                {
                    var name = absoluteUri.Slice(0, nextSlash).ToString();
                    actorPath = new ChildActorPath(actorPath, name, ActorCell.UndefinedUid);
                }
                else if (nextSlash < 0 && absoluteUri.Length > 0) // final segment
                {
                    var fragLoc = absoluteUri.IndexOf('#');
                    if (fragLoc > -1)
                    {
                        var fragment = absoluteUri.Slice(fragLoc + 1);
                        var fragValue = SpanHacks.Parse(fragment);
                        absoluteUri = absoluteUri.Slice(0, fragLoc);
                        actorPath = new ChildActorPath(actorPath, absoluteUri.ToString(), fragValue);
                    }
                    else
                    {
                        actorPath = new ChildActorPath(actorPath, absoluteUri.ToString(), ActorCell.UndefinedUid);
                    }

                }

                absoluteUri = absoluteUri.Slice(nextSlash + 1);
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
        public static bool TryParseAddress(string path, out Address address, out ReadOnlySpan<char> absoluteUri)
        {
            address = default;

            if (!TryParseParts(path.AsSpan(), out var addressSpan, out absoluteUri))
                return false;

            if (!Address.TryParse(addressSpan, out address))
                return false;

            return true;
        }

        /// <summary>
        /// Attempts to parse an <see cref="Address"/> from a stringified <see cref="ActorPath"/>.
        /// </summary>
        /// <param name="path">The string representation of the <see cref="ActorPath"/>.</param>
        /// <param name="address">A <see cref="ReadOnlySpan{T}"/> containing the address part.</param>
        /// <param name="absoluteUri">A <see cref="ReadOnlySpan{T}"/> containing the path following the address.</param>
        /// <returns><c>true</c> if the path parts could be parsed, <c>false</c> otherwise.</returns>
        public static bool TryParseParts(ReadOnlySpan<char> path, out ReadOnlySpan<char> address, out ReadOnlySpan<char> absoluteUri)
        {
            var firstAtPos = path.IndexOf(':');
            if (firstAtPos < 4 || 255 < firstAtPos)
            {
                //missing or invalid scheme
                address = default;
                absoluteUri = path;
                return false;
            }

            var doubleSlash = path.Slice(firstAtPos + 1);
            if (doubleSlash.Length < 2 || !(doubleSlash[0] == '/' && doubleSlash[1] == '/'))
            {
                //missing double slash
                address = default;
                absoluteUri = path;
                return false;
            }

            var nextSlash = path.Slice(firstAtPos + 3).IndexOf('/');
            if (nextSlash == -1)
            {
                address = path;
                absoluteUri = "/".AsSpan(); // RELY ON THE JIT
            }
            else
            {
                address = path.Slice(0, firstAtPos + 3 + nextSlash);
                absoluteUri = path.Slice(address.Length);
            }

            return true;
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
                Span<char> buffer = prefix.Length < 1024 ? stackalloc char[prefix.Length + 1] : new char[prefix.Length + 1];
                prefix.CopyTo(buffer);
                buffer[buffer.Length - 1] = '/';
                return buffer.ToString(); //todo use string.Create() when available
            }
            else
            {
                // Resolve length of final string
                var totalLength = prefix.Length;
                var p = this;
                while (p._depth > 0)
                {
                    totalLength += p._name.Length + 1;
                    p = p._parent;
                }

                // Concatenate segments (in reverse order) into buffer with '/' prefixes                
                Span<char> buffer = totalLength < 1024 ? stackalloc char[totalLength] : new char[totalLength];
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
                    p = p._parent;
                }
                return buffer.ToString(); //todo use string.Create() when available
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
                for (var p = this; !(p is null); p = p._parent)
                    hash = (hash * 23) ^ p._name.GetHashCode();
                return hash;
            }
        }

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
