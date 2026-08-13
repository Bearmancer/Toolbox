# draft: sacdprobe-editorconfig

intent: clear
review_required: false
status: plan-delivered
plan_path: .omo/plans/sacdprobe-editorconfig.md

## evidence gathered

### exploration findings
- SacdProbe: 5 files in tools/SacdProbe/, diagnostic harness for saracon wxWidgets bug
- Audio: 15 files in src/Services/Audio/, production SACD→FLAC pipeline
- SacdProbe has hardcoded paths, test fixtures, references Audio services
- Audio already has SacdExtractService for production use

### editorconfig defects
- Line 53: `csharp_style_implicit_object_creation_when_type_is_apparent = error` (malformed, missing `true:`)
- Line 55: `dotnet_style_prefer_collection_expression = error` (malformed, missing `true:`)
- IDE0130: error at line 136, none at lines 166-167 and 172-173 (duplicate, last wins → none)
- Lines 49, 104, 106: no severity suffix → silent at build

### librarian research
- .NET 9+ supports inline `option:severity` syntax at build
- Diagnostic tools should stay separate when they have test fixtures/hardcoded paths
- User override accepted: merge SacdProbe into Audio despite best-practice recommendation
- Oracle will justify decision

## decisions adopted
- Fix all 4 editorconfig defects
- Merge SacdProbe into Audio as SacdProbeService + SacdProbeRunner
- Move RealDffFixture into Audio
- Delete DffFixtureFactory (dead code)
- Delete tools/SacdProbe/ directory
- Update DI registration in AudioSetup.cs
- Oracle explains final structure and justifies decision

## status: plan-delivered
