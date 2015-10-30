using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Akka.Logger.Serilog.SemanticAdapter
{
    internal class TemplateTransform
    {
        private static readonly TextInfo TextInfo = new CultureInfo("en-US", false).TextInfo;

        private static readonly ConcurrentDictionary<string, string> Templates =
            new ConcurrentDictionary<string, string>();

        public static string Transform(string template)
        {
            //template is not of old format
            if (!template.Contains("{0"))
                return template;

            string transformed;

            if (!Templates.TryGetValue(template, out transformed))
            {
                try
                {
                    var unknownIndex = 0;

                    var parts = Parse(template);
                    var sb = new StringBuilder();
                    foreach (var part in parts)
                    {
                        sb.Append(part.Text);
                        if (part.Index >= 0)
                        {
                            sb.Append("{");
                            var cleaned = TextInfo.ToTitleCase(part.Text);
                            var x = (from c in cleaned
                                where char.IsLetterOrDigit(c)
                                select c).ToArray();
                            var formatName = new string(x);
                            if (string.IsNullOrEmpty(formatName))
                            {
                                formatName = "Obj" + unknownIndex++;
                            }
                            sb.Append(formatName);
                            sb.Append("}");
                        }
                    }
                    transformed = sb.ToString();
                    Templates.TryAdd(template, transformed);
                }
                catch
                {
                    //transform broke, fall back to original
                    transformed = template;
                    Templates.TryAdd(template, transformed);
                }
            }

            return transformed;
        }

        private static List<FormatPart> Parse(string template)
        {
            var sb = new StringBuilder();
            var parts = new List<FormatPart>();


            int lastGoodIndex = 0;
            Func<List<FormatPart>> appendEnd = () =>
            {
                parts.Add(new FormatPart(template.Substring(lastGoodIndex, template.Length - lastGoodIndex), -1));
                return parts;
            };
            for (var i = 0; i < template.Length; i++)
            {
                var c = template[i];
                if (c == '{')
                {
                    var prefix = template.Substring(lastGoodIndex, i - lastGoodIndex);
                    i++;
                    if (i >= template.Length)
                        return appendEnd();

                    int index = 0;
                    while (char.IsDigit(template[i]))
                    {
                        index = index * 10 + template[i] - '0';
                        i++;
                        //walk past index
                        if (i >= template.Length)
                            return appendEnd();
                    }

                    if (template[i] == ':')
                    {
                        //skip format
                        i++;
                        if (i >= template.Length)
                            return appendEnd();

                    }
                    while (template[i] != '}')
                    {
                        sb.Append(template[i]);
                        //walk to closing }
                        i++;
                        if (i >= template.Length)
                            return appendEnd();

                    }
                    parts.Add(new FormatPart(prefix, index));
                    sb.Clear();
                    lastGoodIndex = i + 1;
                }
            }
            //append any text present after the last placeholder
            return appendEnd();
        }
    }
}