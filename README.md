# QR Master

Lightweight, Fluent-styled QR code generator and reader for Windows, built with Avalonia (.NET 10).

- **Generate**: live QR preview; copy to clipboard; export PNG / JPEG / WebP / BMP / SVG; colors, quiet zone, error correction, size.
- **Read**: open an image, paste from clipboard, or drag & drop; shows decoded value(s), copy / open link.
- Dark / Light / System theme, English (default) + Turkish.
- Update channels **prod / test / dev** with in-app updates from GitHub Releases (SHA-256 verified).

## Setup
1. Create a GitHub repo and push this folder to it.
2. (Local builds only) set `GitHubRepository` in `src/QrMaster/QrMaster.csproj` to `owner/repo`. CI overrides it automatically.

## Releasing (tag decides the channel)
| Tag | Channel | GitHub release |
|---|---|---|
| `v1.2.0` | prod | stable |
| `v1.3.0-test.1` | test | pre-release |
| `v1.3.0-dev.4` | dev | pre-release |

`git tag v1.0.0 && git push --tags` runs `.github/workflows/release.yml`, which tests, publishes trimmed x64/arm64 builds,
builds Inno Setup installers (`QrMaster-Setup-<ver>-win-<arch>.exe`), portable zips and `.sha256` files, and creates the release.
A channel also receives more-stable releases (dev ⊃ test ⊃ prod).

## Develop
```
dotnet run --project src/QrMaster
dotnet test
dotnet publish src/QrMaster -c Release -r win-x64 -p:PublishTrimmed=true
```
