using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DistributedData
{
    public static class MultiMapBinding
    {
        public static void AddBinding<T,U>(this IDictionary<T, ISet<U>> dict, T key, U value)
        {
            if(!dict.ContainsKey(key))
            {
                dict.Add(key, (ISet<U>)new HashSet<U>());
            }
            dict[key].Add(value);
        }

        public static void RemoveBinding<T,U>(this IDictionary<T, ISet<U>> dict, T key, U value)
        {
            if(dict.ContainsKey(key))
            {
                dict[key].Remove(value);
            }
        }
    }
}
