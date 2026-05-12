using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.7.3.1 - Production implementation of IUserManagerAdapter that wraps Jellyfin's
    /// IUserManager + IUserDataManager. Fail-open: lookup exceptions are logged at Warning
    /// and return false (treat as unwatched) so the scheduled scan keeps progressing.
    /// </summary>
    public class UserManagerAdapter : IUserManagerAdapter
    {
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<UserManagerAdapter> _logger;

        public UserManagerAdapter(
            IUserManager userManager,
            IUserDataManager userDataManager,
            ILogger<UserManagerAdapter> logger)
        {
            _userManager = userManager;
            _userDataManager = userDataManager;
            _logger = logger;
        }

        public bool IsAnyUserPlayed(BaseItem item)
        {
            if (item == null) return false;
            try
            {
                foreach (var user in _userManager.Users)
                {
                    var data = _userDataManager.GetUserData(user, item);
                    if (data != null && (data.Played || data.PlayCount > 0))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI Upscaler: IsAnyUserPlayed lookup failed for {Path}, treating as unwatched", item.Path);
            }
            return false;
        }
    }
}
