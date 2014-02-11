using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Configuration.Hocon
{
    public class HoconObject : IHoconElement
    {
        private readonly Dictionary<string, HoconValue> _children = new Dictionary<string, HoconValue>();
        public IEnumerable<HoconValue> Children
        {
            get { return _children.Values; }
        }

        public HoconValue GetKey(string key)
        {
            if (_children.ContainsKey(key))
            {
                return _children[key];
            }
            return null;
        }
        public HoconValue GetOrCreateKey(string key)
        {
            if (_children.ContainsKey(key))
            {
                return _children[key];
            }
            var child = new HoconValue();            
            _children.Add(key, child);
            return child;
        }

        public bool IsString()
        {
            return false;
        }

        public string GetString()
        {
            throw new NotImplementedException();
        }

        public bool IsArray()
        {
            return false;
        }

        public IList<HoconValue> GetArray()
        {
            throw new NotImplementedException();
        }
    }
}
