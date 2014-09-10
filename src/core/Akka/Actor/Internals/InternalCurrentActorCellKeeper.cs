using System;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor.Internals;

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL!
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class InternalCurrentActorCellKeeper
    {
        [ThreadStatic]
        private static ActorCell _current;


        /// <summary>INTERNAL!
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static ActorCell Current { get { return _current; } set { _current = value; } }
    }
}