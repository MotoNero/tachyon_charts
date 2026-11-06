# Code signing policy

The showroom apps of each release are signed, so that Windows names their publisher instead of warning about an unknown one.

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org).

## What is signed

- `Tachyon.Charts.Samples.Wpf.exe` and `Tachyon.Charts.Samples.Avalonia.exe`, and the libraries of this project (`Tachyon.Charts*.dll`) inside the showroom downloads `Tachyon.Charts.Showroom-*.zip`.
- Third-party libraries are not signed by this project; they come as their makers ship them.
- Only binaries built from the source of this repository are signed. A workflow on a GitHub-hosted runner builds them from the tag of the release (`.github/workflows/sign-release.yml`); a maintainer approves each signing request.

## Team roles

- Committers and reviewers: [maintainers of this repository](https://github.com/MotoNero/tachyon_charts/graphs/contributors)
- Approvers: [owner of this repository](https://github.com/MotoNero)

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. The showroom shows synthetic data.

## A note on Windows SmartScreen

A signature tells Windows who published a program. For a new program Windows may still show a warning until the program has been downloaded often enough; this no longer depends on the kind of certificate. Choose *More info* and *Run anyway*, or check the publisher shown there.
