using Microsoft.Extensions.Time.Testing;
using Zettelkasten.Core.Common;

namespace Zettelkasten.Tests.Core;

public class DebouncerTests
{
    [Fact]
    public async Task Runs_action_only_after_delay()
    {
        var time = new FakeTimeProvider();
        using var d = new Debouncer(TimeSpan.FromSeconds(1), time);
        var runs = 0;

        var t = d.Trigger(() => { runs++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(0, runs);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await t;
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Retrigger_restarts_delay_and_runs_once()
    {
        var time = new FakeTimeProvider();
        using var d = new Debouncer(TimeSpan.FromSeconds(1), time);
        var runs = 0;

        var first = d.Trigger(() => { runs++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(600));
        var second = d.Trigger(() => { runs++; return Task.CompletedTask; });
        time.Advance(TimeSpan.FromMilliseconds(600));
        await first;
        Assert.Equal(0, runs);

        time.Advance(TimeSpan.FromMilliseconds(400));
        await second;
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task Cancel_prevents_run()
    {
        var time = new FakeTimeProvider();
        using var d = new Debouncer(TimeSpan.FromSeconds(1), time);
        var runs = 0;
        var t = d.Trigger(() => { runs++; return Task.CompletedTask; });
        d.Cancel();
        time.Advance(TimeSpan.FromSeconds(2));
        await t;
        Assert.Equal(0, runs);
    }
}
