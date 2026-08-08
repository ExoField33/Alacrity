# Client Builder

`BuildAlacrityClient.bat` creates a self-contained Alacrity client from a vanilla Terraria 1.4.5.6 installation. It is deliberately a temporary developer-facing builder until the dedicated client builder replaces it.

## Layout

Clone the repository directly inside the folder containing the vanilla `Terraria.exe`:

```text
Terraria\
  Terraria.exe                 # vanilla; never modified
  Alacrity\                    # this repository
    BuildAlacrityClient.bat
```

Run `Alacrity\BuildAlacrityClient.bat`. The default result is a sibling directory:

```text
Terraria\
  Terraria.exe                 # untouched
  Alacrity\                    # source clone
  AlacrityClient\
    Alacrity.exe               # version-locked patched copy
    Terraria.exe               # copied vanilla input retained for rebuilds
    bin\                       # staged bridge, Core, SDK, App, bootstrap, and facade assemblies
    plugins\                   # bundled package output
    data\                      # client-owned runtime data
```

Pass an output directory as the first argument to choose a different client location. It must not be the vanilla Terraria folder or the source clone.

For an unusual local layout, set `ALACRITY_TERRARIA_DIRECTORY` to the vanilla installation before running the script. The normal clone-inside-Terraria layout needs no environment variables.

## Prerequisites

- .NET SDK 8 or later.
- Terraria 1.4.5.6 with the Microsoft XNA Framework 4.0 runtime installed.
- When XNA is not in the standard Windows GAC location, set `ALACRITY_XNA_REFERENCE_DIRECTORY` to the directory containing the `Microsoft.Xna.Framework` GAC subdirectories.

The script copies the vanilla client, builds `artifacts/runtime/` from the current sources, deploys that coherent assembly set and its `VERSION` metadata to the copied client, creates `plugins` and `data`, then runs the repository-owned version-locked patcher. It never writes to the original `Terraria.exe`, repository root, or a live client installation during an ordinary managed build.

The patch tool verifies the audited Terraria 1.4.5.6 assembly identity and SHA-256 before it creates `Alacrity.exe`. A different game executable fails closed rather than producing a partially patched client.
