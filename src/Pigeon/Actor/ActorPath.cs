using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract class ActorPath : IEnumerable<string> , IEquatable<ActorPath>
    {
        public static ActorPath Parse(string path,ActorSystem system)
        {
            var elements = path.Split('/');
            if (elements.First().StartsWith("akka"))
            {
                //TODO: better parser
                var first = elements.First();
                var protocol = first.Split(':')[0];
                var systemName = elements[2].Split('@')[0];
                var hostPort = elements[2].Split('@')[1];
                var host = hostPort.Split(':')[0];
                var port = int.Parse(hostPort.Split(':')[1]);
                var rest = elements.Skip(3);

                var pathElements = elements.Skip(3).ToList();
                pathElements.Insert(0, "");
                return new RootActorPath(new Address(protocol, systemName, host, port), pathElements);
            }
            else
            {
                return new RootActorPath(new Address("akka", system.Name), elements);
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

        public ActorPath(IEnumerable<string> parts)
        {
            this.elements = parts.ToList();
        }
        public ActorPath(Address address, IEnumerable<string> parts)
        {
            this.Address = address;
            this.elements = parts.ToList();
        }
        public ActorPath(string path)
        {
            elements = path.Split('/').ToList();
        }

        public ActorPath(string parentPath,string name)
        {
            elements = parentPath.Split('/').ToList();
            elements.Add(name);
        }

        public ActorPath(Address address, string name)
        {
            this.elements.Add(name);
            this.Address = address;
        }

        public ActorPath(ActorPath parentPath, string name)
        {
            this.Address = parentPath.Address;
            elements.AddRange(parentPath.elements);
            elements.Add(name);
        }


        public string ToStringWithoutAddress()
        {
            return string.Join("/", elements);
        }

        public override string ToString()
        {
            return ToStringWithoutAddress();
        }

        public ActorPath Child(string childName)
        {
            return new ChildActorPath(this, childName);
        }

        public IEnumerator<string> GetEnumerator()
        {
            return elements.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
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
            return string.Format("{0}{1}", address, string.Join("/", elements));
        }       
    }

    public class RootActorPath : ActorPath
    {
        public RootActorPath(Address address,string name ="/") : base(address,name)
        {

        }

        public RootActorPath(Address address, IEnumerable<string> elements)
            : base(address,elements)
        {

        }
    }

    public class ChildActorPath : ActorPath
    {
        public ChildActorPath(ActorPath parentPath, string name)
            : base(parentPath, name)
        {
        }
    }
}
