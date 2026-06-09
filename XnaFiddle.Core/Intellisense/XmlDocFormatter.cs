using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace XnaFiddle.Intellisense
{
    /// <summary>
    /// Parses a Roslyn-produced XML doc comment string into lightweight markdown
    /// suitable for Monaco's markdown-rendered hover. Handles summary, params,
    /// typeparams, returns, remarks, exceptions, and common inline tags
    /// (see/seealso/paramref/typeparamref/c/code). Unknown tags fall through to
    /// their inner text. Returns empty string on any parse failure.
    /// </summary>
    public static class XmlDocFormatter
    {
        public static string Format(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return "";
            try
            {
                XDocument doc;
                try
                {
                    doc = XDocument.Parse("<root>" + xml + "</root>");
                }
                catch
                {
                    return "";
                }

                var rootContainer = (XContainer)doc.Root;
                // Roslyn's GetDocumentationCommentXml commonly yields <member name="...">...</member>.
                // Descend into that if present; otherwise treat the wrapper <root> as the container.
                var memberEl = doc.Root.Element("member");
                if (memberEl != null && memberEl.Attribute("name") != null)
                {
                    rootContainer = memberEl;
                }

                var sb = new StringBuilder();

                var summary = rootContainer.Element("summary");
                if (summary != null)
                {
                    var text = RenderInline(summary).Trim();
                    if (text.Length > 0) sb.Append(text).Append("\n\n");
                }

                var typeParams = rootContainer.Elements("typeparam").ToList();
                if (typeParams.Count > 0)
                {
                    sb.Append("**Type parameters:**\n\n");
                    foreach (var tp in typeParams)
                    {
                        var name = tp.Attribute("name")?.Value ?? "";
                        sb.Append("- `").Append(name).Append("`: ")
                            .Append(RenderInline(tp).Trim()).Append('\n');
                    }
                    sb.Append('\n');
                }

                var parameters = rootContainer.Elements("param").ToList();
                if (parameters.Count > 0)
                {
                    sb.Append("**Parameters:**\n\n");
                    foreach (var p in parameters)
                    {
                        var name = p.Attribute("name")?.Value ?? "";
                        sb.Append("- `").Append(name).Append("`: ")
                            .Append(RenderInline(p).Trim()).Append('\n');
                    }
                    sb.Append('\n');
                }

                var returns = rootContainer.Element("returns");
                if (returns != null)
                {
                    var text = RenderInline(returns).Trim();
                    if (text.Length > 0)
                    {
                        sb.Append("**Returns:** ").Append(text).Append("\n\n");
                    }
                }

                var remarks = rootContainer.Element("remarks");
                if (remarks != null)
                {
                    var text = RenderInline(remarks).Trim();
                    if (text.Length > 0)
                    {
                        sb.Append("**Remarks:** ").Append(text).Append("\n\n");
                    }
                }

                var exceptions = rootContainer.Elements("exception").ToList();
                if (exceptions.Count > 0)
                {
                    sb.Append("**Exceptions:**\n\n");
                    foreach (var ex in exceptions)
                    {
                        var cref = StripCrefPrefix(ex.Attribute("cref")?.Value ?? "");
                        sb.Append("- `").Append(cref).Append("`: ")
                            .Append(RenderInline(ex).Trim()).Append('\n');
                    }
                    sb.Append('\n');
                }

                return sb.ToString().TrimEnd();
            }
            catch
            {
                return "";
            }
        }

        private static string RenderInline(XElement element)
        {
            var sb = new StringBuilder();
            foreach (var node in element.Nodes())
            {
                if (node is XText t)
                {
                    // Collapse internal whitespace runs to single spaces.
                    var raw = t.Value;
                    var collapsed = Regex.Replace(raw, @"\s+", " ");
                    sb.Append(collapsed);
                }
                else if (node is XElement child)
                {
                    var name = child.Name.LocalName;
                    switch (name)
                    {
                        case "see":
                        case "seealso":
                        {
                            var cref = StripCrefPrefix(child.Attribute("cref")?.Value ?? "");
                            if (string.IsNullOrEmpty(cref))
                            {
                                cref = child.Attribute("langword")?.Value
                                    ?? child.Attribute("href")?.Value
                                    ?? RenderInline(child);
                            }
                            sb.Append('`').Append(cref).Append('`');
                            break;
                        }
                        case "paramref":
                        case "typeparamref":
                        {
                            var n = child.Attribute("name")?.Value ?? "";
                            sb.Append('`').Append(n).Append('`');
                            break;
                        }
                        case "c":
                            sb.Append('`').Append(RenderInline(child)).Append('`');
                            break;
                        case "code":
                            sb.Append("\n```\n").Append(child.Value).Append("\n```\n");
                            break;
                        case "para":
                            sb.Append('\n').Append(RenderInline(child)).Append('\n');
                            break;
                        default:
                            sb.Append(RenderInline(child));
                            break;
                    }
                }
            }
            return sb.ToString();
        }

        private static string StripCrefPrefix(string cref)
        {
            if (string.IsNullOrEmpty(cref)) return cref;
            if (cref.Length >= 2 && cref[1] == ':')
            {
                char prefix = cref[0];
                if (prefix == 'T' || prefix == 'M' || prefix == 'P'
                    || prefix == 'F' || prefix == 'E' || prefix == 'N')
                {
                    return cref.Substring(2);
                }
            }
            return cref;
        }
    }
}
