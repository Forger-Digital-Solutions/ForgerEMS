# GitHub Actions Immutable-SHA Pinning Recommendation

The current workflows intentionally remain unchanged in this preview-readiness pass because immutable release-to-commit verification was not performed for every action in the active workflows. Do not replace a tag with a guessed SHA.

Actions requiring verified pinning before a future hardening commit:

- `actions/checkout@v4` in `build.yml`, `release.yml`, and `kyra-sdk-package-mode.yml`
- `actions/setup-dotnet@v4` in `build.yml`, `release.yml`, and `kyra-sdk-package-mode.yml`
- `actions/upload-artifact@v4` in `build.yml` and `kyra-sdk-package-mode.yml`
- `softprops/action-gh-release@v2` in `release.yml`

For each action, an owner should resolve the desired official release tag from its official GitHub repository, verify that the selected commit SHA is the tag target, replace the reference with the full 40-character SHA, and retain a comment such as `# actions/checkout@v4.2.2`. Run the workflow YAML and repository validation checks afterward. Keep an update mechanism (for example Dependabot) or document the manual review process before pinning.

This is supply-chain hardening guidance; it does not change the public-preview status or represent a claim of certification.
