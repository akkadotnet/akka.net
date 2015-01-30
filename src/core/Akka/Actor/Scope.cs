using System;

namespace Akka.Actor
{
    public class Scope : IEquatable<Scope>
    {
        public static readonly LocalScope Local = new LocalScope();
        private Scope _fallback;

        public Scope WithFallback(Scope other)
        {
            Scope copy = Copy();
            copy._fallback = other;
            return copy;
        }

        private Scope Copy()
        {
            return new Scope
            {
                _fallback = _fallback,
            };
        }

        public virtual bool Equals(Scope other)
        {
            if (other == null) return false;

            //we don't do equality checks on fallbacks
            return GetType() == other.GetType();
        }
    }
}
