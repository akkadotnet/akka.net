//-----------------------------------------------------------------------
// <copyright file="ActorPath.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class as root.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <param name="name"> The name. </param>
        protected ActorPath(Address address, string name)
        {
            Address = address;
            Parent = null;
            Depth = 0;
            Name = name;
            Uid = ActorCell.UndefinedUid;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class as child.
        /// </summary>
        /// <param name="parentPath"> The parentPath. </param>
        /// <param name="name"> The name. </param>
        /// <param name="uid"> The uid. </param>
        protected ActorPath(ActorPath parentPath, string name, long uid)
        {
            Parent = parentPath;
            Address = parentPath.Address;
            Depth = parentPath.Depth + 1;
            Name = name;
            Uid = uid;
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
        /// Gets the uid.
        /// </summary>
        /// <value> The uid. </value>
        public long Uid { get; }

        /// <summary>
        /// The path of the parent to this actor.
        /// </summary>
        public ActorPath Parent { get; }

        /// <summary>
        /// The the depth of the actor.
        /// </summary>
        public int Depth { get; }

        /// <summary>
        /// Gets the elements.
        /// </summary>
        /// <value> The elements. </value>
        public IReadOnlyList<string> Elements
        {
            get
            {
                if (Depth == 0)
                    return ImmutableArray<string>.Empty;

                var b = ImmutableArray.CreateBuilder<string>(Depth);
                b.Count = Depth;
                var p = this;
                for (var i = 0; i < Depth; i++)
                {
                    b[Depth - i - 1] = p.Name;
                    p = p.Parent;
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
                if (Depth == 0)
                    return ImmutableArray<string>.Empty;

                var b = ImmutableArray.CreateBuilder<string>(Depth);
                b.Count = Depth;
                var p = this;
                for (var i = 0; i < Depth; i++)
                {
                    b[Depth - i - 1] = i > 0 ? p.Name : AppendUidFragment(p.Name);
                    p = p.Parent;
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
            if (other is null || Depth != other.Depth)
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
                else if (a.Name != b.Name)
                    return false;

                a = a.Parent;
                b = b.Parent;
            }
        }

        public int CompareTo(ActorPath other)
        {
            if (Depth == 0)
            {
                if (other is null || other.Depth > 0) return 1;
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

            if (left.Depth == 0)
                return left.CompareTo(right);

            if (right.Depth == 0)
                return -right.CompareTo(left);

            var nameCompareResult = StringComparer.Ordinal.Compare(left.Name, right.Name);
            if (nameCompareResult != 0)
                return nameCompareResult;

            return InternalCompareTo(left.Parent, right.Parent);
        }

        /// <summary>
        /// Creates a copy of the given ActorPath and applies a new Uid
        /// </summary>
        /// <param name="uid"> The uid. </param>
        /// <returns> ActorPath. </returns>
        public ActorPath WithUid(long uid)
        {
            if (Depth == 0)
            {
                if (uid != 0) throw new NotSupportedException("RootActorPath must have undefined Uid");
                return this;
            }

            return uid != Uid ? new ChildActorPath(Parent, Name, uid) : this;
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
                while (current.Depth > depth)
                    current = current.Parent;
            }
            else
            {
                for (var i = depth; i < 0 && current.Depth > 0; i++)
                    current = current.Parent;
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
            if (firstAtPos is < 4 or > 255)
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
        /// <param name="uid">Optional - the UID for this path.</param>
        /// <returns> System.String. </returns>
        private string Join(ReadOnlySpan<char> prefix, long? uid = null)
        {
            void AppendUidSpan(ref Span<char> writeable, int startPos, int sizeHint)
            {
                if (uid == null) return;
                writeable[startPos] = '#';
                SpanHacks.TryFormat(uid.Value, startPos+1, ref writeable, sizeHint);
            }

            if (Depth == 0)
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
                while (p.Depth > 0)
                {
                    totalLength += p.Name.Length + 1;
                    p = p.Parent;
                }
                
                // UID calculation
                var uidSizeHint = 0;
                if (uid != null)
                {
                    // 1 extra character for the '#'
                    uidSizeHint = SpanHacks.Int64SizeInCharacters(uid.Value) + 1;
                    totalLength += uidSizeHint; 
                }

                // Concatenate segments (in reverse order) into buffer with '/' prefixes                
                Span<char> buffer = totalLength < 1024 ? stackalloc char[totalLength] : new char[totalLength];
                prefix.CopyTo(buffer);

                var offset = buffer.Length - uidSizeHint;
                // append UID span first
                AppendUidSpan(ref buffer, offset, uidSizeHint-1); // -1 for the '#'
                
                p = this;
                while (p.Depth > 0)
                {
                    var name = p.Name.AsSpan();
                    offset -= name.Length + 1;
                    buffer[offset] = '/';
                    name.CopyTo(buffer.Slice(offset + 1, name.Length));
                    p = p.Parent;
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
            return Join(Address.ToString().AsSpan());
        }

        /// <summary>
        /// Returns a string representation of this instance including uid.
        /// </summary>
        /// <returns>TBD</returns>
        public string ToStringWithUid()
        {
            return Uid != ActorCell.UndefinedUid ? $"{ToStringWithAddress()}#{Uid}" : ToStringWithAddress();
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
            return ToStringWithAddress(Address, false);
        }

        private string ToStringWithAddress(bool includeUid)
        {
            return ToStringWithAddress(Address, includeUid);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public string ToSerializationFormat()
        {
            return ToStringWithAddress(true);
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
            var result = ToStringWithAddress(address, true);
            return result;
        }

        private string AppendUidFragment(string withAddress)
        {
            return Uid != ActorCell.UndefinedUid ? $"{withAddress}#{Uid}" : withAddress;
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
            return ToStringWithAddress(address, false);
        }

        private string ToStringWithAddress(Address address, bool includeUid)
        {
            if (IgnoreActorRef.IsIgnoreRefPath(this))
            {
                // we never change address for IgnoreActorRef
                return ToString();
            }
            
            long? uid = null;
            if (includeUid && Uid != ActorCell.UndefinedUid)
                uid = Uid;
            
            if (Address.Host != null && Address.Port.HasValue)
                return Join(Address.ToString().AsSpan(), uid);

            return Join(address.ToString().AsSpan(), uid);
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
