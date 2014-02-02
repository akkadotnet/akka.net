using System;
using System.Collections.Generic;
using System.Linq;

namespace Pigeon.Configuration.Hocon
{
    public class Parser
    {
        public static HoconValue Parse(string text)
        {
            return new Parser().ParseText(text);
        }

        private HoconTokenizer reader;
        private HoconValue ParseText(string text)
        {
            var root = new HoconValue();
            reader = new HoconTokenizer(text);
            reader.PullWhitespaceAndComments();
            ParseObject( root,true);
            return root;
        }

        private void ParseObject( HoconValue owner,bool root)
        {
            if (owner.IsObject())
            {
                //the value of this KVP is already an object
            }
            else
            {      
                //the value of this KVP is not an object, thus, we should add a new
                owner.NewValue(new HoconObject()); 
            }

            var currentObject = owner.GetObject();

            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.Key:
                        var value = currentObject.GetOrCreateKey(t.Value.ToString());
                        ParseKeyContent( value);
                        if (!root)
                            return;
                        break;
                    
                    case TokenType.ObjectEnd:
                        reader.PullWhitespace();
                        if (reader.IsComma())
                        {
                            reader.PullComma();
                        }
                        return;
                }
            }
        }

        private void ParseKeyContent(HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNext();
                switch (t.Type)
                {
                    case TokenType.Dot:
                        ParseObject(value,false);
                        return; 
                    case TokenType.Assign:
                        ParseValue(reader, value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( value,true);
                        return;
                }
            }            
        }

        public void ParseValue(HoconTokenizer reader, HoconValue value)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        ParseSimpleValue(value, t.Value);
                        return;
                    case TokenType.ObjectStart:
                        ParseObject( value,true);
                        return;
                    case TokenType.ArrayStart:
                        ParseArray(value);
                        return;
                    case TokenType.Substitute:
                        ParseSubstitution(value, (string)t.Value);
                        return;
                }
            }
        }

        private static void ParseSubstitution(HoconValue owner, string value )
        {
            owner.NewValue(new HoconSubstitution(value));
        }

        public void ParseArray(HoconValue owner)
        {
            var arr = new HoconArray();
            owner.NewValue(arr);
            ParseArrayContents(arr);
            reader.PullSpaceOrTab();
            if (reader.IsArrayStart())
            {
                reader.Take();
                arr = new HoconArray();
                ParseArrayContents(arr);
                owner.AppendValue(arr);
            }
        }

        private void ParseArrayContents(HoconArray arr)
        {
            while (!reader.EoF)
            {
                Token t = reader.PullNextValue();
                switch (t.Type)
                {
                    case TokenType.EoF:
                        break;
                    case TokenType.LiteralValue:
                        {
                            var value = new HoconValue();
                            arr.Add(value);
                            ParseSimpleValue(value, t.Value);
                            break;
                        }
                    case TokenType.ObjectStart:
                        {
                            var value = new HoconValue();
                            arr.Add(value);
                            ParseObject(value, true);
                            break;
                        }
                    case TokenType.ArrayStart:
                        {
                            var value = new HoconValue();
                            ParseArray(value);
                            arr.Add(value);
                            break;
                        }
                    case TokenType.ArrayEnd:
                        {
                            reader.PullWhitespace();
                            IgnoreComma();

                            return;
                        }
                }
            }
            throw new Exception("End of file reached when parsing array");
        }

        private void ParseSimpleValue(HoconValue v, object value)
        {
            v.NewValue(value);
            while (reader.IsStartSimpleValue()) //fetch rest of values if string concat
            {
                var t = reader.PullSimpleValue();
                v.AppendValue(t.Value);
            }
            IgnoreComma();
        }

        private void IgnoreComma()
        {
            if (reader.IsComma()) //optional end of value
            {
                reader.PullComma();
            }
        }
    }
}