using DockGx.Models;
using Xunit;

namespace DockGx.Tests.Models;

public class DockConfigTests
{
    [Fact]
    public void Defaults_match_the_first_run_experience()
    {
        var cfg = new DockConfig();

        Assert.Empty(cfg.Items);
        Assert.False(cfg.Snapped);
        Assert.Equal(DockEdge.Bottom, cfg.Edge);
        Assert.Null(cfg.FreeX);
        Assert.Null(cfg.FreeY);
        Assert.True(cfg.AutoHide);
        Assert.False(cfg.LaunchAtStartup);
        Assert.True(cfg.AlwaysOnTop);
        Assert.False(cfg.Seeded);
    }
}
