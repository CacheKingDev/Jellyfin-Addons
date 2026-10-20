namespace Jellyfin.Plugin.MovieCode.Models;

/// <summary>
/// Zustand einer Filmfreigabe.
/// </summary>
public enum GrantStatus
{
    /// <summary>
    /// Der Code wurde erzeugt, aber noch nicht eingegeben.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Der Code wurde eingegeben; der Film darf jetzt gestartet werden.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Der Film wurde zu Ende geschaut; die Freigabe ist verbraucht.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Die Gültigkeitsdauer des Codes ist abgelaufen.
    /// </summary>
    Expired = 3
}
