using Jellyfin.Plugin.MovieCode.Services;

namespace Jellyfin.Plugin.MovieCode.Tests;

public sealed class UnlockRateLimiterTests
{
    [Fact]
    public void TryAcquire_BlocksAfterLimit()
    {
        var clock = new FakeTimeProvider();
        var limiter = new UnlockRateLimiter(clock);

        Assert.True(limiter.TryAcquire("127.0.0.1", 2, TimeSpan.FromMinutes(10)));
        Assert.True(limiter.TryAcquire("127.0.0.1", 2, TimeSpan.FromMinutes(10)));
        Assert.False(limiter.TryAcquire("127.0.0.1", 2, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void TryAcquire_AllowsAgainAfterWindow()
    {
        var clock = new FakeTimeProvider();
        var limiter = new UnlockRateLimiter(clock);

        Assert.True(limiter.TryAcquire("127.0.0.1", 1, TimeSpan.FromMinutes(10)));
        Assert.False(limiter.TryAcquire("127.0.0.1", 1, TimeSpan.FromMinutes(10)));

        clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        Assert.True(limiter.TryAcquire("127.0.0.1", 1, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Reset_ClearsCounter()
    {
        var clock = new FakeTimeProvider();
        var limiter = new UnlockRateLimiter(clock);

        Assert.True(limiter.TryAcquire("127.0.0.1", 1, TimeSpan.FromMinutes(10)));
        Assert.False(limiter.TryAcquire("127.0.0.1", 1, TimeSpan.FromMinutes(10)));

        limiter.Reset("127.0.0.1");

        Assert.True(limiter.TryAcquire("127.0.0.1", 1, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ZeroLimit_DisablesRateLimiting()
    {
        var clock = new FakeTimeProvider();
        var limiter = new UnlockRateLimiter(clock);

        for (var index = 0; index < 100; index++)
        {
            Assert.True(limiter.TryAcquire("127.0.0.1", 0, TimeSpan.FromMinutes(10)));
        }
    }
}
