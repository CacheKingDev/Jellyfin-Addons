using Jellyfin.Plugin.MovieCode.Models;
using Jellyfin.Plugin.MovieCode.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.MovieCode.Tests;

public sealed class GrantStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;
    private readonly string _keyPath;
    private readonly FakeTimeProvider _clock = new();

    public GrantStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "moviecode-tests", Guid.NewGuid().ToString("N"));
        _filePath = Path.Combine(_directory, "grants.json");
        _keyPath = Path.Combine(_directory, "lookup.key");
    }

    [Fact]
    public void CreateBatch_ReturnsTenUniqueCodesAndPendingGrants()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();

        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        Assert.Equal(10, batch.Codes.Count);
        Assert.Equal(10, batch.Codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(batch.Codes, code =>
        {
            Assert.Equal(6, code.Length);
            Assert.True(code.All(char.IsDigit));
        });

        var grants = store.GetAllForUser(userId);
        Assert.Equal(10, grants.Count);
        Assert.All(grants, grant =>
        {
            Assert.Equal(batch.BatchId, grant.BatchId);
            Assert.Equal(userId, grant.UserId);
            Assert.Equal("Kind", grant.UserName);
            Assert.Null(grant.ItemId);
            Assert.Equal(GrantStatus.Pending, grant.Status);
            Assert.NotNull(grant.ExpiresUtc);
        });
        Assert.Equal(Enumerable.Range(1, 10), grants.Select(grant => grant.BatchIndex));
        Assert.True(File.Exists(_filePath));
        Assert.True(File.Exists(_keyPath));
    }

    [Fact]
    public void Unlock_WithCorrectCodeAndItem_BindsGrant()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        var result = store.TryUnlock(batch.Codes[0], itemId, "Der Film");

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
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        var result = store.TryUnlock(batch.Codes[0]);
        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.Null(result.Grant!.ItemId);

        var evaluation = store.Evaluate(userId, itemId, "Der Film");

        Assert.Equal(PlaybackDecision.Allowed, evaluation.Decision);
        Assert.Equal(itemId, evaluation.Grant!.ItemId);
        Assert.Equal("Der Film", evaluation.Grant.ItemName);
    }

    [Fact]
    public void Unlock_WithWrongCode_LeavesAllCodesPending()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        var result = store.TryUnlock("000000");

        Assert.Equal(UnlockStatus.Invalid, result.Status);
        Assert.All(store.GetAllForUser(userId), grant => Assert.Equal(GrantStatus.Pending, grant.Status));
        Assert.Equal(10, store.GetAllForUser(userId).Count);
        Assert.NotEmpty(batch.Codes);
    }

    [Fact]
    public void Unlock_SecondTime_ReportsAlreadyUsed()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(batch.Codes[0], itemId, "Der Film").Status);
        Assert.Equal(UnlockStatus.AlreadyUsed, store.TryUnlock(batch.Codes[0], itemId, "Der Film").Status);
    }

    [Fact]
    public void Unlock_SecondCodeWhileActive_ReportsAlreadyActive()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(batch.Codes[0], firstItemId, "Film Eins").Status);

        var result = store.TryUnlock(batch.Codes[1], secondItemId, "Film Zwei");

        Assert.Equal(UnlockStatus.AlreadyActive, result.Status);
        Assert.Equal("Film Eins", result.Grant!.ItemName);
        Assert.Single(store.GetAllForUser(userId), grant => grant.Status == GrantStatus.Active);
        Assert.Equal(9, store.GetAllForUser(userId).Count(grant => grant.Status == GrantStatus.Pending));
    }

    [Fact]
    public void Unlock_AfterCompletion_AllowsNextCode()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var firstItemId = Guid.NewGuid();
        var secondItemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        var first = store.TryUnlock(batch.Codes[0], firstItemId, "Film Eins");
        Assert.True(store.Complete(first.Grant!.Id, releaseUser: false));

        Assert.Single(store.GetAllForUser(userId), grant => grant.Status == GrantStatus.Completed);
        Assert.Equal(9, store.GetAllForUser(userId).Count(grant => grant.Status == GrantStatus.Pending));
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, secondItemId).Decision);

        var second = store.TryUnlock(batch.Codes[1], secondItemId, "Film Zwei");

        Assert.Equal(UnlockStatus.Success, second.Status);
        Assert.Equal(GrantStatus.Active, second.Grant!.Status);
        Assert.Equal(secondItemId, second.Grant.ItemId);
    }

    [Fact]
    public void Evaluate_AllowsGrantedItemAndBlocksEverythingElse()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var otherItemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);
        store.TryUnlock(batch.Codes[0], itemId, "Der Film");

        Assert.Equal(PlaybackDecision.Allowed, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, otherItemId).Decision);
    }

    [Fact]
    public void Evaluate_BlocksBeforeCodeIsEntered()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        store.CreateBatch(userId, "Kind", 10, 24);

        var evaluation = store.Evaluate(userId, itemId);

        Assert.Equal(PlaybackDecision.Blocked, evaluation.Decision);
        Assert.Equal(GrantStatus.Pending, evaluation.Grant!.Status);
    }

    [Fact]
    public void Evaluate_IsUnrestrictedWithoutGrant()
    {
        var store = CreateStore();

        Assert.Equal(PlaybackDecision.Unrestricted, store.Evaluate(Guid.NewGuid(), Guid.NewGuid()).Decision);
    }

    [Fact]
    public void Complete_WithoutRelease_KeepsRemainingCodes()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);
        var unlocked = store.TryUnlock(batch.Codes[0], itemId, "Der Film");

        Assert.True(store.Complete(unlocked.Grant!.Id, releaseUser: false));

        var current = store.GetForUser(userId);
        Assert.NotNull(current);
        Assert.Equal(GrantStatus.Pending, current.Status);
        Assert.Equal(PlaybackDecision.Blocked, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(batch.Codes[0]).Status);
        Assert.Equal(9, store.GetAllForUser(userId).Count(grant => grant.Status == GrantStatus.Pending));
    }

    [Fact]
    public void Complete_WithRelease_RemovesAllCodesAndRestriction()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);
        var unlocked = store.TryUnlock(batch.Codes[0], itemId, "Der Film");

        Assert.True(store.Complete(unlocked.Grant!.Id, releaseUser: true));
        Assert.Null(store.GetForUser(userId));
        Assert.Empty(store.GetAllForUser(userId));
        Assert.Equal(PlaybackDecision.Unrestricted, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(batch.Codes[1]).Status);
    }

    [Fact]
    public void RemoveBatch_InvalidatesAllRemainingCodes()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        Assert.True(store.RemoveBatch(batch.BatchId));
        Assert.False(store.RemoveBatch(batch.BatchId));
        Assert.Empty(store.GetAllForUser(userId));
        Assert.Equal(PlaybackDecision.Unrestricted, store.Evaluate(userId, itemId).Decision);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(batch.Codes[0]).Status);
    }

    [Fact]
    public void Remove_RemovesSingleCodeOnly()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);
        var grants = store.GetAllForUser(userId);

        Assert.True(store.Remove(grants[0].Id));
        Assert.False(store.Remove(grants[0].Id));
        Assert.Equal(9, store.GetAllForUser(userId).Count);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(batch.Codes[0]).Status);
        Assert.Equal(UnlockStatus.Success, store.TryUnlock(batch.Codes[1], itemId, "Der Film").Status);
    }

    [Fact]
    public void ExpiredBatch_IsBlockedAndCodesAreInvalid()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 24);

        _clock.Advance(TimeSpan.FromHours(25));

        var evaluation = store.Evaluate(userId, itemId);
        Assert.Equal(PlaybackDecision.Blocked, evaluation.Decision);
        Assert.Equal(GrantStatus.Expired, evaluation.Grant!.Status);
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(batch.Codes[0]).Status);
        Assert.All(store.GetAllForUser(userId), grant => Assert.Equal(GrantStatus.Expired, grant.Status));
    }

    [Fact]
    public void ZeroValidity_NeverExpires()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var batch = store.CreateBatch(userId, "Kind", 10, 0);

        _clock.Advance(TimeSpan.FromDays(3650));

        Assert.Equal(UnlockStatus.Success, store.TryUnlock(batch.Codes[0]).Status);
    }

    [Fact]
    public void CreateBatch_ReplacesExistingBatchForUser()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        var firstBatch = store.CreateBatch(userId, "Kind", 10, 24);

        _clock.Advance(TimeSpan.FromMinutes(1));
        var secondBatch = store.CreateBatch(userId, "Kind", 10, 24);

        var grants = store.GetAllForUser(userId);
        Assert.Equal(10, grants.Count);
        Assert.All(grants, grant => Assert.Equal(secondBatch.BatchId, grant.BatchId));
        Assert.Equal(UnlockStatus.Invalid, store.TryUnlock(firstBatch.Codes[0]).Status);
        Assert.Equal(UnlockStatus.Success, store.TryUnlock(secondBatch.Codes[0]).Status);
    }

    [Fact]
    public void CreateBatch_GeneratesDistinctCodesAcrossUsers()
    {
        var store = CreateStore();
        var firstBatch = store.CreateBatch(Guid.NewGuid(), "Kind Eins", 10, 24);
        var secondBatch = store.CreateBatch(Guid.NewGuid(), "Kind Zwei", 10, 24);

        Assert.Empty(firstBatch.Codes.Intersect(secondBatch.Codes, StringComparer.Ordinal));
        Assert.Equal(20, store.GetAll().Count);
    }

    [Fact]
    public void Grants_AreReloadedFromDisk()
    {
        var userId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var firstStore = CreateStore();
        var batch = firstStore.CreateBatch(userId, "Kind", 10, 24);

        var secondStore = CreateStore();
        var result = secondStore.TryUnlock(batch.Codes[0], itemId, "Der Film");

        Assert.Equal(UnlockStatus.Success, result.Status);
        Assert.Equal(userId, result.Grant!.UserId);
        Assert.Equal(itemId, result.Grant.ItemId);
        Assert.Equal(10, secondStore.GetAllForUser(userId).Count);
    }

    [Fact]
    public void GetAllForUser_ReturnsCodesInBatchOrder()
    {
        var store = CreateStore();
        var userId = Guid.NewGuid();
        store.CreateBatch(userId, "Kind", 10, 24);

        var grants = store.GetAllForUser(userId);

        Assert.Equal(Enumerable.Range(1, 10), grants.Select(grant => grant.BatchIndex));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }

    private GrantStore CreateStore()
    {
        var lookupKeyProvider = new CodeLookupKeyProvider(
            _keyPath,
            NullLogger<CodeLookupKeyProvider>.Instance);
        return new GrantStore(
            _filePath,
            lookupKeyProvider,
            _clock,
            NullLogger<GrantStore>.Instance);
    }
}
