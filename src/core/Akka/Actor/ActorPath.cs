//-----------------------------------------------------------------------
// <copyright file="ActorPath.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            for (;;)
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
            var nameAndUid = ActorCell.SplitNameAndUid(name);
            return new ChildActorPath(path, nameAndUid.Name, nameAndUid.Uid);
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


            Address address;
            Uri uri;
            if (!TryParseAddress(path, out address, out uri)) return false;
            var pathElements = uri.AbsolutePath.Split('/');
            actorPath = new RootActorPath(address) / pathElements.Skip(1);
            if (uri.Fragment.StartsWith("#"))
            {
                var uid = int.Parse(uri.Fragment.Substring(1));
                actorPath = actorPath.WithUid(uid);
            }
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="address">TBD</param>
        /// <returns>TBD</returns>
        public static bool TryParseAddress(string path, out Address address)
        {
            Uri uri;
            return TryParseAddress(path, out address, out uri);
        }

        private static bool TryParseAddress(string path, out Address address, out Uri uri)
        {
            //This code corresponds to AddressFromURIString.unapply
            address = null;
            if (!Uri.TryCreate(path, UriKind.Absolute, out uri))
                return false;
            var protocol = uri.Scheme; //Typically "akka"
            if (!protocol.StartsWith("akka", StringComparison.OrdinalIgnoreCase))
            {
                // Protocol must start with 'akka.*
                return false;
            }


            string systemName;
            string host = null;
            int? port = null;
            if (IsNullOrEmpty(uri.UserInfo))
            {
                //  protocol://SystemName/Path1/Path2
                if (uri.Port > 0)
                {
                    //port may not be specified for these types of paths
                    return false;
                }
                //System name is in the "host" position. According to rfc3986 host is case 
                //insensitive, but should be produced as lowercase, so if we use uri.Host 
                //we'll get it in lower case.
                //So we'll extract it ourselves using the original path.
                //We skip the protocol and "://"
                var systemNameLength = uri.Host.Length;
                systemName = path.Substring(protocol.Length + 3, systemNameLength);
            }
            else
            {
                //  protocol://SystemName@Host:port/Path1/Path2
                systemName = uri.UserInfo;
                host = uri.Host;
                port = uri.Port;
            }
            address = new Address(protocol, systemName, host, port);
            return true;
        }


        /// <summary>
        /// Joins this instance.
        /// </summary>
        /// <returns> System.String. </returns>
        private string Join()
        {
            if (this is RootActorPath)
                return "/";

            // Resolve length of final string
            int totalLength = 0;
            ActorPath p = this;
            while (!(p is RootActorPath))
            {
                totalLength += p.Name.Length + 1;
                p = p.Parent;
            }

            // Concatenate segments (in reverse order) into buffer with '/' prefixes
            char[] buffer = new char[totalLength];
            int offset = buffer.Length;
            p = this;
            while (!(p is RootActorPath))
            {
                offset -= p.Name.Length + 1;
                buffer[offset] = '/';
                p.Name.CopyTo(0, buffer, offset + 1, p.Name.Length);
                p = p.Parent;
            }

            return new string(buffer);
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
            return ToStringWithAddress();
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
            return ToStringWithAddress() + "#" + uid;
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
            return AppendUidFragment(ToStringWithAddress());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="address">TBD</param>
        /// <returns>TBD</returns>
        public string ToSerializationFormatWithAddress(Address address)
        {
            var withAddress = ToStringWithAddress(address);
            var result = AppendUidFragment(withAddress);
            return result;
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
            if (Address.Host != null && Address.Port.HasValue)
                return $"{Address}{Join()}";

            return $"{address}{Join()}";
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
