# Security Policy

## Scope

This repository is **Kronos**, an independent modification of
[DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
[beeradmoore](https://github.com/beeradmoore), released under the same GPL-3.0 licence.

It is not affiliated with or endorsed by the original maintainer or by NVIDIA. If you want the
official, signed DLSS Swapper, use [upstream](https://github.com/beeradmoore/dlss-swapper) rather
than a build of this one.

Kronos is a personal project maintained by one person. Two things follow, and both are worth
knowing before you report anything:

- **Bugs and feature requests belong [upstream](https://github.com/beeradmoore/dlss-swapper/issues)**,
  whose maintainers decide what lands in the official app. Issues are not enabled on this
  repository, so there is nowhere to file them here even if you wanted to.
- **Most of the code here is upstream's.** If the problem exists in stock DLSS Swapper, reporting it
  upstream fixes it for far more people than a fix here would.

What follows is about the parts that are specific to this fork.

## Reporting a vulnerability

**Do not open a public issue.** Issues are disabled, and a public report is public before it is
triaged.

Use **GitHub Security Advisories** on this repository: *Security* → *Advisories* → *Report a
vulnerability*. That gives a private channel between you and the maintainer. If that button is not
available to you, the Security tab will show whether private reporting is enabled.

Please include:

- What the issue does, and what an attacker or a user would have to do to trigger it.
- The version. The Settings page shows the build date and the short commit hash for this exact
  reason, and the installer filename carries the version, so a build can be identified precisely.
- Whether it needs a specific game library, a specific DLL, or a portable install.

Expect no guaranteed response time. This is one person.

## What is security-relevant about this tool

Kronos downloads and swaps DLLs inside game installations, and its updater downloads and runs an
executable. Both are worth understanding before you decide what to trust.

**DLL signature verification.** Kronos checks the Authenticode signature of a DLL before importing
it, and has an "allow untrusted" setting. Please do not weaken that check, and do not distribute
prebuilt DLL bundles from this repository. The DLLs themselves come from NVIDIA via upstream's
public manifest at `beeradmoore.github.io`; Kronos deliberately does not mirror or repoint that,
so it depends on that endpoint staying trustworthy.

**Local data holds executable code.** Settings, the SQLite database, and a cache of imported DLLs
are written under `%LOCALAPPDATA%\Kronos`, or inside the build output for portable and Debug builds.
Treat that directory as sensitive: it can contain DLLs you imported.

This path is a change from earlier builds, which used `%LOCALAPPDATA%\DLSS Swapper`. A build from
before the rename will not read or write the same data, and the installer deliberately leaves the
old folder alone rather than deleting another product's data.

**The update channel is the trust anchor.** Installed copies check this repository's Releases for a
newer version and download the installer from there. That makes the integrity of the releases the
thing worth verifying:

- Every release asset on this repository is **unsigned**. Upstream signs via SignPath; those
  credentials are not available here, so Windows SmartScreen will warn. That is expected and is not
  by itself a vulnerability.
- GitHub publishes a SHA-256 digest for each uploaded asset. Compare the installer you downloaded
  against the digest shown for that asset on the release page if you want to confirm it arrived
  intact.
- The build metadata embedded in the app (version, build date, commit hash) is injected at compile
  time and matches the tag, so a downloaded binary can be tied back to a specific commit.

**Dependency posture.** NuGet auditing is enabled across the whole package graph, transitive
included, and the resolved package graph is committed as `packages.lock.json` so a build resolves to
the versions it was verified against. The Jekyll site no longer uses the `github-pages` meta-gem,
whose pins held `rubyzip` below a published path-traversal fix; that tree is free of known
advisories as of this writing.

## Supported versions

Only the newest release is supported. Older builds are not patched, and because updates are
delivered as whole installers there is no backport path — upgrade rather than staying behind.

| Version | Supported |
| --- | --- |
| 1.49 and later | Yes |
| 1.48 and earlier | No |
