using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

#if CORECLR
namespace Akka.MultiNodeTestRunner.Shared.Extensions
{
    internal static class TypeExtension
    {
        public static MethodInfo GetMethod(this Type type, string method, params Type[] parameters)
        {
            return type.GetRuntimeMethod(method, parameters);
        }
    }
}
#endif
