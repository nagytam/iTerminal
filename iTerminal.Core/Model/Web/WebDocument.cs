using System.Text;

namespace iTerminal.Core.Model.Web;

internal enum WebFieldKind
{
    Text,
    Password,
    Checkbox,
    Radio,
    Submit,
    Reset,
    Button,
    Select,
    TextArea,
    Hidden,
    File,
}

/// <summary>An input, button, select or textarea of a page. Its state lives here, so it survives a re-layout.</summary>
internal sealed class WebField
{
    public WebFieldKind Kind { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public bool Checked { get; set; }
    public bool Disabled { get; init; }
    public bool ReadOnly { get; init; }
    public int Size { get; init; } = 20;
    public WebForm? Form { get; set; }
    public List<WebOption> Options { get; } = [];
    public int SelectedIndex { get; set; }
    private readonly string _initialValue;
    private readonly bool _initialChecked;
    private readonly int _initialIndex;

    public WebField(string initialValue, bool initialChecked, int initialIndex)
    {
        _initialValue = initialValue;
        _initialChecked = initialChecked;
        _initialIndex = initialIndex;
        Value = initialValue;
        Checked = initialChecked;
        SelectedIndex = initialIndex;
    }

    public bool IsTextEntry => Kind is WebFieldKind.Text or WebFieldKind.Password or WebFieldKind.TextArea;

    public bool IsButton => Kind is WebFieldKind.Submit or WebFieldKind.Reset or WebFieldKind.Button;

    /// <summary>What the form sends for this field, or null when it sends nothing.</summary>
    public string? SubmittedValue => Kind switch
    {
        WebFieldKind.Checkbox or WebFieldKind.Radio => Checked ? (Value.Length == 0 ? "on" : Value) : null,
        WebFieldKind.Select => Options.Count == 0 ? null : Options[Math.Clamp(SelectedIndex, 0, Options.Count - 1)].Value,
        WebFieldKind.Submit or WebFieldKind.Reset or WebFieldKind.Button or WebFieldKind.File => null,
        _ => Value,
    };

    public void Reset()
    {
        Value = _initialValue;
        Checked = _initialChecked;
        SelectedIndex = _initialIndex;
    }

    /// <summary>The number of columns the field takes on a line; fixed when the page is laid out.</summary>
    public int DisplayWidth(int available)
    {
        var width = Kind switch
        {
            WebFieldKind.Checkbox or WebFieldKind.Radio => 3,
            WebFieldKind.Submit or WebFieldKind.Reset or WebFieldKind.Button => Label.Length + 4,
            WebFieldKind.Select => Math.Min(40, (Options.Count == 0 ? 0 : Options.Max(o => o.Text.Length)) + 4),
            WebFieldKind.File => 15,
            WebFieldKind.Hidden => 0,
            _ => Math.Max(6, Size + 2),
        };
        return Math.Max(Math.Min(width, Math.Max(3, available)), Kind == WebFieldKind.Hidden ? 0 : 3);
    }

    /// <summary>
    /// The field as it appears on screen, exactly <paramref name="width"/> columns wide. While it is being
    /// edited the text cursor is part of the text, kept in view by showing the tail of what was typed.
    /// </summary>
    public string Render(int width, bool editing)
    {
        string text;
        switch (Kind)
        {
            case WebFieldKind.Checkbox:
                text = Checked ? "[x]" : "[ ]";
                break;
            case WebFieldKind.Radio:
                text = Checked ? "(*)" : "( )";
                break;
            case WebFieldKind.Submit or WebFieldKind.Reset or WebFieldKind.Button:
                text = "[ " + Label + " ]";
                break;
            case WebFieldKind.Select:
                var selected = Options.Count == 0 ? string.Empty : Options[Math.Clamp(SelectedIndex, 0, Options.Count - 1)].Text;
                text = "[" + selected.PadRight(Math.Max(0, width - 4)) + " ▾]";
                break;
            case WebFieldKind.File:
                text = "[Choose file]";
                break;
            default:
                var inner = Math.Max(1, width - 2);
                var shown = Kind == WebFieldKind.Password ? new string('•', Value.Length) : Value.Replace('\n', '⏎').Replace('\r', ' ');
                if (editing)
                {
                    shown += "█";
                    shown = shown.Length > inner ? shown[^inner..] : shown;
                }
                else if (shown.Length == 0)
                {
                    shown = Placeholder;
                }

                shown = shown.Length > inner ? shown[..inner] : shown;
                text = "[" + shown.PadRight(inner, editing || Value.Length > 0 ? ' ' : '_') + "]";
                break;
        }

        return text.Length > width ? text[..width] : text.PadRight(width);
    }
}

internal sealed record WebOption(string Value, string Text);

internal sealed class WebForm
{
    public string Action { get; init; } = string.Empty;
    public bool IsPost { get; init; }
    public List<WebField> Fields { get; } = [];

    /// <summary>The name/value pairs a submit sends; the clicked button adds its own pair.</summary>
    public List<KeyValuePair<string, string>> Collect(WebField? submitter)
    {
        List<KeyValuePair<string, string>> pairs = [];
        foreach (var field in Fields)
        {
            if (field.Disabled || field.Name.Length == 0)
            {
                continue;
            }

            if (field.IsButton)
            {
                if (field == submitter && field.Kind == WebFieldKind.Submit)
                {
                    pairs.Add(new(field.Name, field.Value));
                }

                continue;
            }

            if (field.SubmittedValue is { } value)
            {
                pairs.Add(new(field.Name, value));
            }
        }

        return pairs;
    }

    public static string Encode(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        var builder = new StringBuilder();
        foreach (var (name, value) in pairs)
        {
            if (builder.Length > 0)
            {
                builder.Append('&');
            }

            builder.Append(Uri.EscapeDataString(name).Replace("%20", "+"))
                .Append('=')
                .Append(Uri.EscapeDataString(value).Replace("%20", "+"));
        }

        return builder.ToString();
    }
}

internal sealed record WebLink(string Href, string Title);

/// <summary>
/// A parsed page: the tree plus the things in it a person can act on — links, forms and fields.
/// Built once per load; the layout is redone when the window is resized.
/// </summary>
internal sealed class WebDocument
{
    public required string Url { get; init; }
    public required HtmlNode Root { get; init; }
    public string Title { get; private set; } = string.Empty;
    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>The target of a <c>meta http-equiv="refresh"</c>, with its delay in seconds.</summary>
    public (int Seconds, string Url)? Refresh { get; private set; }

    public List<WebLink> Links { get; } = [];
    public List<WebField> Fields { get; } = [];
    public Dictionary<HtmlNode, int> LinkOf { get; } = [];
    public Dictionary<HtmlNode, int> FieldOf { get; } = [];

    public static WebDocument Parse(string? url, string? html)
    {
        url ??= string.Empty;
        var document = new WebDocument { Url = url, Root = HtmlParser.Parse(html) };
        document.BaseUrl = url;
        document.Build(document.Root, null);
        return document;
    }

    /// <summary>Resolves a link against the page's address (and its <c>base</c> element).</summary>
    public string Resolve(string href) => WebAddress.Resolve(BaseUrl, href);

    private void Build(HtmlNode node, WebForm? form)
    {
        switch (node.Tag)
        {
            case "title" when Title.Length == 0:
                Title = Collapse(node.InnerText());
                return;
            case "base":
                if (node.Attr("href") is { Length: > 0 } baseHref)
                {
                    BaseUrl = WebAddress.Resolve(Url, baseHref);
                }

                return;
            case "meta":
                ReadRefresh(node);
                return;
            case "script" or "style":
                return;
            case "form":
                form = new WebForm { Action = node.Attr("action") ?? string.Empty, IsPost = string.Equals(node.Attr("method"), "post", StringComparison.OrdinalIgnoreCase) };
                break;
            case "a" when node.Attr("href") is { } href:
                LinkOf[node] = Links.Count;
                Links.Add(new WebLink(href, node.Attr("title") ?? string.Empty));
                break;
            case "iframe" or "frame":
                if (node.Attr("src") is { Length: > 0 } source)
                {
                    LinkOf[node] = Links.Count;
                    Links.Add(new WebLink(source, node.Attr("title") ?? string.Empty));
                }

                return;
            case "input":
                AddInput(node, form);
                return;
            case "button":
                AddButton(node, form);
                return;
            case "select":
                AddSelect(node, form);
                return;
            case "textarea":
                AddTextArea(node, form);
                return;
        }

        foreach (var child in node.Children)
        {
            Build(child, form);
        }
    }

    private void ReadRefresh(HtmlNode node)
    {
        if (!string.Equals(node.Attr("http-equiv"), "refresh", StringComparison.OrdinalIgnoreCase) || node.Attr("content") is not { } content)
        {
            return;
        }

        var parts = content.Split([';', ','], 2);
        if (!int.TryParse(parts[0].Trim(), out var seconds) || parts.Length < 2)
        {
            return;
        }

        var target = parts[1].Trim();
        if (target.StartsWith("url", StringComparison.OrdinalIgnoreCase))
        {
            target = target[3..].TrimStart().TrimStart('=').Trim().Trim('\'', '"');
        }

        if (target.Length > 0)
        {
            Refresh = (Math.Max(0, seconds), WebAddress.Resolve(Url, target));
        }
    }

    private void AddField(HtmlNode node, WebField field, WebForm? form)
    {
        field.Form = form;
        form?.Fields.Add(field);
        FieldOf[node] = Fields.Count;
        Fields.Add(field);
    }

    private void AddInput(HtmlNode node, WebForm? form)
    {
        var type = (node.Attr("type") ?? "text").ToLowerInvariant();
        var kind = type switch
        {
            "password" => WebFieldKind.Password,
            "checkbox" => WebFieldKind.Checkbox,
            "radio" => WebFieldKind.Radio,
            "submit" or "image" => WebFieldKind.Submit,
            "reset" => WebFieldKind.Reset,
            "button" => WebFieldKind.Button,
            "hidden" => WebFieldKind.Hidden,
            "file" => WebFieldKind.File,
            _ => WebFieldKind.Text,
        };

        var value = node.Attr("value") ?? string.Empty;
        var label = kind switch
        {
            WebFieldKind.Submit when type == "image" => node.Attr("alt") ?? "Submit",
            WebFieldKind.Submit => value.Length > 0 ? value : "Submit",
            WebFieldKind.Reset => value.Length > 0 ? value : "Reset",
            WebFieldKind.Button => value.Length > 0 ? value : "Button",
            _ => string.Empty,
        };

        var size = int.TryParse(node.Attr("size"), out var parsed) && parsed > 0 ? Math.Min(parsed, 60) : 20;
        AddField(node, new WebField(value, node.HasAttr("checked"), 0)
        {
            Kind = kind,
            Name = node.Attr("name") ?? string.Empty,
            Placeholder = node.Attr("placeholder") ?? string.Empty,
            Label = Collapse(label),
            Disabled = node.HasAttr("disabled"),
            ReadOnly = node.HasAttr("readonly"),
            Size = size,
        }, form);
    }

    private void AddButton(HtmlNode node, WebForm? form)
    {
        var type = (node.Attr("type") ?? "submit").ToLowerInvariant();
        var kind = type switch
        {
            "reset" => WebFieldKind.Reset,
            "button" => WebFieldKind.Button,
            _ => WebFieldKind.Submit,
        };

        var label = Collapse(node.InnerText());
        AddField(node, new WebField(node.Attr("value") ?? string.Empty, false, 0)
        {
            Kind = kind,
            Name = node.Attr("name") ?? string.Empty,
            Label = label.Length > 0 ? label : "Button",
            Disabled = node.HasAttr("disabled"),
        }, form);
    }

    private void AddSelect(HtmlNode node, WebForm? form)
    {
        List<WebOption> options = [];
        var selected = -1;
        CollectOptions(node, options, ref selected);
        var field = new WebField(string.Empty, false, Math.Max(0, selected))
        {
            Kind = WebFieldKind.Select,
            Name = node.Attr("name") ?? string.Empty,
            Disabled = node.HasAttr("disabled"),
        };
        field.Options.AddRange(options);
        AddField(node, field, form);
    }

    private static void CollectOptions(HtmlNode node, List<WebOption> options, ref int selected)
    {
        foreach (var child in node.Children)
        {
            if (child.Tag == "option")
            {
                var text = Collapse(child.InnerText());
                if (child.HasAttr("selected") && selected < 0)
                {
                    selected = options.Count;
                }

                options.Add(new WebOption(child.Attr("value") ?? text, text));
            }
            else if (!child.IsText)
            {
                CollectOptions(child, options, ref selected);
            }
        }
    }

    private void AddTextArea(HtmlNode node, WebForm? form)
    {
        var text = node.InnerText();
        if (text.StartsWith('\n'))
        {
            text = text[1..];
        }

        var columns = int.TryParse(node.Attr("cols"), out var parsed) && parsed > 0 ? Math.Min(parsed, 60) : 30;
        AddField(node, new WebField(text, false, 0)
        {
            Kind = WebFieldKind.TextArea,
            Name = node.Attr("name") ?? string.Empty,
            Placeholder = node.Attr("placeholder") ?? string.Empty,
            Disabled = node.HasAttr("disabled"),
            ReadOnly = node.HasAttr("readonly"),
            Size = columns,
        }, form);
    }

    internal static string Collapse(string? text)
    {
        text ??= string.Empty;
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) && c != '\u00A0')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c == '\u00A0' ? ' ' : c);
        }

        return builder.ToString();
    }
}
