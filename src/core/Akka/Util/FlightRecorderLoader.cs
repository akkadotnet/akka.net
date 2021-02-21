using System;
using System.Collections.Generic;
using System.Text;
using Akka.Actor;

namespace Akka.Util
{
    internal static class FlightRecorderLoader
    {
        public static T Load<T>(IClassicActorSystemProvider casp, string fqcn, T fallback) where T : class
        {
            throw new NotImplementedException();
        }
    }
}
