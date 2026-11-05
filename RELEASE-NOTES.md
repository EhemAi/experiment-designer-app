# Experiment Designer v1.1.5

This update adds sex composition controls and corrects RCBD BW grouping so pens
receive comparable ranked weight distributions while treatment pen means are
balanced within each replicate. Heaviest-first selection follows the specified
sex counts, and balancing never draws light EX animals into the selected group.

- Ignore, Balance or Separate sexes; explicit pen counts and two replicate groups.
- Automatic 0.5% block CV target with range comparison and visible feedback when unattainable.
- Existing male/female replicate order and prepared treatment locations retained.
- Unit-neutral BW: new imports preserve entered numbers; old values are unchanged.
- Concise Excel exports, numeric old locations and replicate/cage BW statistics.
- Earlier saved allocations retain their exact results and grouping rule.

## Windows download

Download **Experiment-Designer-1.1.5-win-x64.zip**, verify its SHA-256 checksum,
and extract the complete ZIP. Open `Setup/ExperimentDesigner.Setup.exe` to
install/update, or `App/ExperimentDesigner.exe` for portable use. No separate
.NET installation is required. Do not download GitHub's source ZIP expecting
a runnable app. The Windows binaries are unsigned.

Generate a new allocation to use the corrected RCBD method. Available weights,
fixed cage size and sex counts still determine achievable pen uniformity.
The treatment ANOVA target p > 0.6 remains separate from the block CV target.

## Verification

See [V1.1.5-RELEASE-AUDIT.md](../V1.1.5-RELEASE-AUDIT.md) for the release checks,
security results, packaging verification and practical limitations.
