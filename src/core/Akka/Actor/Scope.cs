using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class Scope
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
    }
}
