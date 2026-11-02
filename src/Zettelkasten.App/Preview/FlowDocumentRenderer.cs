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

/// <summary>Renders Markdown as a native WPF FlowDocument; [[id]] links become clickable note links.</summary>
public sealed class FlowDocumentRenderer(Brush linkBrush, Brush brokenBrush, Brush codeBackground)
{
    public const string BrokenToolTip = "No note with this ID";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAutoLinks().Build();
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public FlowDocumentRenderer()
        : this(new SolidColorBrush(Color.FromRgb(0x2F, 0x6F, 0xDF)),
               new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)),
               new SolidColorBrush(Color.FromRgb(0xF3, 0xF2, 0xEE)))
    {
    }

    public FlowDocument Render(string markdown, Func<string, string?> titleLookup, Action<string> onLink)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            PagePadding = new Thickness(20, 8, 20, 20),
            TextAlignment = TextAlignment.Left,
        };
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
        CodeBlock c => new Paragraph(new Run(c.Lines.ToString().TrimEnd()))
        {
            FontFamily = Mono, FontSize = 13, Background = codeBackground,
            Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 10),
        },
        ThematicBreakBlock => new BlockUIContainer(new Separator { Margin = new Thickness(0, 6, 0, 12) }),
        HtmlBlock html => new Paragraph(new Run(html.Lines.ToString())),
        LeafBlock leaf when leaf.Inline is not null => WithInlines(new Paragraph(), leaf.Inline, onLink),
        _ => null,
    };

    private Section Quote(QuoteBlock quote, Action<string> onLink)
    {
        var section = new Section
        {
            BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 0, 0, 0), Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 10),
        };
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
                    target.Add(new Run(code.Content) { FontFamily = Mono, Background = codeBackground });
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
        var hyperlink = new Hyperlink { Foreground = linkBrush, Cursor = System.Windows.Input.Cursors.Hand };
        fill(hyperlink);
        if (url.StartsWith(WikiLinkRewriter.BrokenScheme, StringComparison.Ordinal))
        {
            hyperlink.Foreground = brokenBrush;
            hyperlink.ToolTip = BrokenToolTip;
        }
        else if (!url.StartsWith(WikiLinkRewriter.NoteScheme, StringComparison.Ordinal))
        {
            hyperlink.ToolTip = url;
        }
        hyperlink.Click += (_, _) => onLink(url);
        return hyperlink;
    }
}
