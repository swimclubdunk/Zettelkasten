using Zettelkasten.Core.Common;

namespace Zettelkasten.Tests.Core;

public class NavigationHistoryTests
{
    [Fact]
    public void Back_and_forward()
    {
        var h = new NavigationHistory();
        h.Visit("a"); h.Visit("b"); h.Visit("c");
        Assert.Equal("b", h.GoBack());
        Assert.Equal("a", h.GoBack());
        Assert.False(h.CanGoBack);
        Assert.Equal("b", h.GoForward());
        Assert.True(h.CanGoForward);
    }

    [Fact]
    public void Visit_clears_forward_and_ignores_same_id()
    {
        var h = new NavigationHistory();
        h.Visit("a"); h.Visit("b"); h.GoBack();
        h.Visit("a");
        Assert.True(h.CanGoForward);  // re-visiting current is a no-op
        h.Visit("c");
        Assert.False(h.CanGoForward);
        Assert.Equal("a", h.GoBack());
    }

    [Fact]
    public void Remove_drops_id_everywhere_and_falls_back()
    {
        var h = new NavigationHistory();
        h.Visit("a"); h.Visit("b"); h.Visit("a"); h.Visit("c");
        // back = [a, b, a], current = c
        Assert.Equal("a", h.Remove("c"));   // current removed -> previous entry
        Assert.Equal("b", h.Remove("a"));   // every "a" removed -> falls back to b
        Assert.False(h.CanGoBack);
        Assert.Equal("b", h.Current);
        Assert.Null(new NavigationHistory().GoBack());
    }
}
