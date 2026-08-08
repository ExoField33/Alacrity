namespace AlacrityTerraria
{
    // The staging project has no runtime role. This marker gives the SDK compiler one source
    // input so its AfterBuild target can stage the real bridge assemblies deterministically.
    internal static class RuntimeStagingMarker
    {
    }
}
