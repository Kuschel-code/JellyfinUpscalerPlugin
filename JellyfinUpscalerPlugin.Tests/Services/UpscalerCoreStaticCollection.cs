using Xunit;

namespace JellyfinUpscalerPlugin.Tests.Services
{
    /// <summary>
    /// v1.8.3.14 — <c>UpscalerCore.HardwareTier</c> is static: the resolver is synchronous
    /// and must never make a network call, so the tier is cached process-wide. xUnit runs
    /// test classes in parallel by default, which would let one class change the tier while
    /// another is mid-assertion. Every class that reads or writes that cache joins this
    /// collection, which xUnit runs serially.
    /// </summary>
    [CollectionDefinition(Name)]
    public class UpscalerCoreStaticCollection
    {
        public const string Name = "UpscalerCore static state";
    }
}
