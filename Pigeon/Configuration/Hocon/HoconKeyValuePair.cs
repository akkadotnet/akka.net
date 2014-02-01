using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Configuration.Hocon
{    
    public class HoconKeyValuePair
    {
        private readonly Dictionary<string, HoconKeyValuePair> _children = new Dictionary<string, HoconKeyValuePair>();

        public HoconKeyValuePair()
        {
            Key = "";
            Content = new HoconValue();
        }

        public string Key { get; set; }

        public HoconValue Content { get; private set; }

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

        public override string ToString()
        {
            return ToString(0);
        }

        public string ToString(int indent)
        {
            var t = new string(' ', indent * 2);
            var sb = new StringBuilder();

            if (Content != null)
            {
                if (Content.GetValue() is string)
                {
                    sb.AppendFormat("{0}{1} = \"{2}\"", t, Key, Content.GetValue());
                }
                else if (Content.GetValue() == null)
                {
                    sb.AppendFormat("{0}{1} = null", t, Key);
                }
                else
                {
                    sb.AppendFormat("{0}{1} = {2}", t, Key, Content.GetValue());
                }
                return sb.ToString();
            }
            sb.AppendLine(t + Key  + " {");

            foreach (HoconKeyValuePair child in Children)
            {
                sb.AppendLine(child.ToString(indent + 1));
            }
            sb.Append(t + "}");
            return sb.ToString();
        }

        public string ToFlatString()
        {
            var sb = new StringBuilder();

            if (Content != null)
            {
                sb.AppendFormat("{0} = {1}", Key, Content);
                return sb.ToString();
            }
            sb.Append(Key + " {");

            sb.Append(string.Join(",", Children.Select(c => c.ToFlatString())));
            sb.Append("}");
            return sb.ToString();
        }

        public void Clear()
        {
            this._children.Clear();
        }
    }
}
