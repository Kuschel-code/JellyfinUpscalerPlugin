using System;
using System.Collections;
using System.Reflection;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace JellyfinUpscalerPlugin.Services
{
    /// <summary>
    /// v1.7.3.1 - Production implementation of IUserManagerAdapter that wraps Jellyfin's
    /// IUserManager + IUserDataManager. Fail-open: lookup exceptions are logged at Warning
    /// and return false (treat as unwatched) so the scheduled scan keeps progressing.
    ///
    /// v1.8.3.4 - Jellyfin 12.0 readiness: 12.0 removed the IUserManager.Users property
    /// in favour of a GetUsers() method (the only compile break against 12.0.0-rc2).
    /// The user enumeration and the per-user data lookup are resolved via reflection at
    /// runtime, so the same DLL works on 10.11.x (Users property) and 12.x (GetUsers()).
    /// A direct interface call would be JIT-bound to the 10.11 member and throw
    /// MissingMethodException on a 12.0 server before the catch below could help.
    /// </summary>
    public class UserManagerAdapter : IUserManagerAdapter
    {
        private readonly IUserManager _userManager;
        private readonly IUserDataManager _userDataManager;
        private readonly ILogger<UserManagerAdapter> _logger;

        private static readonly PropertyInfo? UsersProperty =
            typeof(IUserManager).GetProperty("Users");

        private static readonly MethodInfo? GetUsersMethod =
            typeof(IUserManager).GetMethod("GetUsers", Type.EmptyTypes);

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
                var users = EnumerateUsers();
                if (users == null) return false;

                foreach (var user in users)
                {
                    if (user == null) continue;
                    var data = GetUserDataFor(user, item);
                    if (data is null) continue;
                    if (data.Played || data.PlayCount > 0)
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

        /// <summary>
        /// All users, via whichever member this server version exposes:
        /// 10.11.x <c>Users</c> property or 12.x <c>GetUsers()</c>. Null when neither
        /// exists (fail-open: caller treats the item as unwatched).
        /// </summary>
        private IEnumerable? EnumerateUsers()
        {
            if (UsersProperty != null)
            {
                return UsersProperty.GetValue(_userManager) as IEnumerable;
            }
            if (GetUsersMethod != null)
            {
                return GetUsersMethod.Invoke(_userManager, null) as IEnumerable;
            }
            _logger.LogWarning("AI Upscaler: IUserManager exposes neither Users nor GetUsers() on this server version, treating items as unwatched");
            return null;
        }

        // Resolved once per user entity type: the reflection lookup otherwise runs
        // N_users x N_items times during a library scan. Benign race - concurrent
        // resolvers compute the same MethodInfo.
        private static MethodInfo? _getUserDataMethod;

        /// <summary>
        /// GetUserData(user, item) bound at runtime so the call survives the user
        /// entity type moving between assemblies across server versions.
        /// </summary>
        private dynamic? GetUserDataFor(object user, BaseItem item)
        {
            var method = _getUserDataMethod ??=
                typeof(IUserDataManager).GetMethod(
                    "GetUserData", new[] { user.GetType(), typeof(BaseItem) })
                ?? FindGetUserData(user);
            return method?.Invoke(_userDataManager, new[] { user, (object)item });
        }

        private static MethodInfo? FindGetUserData(object user)
        {
            foreach (var m in typeof(IUserDataManager).GetMethods())
            {
                if (m.Name != "GetUserData") continue;
                var p = m.GetParameters();
                if (p.Length == 2
                    && p[0].ParameterType.IsInstanceOfType(user)
                    && typeof(BaseItem).IsAssignableFrom(p[1].ParameterType))
                {
                    return m;
                }
            }
            return null;
        }
    }
}
