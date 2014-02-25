using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public abstract class ActorPath : IEquatable<ActorPath>
    {
        public long Uid { get;private set; }

        public abstract ActorPath WithUid(long uid);

        public static readonly Regex ElementRegex = new Regex(@"(?:[-\w:@&=+,.!~*'_;]|%\\p{N}{2})(?:[-\w:@&=+,.!~*'$_;]|%\\p{N}{2})*",RegexOptions.Compiled);

        public IEnumerable<string> Elements
        {
            get
            {
                return this.elements;
            }
        }
        public static ActorPath operator /(ActorPath path, string name)
        {
            return new ChildActorPath(path, name,0);
        }

        public static ActorPath operator /(ActorPath path, IEnumerable<string> name)
        {
            var a = path;
            foreach (var element in name)
            {
                a = a / element;
            }
            return a;
        }       

        public string Head
        {
            get
            {
                return elements[1];
            }
        }       

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


        public string First
        {
            get
            {
                return this.elements.FirstOrDefault();
            }
        }

        public string Name
        {
            get
            {
                return this.elements.LastOrDefault();
            }
        }

        public Address Address { get;private set; }

        private List<string> elements = new List<string>();

        public ActorPath(Address address, string name)
        {
            this.elements.Add(name);
            this.Address = address;
        }

        public ActorPath(ActorPath parentPath, string name,long uid)
        {
            this.Address = parentPath.Address;
            this.Uid = uid;
            elements.AddRange(parentPath.elements);
            elements.Add(name);
        }

        private string Join()
        {
            var joined = string.Join("/", elements);
            if (elements.Count == 1)
                return joined + "/";
            return joined;
        }

        public string ToStringWithoutAddress()
        {
            return Join();
        }

        public override string ToString()
        {
            return ToStringWithAddress();
        }

        public ActorPath Child(string childName)
        {
            return this / childName;
        }

        public IEnumerator<string> GetEnumerator()
        {
            return elements.GetEnumerator();
        }


        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return this.Equals((ActorPath)obj);
        }

        public bool Equals(ActorPath other)
        {
            return this.elements.SequenceEqual(other.elements);
        }

        public string ToStringWithAddress()
        {
            return ToStringWithAddress(Address);
        }

        public string ToStringWithAddress(Address address)
        {
            if (this.Address.Host != null && this.Address.Port.HasValue)
                return string.Format("{0}{1}", this.Address, Join());

            return string.Format("{0}{1}", address, Join());
        }       
    }

    public class RootActorPath : ActorPath
    {
        public RootActorPath(Address address,string name ="") : base(address,name)
        {

        }

        public override ActorPath WithUid(long uid)
        {
            if (uid == 0)
                return this;
            else
                throw new NotSupportedException("RootActorPath must have undefinedUid");
        }
    }

    public class ChildActorPath : ActorPath
    {
        private ActorPath parent;
        private string name;
        public ChildActorPath(ActorPath parentPath, string name,long uid)
            : base(parentPath, name,0)
        {
            this.name = name;
            this.parent = parentPath;
        }

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
