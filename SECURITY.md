# Security Policy

## Scope

This repository is **Kronos**, a private, personal modification of
[DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
[beeradmoore](https://github.com/beeradmoore). It is not published and does not accept reports, so
there is no disclosure channel here.

For vulnerabilities in the upstream application — which is what actually runs on other people's
machines — report them through
[GitHub's private vulnerability reporting on the upstream repository](https://github.com/beeradmoore/dlss-swapper/security/advisories/new).

If you believe a site or binary is impersonating DLSS Swapper, report it to
[upstream](https://github.com/beeradmoore/dlss-swapper/issues/new?template=other_issue.yml) rather
than here.

## A note on what this tool does

DLSS Swapper downloads and swaps DLLs inside game installations. Two consequences:

- **Signature verification matters.** This app checks the Authenticode signature of DLLs before
  importing them, and has an "allow untrusted" setting. Do not weaken that check, and do not
  distribute prebuilt DLL bundles from this repository.
- **Local data lives outside the install directory.** Settings, the SQLite database, and a cache of
  imported DLLs are written under `%LOCALAPPDATA%\Kronos` (or, for portable/Debug builds, inside
  the build output). Treat that directory as sensitive: it can contain DLLs you imported.

Note that this path changed. Earlier builds used `%LOCALAPPDATA%\DLSS Swapper`, so a build from
before the rename will not read or write the same data — and the installer deliberately does not
delete the old folder, because that would remove the official app's data.

## Reporting a vulnerability

There is no public reporting channel for this repository. If you are the maintainer and find
something wrong in the local changes, treat it as a normal bug.

If the issue is in upstream DLSS Swapper rather than in the local changes, it belongs upstream via
the link at the top of this file, and doing so is the more useful outcome — the fix reaches everyone.

## Builds are unsigned

Builds produced here are not code-signed, so Windows SmartScreen will warn. That is expected and is
not by itself a vulnerability. Upstream signs its releases via SignPath; those credentials are not
available for a personal build.
