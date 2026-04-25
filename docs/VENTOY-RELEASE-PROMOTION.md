# Ventoy Release Promotion

## Promotion Path

Release flow:

`dev -> candidate -> stable`

## `dev`

Intended for:

- local development
- structure changes
- early validation

Required conditions:

- manifest is valid
- offline verification passes
- release build completes

Allowed state:

- missing managed checksum coverage only warns
- online verification is optional
- human review is informal

Expected artifacts:

- release folder if built
- `CHECKSUMS.sha256`
- `SIGNATURE.txt`

## `candidate`

Intended for:

- controlled review
- pre-release distribution
- field validation with traceable artifacts

Required conditions:

- `releaseType` is `candidate`
- `managedChecksumPolicy` is `require-for-release`
- offline verification passes
- managed download revalidation passes
- managed-download summary reviewed
- no unresolved issues remain in maintenance ranks `1-7`
- release build passes
- built bundle verification passes
- every manifest-managed `file` item has checksum coverage

Recommended conditions:

- `-Online` verification reviewed for warnings
- hosted CI run passes on the commit being promoted

Expected artifacts:

- release folder under `release/ventoy-core/<version>/`
- `VERSION.txt`
- `RELEASE-BUNDLE.txt`
- `CHECKSUMS.sha256`
- `SIGNATURE.txt`

Approval:

- current project maintainer/owner reviews and approves promotion to
  `candidate`

## `stable`

Intended for:

- real distribution
- operator handoff
- reproducible release publication

Required conditions:

- all `candidate` conditions
- hosted CI pass on the intended release commit
- release artifacts reviewed by a human operator
- no unresolved blocking warnings that would make the bundle misleading

Expected artifacts:

- the full candidate artifact set
- retained hosted CI evidence for the released commit

Approval:

- current project owner/maintainer explicitly approves promotion to `stable`

## Promotion Rules

Do not promote when:

- managed checksum coverage is incomplete for a `candidate` or `stable`
- the bundle fails offline verification
- managed download revalidation fails
- the top fragility slice (maintenance ranks `1-7`) has unresolved drift,
  checksum ambiguity, or provenance ambiguity
- the release build fails
- the supported vs unmanaged boundary would be misleading to an operator

Do promote only when:

- the manifest metadata matches the intended class
- the built bundle is reproducible from source
- the release artifacts communicate their scope and integrity clearly
