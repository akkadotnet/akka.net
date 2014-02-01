using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration.Hocon
{
    public class HoconObject
    {
        private readonly Dictionary<string, HoconKeyValuePair> _children = new Dictionary<string, HoconKeyValuePair>();
        public IEnumerable<HoconKeyValuePair> Children
        {
            get { return _children.Values; }
        }
        public HoconKeyValuePair GetOrCreateKey(string key)
        {
            if (_children.ContainsKey(key))
            {
                return _children[key];
            }
            var child = new HoconKeyValuePair
            {
                Key = key,
            };
            _children.Add(key, child);
            return child;
        }
    }
}
