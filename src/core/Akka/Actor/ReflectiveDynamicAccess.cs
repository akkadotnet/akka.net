using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class ReflectiveDynamicAccess : IDynamicAccess
    {
        readonly Assembly _assembly;

        public T CreateInstanceFor<T>(Type type, params object[] args)
        {
            var types = args.Select(x => x.GetType()).ToArray();
            var constructor = type.GetConstructor(types);
            var obj = constructor.Invoke(args);
            var t = typeof(T);
            if(t.IsInstanceOfType(obj))
            {
                return (T)obj;
            }
            else
            {
                throw new ArgumentException(String.Format("{0} is not a subtype of {1}", type.Name, t.Name));
            }
        }

        public Type GetClassFor<T>(string fqtn)
        {
            var c = Assembly.GetType(fqtn);
            var t = typeof(T);
            if(t.IsAssignableFrom(c))
            {
                return c;
            }
            else
            {
                throw new ArgumentException(String.Format("{0} is not assignable from {1}", t.Name, c.Name));
            }
        }

        public T CreateInstanceFor<T>(string fqcn, params object[] args)
        {
            var clazz = GetClassFor<T>(fqcn);
            return CreateInstanceFor<T>(clazz, args);
        }

        public Assembly Assembly
        {
            get { return _assembly; }
        }

        public ReflectiveDynamicAccess(Assembly assembly)
        {
            _assembly = assembly;
        }
    }
}
