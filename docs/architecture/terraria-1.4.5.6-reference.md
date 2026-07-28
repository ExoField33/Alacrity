# Terraria 1.4.5.6 reference baseline

The readable source under `Alacrity/Decompiled Terraria 1.4.5.6/` is a local,
Git-excluded navigation aid only. It was confirmed by the repository owner to
have been decompiled from the authoritative `Alacrity/Terraria.exe` below.

| Property | Value |
| --- | --- |
| Terraria assembly version | `1.4.5.6` |
| SHA-256 | `A89A24C6531D88A972662821044ACF1B3B5817621DD6C81D4BD7523BC4BBDDA9` |

Every executable patch must verify the assembly version and this SHA-256 before
using the decompiled source for navigation. The patcher must then locate its
target through semantic IL matching and fail when that match is missing or
ambiguous. The decompiled source itself must never be edited or compiled.
