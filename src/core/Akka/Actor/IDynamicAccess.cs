using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public interface IDynamicAccess
    {
        T CreateInstanceFor<T>(Type type, params Object[] args);
        Type GetClassFor<T>(string fqtn);
        T CreateInstanceFor<T>(string fqcn, params Object[] args);
        Assembly Assembly { get; }
    }
}
