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
one coherent DLL set under `artifacts/runtime/`, including the bundled plugin packages under
`artifacts/runtime/plugins/`. Normal builds never modify the repository root or a Terraria
installation. The stage manifest contains SHA-256 values for every copied runtime assembly.
Bundled plugin projects likewise package to `artifacts/plugins/` by default; the staging project
copies only the packages it just built into the deployable runtime set.

The patched executable resolves `Alacrity.PluginUiRuntime.dll` from its application directory.
The version-locked patcher also receives an identical facade copy under `bin/` for metadata
import; the bridge and its Core/SDK/App dependencies are loaded from `bin/` at runtime.

```powershell
dotnet build src\Alacrity.TerrariaIntegration\Alacrity.RuntimeStaging.csproj -c Release -p:AlacrityTerrariaAssemblyPath=C:\Games\Terraria\Terraria.exe -p:AlacrityXnaReferenceDirectory=C:\Windows\Microsoft.NET\assembly\GAC_32
```

```powershell
dotnet build src\Alacrity.TerrariaIntegration\Alacrity.RuntimeStaging.csproj -c Release -p:AlacrityTerrariaAssemblyPath=C:\Games\Terraria\Terraria.exe -p:AlacrityXnaReferenceDirectory=C:\Windows\Microsoft.NET\assembly\GAC_32 -p:AlacrityRuntimeArtifactDirectory=C:\Temp\Alacrity-stage
```

Deploying into a Terraria client is deliberately a separate explicit action:

```powershell
dotnet msbuild src\Alacrity.TerrariaIntegration\Alacrity.RuntimeDeployment.csproj -t:DeployAlacrityRuntime -p:AlacrityRuntimeDeployDirectory=C:\Games\Alacrity
```

`Directory.Build.targets` validates every required external reference before resolving assemblies and reports whether a required property was not configured or its configured path is missing.

For a complete, separate client copy, use the repository-root `BuildAlacrityClient.bat` helper described in [client-builder.md](client-builder.md). It copies vanilla Terraria into a sibling output directory, stages the managed runtime, creates `plugins` and `data`, and invokes the repository-owned version-locked patch tool to create `Alacrity.exe` without modifying the original executable.
