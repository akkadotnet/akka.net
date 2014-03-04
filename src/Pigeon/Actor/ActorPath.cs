using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Actor path is a unique path to an actor that shows the creation path
    /// up through the actor tree to the root actor.
    ///
    /// ActorPath defines a natural ordering (so that ActorRefs can be put into
    /// collections with this requirement); this ordering is intended to be as fast
    /// as possible, which owing to the bottom-up recursive nature of ActorPath
    /// is sorted by path elements FROM RIGHT TO LEFT, where RootActorPath >
    /// ChildActorPath in case the number of elements is different.
    ///
    /// Two actor paths are compared equal when they have the same name and parent
    /// elements, including the root address information. That does not necessarily
    /// mean that they point to the same incarnation of the actor if the actor is
    /// re-created with the same path. In other words, in contrast to how actor
    /// references are compared the unique id of the actor is not taken into account
    /// when comparing actor paths.
    /// </summary>
    public abstract class ActorPath : IEquatable<ActorPath>
    {
        /// <summary>
        /// Gets the uid.
        /// </summary>
        /// <value>The uid.</value>
        public long Uid { get;private set; }

        /// <summary>
        /// Withes the uid.
        /// </summary>
        /// <param name="uid">The uid.</param>
        /// <returns>ActorPath.</returns>
        public abstract ActorPath WithUid(long uid);

        /// <summary>
        /// The element regex
        /// </summary>
        public static readonly Regex ElementRegex = new Regex(@"(?:[-\w:@&=+,.!~*'_;]|%\\p{N}{2})(?:[-\w:@&=+,.!~*'$_;]|%\\p{N}{2})*",RegexOptions.Compiled);

        /// <summary>
        /// Gets the elements.
        /// </summary>
        /// <value>The elements.</value>
        public IEnumerable<string> Elements
        {
            get
            {
                return this.elements;
            }
        }
        /// <summary>
        /// Create a new child actor path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="name">The name.</param>
        /// <returns>The result of the operator.</returns>
        public static ActorPath operator /(ActorPath path, string name)
        {
            return new ChildActorPath(path, name,0);
        }

        /// <summary>
        /// Recursively create a descendant’s path by appending all child names.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <param name="name">The name.</param>
        /// <returns>The result of the operator.</returns>
        public static ActorPath operator /(ActorPath path, IEnumerable<string> name)
        {
            var a = path;
            foreach (var element in name)
            {
                a = a / element;
            }
            return a;
        }

        /// <summary>
        /// Parses the specified path.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>ActorPath.</returns>
        /// <exception cref="System.UriFormatException">Protocol must be 'akka.*'</exception>
        public static ActorPath Parse(string path)
        {
            var uri = new Uri(path);
            var protocol = uri.Scheme;
            if (!protocol.ToLowerInvariant().StartsWith("akka"))
                throw new UriFormatException("Protocol must be 'akka.*'");

            if (string.IsNullOrEmpty(uri.UserInfo))
            {                
                var systemName = uri.Host;
                var pathElements = uri.AbsolutePath.Split('/');
                return new RootActorPath(new Address(protocol, systemName, null, null)) / pathElements.Skip(1);
            }
            else
            {
                var systemName = uri.UserInfo;
                var host = uri.Host;
                var port = uri.Port;
                var pathElements = uri.AbsolutePath.Split('/');
                return new RootActorPath(new Address(protocol, systemName, host, port)) / pathElements.Skip(1);
            }
        }

        /// <summary>
        /// Gets the name.
        /// </summary>
        /// <value>The name.</value>
        public string Name
        {
            get
            {
                return this.elements.LastOrDefault();
            }
        }

        /// <summary>
        /// The Address under which this path can be reached; walks up the tree to
        /// the RootActorPath.
        /// </summary>
        /// <value>The address.</value>
        public Address Address { get;private set; }

        /// <summary>
        /// The elements
        /// </summary>
        private List<string> elements = new List<string>();

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath"/> class.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <param name="name">The name.</param>
        public ActorPath(Address address, string name)
        {
            if (name != "")
                this.elements.Add(name);

            this.Address = address;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorPath"/> class.
        /// </summary>
        /// <param name="parentPath">The parent path.</param>
        /// <param name="name">The name.</param>
        /// <param name="uid">The uid.</param>
        public ActorPath(ActorPath parentPath, string name,long uid)
        {
            this.Address = parentPath.Address;
            this.Uid = uid;
            elements.AddRange(parentPath.elements);
            elements.Add(name);
        }

        /// <summary>
        /// Joins this instance.
        /// </summary>
        /// <returns>System.String.</returns>
        private string Join()
        {
            var joined = string.Join("/", elements);
            return "/" + joined;
        }

        /// <summary>
        /// String representation of the path elements, excluding the address
        /// information. The elements are separated with "/" and starts with "/",
        /// e.g. "/user/a/b".
        /// </summary>
        /// <returns>System.String.</returns>
        public string ToStringWithoutAddress()
        {
            return Join();
        }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return ToStringWithAddress();
        }

        /// <summary>
        /// Childs the specified child name.
        /// </summary>
        /// <param name="childName">Name of the child.</param>
        /// <returns>ActorPath.</returns>
        public ActorPath Child(string childName)
        {
            return this / childName;
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.</returns>
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" /> is equal to this instance.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns><c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.</returns>
        public override bool Equals(object obj)
        {
            return this.Equals((ActorPath)obj);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.</returns>
        public bool Equals(ActorPath other)
        {
            return this.elements.SequenceEqual(other.elements);
        }

        /// <summary>
        /// Generate String representation, with the address in the RootActorPath.
        /// </summary>
        /// <returns>System.String.</returns>
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
        /// <param name="address">The address.</param>
        /// <returns>System.String.</returns>
        public string ToStringWithAddress(Address address)
        {
            if (this.Address.Host != null && this.Address.Port.HasValue)
                return string.Format("{0}{1}", this.Address, Join());

            return string.Format("{0}{1}", address, Join());
        }       
    }

    /// <summary>
    /// Class RootActorPath.
    /// </summary>
    public class RootActorPath : ActorPath
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RootActorPath"/> class.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <param name="name">The name.</param>
        public RootActorPath(Address address,string name ="") : base(address,name)
        {

        }

        /// <summary>
        /// Withes the uid.
        /// </summary>
        /// <param name="uid">The uid.</param>
        /// <returns>ActorPath.</returns>
        /// <exception cref="System.NotSupportedException">RootActorPath must have undefinedUid</exception>
        public override ActorPath WithUid(long uid)
        {
            if (uid == 0)
                return this;
            else
                throw new NotSupportedException("RootActorPath must have undefinedUid");
        }
    }

    /// <summary>
    /// Class ChildActorPath.
    /// </summary>
    public class ChildActorPath : ActorPath
    {
        /// <summary>
        /// The parent
        /// </summary>
        private ActorPath parent;
        /// <summary>
        /// The name
        /// </summary>
        private string name;
        /// <summary>
        /// Initializes a new instance of the <see cref="ChildActorPath"/> class.
        /// </summary>
        /// <param name="parentPath">The parent path.</param>
        /// <param name="name">The name.</param>
        /// <param name="uid">The uid.</param>
        public ChildActorPath(ActorPath parentPath, string name,long uid)
            : base(parentPath, name,0)
        {
            this.name = name;
            this.parent = parentPath;
        }

        /// <summary>
        /// Creates a copy of the given ActorPath and applies a new Uid
        /// </summary>
        /// <param name="uid">The uid.</param>
        /// <returns>ActorPath.</returns>
        public override ActorPath WithUid(long uid)
        {
            if (uid == this.Uid)
                return this;
            else
            {
                return new ChildActorPath(parent, name,uid);
            }
        }
    }
}
