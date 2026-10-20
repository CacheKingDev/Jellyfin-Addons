using Jellyfin.Plugin.MovieCode.Models;
using Jellyfin.Plugin.MovieCode.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.MovieCode.Tests;

public sealed class GrantStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;
    private readonly FakeTimeProvider _clock = new();

    public GrantStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "moviecode-tests", Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_directory, "grants.json");
    }

    [Fact]
    public void Create_ReturnsSixDigitCodeAndLeavesItemOpen()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        var (grant, code) = store.Create(userId, "Kind", 24);

        Assert.Equal(6, code.Length);
        Assert.Equal(userId, grant.UserId);
        Assert.Null(grant.ItemId);
        Assert.Equal(GrantStatus.Pending, grant.Status);
        Assert.True(File.Exists(_filePath));
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void Unlock_WithCorrectCodeAndItem_BindsGrant()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 24);

        var result = store.TryUnlock(code, itemId, "Der Film");

        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.NotNull(result.Grant);
        Assert.Equal(GrantStatus.Active, result.Grant.Status);
        Assert.Equal(itemId, result.Grant.ItemId);
        Assert.Equal("Der Film", result.Grant.ItemName);
        Assert.NotNull(result.Grant.UnlockedUtc);
    }

    [Fact]
    public void Unlock_WithoutItem_BindsOnFirstPlayback()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 24);

        var result = store.TryUnlock(code);
        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.Null(result.Grant!.ItemId);

        var evaluation = store.Evaluate(userId, itemId, "Der Film");

        Assert.Equal(PlaybackDecision.Allowed, evaluation.Decision);
        Assert.Equal(itemId, evaluation.Grant!.ItemId);
        Assert.Equal("Der Film", evaluation.Grant.ItemName);
    }

    [Fact]
    public void Unlock_WithWrongCode_LeavesGrantPending()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var (grant, _) = store.Create(userId, "Kind", 24);

        var result = store.TryUnlock("000000");

        Assert.Equal(UnlockStatus.Invalid, result.Status);
        Assert.Equal(GrantStatus.Pending, store.GetForUser(userId)!.Status);
        Assert.Equal(GrantStatus.Pending, grant.Status);
    }

    [Fact]
    public void Unlock_SecondTime_ReportsAlreadyUsed()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 24);

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(code, itemId, "Der Film").Status);
        Assert.Equal(UnlockStatus.AlreadyUsed, store.TryUnlock(code, itemId, "Der Film").Status);
    }

    [Fact]
    public void Unlock_SecondTimeWithOtherItem_DoesNotRebind()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 24);
        store.TryUnlock(code, itemId, "Der Film");

        var result = store.TryUnlock(code, otherItemId, "Anderer Film");

        Assert.Equal(UnlockStatus.AlreadyUsed, result.Status);
        Assert.Equal(itemId, store.GetForUser(userId)!.ItemId);
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, otherItemId).Decision);
    }

    [Fact]
    public void Evaluate_AllowsGrantedItemAndBlocksEverythingElse()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 24);
        store.TryUnlock(code, itemId, "Der Film");

        Assert.Equal(PlaybackDecision.Allowed, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, otherItemId).Decision);
    }

    [Fact]
    public void Evaluate_BlocksBeforeCodeIsEntered()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        store.Create(userId, "Kind", 24);

        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, itemId).Decision);
    }

    [Fact]
    public void Evaluate_IsUnrestrictedWithoutGrant()
    {
        var store = CreateStore();

        Assert.Equal(PlaybackDecision.Unrestricted, store.Evaluate(Guid.NewGuid(), Guid.NewGuid()).Decision);
    }

    [Fact]
    public void Complete_BlocksPlaybackAndInvalidatesCode()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (grant, code) = store.Create(userId, "Kind", 24);
        store.TryUnlock(code, itemId, "Der Film");

        Assert.True(store.Complete(grant.Id, releaseUser: false));

        var current = store.GetForUser(userId);
        Assert.NotNull(current);
        Assert.Equal(GrantStatus.Completed, current.Status);
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(code).Status);
    }

    [Fact]
    public void Complete_WithRelease_RemovesRestriction()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (grant, code) = store.Create(userId, "Kind", 24);
        store.TryUnlock(code, itemId, "Der Film");

        Assert.True(store.Complete(grant.Id, releaseUser: true));
        Assert.Null(store.GetForUser(userId));
        Assert.Equal(PlaybackDecision.Unrestricted, store.Evaluate(userId, itemId).Decision);
    }

    [Fact]
    public void Remove_LiftsRestriction()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (grant, _) = store.Create(userId, "Kind", 24);

        Assert.True(store.Remove(grant.Id));
        Assert.False(store.Remove(grant.Id));
        Assert.Equal(PlaybackDecision.Unrestricted, store.Evaluate(userId, itemId).Decision);
    }

    [Fact]
    public void ExpiredGrant_IsBlockedAndCodeIsInvalid()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 24);

        _clock.Advance(TimeSpan.FromHours(25));

        var evaluation = store.Evaluate(userId, itemId);
        Assert.Equal(PlaybackDecision.Blocked, evaluation.Decision);
        Assert.Equal(GrantStatus.Expired, evaluation.Grant!.Status);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(code).Status);
    }

    [Fact]
    public void ZeroValidity_NeverExpires()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", 0);

        _clock.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(code).Status);
    }

    [Fact]
    public void Create_ReplacesExistingGrantForUser()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var (_, firstCode) = store.Create(userId, "Kind", 24);

        _clock.Advance(TimeSpan.FromMinutes(1));
        var (_, secondCode) = store.Create(userId, "Kind", 24);

        Assert.Single(store.GetAll());
        Assert.Null(store.GetForUser(userId)!.ItemId);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(firstCode).Status);
        Assert.Equal(UnlockStatus.Success, store.TryUnlock(secondCode).Status);
    }

    [Fact]
    public void Grants_AreReloadedFromDisk()
    {
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        string code;

        var firstStore = CreateStore();
        (_, code) = firstStore.Create(userId, "Kind", 24);

        var secondStore = CreateStore();
        var result = secondStore.TryUnlock(code, itemId, "Der Film");

        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.Equal(userId, result.Grant!.UserId);
        Assert.Equal(itemId, result.Grant.ItemId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private GrantStore CreateStore()
        => new(_filePath, _clock, NullLogger<GrantStore>.Instance);
}
