# Release review

The bundles are organized for GitHub and Nexus Mods. They have not been uploaded.

- [x] Current runtime and authored translation source included in the GitHub bundle.
- [x] Nexus installer is byte-identical to TEST06, which passed 17 installer cases, Unicode-path restoration and five version-transition cases.
- [x] Dialogue-control fix passed 24 offline cases; source maps, styling and typewriter indices are retained.
- [x] Both selected fonts are unchanged; notices are included.
- [x] Full game assets, executable/metadata dumps, user saves, game-generated interops, internal progress and local logs are excluded from the GitHub bundle.
- [ ] Exact-version audit of every bundled runtime dependency, license and corresponding-source requirement, including the .NET 6.0.7 bundle and UKIJ source notice. The existing test notices explicitly record this unfinished audit.
- [ ] Confirm TEST06 dialogue display and full in-game fit. Human screenshots show the previous TEST05 problem, not a visual pass of this revision.
- [ ] Integrate 25 inline-script occurrences (17 unique) and 89 native credit role strings if advertising complete coverage.

Do not describe this as 100% complete or a fully verified public release. Keep the beta status and known limitations visible. Source code availability alone does not license third-party assets or establish all redistribution obligations.
