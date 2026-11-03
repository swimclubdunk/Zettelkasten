using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace Zettelkasten.App.Editor;

/// <summary>A syntax color looked up in the current theme palette each time text is drawn.</summary>
public sealed class ResourceHighlightingBrush(string resourceKey) : HighlightingBrush
{
    /// <summary>Highlighting color name in Markdown.xshd → palette brush key.</summary>
    public static readonly IReadOnlyDictionary<string, string> MarkdownColors = new Dictionary<string, string>
    {
        ["Heading"] = "SyntaxHeadingBrush",
        ["Code"] = "SyntaxCodeBrush",
        ["WikiLink"] = "AccentBrush",
        ["Link"] = "AccentBrush",
        ["Quote"] = "MutedBrush",
        ["ListMarker"] = "SyntaxListBrush",
    };

    public string ResourceKey { get; } = resourceKey;

    public override Brush? GetBrush(ITextRunConstructionContext context) =>
        Application.Current?.TryFindResource(ResourceKey) as Brush;

    public override string ToString() => ResourceKey;

    /// <summary>Points the definition's foreground colors at the palette instead of the fixed xshd values.</summary>
    public static void Apply(IHighlightingDefinition definition)
    {
        foreach (var (name, key) in MarkdownColors)
            if (definition.GetNamedColor(name) is { } color) color.Foreground = new ResourceHighlightingBrush(key);
    }
}
