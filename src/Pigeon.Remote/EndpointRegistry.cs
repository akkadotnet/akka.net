using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote
{
    public class EndpointPolicy
    {
    }

    public class Pass : EndpointPolicy
    {
        public ActorRef Endpoint { get; private set; }
        public Pass(ActorRef endpoint)
          
        {
            this.Endpoint = endpoint;
        }
    }

    public class Gated : EndpointPolicy
    {
        public Gated(Deadline deadline)
        {
            this.TimeOfRelease = deadline;
        }

        public Deadline TimeOfRelease { get;private set; }
    }

    public class Deadline
    {
        public Deadline(DateTime when)
        {
            this.When = when;
        }

        public bool IsOverdue
        {
            get
            {
                return DateTime.Now > When;
            }
        }

        public DateTime When { get;private set; }
    }

    public class Quarantined : EndpointPolicy
    {
        public Quarantined(long uid, Deadline deadline)
        {
            this.Uid = uid;
            this.Deadline = deadline;
        }

        public long Uid { get;private set; }

        public Deadline Deadline { get; private set; }
    }

    public class EndpointRegistry
    {
        private Dictionary<Address,EndpointPolicy> addressToWritable = new Dictionary<Address,EndpointPolicy>();
        private Dictionary<ActorRef,Address> writableToAddress = new Dictionary<ActorRef,Address>();
        private Dictionary<Address,ActorRef> addressToReadonly = new Dictionary<Address,ActorRef>();
        private Dictionary<ActorRef, Address> readonlyToAddress = new Dictionary<ActorRef, Address>();

        /*
def registerWritableEndpoint(address: Address, endpoint: ActorRef): ActorRef = addressToWritable.get(address) match {
      case Some(Pass(e)) ⇒
        throw new IllegalArgumentException(s"Attempting to overwrite existing endpoint [$e] with [$endpoint]")
      case _ ⇒
        addressToWritable += address -> Pass(endpoint)
        writableToAddress += endpoint -> address
        endpoint
    }
*/
        public ActorRef RegisterWritableEndpoint(Address address, ActorRef endpoint)
        {
            EndpointPolicy existing;
            if (addressToWritable.TryGetValue(address, out existing))
            {
                throw new ArgumentException("Attempting to overwrite existing endpoint " + existing + " with " + endpoint);
            }
            else
            {
                addressToWritable.Add(address, new Pass(endpoint));
                writableToAddress.Add(endpoint, address);
                return endpoint;
            }             
        }

        /*
    def registerReadOnlyEndpoint(address: Address, endpoint: ActorRef): ActorRef = {
      addressToReadonly += address -> endpoint
      readonlyToAddress += endpoint -> address
      endpoint
    }
*/
        public ActorRef RegisterReadOnlyEndpoint(Address address, ActorRef endpoint)
        {
            addressToReadonly.Add(address, endpoint);
            readonlyToAddress.Add(endpoint, address);
            return endpoint;
        }

        /*
 def readOnlyEndpointFor(address: Address): Option[ActorRef] = addressToReadonly.get(address)

    def isWritable(endpoint: ActorRef): Boolean = writableToAddress contains endpoint

    def isReadOnly(endpoint: ActorRef): Boolean = readonlyToAddress contains endpoint

      def writableEndpointWithPolicyFor(address: Address): Option[EndpointPolicy] = addressToWritable.get(address)
*/
        public ActorRef ReadOnlyEndpointFor(Address address)
        {
            ActorRef tmp;
            if (addressToReadonly.TryGetValue(address,out tmp))
            {
                return tmp;
            }
            return null;
        }
        public bool IsWritable(ActorRef endpoint)
        {
            return writableToAddress.ContainsKey(endpoint);
        }

        public bool IsReadable(ActorRef endpoint)
        {
            return readonlyToAddress.ContainsKey(endpoint);
        }

        public EndpointPolicy WritableEndpointWithPolicyFor(Address address)
        {
            EndpointPolicy tmp;
            if (addressToWritable.TryGetValue(address, out tmp))
            {
                return tmp;
            }
            return null;
        }

        //TODO: port the rest of this class
    }
}
