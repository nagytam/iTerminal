using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using iConsole.Model;

namespace iConsole.Xml;

/// <summary>
/// A node of an XML tree shown in a <see cref="iConsole.Controls.TreeView"/>: an element, a run of text, a comment or a
/// processing instruction. The document itself is the root node.
/// </summary>
public class XmlNodeItem(XmlNodeItem? parent, string name, XObject? node) : HierarchicalItemModel(parent, name)
{
    /// <summary>The XML node this item shows; null for the document itself.</summary>
    public XObject? Node { get; } = node;
}

/// <summary>
/// The XML document as a tree of <see cref="XmlNodeItem"/>s, with an index from each XML node to its item so that a
/// query result can be shown at its place in the tree.
/// </summary>
public sealed class XmlTree
{
    /// <summary>Longest text shown on one node; a longer run is cut with an ellipsis.</summary>
    public const int MaxTextLength = 120;

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private readonly Dictionary<XObject, XmlNodeItem> _items = new(ReferenceEqualityComparer.Instance);

    private XmlTree(XDocument document, XmlNodeItem root)
    {
        Document = document;
        Root = root;
    }

    public XDocument Document { get; }

    public XmlNodeItem Root { get; }

    /// <summary>The item for an XML node, or null when the node is not part of the tree (an attribute, say).</summary>
    public XmlNodeItem? ItemFor(XObject node) => _items.GetValueOrDefault(node);

    /// <summary>Builds the tree for a parsed document. The root is named after the document's root element.</summary>
    public static XmlTree Build(XDocument document)
    {
        XmlNodeItem root = new(null, "(document)", document);
        XmlTree tree = new(document, root);
        tree.Index(document, root);
        tree.AddChildren(document, root);
        return tree;
    }

    private void AddChildren(XContainer container, XmlNodeItem parent)
    {
        foreach (XNode child in container.Nodes())
        {
            switch (child)
            {
                case XElement element:
                    AddElement(element, parent);
                    break;

                case XText text when text.Value.Trim().Length > 0:
                    AddLeaf(parent, text, $"\"{Shorten(text.Value)}\"");
                    break;

                case XComment comment:
                    AddLeaf(parent, comment, $"<!-- {Shorten(comment.Value)} -->");
                    break;

                case XProcessingInstruction instruction:
                    AddLeaf(parent, instruction, $"<?{instruction.Target} {Shorten(instruction.Data)}?>");
                    break;
            }
        }
    }

    private void AddElement(XElement element, XmlNodeItem parent)
    {
        bool hasElementChildren = element.Elements().Any();
        string text = string.Concat(element.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        bool hasComments = element.Nodes().Any(n => n is XComment or XProcessingInstruction);

        // An element whose only content is text shows that text on its own line, the way a value reads in a document.
        if (!hasElementChildren && !hasComments)
        {
            string name = text.Length == 0
                ? $"<{Tag(element)}{Attributes(element)}/>"
                : $"<{Tag(element)}{Attributes(element)}>{Shorten(text)}</{Tag(element)}>";
            AddLeaf(parent, element, name);
            return;
        }

        XmlNodeItem item = new(parent, $"<{Tag(element)}{Attributes(element)}>", element);
        Index(element, item);
        parent.Children.Add(item);
        AddChildren(element, item);
    }

    private void AddLeaf(XmlNodeItem parent, XObject node, string name)
    {
        XmlNodeItem item = new(parent, name, node);
        Index(node, item);
        parent.Children.Add(item);
    }

    private void Index(XObject node, XmlNodeItem item) => _items[node] = item;

    /// <summary>The element's name, with its prefix when it has one.</summary>
    public static string Tag(XElement element) =>
        element.Name.NamespaceName.Length > 0 && element.GetPrefixOfNamespace(element.Name.Namespace) is { Length: > 0 } prefix
            ? $"{prefix}:{element.Name.LocalName}"
            : element.Name.LocalName;

    /// <summary>The attributes of an element as they are written: a space and name="value" for each, no namespace declarations.</summary>
    public static string Attributes(XElement element) =>
        string.Concat(element.Attributes().Where(a => !a.IsNamespaceDeclaration)
            .Select(a => $" {AttributeName(a)}=\"{Shorten(a.Value)}\""));

    /// <summary>The attribute's name as written: its prefix, if it has one, and its local name.</summary>
    public static string AttributeName(XAttribute attribute)
    {
        string? prefix = attribute.Parent?.GetPrefixOfNamespace(attribute.Name.Namespace);
        return prefix is { Length: > 0 } ? $"{prefix}:{attribute.Name.LocalName}" : attribute.Name.LocalName;
    }

    /// <summary>Collapses whitespace and cuts text longer than <see cref="MaxTextLength"/>.</summary>
    public static string Shorten(string text)
    {
        string collapsed = Spaces.Replace(text, " ").Trim();
        return collapsed.Length > MaxTextLength ? collapsed[..(MaxTextLength - 1)] + "…" : collapsed;
    }

    /// <summary>
    /// The item for the element that starts at or before a line, the closest one above it; the root when the line is before
    /// the first element. Lines are kept for the parsed elements, so a validation error can be shown in the tree.
    /// </summary>
    public XmlNodeItem? ItemAtLine(int line)
    {
        XElement? closest = null;
        foreach (XElement element in Document.Descendants())
        {
            if (((IXmlLineInfo)element).LineNumber > line)
            {
                break;
            }

            closest = element;
        }

        return closest is not null ? ItemFor(closest) ?? Root : Root;
    }

    /// <summary>Parses a document, or throws an <see cref="XmlException"/> that says where it is malformed. Elements keep their line numbers.</summary>
    public static XDocument Parse(byte[] content) => XDocument.Load(new MemoryStream(content), LoadOptions.SetLineInfo);
}
