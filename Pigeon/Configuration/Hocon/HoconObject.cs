using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Pigeon.Configuration.Hocon
{

    
    public class HoconObject
    {
        private readonly Dictionary<string, HoconObject> _children = new Dictionary<string, HoconObject>();

        public HoconObject()
        {
            Id = "";
            Content = new HoconValue();
        }

        public string Id { get; set; }

        public HoconValue Content { get; private set; }

        public IEnumerable<HoconObject> Children
        {
            get { return _children.Values; }
        }

        public HoconObject CreateChild(string id)
        {
            if (_children.ContainsKey(id))
            {
                return _children[id];
            }
            var child = new HoconObject
            {
                Id = id,
            };
            _children.Add(id, child);
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
                if (Content is string)
                {
                    sb.AppendFormat("{0}{1} = \"{2}\"", t, Id, Content);
                }
                else if (Content == null)
                {
                    sb.AppendFormat("{0}{1} = null", t, Id);
                }
                else
                {
                    sb.AppendFormat("{0}{1} = {2}", t, Id, Content);
                }
                return sb.ToString();
            }
            sb.AppendLine(t + Id  + " {");

            foreach (HoconObject child in Children)
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
                sb.AppendFormat("{0} = {1}", Id, Content);
                return sb.ToString();
            }
            sb.Append(Id + " {");

            sb.Append(string.Join(",", Children.Select(c => c.ToFlatString())));
            sb.Append("}");
            return sb.ToString();
        }
    }
}
