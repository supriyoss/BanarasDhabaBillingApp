# Restaurant POS installer

The release process publishes a self-contained 64-bit Windows executable and wraps it in a standard `.msi` installer.

The installer deliberately contains only the application. The setup wizard lets the user choose the application installation directory.

The license page displays the approved software license and support agreement from `LicenseAgreement.rtf` before installation can continue.

The database, logs, and backups remain under `%LocalAppData%\RestaurantPos`, so application upgrades do not overwrite sales data. During an interactive uninstall, the user is asked whether to keep or permanently remove that local data. Unattended and upgrade uninstalls preserve it by default.

## Build an MSI manually

Run these commands from the repository root in PowerShell. Set `$releaseVersion` to the version already recorded in both `RestaurantPos.Desktop.csproj` and `RestaurantPos.wxs`.

```powershell
$releaseVersion = "3.8.3"
$repositoryRoot = (Resolve-Path ".").Path
$releaseDirectory = Join-Path $repositoryRoot "artifacts\release-v$releaseVersion"
$publishDirectory = Join-Path $releaseDirectory "publish"
$wix = Join-Path $repositoryRoot "artifacts\wix-tool\wix.exe"

dotnet test RestaurantPos.sln --configuration Release
dotnet publish src\RestaurantPos.Desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o $publishDirectory

& $wix build installer\RestaurantPos.wxs `
  -arch x64 `
  -d "PublishDir=$publishDirectory" `
  -ext WixToolset.UI.wixext `
  -ext WixToolset.Util.wixext `
  -o (Join-Path $releaseDirectory "RestaurantPos-Setup-$releaseVersion.msi")
```

For a fresh development checkout that does not yet contain the local WiX tool, prepare it once:

```powershell
dotnet tool install wix --tool-path .\artifacts\wix-tool --version 5.0.2
.\artifacts\wix-tool\wix.exe extension add WixToolset.UI.wixext/5.0.2
.\artifacts\wix-tool\wix.exe extension add WixToolset.Util.wixext/5.0.2
```

The resulting MSI is written under `artifacts\release-v<version>`. The WiX source automatically includes the approved agreement, install-directory page, and uninstall data-removal question. Build the MSI only after committing the release so the published executable records the correct Git revision.
