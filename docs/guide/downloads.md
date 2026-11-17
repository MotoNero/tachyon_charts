# Downloads and how to check them

The showroom and configurator downloads of each release are **not signed**. Windows therefore may warn about an
unknown publisher when you start them the first time.

## The warning of Windows

Windows SmartScreen shows "Windows protected your PC" for programs from an unknown publisher. If you downloaded the
zip from the [Releases](https://github.com/MotoNero/tachyon_charts/releases) page of this repository and its checksum
matches (see below), choose *More info* and *Run anyway*. Building from source avoids the warning altogether:

```bash
dotnet run --project samples/Tachyon.Charts.Samples.Wpf -c Release
```

## Checking a download

Each release carries `SHA256SUMS.txt` with the SHA-256 checksum of every file of the release. Compare it with the
checksum of your download:

```powershell
Get-FileHash .\Tachyon.Charts.Showroom-Wpf-0.17.0-win-x64.zip -Algorithm SHA256
```

```bash
sha256sum -c SHA256SUMS.txt --ignore-missing
```

The NuGet packages of a release come with the same checksums.

## Privacy

The programs transfer no information to other systems unless you ask for it, such as when you download sample data
or tiles of a map server you name. The showroom shows synthetic data.
