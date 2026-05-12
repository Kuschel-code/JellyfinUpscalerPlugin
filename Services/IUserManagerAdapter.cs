using MediaBrowser.Controller.Entities;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.7.3.1 - Adapter over Jellyfin's IUserManager + IUserDataManager so the
    /// RestrictToUnwatchedContent check can be unit-tested without spinning up the
    /// full Jellyfin DI graph.
    /// </summary>
    public interface IUserManagerAdapter
    {
        /// <summary>
        /// True if at least one user has marked the item as played (PlayCount > 0 or
        /// Played flag set). Conservative: any single user counts as "watched".
        /// Fail-open: returns false if the lookup throws.
        /// </summary>
        bool IsAnyUserPlayed(BaseItem item);
    }
}
