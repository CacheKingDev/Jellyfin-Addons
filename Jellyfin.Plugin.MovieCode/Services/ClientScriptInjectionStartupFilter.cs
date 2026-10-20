using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MovieCode.Services;

/// <summary>
/// Bindet die Web-Client-Skripte zur Laufzeit in die index.html des Jellyfin-Web-Clients ein.
/// Jellyfin 12 bietet dafür keinen offiziellen Plugin-Hook; die Middleware läuft daher
/// vor der statischen Dateiauslieferung und verändert nur die ausgelieferte Antwort.
/// </summary>
public sealed class ClientScriptInjectionStartupFilter : IStartupFilter
{
    private const string ClientScriptPath = "/MovieCode/client.js";
    private const string AdminScriptPath = "/MovieCode/admin.js";

    private readonly ILogger<ClientScriptInjectionStartupFilter> _logger;
    private int _logged;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientScriptInjectionStartupFilter"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ClientScriptInjectionStartupFilter(ILogger<ClientScriptInjectionStartupFilter> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(InvokeAsync);
            next(app);
        };
    }

    private async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!IsIndexRequest(context.Request.Path.Value)
            || !HttpMethods.IsGet(context.Request.Method))
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Unkomprimiert und vollständig ausliefern, damit die HTML-Antwort
        // verändert werden kann.
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next().ConfigureAwait(false);
        }
        catch
        {
            context.Response.Body = originalBody;
            throw;
        }

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);

        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);

        if (!isHtml)
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        try
        {
            html = InjectScripts(context, html);
        }
        catch (Exception ex)
        {
            // Niemals die index.html beschädigen: im Fehlerfall Original ausliefern.
            _logger.LogWarning(ex, "Movie Code: Popup-Skript konnte nicht eingebunden werden.");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html;charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");
        await originalBody.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
    }

    private string InjectScripts(HttpContext context, string html)
    {
        var pathBase = context.Request.PathBase.HasValue ? context.Request.PathBase.Value : string.Empty;
        var version = typeof(MovieCodePlugin).Assembly.GetName().Version?.ToString() ?? "1";
        var tags = new StringBuilder();

        var popupEnabled = MovieCodePlugin.Instance?.Configuration.EnableWebPopup ?? false;
        if (popupEnabled && html.IndexOf(ClientScriptPath, StringComparison.OrdinalIgnoreCase) < 0)
        {
            tags.Append($"<script src=\"{pathBase}{ClientScriptPath}?v={version}\"></script>\n");
        }

        if (html.IndexOf(AdminScriptPath, StringComparison.OrdinalIgnoreCase) < 0)
        {
            tags.Append($"<script src=\"{pathBase}{AdminScriptPath}?v={version}\"></script>\n");
        }

        if (tags.Length == 0)
        {
            return html;
        }

        var bodyClose = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        html = bodyClose >= 0
            ? html.Insert(bodyClose, tags.ToString())
            : html + "\n" + tags;

        if (Interlocked.Exchange(ref _logged, 1) == 0)
        {
            _logger.LogInformation("Movie Code: Web-Client-Skripte wurden in den Web-Client eingebunden.");
        }

        return html;
    }

    private static bool IsIndexRequest(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase);
    }
}
