# Dodgeball Academia Arabic Localization

Arabic localization project for **Dodgeball Academia**, maintained by **v7dt / axxx503**. Current version: **0.6 TEST / BETA**.

This is the source bundle for GitHub. Players should use the separate all-in-one installer. The original game is required. Select English in-game after installation.

## Contents
- `translations/`: the 16 authored Arabic JSON files.
- `runtime/`: Arabic joining, ICU bidirectional layout, native source projection and transactional glyph placement, plus an offline native-field regression harness.
- `Installer.cs`: installer, original-file verification, backup and restoration implementation.
- Root Python tools: inspect original assets, extract localization tables locally, merge source-bound Arabic translations, build and validate local text resources, and render unchanged supplied TTF outlines into engine assets.
- `translation-source-bindings.json` and `SUPPORTED-ORIGINALS.json`: source and version fingerprints, without the original English text catalog.
- `font-provided/`: chosen-font metadata and license notices; acquire the exact original TTFs separately.

## Build and verification
See `BUILDING.md`. This is project source, not a dependency-complete, one-command reproducible release. Original game inputs, matching BepInEx/.NET components and locally generated game interops must be obtained separately. No automatic download or game launch is performed by the included scripts.

## Current coverage and limitations
The current package contains 5,189 authored table records, including 91 deliberately unchanged records. 142 control/reference-only entries are preserved. Some script-embedded dialogue (25 occurrences / 17 unique strings) and 89 native credit-role labels are not integrated yet. Do not advertise 100% completion. The 0.6 dialogue fix passed 24 offline fixtures; that is not a full playthrough or visual proof. `RELEASE-CHECKLIST.md` records the remaining publication review.

## Fonts and components
UKIJ Qolyazma Tuz handles light text; Ario Dots 1 handles heavier labels. Original TTFs remain unchanged. [Ario upstream](https://github.com/MohamadDarvishi/Ario). Runtime integration uses BepInEx, UnityDoorstop, .NET, Windows system ICU and xdelta3. Third-party license/source audit is still pending; original game assets and dependency binaries are not included here.

## Support
This localization is free. Optional support for future Arabic localization projects: [Buy Me a Coffee](https://buymeacoffee.com/axxx503).

## Reporting issues
Include the mod version, scene, speaker and a screenshot. Do not upload original game assets or save files. Read `RIGHTS.md` before redistributing project files.
