//-----------------------------------------------------------------------
// <copyright file="ActorPath.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using Akka.Actor.Dsl;
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
                ActorPath path;
                if (TryParse(Path, out path))
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

        /** INTERNAL API */
        /// <summary>
        /// TBD
        /// </summary>
        internal static char[] ValidSymbols = @"""-_.*$+:@&=,!~';""()".ToCharArray();

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
            return !s.StartsWith("$") && Validate(s.ToCharArray(), s.Length);
        }

        private static bool IsValidChar(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || ValidSymbols.Contains(c);
        private static bool IsHexChar(char c) => (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F') || (c >= '0' && c <= '9');

        private static bool Validate(IReadOnlyList<char> chars, int len)
        {
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

        private static readonly Func<ActorPath, IList<string>> FillElementsFunc =
            actorPath => FillElements(actorPath);

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <param name="name"> The name. </param>
        protected ActorPath(Address address, string name)
        {
            Name = name;
            Address = address;
            _elements = new FastLazy<ActorPath, IList<string>>(FillElementsFunc, this);
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
            _elements = new FastLazy<ActorPath, IList<string>>(FillElementsFunc, this);
        }

        /// <summary>
        /// Gets the uid.
        /// </summary>
        /// <value> The uid. </value>
        public long Uid { get; }
        
        private readonly FastLazy<ActorPath, IList<string>> _elements;

        private static readonly string[] _emptyElements = { };
        private static readonly string[] _systemElements = { "system" };
        private static readonly string[] _userElements = { "user" };

        /// <summary>
        /// This method pursuits optimization goals mostly in terms of allocations.
        /// We're computing elements chain only once and storing it in <see cref="_elements" />.
        /// Computed chain meant to be reused not only by calls to <see cref="Elements" /> 
        /// but also during chain computation of children actors.
        /// </summary>
        private static IList<string> FillElements(ActorPath actorPath)
        {
            // fast path next three `if`
            if(actorPath is RootActorPath)
                return _emptyElements;
            if (actorPath.Parent is RootActorPath)
            {
                if (actorPath.Name.Equals("system", StringComparison.Ordinal))
                    return _systemElements;
                if (actorPath.Name.Equals("user", StringComparison.Ordinal))
                    return _userElements;
                return new [] {actorPath.Name};
            }
            // if our direct parent has computed chain we can skip list (for intermediate results) creation and resizing
            if (actorPath.Parent._elements.IsValueCreated)
            {
                var parentElems = actorPath.Parent._elements.Value;
                var myElems = new string[parentElems.Count + 1];
                parentElems.CopyTo(myElems, 0);
                myElems[myElems.Length - 1] = actorPath.Name;
                return myElems;
            }

            // walking from `this` instance upto root actor
            var current = actorPath;
            var elements = new List<string>();
            while (!(current is RootActorPath))
            {
                // there may be already computed elements chain for some of our parents, so reuse it!
                if (current._elements.IsValueCreated)
                {
                    var parentElems = current._elements.Value;
                    var myElems = new string[parentElems.Count + elements.Count];
                    parentElems.CopyTo(myElems, 0);
                    // parent's chain already in order, we need to reverse values collected so far
                    for (int i = elements.Count - 1; i >= 0; i--)
                    {
                        myElems[parentElems.Count + (elements.Count - 1 - i)] = elements[i];
                    }
                    return myElems;
                }
                elements.Add(current.Name);
                current = current.Parent;
            }
            // none of our parents have computed chain (no calls to Elements issued)
            elements.Reverse();
            return elements;
        }

        /// <summary>
        /// Gets the elements.
        /// </summary>
        /// <value> The elements. </value>
        public IReadOnlyList<string> Elements => new ReadOnlyCollection<string>(_elements.Value);

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
                if(this is RootActorPath) return new []{""};
                var elements = _elements.Value;
                var elementsWithUid = new string[elements.Count];
                elements.CopyTo(elementsWithUid, 0);
                elementsWithUid[elementsWithUid.Length - 1] = AppendUidFragment(Name);
                return elementsWithUid;
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
        /// TBD
        /// </summary>
        public abstract ActorPath Root { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public abstract ActorPath Parent { get; }

        /// <inheritdoc/>
        public bool Equals(ActorPath other)
        {
            if (other == null)
                return false;

            return Address.Equals(other.Address) && Elements.SequenceEqual(other.Elements);
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
            var joined = String.Join("/", Elements);
            return "/" + joined;
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
            return ToString().GetHashCode();
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
    /// Class RootActorPath.
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

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Parent => null;

        /// <summary>
        /// TBD
        /// </summary>
        [JsonIgnore]
        public override ActorPath Root => this;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="uid">TBD</param>
        /// <exception cref="NotSupportedException">This exception is thrown if the given <paramref name="uid"/> is not equal to 0.</exception>
        /// <returns>TBD</returns>
        public override ActorPath WithUid(long uid)
        {
            if (uid == 0)
                return this;
            throw new NotSupportedException("RootActorPath must have undefined Uid");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public override int CompareTo(ActorPath other)
        {
            if (other is ChildActorPath) return 1;
            return Compare(ToString(), other.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Class ChildActorPath.
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

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Parent => _parent;

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Root
        {
            get
            {
                var current = _parent;
                while (current is ChildActorPath)
                {
                    current = ((ChildActorPath)current)._parent;
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
