# Security Policy

## Scope

This repository is an **unofficial fork** of
[DLSS Swapper](https://github.com/beeradmoore/dlss-swapper). Only security issues in this fork's own
local changes are handled here.

For vulnerabilities in the upstream application, report them through
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
  imported DLLs are written under `%LOCALAPPDATA%\DLSS Swapper` (or, for portable/Debug builds,
  inside the build output). Treat that directory as sensitive: it can contain DLLs you imported.

## Reporting a vulnerability

Please report security issues privately rather than opening a public issue. Use GitHub's
[private vulnerability reporting](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
on this repository if it is enabled; otherwise open an issue describing the problem in general terms
without working exploit details and ask for a private channel.

Please include:

- What the issue is and what an attacker gains.
- Steps to reproduce, or the relevant file and line.
- The version or commit you tested.

## Builds from this fork are unsigned

Releases here are not code-signed, so Windows SmartScreen will warn. That is expected for this fork
and is not by itself a vulnerability.
