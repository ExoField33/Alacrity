# Client Builder

`BuildAlacrityClient.bat` invokes the repository-owned `Alacrity.ClientBuilder` to turn a clone inside a vanilla Terraria 1.4.5.6 installation into an Alacrity client.

## Layout

Clone the repository directly inside the folder containing the vanilla `Terraria.exe`:

```text
Terraria\
  Terraria.exe                 # vanilla; never modified
  Alacrity\                    # this repository and generated Alacrity client
    BuildAlacrityClient.bat
```

Run `Alacrity\BuildAlacrityClient.bat`. It copies the vanilla runtime dependencies into the clone, then creates the generated client there:

```text
Terraria\
  Terraria.exe                 # untouched
  Alacrity\                    # source clone and generated client
    Alacrity.exe               # version-locked patched copy
    Terraria.exe               # copied vanilla input retained for rebuilds
    bin\                       # staged bridge, Core, SDK, App, bootstrap, and facade assemblies
    plugins\                   # bundled package output
    data\                      # client-owned runtime data
```

The script accepts no output-directory argument; its output is always the clone folder. For an unusual local layout, set `ALACRITY_TERRARIA_DIRECTORY` to the vanilla installation before running the script. The normal clone-inside-Terraria layout needs no environment variables.

## Prerequisites

- .NET SDK 8 or later.
- Terraria 1.4.5.6 with the Microsoft XNA Framework 4.0 runtime installed.
- When XNA is not in the standard Windows GAC location, set `ALACRITY_XNA_REFERENCE_DIRECTORY` to the directory containing the `Microsoft.Xna.Framework` GAC subdirectories.

The script copies the vanilla client dependencies into the clone, builds `artifacts/runtime/` from the current sources, then asks `Alacrity.ClientBuilder` to validate the clean source, validate the staged runtime hashes and bridge ABI, patch a temporary copy, and atomically deploy only pipeline-owned outputs. It never writes to the original `Terraria.exe`. Generated runtime files remain ignored by Git, so source remains authoritative.

The patch tool verifies the audited Terraria 1.4.5.6 assembly identity and SHA-256 before it creates `Alacrity.exe`. A different game executable fails closed rather than producing a partially patched client.

## Direct builder use

The supported command surface is deliberately small:

```powershell
dotnet build src/Alacrity.TerrariaIntegration/Alacrity.RuntimeStaging.csproj -c Release `
  -p:AlacrityTerrariaAssemblyPath='C:\Terraria\Terraria.exe' `
  -p:AlacrityXnaReferenceDirectory='C:\Windows\Microsoft.NET\assembly\GAC_32'

dotnet run --project tools/Alacrity.ClientBuilder/Alacrity.ClientBuilder.csproj -c Release -- `
  generate --source 'C:\Terraria\Terraria.exe' --runtime artifacts/runtime
```

Without `--deploy`, output is safely generated under `artifacts/client`. `--deploy --output <existing-client-folder>` is explicit and only replaces files recorded in the prior client manifest. `validate` prints source acceptance; `inspect` prints source assembly identity and hash. Legacy ad-hoc patch commands are not part of the supported generation path.

See [permanent-patches.md](permanent-patches.md) for the version-locked bridge inventory and the
distinction between build-time patches and normal scoped plugin capabilities.
