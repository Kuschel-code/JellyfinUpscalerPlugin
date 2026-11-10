namespace JellyfinUpscalerPlugin.Models
{
    /// <summary>
    /// The settings player-integration.js reads, served to any signed-in user by
    /// GET /Upscaler/config.
    /// </summary>
    /// <remarks>
    /// Jellyfin's own /Plugins/{id}/Configuration is admin-only, so a player that read it
    /// never started real-time upscaling for anyone else (403). This copy carries exactly
    /// the fields the player script reads - never the AI service URL, its token or the
    /// webhooks - and PlayerConfigEndpointTests keeps the two in step. It is read-only:
    /// Jellyfin replaces the whole configuration with whatever is posted, so a change
    /// must start from the full configuration, never from this copy.
    /// </remarks>
    public sealed class UpscalerPlayerConfig
    {
        public bool EnablePlugin { get; init; }
        public bool EnableRealtimeUpscaling { get; init; }
        public string RealtimeMode { get; init; } = string.Empty;
        public int RealtimeCaptureWidth { get; init; }
        public bool ClientDriverUpscalingActive { get; init; }
        public string Model { get; init; } = string.Empty;
        public string FavoriteModels { get; init; } = string.Empty;
        public int ScaleFactor { get; init; }
        public bool EnableAutoModelSelection { get; init; }
        public bool EnableVideoFilters { get; init; }
        public string ActiveFilterPreset { get; init; } = string.Empty;
        public bool EnableFaceRestore { get; init; }
        public string FaceRestoreModel { get; init; } = string.Empty;
        public bool EnableObjectMasking { get; init; }
        public string ObjectMaskMode { get; init; } = string.Empty;
        public string ObjectMaskClasses { get; init; } = string.Empty;
        public string ButtonPosition { get; init; } = string.Empty;

        public static UpscalerPlayerConfig From(PluginConfiguration config) => new()
        {
            EnablePlugin = config.EnablePlugin,
            EnableRealtimeUpscaling = config.EnableRealtimeUpscaling,
            RealtimeMode = config.RealtimeMode,
            RealtimeCaptureWidth = config.RealtimeCaptureWidth,
            ClientDriverUpscalingActive = config.ClientDriverUpscalingActive,
            Model = config.Model,
            FavoriteModels = config.FavoriteModels,
            ScaleFactor = config.ScaleFactor,
            EnableAutoModelSelection = config.EnableAutoModelSelection,
            EnableVideoFilters = config.EnableVideoFilters,
            ActiveFilterPreset = config.ActiveFilterPreset,
            EnableFaceRestore = config.EnableFaceRestore,
            FaceRestoreModel = config.FaceRestoreModel,
            EnableObjectMasking = config.EnableObjectMasking,
            ObjectMaskMode = config.ObjectMaskMode,
            ObjectMaskClasses = config.ObjectMaskClasses,
            ButtonPosition = config.ButtonPosition,
        };
    }
}
