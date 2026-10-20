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
    public void Create_ReturnsSixDigitCodeAndPersistsGrant()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var (grant, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);

        Assert.Equal(6, code.Length);
        Assert.Equal(userId, grant.UserId);
        Assert.Equal(itemId, grant.ItemId);
        Assert.Equal(GrantStatus.Pending, grant.Status);
        Assert.True(File.Exists(_filePath));
        Assert.Single(store.GetAll());
    }

    [Fact]
    public void Unlock_WithCorrectCode_ActivatesGrant()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);

        var result = store.TryUnlock(code);

        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.NotNull(result.Grant);
        Assert.Equal(GrantStatus.Active, result.Grant.Status);
        Assert.NotNull(result.Grant.UnlockedUtc);
    }

    [Fact]
    public void Unlock_WithWrongCode_LeavesGrantPending()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var (grant, _) = store.Create(userId, "Kind", itemId, "Der Film", 24);

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
        var (_, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(code).Status);
        Assert.Equal(UnlockStatus.AlreadyUsed, store.TryUnlock(code).Status);
    }

    [Fact]
    public void Evaluate_AllowsGrantedItemAndBlocksEverythingElse()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);
        store.TryUnlock(code);

        Assert.Equal(PlaybackDecision.Allowed, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, otherItemId).Decision);
    }

    [Fact]
    public void Evaluate_BlocksBeforeCodeIsEntered()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        store.Create(userId, "Kind", itemId, "Der Film", 24);

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
        var (grant, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);
        store.TryUnlock(code);

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
        var (grant, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);
        store.TryUnlock(code);

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
        var (grant, _) = store.Create(userId, "Kind", itemId, "Der Film", 24);

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
        var (_, code) = store.Create(userId, "Kind", itemId, "Der Film", 24);

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
        var itemId = Guid.NewGuid();
        var (_, code) = store.Create(userId, "Kind", itemId, "Der Film", 0);

        _clock.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(code).Status);
    }

    [Fact]
    public void Create_ReplacesExistingGrantForUser()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var (_, firstCode) = store.Create(userId, "Kind", firstItemId, "Film eins", 24);

        _clock.Advance(TimeSpan.FromMinutes(1));
        var (_, secondCode) = store.Create(userId, "Kind", secondItemId, "Film zwei", 24);

        Assert.Single(store.GetAll());
        Assert.Equal(secondItemId, store.GetForUser(userId)!.ItemId);
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
        (_, code) = firstStore.Create(userId, "Kind", itemId, "Der Film", 24);

        var secondStore = CreateStore();
        var result = secondStore.TryUnlock(code);

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
