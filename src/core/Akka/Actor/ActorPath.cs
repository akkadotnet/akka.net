using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
    public abstract class ActorPath : IEquatable<ActorPath>, IComparable<ActorPath>
    {
        /// <summary> The regex that actor names must conform to RFC 2396, http://www.ietf.org/rfc/rfc2396.txt </summary>
        //Note that AKKA JVM does not allow parenthesis ( ) but, according to RFC 2396 those are allowed, and 
        //since we use URL Encode to create valid actor names, we must allow them
        public static readonly Regex ElementRegex =
            new Regex(@"^(?:[-a-zA-Z0-9:@&=+,.!~*'_;\(\)]|%[0-9a-fA-F]{2})(?:[-a-zA-Z0-9:@&=+,.!~*'\$_;\(\)]|%[0-9a-fA-F]{2})*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private readonly string _name;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath" /> class.
        /// </summary>
        /// <param name="address"> The address. </param>
        /// <param name="name"> The name. </param>
        protected ActorPath(Address address, string name)
        {
            _name = name;
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
            _name = name;
        }

        /// <summary>
        /// Gets the uid.
        /// </summary>
        /// <value> The uid. </value>
        public long Uid { get; private set; }

        /// <summary>
        /// Gets the elements.
        /// </summary>
        /// <value> The elements. </value>
        public IReadOnlyList<string> Elements
        {
            get
            {
                var current = this;
                var elements = new List<string>();
                while (!(current is RootActorPath))
                {
                    elements.Add(current.Name);
                    current = current.Parent;
                }
                elements.Reverse();
                return elements.AsReadOnly();
            }
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value> The name. </value>
        public string Name
        {
            get { return _name; }
        }

        /// <summary>
        /// The Address under which this path can be reached; walks up the tree to
        /// the RootActorPath.
        /// </summary>
        /// <value> The address. </value>
        public Address Address { get; private set; }

        public abstract ActorPath Root { get; }
        public abstract ActorPath Parent { get; }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other"> An object to compare with this object. </param>
        /// <returns> true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false. </returns>
        public bool Equals(ActorPath other)
        {
            return Address.Equals(other.Address) && Elements.SequenceEqual(other.Elements);
        }

        public abstract int CompareTo(ActorPath other);

        /// <summary>
        /// Withes the uid.
        /// </summary>
        /// <param name="uid"> The uid. </param>
        /// <returns> ActorPath. </returns>
        public abstract ActorPath WithUid(long uid);

        /// <summary>
        /// Create a new child actor path.
        /// </summary>
        /// <param name="path"> The path. </param>
        /// <param name="name"> The name. </param>
        /// <returns> The result of the operator. </returns>
        public static ActorPath operator /(ActorPath path, string name)
        {
            return new ChildActorPath(path, name, 0);
        }

        /// <summary>
        /// Recursively create a descendant’s path by appending all child names.
        /// </summary>
        /// <param name="path"> The path. </param>
        /// <param name="name"> The name. </param>
        /// <returns> The result of the operator. </returns>
        public static ActorPath operator /(ActorPath path, IEnumerable<string> name)
        {
            var a = path;
            foreach (string element in name)
            {
                a = a / element;
            }
            return a;
        }


        /// <summary>
        /// Tries to parse the uri, which should be a full uri, i.e containing protocol.
        /// For example "akka://System/user/my-actor"
        /// </summary>
        public static bool TryParse(string path, out ActorPath actorPath)
        {
            actorPath = null;


            Address address;
            Uri uri;
            if (!TryParseAddress(path, out address, out uri)) return false;
            var pathElements = uri.AbsolutePath.Split('/');
            actorPath = new RootActorPath(address) / pathElements.Skip(1);
            return true;
        }

        public static bool TryParseAddress(string path, out Address address)
        {
            Uri uri;
            return TryParseAddress(path, out address, out uri);
        }

        private static bool TryParseAddress(string path, out Address address, out Uri uri)
        {
            //This code corresponds to AddressFromURIString.unapply
            uri = null;
            address = null;
            try
            {
                uri = new Uri(path);
            }
            catch (UriFormatException)
            {
                return false;
            }
            var protocol = uri.Scheme; //Typically "akka"
            if (!protocol.StartsWith("akka", StringComparison.OrdinalIgnoreCase))
            {
                // Protocol must start with 'akka.*
                return false;
            }


            string systemName;
            string host = null;
            int? port = null;
            if (string.IsNullOrEmpty(uri.UserInfo))
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
            var joined = string.Join("/", Elements);
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

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns> A <see cref="System.String" /> that represents this instance. </returns>
        public override string ToString()
        {
            return ToStringWithAddress();
        }

        /// <summary>
        /// Returns a string representation of this instance including uid.
        /// </summary>
        /// <returns></returns>
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

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns> A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. </returns>
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj"> The object to compare with the current object. </param>
        /// <returns>
        /// <c> true </c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise,
        /// <c> false </c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            return Equals((ActorPath)obj);
        }

        public static bool operator ==(ActorPath left, ActorPath right)
        {
            return Equals(left, right);
        }

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

        public string ToSerializationFormat()
        {
            return ToStringWithAddress();
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
                return string.Format("{0}{1}", Address, Join());

            return string.Format("{0}{1}", address, Join());
        }

        public static string FormatPathElements(IEnumerable<string> pathElements)
        {
            return String.Join("/", pathElements);
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

        public override ActorPath Parent
        {
            get { return null; }
        }

        public override ActorPath Root
        {
            get { return this; }
        }

        /// <summary>
        /// Withes the uid.
        /// </summary>
        /// <param name="uid"> The uid. </param>
        /// <returns> ActorPath. </returns>
        /// <exception cref="System.NotSupportedException"> RootActorPath must have undefinedUid </exception>
        public override ActorPath WithUid(long uid)
        {
            if (uid == 0)
                return this;
            throw new NotSupportedException("RootActorPath must have undefinedUid");
        }

        public override int CompareTo(ActorPath other)
        {
            if (other is ChildActorPath) return 1;
            return System.String.Compare(ToString(), other.ToString(), StringComparison.Ordinal);
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

        public override ActorPath Parent
        {
            get { return _parent; }
        }

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
            var nameCompareResult = String.Compare(left.Name, right.Name, StringComparison.Ordinal);
            if (nameCompareResult != 0)
                return nameCompareResult;
            return InternalCompareTo(left.Parent, right.Parent);
        }
    }
}
