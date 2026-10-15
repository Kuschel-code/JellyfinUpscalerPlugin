using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using JellyfinUpscalerPlugin.Controllers;
using JellyfinUpscalerPlugin.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// GET /Upscaler/config: the player's settings for users who may not read Jellyfin's
    /// admin-only plugin configuration. Without it, real-time upscaling never started for
    /// anyone but an administrator (the read failed with 403).
    /// </summary>
    public class PlayerConfigEndpointTests
    {
        private static readonly PropertyInfo[] Exposed =
            typeof(UpscalerPlayerConfig).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "JellyfinUpscalerPlugin.csproj")))
            {
                dir = dir.Parent;
            }
            dir.Should().NotBeNull();
            return File.ReadAllText(Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray()));
        }

        [Fact]
        public void The_copy_carries_exactly_the_settings_the_player_script_reads()
        {
            // One too few and that setting silently takes the player's own default for every
            // non-admin; one too many and it is shown to every user for no reason.
            var js = RepoFile("Configuration", "player-integration.js");
            var read = typeof(PluginConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .Where(name => Regex.IsMatch(js, "[.'\"\\[]" + Regex.Escape(name) + "\\b"))
                .ToList();

            read.Should().NotBeEmpty();
            Exposed.Select(p => p.Name).Should().BeEquivalentTo(read);
        }

        [Theory]
        [InlineData("AiServiceUrl")]
        [InlineData("AiServiceApiToken")]
        [InlineData("WebhookUrl")]
        [InlineData("ModelDownloadUrl")]
        [InlineData("FilterLutPath")]
        [InlineData("EnabledLibraryIds")]
        public void The_copy_never_carries_a_setting_only_admins_may_see(string name)
        {
            typeof(UpscalerPlayerConfig).GetProperty(name).Should().BeNull(
                "every signed-in user can read GET /Upscaler/config");
        }

        [Fact]
        public void Each_field_is_copied_from_its_own_setting()
        {
            // Changed one setting at a time, so two fields wired to each other's setting fail
            // even when both happen to hold the same value.
            var baseline = UpscalerPlayerConfig.From(new PluginConfiguration());
            foreach (var field in Exposed)
            {
                var setting = typeof(PluginConfiguration).GetProperty(field.Name);
                setting.Should().NotBeNull($"{field.Name} must be a real setting");
                setting!.PropertyType.Should().Be(field.PropertyType, field.Name);

                var config = new PluginConfiguration();
                var current = setting.GetValue(config);
                object changed = setting.PropertyType == typeof(bool) ? !(bool)current!
                    : setting.PropertyType == typeof(int) ? (int)current! + 1
                    : setting.PropertyType == typeof(string) ? "changed-" + field.Name
                    : throw new InvalidOperationException($"no test value for {setting.PropertyType} {field.Name}");
                setting.SetValue(config, changed);
                setting.GetValue(config).Should().Be(changed, $"the test needs a value {field.Name} accepts");

                var copy = UpscalerPlayerConfig.From(config);
                foreach (var other in Exposed)
                {
                    other.GetValue(copy).Should().Be(other == field ? changed : other.GetValue(baseline),
                        $"changing {field.Name} must change {field.Name} in the copy and nothing else");
                }
            }
        }

        [Fact]
        public void Any_signed_in_user_may_read_it_but_no_anonymous_caller()
        {
            var method = typeof(UpscalerController).GetMethod(nameof(UpscalerController.GetPlayerConfig))!;
            method.GetCustomAttribute<HttpGetAttribute>()!.Template.Should().Be("config");
            method.GetCustomAttributes<AuthorizeAttribute>().Should().NotContain(a => a.Policy == "RequiresElevation",
                "the endpoint exists so that users who are not administrators can read it");
            method.GetCustomAttribute<AllowAnonymousAttribute>().Should().BeNull();
            typeof(UpscalerController).GetCustomAttribute<AuthorizeAttribute>().Should().NotBeNull(
                "the controller-wide [Authorize] is what keeps anonymous callers out");
        }

        [Fact]
        public void Without_a_loaded_configuration_it_answers_an_error_not_invented_settings()
        {
            // A copy made of defaults would start real-time upscaling that an administrator
            // turned off. Plugin.Instance is null in unit tests, as it is before the plugin loads.
            var controller = new UpscalerController(
                NullLogger<UpscalerController>.Instance,
                null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

            controller.GetPlayerConfig().Result.Should().BeOfType<BadRequestResult>();
        }
    }
}
