using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Marker Interface NoSerializationVerificationNeeded, this interface prevents 
    /// implementing message types from being serialized if configuration setting 'akka.actor.serialize-messages' is "on"
    /// </summary>
    public interface NoSerializationVerificationNeeded
    {
    }
}
