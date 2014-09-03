using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{
    /// <summary>
    ///     Class StandardOutLogger.
    /// </summary>
    public class StandardOutLogger : MinimalActorRef
    {
        private ActorPath _path = new RootActorPath(Address.AllSystems, "/StandardOutLogger");

        /// <summary>
        ///     Initializes a new instance of the <see cref="StandardOutLogger" /> class.
        /// </summary>
        public StandardOutLogger()
        {
        }

        /// <summary>
        ///     Gets the provider.
        /// </summary>
        /// <value>The provider.</value>
        /// <exception cref="System.Exception">StandardOutLogged does not provide</exception>
        public override ActorRefProvider Provider
        {
            get { throw new Exception("StandardOutLogger does not provide"); }
        }

        public override ActorPath Path
        {
            get { return _path; }
        }

        /// <summary>
        ///     Tells the internal.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <exception cref="System.ArgumentNullException">message</exception>
        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message == null)
                throw new ArgumentNullException("message");
            Console.WriteLine(message);
        }
    }
}
