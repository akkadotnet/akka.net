using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconObject Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconObject ParseText(string text)
        {
            var root = new HoconObject();
            var reader = new HoconTokenizer(text);
            ParseObject(reader, root,true);
            return root;
        }

        private void ParseObject(HoconTokenizer reader, HoconObject context,bool root)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        var self = context.CreateChild(t.Value.ToString());
                        ParseKeyContent(reader, self);
                        if (!root)
                            return;
                        break;
                    
                    case TokenType.ObjectEnd:
                        return;
                }
            }
        }

        private void ParseKeyContent(HoconTokenizer reader, HoconObject self)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(reader, self,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(reader, self);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject(reader, self,true);
                        return;
                }
            }
            
        }

        public void ParseValue(HoconTokenizer reader, HoconObject context)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        context.Content.NewValue(t.Value);
                        while (reader.IsStartSimpleValue()) //fetch rest of values if string concat
                        {
                            t = reader.PullSimpleValue();
                            context.Content.AppendValue(t.Value);
                        }
                        if (reader.IsComma()) //optional end of value
                        {
                            reader.PullComma();
                        }

                        return;
                    case TokenType.ObjectStart:
                        ParseObject(reader, context,true);
                        return;
                    case TokenType.ArrayStart:
                        var arr = ParseArray(reader, context);
                        context.Content.NewValue(arr);
                        return;
                }
            }
        }

        public HoconArray ParseArray(HoconTokenizer reader, HoconObject context)
        {
            var arr = new HoconArray();
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        arr.Add(t.Value);
                        break;
                    case TokenType.ObjectStart:
                        var c = new HoconObject();
                        ParseObject(reader, c,true);
                        arr.Add(c);
                        break;
                    case TokenType.ArrayStart:
                        var c2 = new HoconObject();
                        var childArr = ParseArray(reader, c2);
                        arr.Add(childArr);
                        break;
                    case TokenType.ArrayEnd:
                        if (!arr.Any() || !arr.All(e => e is HoconArray)) 
                            return arr;

                        //concat arrays in arrays
                        var x = (from a in arr.OfType<HoconArray>()
                            from e in a 
                            select e).ToArray();
                        arr.Clear();
                        arr.AddRange(x);
                        return arr;
                }
            }
            return arr;
        }
    }
}