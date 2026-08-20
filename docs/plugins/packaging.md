# Packaging

A package lives in `Plugins/<plugin-id>/` and contains `plugin.json` plus the entry DLL. The manifest is authoritative and is parsed before the DLL is loaded. Plugin build targets copy their DLL, manifest, and declared package assets together. Package data is isolated under `data/plugins/<plugin-id>/` and is not mixed with the installation directory.

Plugins that use `context.Network` must declare the `Networking` capability, `NetworkAccess`
permission, and exact bare DNS names in `networkHosts`. The runtime allows only HTTPS requests to
those hosts. API keys and other credentials belong in the local settings store or an OS secret
provider, never in `plugin.json` or the package itself.

Terraria-facing projects intentionally keep Terraria and XNA references external. Build them with
the verified local client paths, for example:

```powershell
dotnet build src/Alacrity.TerrariaIntegration/Alacrity.TerrariaIntegration.csproj -c Release /warnaserror `
  -p:AlacrityTerrariaAssemblyPath="C:\\path\\to\\Terraria.exe" `
  -p:AlacrityXnaReferenceDirectory="C:\\Windows\\Microsoft.NET\\assembly\\GAC_32"
```

The shared `Directory.Build.targets` validation fails before compilation when either path or a
required XNA assembly is absent. Vanilla Terraria's managed ReLogic implementation is embedded in
`Terraria.exe`; this repository deliberately does not require a separate `ReLogic.dll`.
