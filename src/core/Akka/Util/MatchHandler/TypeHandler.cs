using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Tools.MatchHandler
{
    public class TypeHandler
    {
        private readonly Type _handlesType;
        private readonly List<PredicateAndHandler> _handlers = new List<PredicateAndHandler>();

        public TypeHandler(Type handlesType)
        {
            if(handlesType == null) throw new ArgumentNullException("handlesType");
            _handlesType = handlesType;
        }

        public Type HandlesType { get { return _handlesType; } }
        public List<PredicateAndHandler> Handlers { get { return _handlers; } }

        public IEnumerable<Argument> GetArguments()
        {
            return _handlers.SelectMany(h => h.Arguments);
        } 
    }
}