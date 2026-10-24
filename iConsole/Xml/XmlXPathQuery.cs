using System.Collections;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;

namespace iConsole.Xml;

/// <summary>One node or value an XPath expression selected: where it is, the tree item to jump to, and its value.</summary>
public sealed record XmlXPathResult(XmlNodeItem? Node, string Path, string Value)
{
    public override string ToString() => Value.Length == 0 ? Path : $"{Path} = {Value}";
}

/// <summary>What a query gave: the first results, how many there were in all, and the error when it did not run.</summary>
public sealed record XmlXPathOutcome(IReadOnlyList<XmlXPathResult> Results, int Total, string? Error)
{
    public static XmlXPathOutcome Empty { get; } = new([], 0, null);
}

/// <summary>
/// Runs an XPath 1.0 expression against an XML tree. A node-set gives one result per node, up to
/// <see cref="MaxResults"/>; a number, text or boolean gives one result for its value. Prefixes the document declares
/// on its root element can be used in the expression.
/// </summary>
public static class XmlXPathQuery
{
    public const int MaxResults = 500;

    public static XmlXPathOutcome Run(XmlTree tree, string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return XmlXPathOutcome.Empty;
        }

        try
        {
            object value = tree.Document.XPathEvaluate(expression, Prefixes(tree.Document));
            if (value is IEnumerable nodes and not string)
            {
                List<XmlXPathResult> results = [];
                int total = 0;
                foreach (object node in nodes)
                {
                    total++;
                    if (results.Count < MaxResults && Describe(tree, node) is { } result)
                    {
                        results.Add(result);
                    }
                }

                return new XmlXPathOutcome(results, total, null);
            }

            string text = value is double number
                ? number.ToString(CultureInfo.InvariantCulture)
                : value is bool flag ? (flag ? "true" : "false") : value.ToString() ?? string.Empty;
            return new XmlXPathOutcome([new XmlXPathResult(null, string.Empty, text)], 1, null);
        }
        catch (XPathException exception)
        {
            return new XmlXPathOutcome([], 0, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return new XmlXPathOutcome([], 0, exception.Message);
        }
    }

    /// <summary>The namespace prefixes the root element declares, for use in the expression. A default namespace has no prefix in XPath and is left out.</summary>
    private static XmlNamespaceManager Prefixes(XDocument document)
    {
        XmlNamespaceManager manager = new(new NameTable());
        foreach (XAttribute declaration in document.Root?.Attributes().Where(a => a.IsNamespaceDeclaration) ?? [])
        {
            if (declaration.Name.LocalName != "xmlns")
            {
                manager.AddNamespace(declaration.Name.LocalName, declaration.Value);
            }
        }

        return manager;
    }

    private static XmlXPathResult? Describe(XmlTree tree, object selected) => selected switch
    {
        XDocument document => new XmlXPathResult(tree.Root, "/", string.Empty),
        XElement element => Result(tree, element, PathOf(element), XmlTree.Shorten(element.Value)),
        XAttribute attribute => Result(tree, attribute.Parent!, PathOf(attribute.Parent!) + "/@" + XmlTree.AttributeName(attribute), attribute.Value),
        XText text => Result(tree, text.Parent!, PathOf(text.Parent!) + "/text()", XmlTree.Shorten(text.Value)),
        XComment comment => Result(tree, comment.Parent!, PathOf(comment.Parent!) + "/comment()", XmlTree.Shorten(comment.Value)),
        XProcessingInstruction instruction => Result(tree, instruction.Parent!, PathOf(instruction.Parent!) + "/processing-instruction()", XmlTree.Shorten(instruction.Data)),
        _ => null,
    };

    /// <summary>A result at the tree item of the node it belongs to; attributes and text are shown at their element.</summary>
    private static XmlXPathResult Result(XmlTree tree, XObject owner, string path, string value)
    {
        XmlNodeItem? node = tree.ItemFor(owner) ?? (owner is XElement element ? AncestorItem(tree, element) : null);
        return new XmlXPathResult(node, path, value);
    }

    private static XmlNodeItem? AncestorItem(XmlTree tree, XElement element)
    {
        for (XElement? current = element.Parent; current is not null; current = current.Parent)
        {
            if (tree.ItemFor(current) is { } item)
            {
                return item;
            }
        }

        return tree.Root;
    }

    /// <summary>
    /// The absolute XPath of an element: each step is its name, with its 1-based position among same-named siblings when
    /// there is more than one of them.
    /// </summary>
    public static string PathOf(XElement element)
    {
        List<string> steps = [];
        for (XElement? current = element; current is not null; current = current.Parent)
        {
            string name = XmlTree.Tag(current);
            if (current.Parent is { } parent)
            {
                List<XElement> siblings = [.. parent.Elements(current.Name)];
                if (siblings.Count > 1)
                {
                    name += $"[{siblings.IndexOf(current) + 1}]";
                }
            }

            steps.Add(name);
        }

        steps.Reverse();
        return "/" + string.Join("/", steps);
    }
}
