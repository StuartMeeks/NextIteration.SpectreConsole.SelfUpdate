# Security policy

## Reporting a vulnerability

Report privately through GitHub's **Report a vulnerability** button under this
repository's Security tab, which opens a private advisory visible only to the
maintainers. Please do not open a public issue for a suspected vulnerability.

Include the affected package and version, what an attacker can achieve, and a
reproduction if you have one.

You can expect an acknowledgement within 7 days, an assessment within 14, and
credit in the advisory and changelog unless you ask otherwise.

## Supported versions

Only the latest released minor of each package receives security fixes. These are
pre-1.0 libraries and there are no long-term support branches.

## Scope

This library downloads a release archive, checks it against a published SHA-256
hash, expands it, and replaces the running application's files. Four things are
explicitly **not** claimed:

- **SHA-256 verification establishes integrity, not authenticity.** The expected
  hash comes from the same place as the asset — either the asset's own `sha256`
  metadata, or a `SHA256SUMS.txt` sibling on the *same* release. Whoever can
  replace the asset can replace the hash alongside it. `Sha256ChecksumVerifier`
  detects corruption and tampering in transit; it does not prove who built the
  release. There is no signature checking. Supply your own `IPackageVerifier` if
  you need provenance rather than integrity.
- **HTTPS is the authenticity boundary, and it is defeatable by configuration.**
  `HttpManifestSource` refuses non-`https` manifest and asset URLs unless
  `SelfUpdaterOptions.AllowInsecureManifestSource` is set, and setting it defeats
  the verifier outright — a hash served over plain HTTP is as MITM-able as the
  bytes it describes. Likewise `UseDefaultSha256Verifier = false` removes hash
  checking entirely. Both are documented opt-outs for tests and trusted networks;
  a report that either is "insecure when enabled" restates the documentation.
- **Archive path-traversal defence is the framework's, not this library's.**
  Extraction goes through `ZipFile.ExtractToDirectory` and
  `TarFile.ExtractToDirectoryAsync`, which reject entries resolving outside the
  destination directory. A traversal escape is a .NET issue, and should be
  reported upstream — though tell us too, so this library can guard explicitly.
- **The `gh` CLI source trusts the local `gh`.** `GhCliReleaseSource` starts the
  `gh` executable found on `PATH` and inherits whatever credentials it holds. A
  shadowed or compromised `gh` on `PATH` is outside the boundary, as is anything
  reachable by an attacker who can already write to the install directory — the
  installer runs as the invoking user and replaces files that user could replace
  anyway.

Reports demonstrating a break *within* those stated boundaries are in scope and
welcome. Reports that only restate a documented limitation are not
vulnerabilities.
