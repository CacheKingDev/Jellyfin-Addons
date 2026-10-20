using System.Collections.Concurrent;
using System.Text;
using Jellyfin.Plugin.MovieCode.Configuration;
using Jellyfin.Plugin.MovieCode.Models;
using Jellyfin.Plugin.MovieCode.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MovieCode.Services;

/// <summary>
/// Überwacht Wiedergaben und stoppt alles, was für den Benutzer nicht freigegeben ist.
/// </summary>
public sealed class PlaybackGuard : IHostedService, IDisposable
{
    private static readonly TimeSpan BlockCooldown = TimeSpan.FromSeconds(5);

    private readonly ISessionManager _sessionManager;
    private readonly GrantStore _grantStore;
    private readonly ILogger<PlaybackGuard> _logger;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastBlock = new(StringComparer.Ordinal);

    private bool _subscribed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackGuard"/> class.
    /// </summary>
    /// <param name="sessionManager">Session-Manager von Jellyfin.</param>
    /// <param name="grantStore">Freigaben.</param>
    /// <param name="logger">Logger.</param>
    public PlaybackGuard(
        ISessionManager sessionManager,
        GrantStore grantStore,
        ILogger<PlaybackGuard> logger)
    {
        _sessionManager = sessionManager;
        _grantStore = grantStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_subscribed)
        {
            return Task.CompletedTask;
        }

        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _subscribed = true;
        _logger.LogInformation("Movie Code: Wiedergabeüberwachung gestartet.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Unsubscribe();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose() => Unsubscribe();

    private static PluginConfiguration GetConfiguration()
        => MovieCodePlugin.Instance?.Configuration ?? new PluginConfiguration();

    private static IEnumerable<Guid> GetUserIds(PlaybackProgressEventArgs args)
    {
        var userIds = args.Users
            .Select(user => user.Id)
            .Where(userId => userId != Guid.Empty)
            .ToList();

        if (userIds.Count == 0 && args.Session is not null && args.Session.UserId != Guid.Empty)
        {
            userIds.Add(args.Session.UserId);
        }

        return userIds.Distinct();
    }

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs args)
    {
        try
        {
            await CheckPlaybackAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Movie Code: Fehler bei der Prüfung des Wiedergabestarts.");
        }
    }

    private async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs args)
    {
        try
        {
            await CheckPlaybackAsync(args).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Movie Code: Fehler bei der Prüfung des Wiedergabefortschritts.");
        }
    }

    private void OnPlaybackStopped(object? sender, PlaybackStopEventArgs args)
    {
        try
        {
            HandlePlaybackStopped(args);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Movie Code: Fehler beim Verarbeiten des Wiedergabeendes.");
        }
    }

    private async Task CheckPlaybackAsync(PlaybackProgressEventArgs args)
    {
        if (args.Item is null || args.Session is null)
        {
            return;
        }

        var configuration = GetConfiguration();
        if (configuration.RestrictOnlyVideo && args.Item is not Video)
        {
            return;
        }

        foreach (var userId in GetUserIds(args))
        {
            var evaluation = _grantStore.Evaluate(userId, args.Item.Id, args.Item.Name);
            if (evaluation.Decision == PlaybackDecision.Blocked)
            {
                await BlockAsync(args.Session, evaluation.Grant, configuration, args.Item.Name).ConfigureAwait(false);
                return;
            }
        }
    }

    private void HandlePlaybackStopped(PlaybackStopEventArgs args)
    {
        if (args.Item is null)
        {
            return;
        }

        var configuration = GetConfiguration();
        var runtimeTicks = args.Item.RunTimeTicks ?? args.MediaInfo?.RunTimeTicks;
        var positionTicks = args.PlaybackPositionTicks ?? 0;
        var watchedToEnd = runtimeTicks is > 0 && positionTicks >= runtimeTicks.Value * 0.95;

        if (!args.PlayedToCompletion && !watchedToEnd && !configuration.ExpireOnEarlyStop)
        {
            return;
        }

        foreach (var userId in GetUserIds(args))
        {
            var grant = _grantStore.GetForUser(userId);
            if (grant is null || grant.ItemId != args.Item.Id || grant.Status != GrantStatus.Active)
            {
                continue;
            }

            _grantStore.Complete(grant.Id, configuration.ReleaseAfterCompletion);
            _logger.LogInformation(
                "Movie Code: Freigabe für Benutzer {UserId} und Film {ItemName} wurde abgeschlossen.",
                userId,
                grant.ItemName);
        }
    }

    private async Task BlockAsync(
        SessionInfo session,
        MovieGrant? grant,
        PluginConfiguration configuration,
        string itemName)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastBlock.TryGetValue(session.Id, out var lastBlock) && now - lastBlock < BlockCooldown)
        {
            return;
        }

        _lastBlock[session.Id] = now;
        _logger.LogInformation(
            "Movie Code: Wiedergabe für Benutzer {UserId} in Session {SessionId} blockiert (Status: {Status}).",
            session.UserId,
            session.Id,
            grant?.Status.ToString() ?? "keine Freigabe");

        try
        {
            await _sessionManager.SendMessageCommand(
                session.Id,
                session.Id,
                new MessageCommand
                {
                    Header = configuration.LockedMessageHeader,
                    Text = BuildBlockedMessage(grant, configuration, itemName),
                    TimeoutMs = 15000
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Movie Code: Sperrmeldung konnte nicht gesendet werden.");
        }

        try
        {
            await _sessionManager.SendPlaystateCommand(
                session.Id,
                session.Id,
                new PlaystateRequest { Command = PlaystateCommand.Stop },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Movie Code: Wiedergabe konnte nicht gestoppt werden.");
        }
    }

    private static string BuildBlockedMessage(
        MovieGrant? grant,
        PluginConfiguration configuration,
        string itemName)
    {
        var message = new StringBuilder(configuration.LockedMessageText);

        switch (grant?.Status)
        {
            case GrantStatus.Pending:
                message.Append(" Der Code für \"").Append(itemName).Append("\" wurde noch nicht eingegeben.");
                break;
            case GrantStatus.Active:
                message.Append(" Für dich ist nur \"").Append(grant.ItemName).Append("\" freigegeben.");
                break;
            case GrantStatus.Completed:
                message.Append(" Die Freigabe für \"").Append(grant.ItemName).Append("\" wurde bereits verbraucht.");
                break;
            case GrantStatus.Expired:
                message.Append(" Die Freigabe für \"").Append(grant.ItemName).Append("\" ist abgelaufen.");
                break;
        }

        if (!string.IsNullOrWhiteSpace(configuration.UnlockPageUrl))
        {
            message.Append(" Freigabeseite: ").Append(configuration.UnlockPageUrl);
        }

        return message.ToString();
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _subscribed = false;
        _logger.LogInformation("Movie Code: Wiedergabeüberwachung gestoppt.");
    }
}
