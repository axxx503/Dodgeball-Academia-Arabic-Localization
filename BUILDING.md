# Building from the project source

Use Windows 64-bit. Obtain a C# compiler, .NET 6 reference/runtime assemblies and matching BepInEx IL2CPP core assemblies. Game interops must be generated locally from your own supported game. They are not in this archive. Font metadata records the required exact TTF fingerprints.

## Runtime and regression tests
`Build-Runtime.ps1` takes explicit compiler and assembly directories. It compiles the real runtime source or the offline native-field test harness. It does not launch or install the game.

    .\Build-Runtime.ps1 -CompilerPath <csc.exe> -NetAssemblies <dotnet6-assemblies-directory> -BepInExCore <core-directory> -GameInterop <interop-directory>
    .\Build-Runtime.ps1 -CompilerPath <csc.exe> -NetAssemblies <dotnet6-assemblies-directory> -Tests
    dotnet .\build\NativeTransactionHarness.dll .\build\native-test-report.json

## Translation resources
Install `requirements.txt`. Set `ACADEMIA_ORIGINAL_GAME` to an unmodified game directory. Do not point this at a patched installation. Original resource hashes are checked. Run, in order: `inspect_assets.py`, `extract_localization.py`, `merge_translations.py`, then `build_text_prototype.py`. The regenerated English catalog and complete resources are local build products; do not commit or redistribute them.

Obtain the two exact TTFs listed in `font-provided/selected-fonts.json`; place them in that directory. Set `ACADEMIA_REVISION=friendly` for the atlas builder, then run `build_provided_font_atlas.py` and the font verification tools. This renders existing outlines into engine assets; it does not create or modify a font. Local output files are ignored by Git.

## Installer source
`Build-Installer.ps1` rebuilds the installer using its source and the embedded manifest/payload from the matching Nexus TEST06 setup. It reads assembly resources without executing the setup. This supplies the existing tested patches and dependencies; it does not regenerate them or automatically incorporate runtime/translation changes. Creating a new payload/version after modifying project files requires separate delta, manifest, license and original-version validation. Never distribute complete original or patched game assets.

    .\Build-Installer.ps1 -CompilerPath <csc.exe> -FrameworkDirectory <Framework64-v4-directory> -ExistingSetup <TEST06-setup.exe>

Build products are written under `build/`. The release source tree intentionally omits personal workspace paths, private build reports, native disassemblies, original game text dumps, dependency binaries and full game files. Review the publication checklist before making a public release.
