# Terraria Build References

Alacrity does not commit Terraria or XNA binaries. The two net472 integration projects use repository-level MSBuild properties so a local build can point at a verified installation without editing project files. Vanilla Terraria embeds its managed `ReLogic.dll` inside `Terraria.exe`; the bridge uses reflection for the few optional ReLogic APIs and does not reference or deploy a separate managed ReLogic assembly.

Provide every external reference through a local `Directory.Build.props.user` import or command-line properties. The repository deliberately has no fallback path, including to the Windows GAC, so an integration build cannot silently target another installation. `Directory.Build.props.user` is intentionally ignored by source control:

```xml
<Project>
  <PropertyGroup>
    <AlacrityTerrariaAssemblyPath>C:\Games\Terraria\Terraria.exe</AlacrityTerrariaAssemblyPath>
    <AlacrityXnaReferenceDirectory>C:\Windows\Microsoft.NET\assembly\GAC_32</AlacrityXnaReferenceDirectory>
  </PropertyGroup>
</Project>
```

Build the managed runtime through the staging project. It rebuilds the core bridge as
`Alacrity.PluginUiCoreBridge.dll`, the injected facade, and the bootstrap runtime, then stages
the precise DLL set the client loads. The default destination is the client directory; use
`AlacrityRuntimeStageDirectory` for a disposable validation destination.

```powershell
dotnet build src\Alacrity.TerrariaIntegration\Alacrity.RuntimeStaging.csproj -c Release -p:AlacrityTerrariaAssemblyPath=C:\Games\Terraria\Terraria.exe -p:AlacrityXnaReferenceDirectory=C:\Windows\Microsoft.NET\assembly\GAC_32
```

```powershell
dotnet build src\Alacrity.TerrariaIntegration\Alacrity.RuntimeStaging.csproj -c Release -p:AlacrityTerrariaAssemblyPath=C:\Games\Terraria\Terraria.exe -p:AlacrityXnaReferenceDirectory=C:\Windows\Microsoft.NET\assembly\GAC_32 -p:AlacrityRuntimeStageDirectory=C:\Temp\Alacrity-stage
```

`Directory.Build.targets` validates every required external reference before resolving assemblies and reports whether a required property was not configured or its configured path is missing.
