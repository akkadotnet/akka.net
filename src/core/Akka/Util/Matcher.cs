using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// A delegate that matches an incoming message with a pattern and optionally
    /// handles the message. Returns whether or not any pattern matched the
    /// message type.
    /// </summary>
    /// <param name="message">The message to match to the pattern.</param>
    /// <returns>Whether or not the message was handled.</returns>
    public delegate bool Matcher(object message);
}
