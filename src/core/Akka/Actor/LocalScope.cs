using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// Used to deploy actors in local scope
    /// </summary>
    public class LocalScope : Scope , ISurrogated
    {
        public class LocalScopeSurrogate : ISurrogate
        {

            public ISurrogated FromSurrogate(ActorSystem system)
            {
                return Instance;
            }
        }

        private LocalScope() { }
        private static readonly LocalScope _instance = new LocalScope();

        public static LocalScope Instance
        {
            get { return _instance; }
        }

        public override Scope WithFallback(Scope other)
        {
            return Instance;
        }

        public override Scope Copy()
        {
            return Instance;
        }

        public ISurrogate ToSurrogate(ActorSystem system)
        {
            return new LocalScopeSurrogate();
        }
    }
}
