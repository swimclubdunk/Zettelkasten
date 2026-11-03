using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Zettelkasten.Core.Links;
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;

namespace Zettelkasten.App.Preview;

/// <summary>Renders Markdown as a native WPF FlowDocument; [[id]] links become clickable note links.
/// Colors are theme resource references, so an open preview follows theme switches.</summary>
public sealed class FlowDocumentRenderer
{
    public const string BrokenToolTip = "No note with this ID";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public FlowDocument Render(string markdown, Func<string, string?> titleLookup, Action<string> onLink)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(20, 8, 20, 20),
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(TextElement.ForegroundProperty, "TextBrush");
        foreach (var block in Markdown.Parse(WikiLinkRewriter.Rewrite(markdown, titleLookup), Pipeline))
            if (RenderBlock(block, onLink) is { } rendered) doc.Blocks.Add(rendered);
        return doc;
    }

    private WpfBlock? RenderBlock(MdBlock block, Action<string> onLink) => block switch
    {
        HeadingBlock h => WithInlines(new Paragraph
        {
            FontWeight = FontWeights.Bold,
            FontSize = h.Level switch { 1 => 24, 2 => 20, 3 => 17, _ => 15 },
            Margin = new Thickness(0, h.Level <= 2 ? 14 : 10, 0, 6),
        }, h.Inline, onLink),
        ParagraphBlock p => WithInlines(new Paragraph { Margin = new Thickness(0, 0, 0, 10) }, p.Inline, onLink),
        QuoteBlock q => Quote(q, onLink),
        ListBlock l => List(l, onLink),
        CodeBlock c => Themed(new Paragraph(new Run(c.Lines.ToString().TrimEnd()))
        {
            FontFamily = Mono, FontSize = 13,
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10),
        }, TextElement.BackgroundProperty, "CodeBackgroundBrush"),
        ThematicBreakBlock => new BlockUIContainer(new Separator { Margin = new Thickness(0, 6, 0, 12) }),
        HtmlBlock html => new Paragraph(new Run(html.Lines.ToString())),
        LeafBlock leaf when leaf.Inline is not null => WithInlines(new Paragraph(), leaf.Inline, onLink),
        _ => null,
    };

    private Section Quote(QuoteBlock quote, Action<string> onLink)
    {
        var section = new Section
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0), Margin = new Thickness(0, 0, 0, 10),
        };
        section.SetResourceReference(WpfBlock.BorderBrushProperty, "LineBrush");
        section.SetResourceReference(TextElement.ForegroundProperty, "MutedBrush");
        foreach (var child in quote)
            if (RenderBlock(child, onLink) is { } rendered) section.Blocks.Add(rendered);
        return section;
    }

    private List List(ListBlock list, Action<string> onLink)
    {
        var result = new List
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 10), Padding = new Thickness(24, 0, 0, 0),
        };
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var listItem = new ListItem();
            foreach (var child in item)
            {
                if (RenderBlock(child, onLink) is not { } rendered) continue;
                if (rendered is Paragraph para) para.Margin = new Thickness(0, 0, 0, 2);
                listItem.Blocks.Add(rendered);
            }
            result.ListItems.Add(listItem);
        }
        return result;
    }

    private Paragraph WithInlines(Paragraph paragraph, ContainerInline? inlines, Action<string> onLink)
    {
        AddInlines(paragraph.Inlines, inlines, onLink);
        return paragraph;
    }

    private void AddInlines(InlineCollection target, ContainerInline? container, Action<string> onLink)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Run(literal.Content.ToString()));
                    break;
                case EmphasisInline emphasis:
                    Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                    AddInlines(span.Inlines, emphasis, onLink);
                    target.Add(span);
                    break;
                case CodeInline code:
                    target.Add(Themed(new Run(code.Content) { FontFamily = Mono }, TextElement.BackgroundProperty, "CodeBackgroundBrush"));
                    break;
                case LinkInline { IsImage: true } image:
                    target.Add(new Run($"[image: {image.Url}]"));
                    break;
                case LinkInline link:
                    target.Add(Link(link.Url ?? "", h => AddInlines(h.Inlines, link, onLink), onLink));
                    break;
                case AutolinkInline auto:
                    target.Add(Link(auto.Url, h => h.Inlines.Add(new Run(auto.Url)), onLink));
                    break;
                case LineBreakInline lineBreak:
                    target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case HtmlEntityInline entity:
                    target.Add(new Run(entity.Transcoded.ToString()));
                    break;
                case HtmlInline html:
                    target.Add(new Run(html.Tag));
                    break;
                case ContainerInline other:
                    AddInlines(target, other, onLink);
                    break;
            }
        }
    }

    private WpfInline Link(string url, Action<Hyperlink> fill, Action<string> onLink)
    {
        var hyperlink = new Hyperlink { Cursor = System.Windows.Input.Cursors.Hand };
        hyperlink.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
        fill(hyperlink);
        if (url.StartsWith(WikiLinkRewriter.BrokenScheme, StringComparison.Ordinal))
        {
            hyperlink.SetResourceReference(TextElement.ForegroundProperty, "ErrorBrush");
            hyperlink.ToolTip = BrokenToolTip;
        }
        else if (!url.StartsWith(WikiLinkRewriter.NoteScheme, StringComparison.Ordinal))
        {
            hyperlink.ToolTip = url;
        }
        hyperlink.Click += (_, _) => onLink(url);
        return hyperlink;
    }

    private static T Themed<T>(T element, DependencyProperty property, string resourceKey) where T : FrameworkContentElement
    {
        element.SetResourceReference(property, resourceKey);
        return element;
    }
}
