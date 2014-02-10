using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public class ActorInitializationException : Exception
    {
    }

    public class ActorKilledException : Exception
    {
        public ActorKilledException(string message) : base(message)
        {
        }
    }

    public class DeathPactException : Exception
    {
    }
}
