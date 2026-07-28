# Runtime And Target Framework

## Terraria runtime boundary

Terraria 1.4.5.6 runs on the legacy .NET Framework/XNA-compatible runtime used by the shipped executable. The injected UI runtime and the Terraria integration bridge therefore target `net472`, reference the exact supported `Terraria.exe`, and keep Terraria-specific reflection at that boundary.

## Why SDK, Core, and App target netstandard2.0

`Alacrity.PluginSdk`, `Alacrity.Core`, and `Alacrity.App` target `netstandard2.0` so the public plugin contracts and host logic can be consumed by the `net472` Terraria bridge while also remaining testable from the `net8.0` foundation test executable. This is the widest compatible contract surface for the current runtime arrangement.

## Constraints

- `netstandard2.0` does not provide a Terraria or XNA API surface. Terraria references belong only in the integration project.
- APIs newer than the .NET Framework compatibility surface must not leak into the SDK/Core/App projects.
- Plugin packages are ordinary managed DLLs, not a security sandbox. Permissions are host policy boundaries, not process isolation.

## Changing targets

Do not change these targets until all of the following are proven: the target Terraria runtime supports the proposed framework, the injected bridge and its dependencies load successfully in a clean installation, plugin package compatibility has a documented migration path, and the full foundation plus game integration validation suite passes for the exact Terraria executable hash.
