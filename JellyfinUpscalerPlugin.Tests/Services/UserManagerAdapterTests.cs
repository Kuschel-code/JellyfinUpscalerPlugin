using System;
using System.Collections.Generic;
using FluentAssertions;
using JellyfinUpscalerPlugin.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.7.3.1 - regression guards for IsAnyUserPlayed fail-open semantics
    /// (introduced in v1.6.1.21, extracted to UserManagerAdapter in v1.7.3.1).
    /// </summary>
    /// <remarks>
    /// Scope-limited to behaviors that don't need to construct real Jellyfin.Data.Entities.User
    /// instances (that would need a separate package ref). The fail-open regression-guard - the
    /// most critical contract - is fully covered. PlayCount/Played-flag scenarios are covered
    /// indirectly via integration with LibraryUpscaleScanTask, which v1.7.4 will test directly
    /// once Jellyfin.Data is referenced.
    /// </remarks>
    public class UserManagerAdapterTests
    {
        private readonly Mock<IUserManager> _userManagerMock;
        private readonly Mock<IUserDataManager> _userDataManagerMock;
        private readonly UserManagerAdapter _adapter;

        public UserManagerAdapterTests()
        {
            _userManagerMock = new Mock<IUserManager>();
            _userDataManagerMock = new Mock<IUserDataManager>();
            _adapter = new UserManagerAdapter(
                _userManagerMock.Object,
                _userDataManagerMock.Object,
                NullLogger<UserManagerAdapter>.Instance);
        }

        [Fact]
        public void IsAnyUserPlayed_ReturnsFalse_WhenItemIsNull()
        {
            // Defensive: null item must not throw, must return false.
            _adapter.IsAnyUserPlayed(null!).Should().BeFalse();
        }

        [Fact]
        public void IsAnyUserPlayed_ReturnsFalse_WhenUsersEnumerableThrows()
        {
            // THE regression-guard: fail-open. If Jellyfin's user manager throws
            // (transient DB issue), IsAnyUserPlayed must return false (treat as
            // unwatched) - the scheduled scan must NOT fail-closed-and-block.
            //
            // If this test fails, the contract has shifted from fail-open to
            // fail-closed: a Jellyfin DB hiccup would stop the scan from making
            // progress, which was the exact bug RestrictToUnwatchedContent's
            // wiring in v1.6.1.21 was designed to avoid.
            _userManagerMock.Setup(m => m.Users)
                .Throws(new InvalidOperationException("simulated user manager failure"));

            var item = MakeItem();

            var act = () => _adapter.IsAnyUserPlayed(item);
            act.Should().NotThrow("the adapter must swallow exceptions and return false");
            _adapter.IsAnyUserPlayed(item).Should().BeFalse(
                "fail-open: exception during user enumeration must return false");
        }

        // BaseItem instances - use Movie which is constructable without DI.
        private static BaseItem MakeItem()
        {
            return new Movie { Path = "/media/test.mkv", Name = "Test Item" };
        }
    }
}
