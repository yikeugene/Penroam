[CmdletBinding()]
param([switch]$AllowDesktopChanges)

$ErrorActionPreference = 'Stop'
if (-not $AllowDesktopChanges) { throw 'Run this smoke test in a disposable Windows account with -AllowDesktopChanges. It installs and uninstalls Penroam and checks desktop shortcuts.' }
$MoyeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
[xml]$MoyeProject = Get-Content -LiteralPath (Join-Path $MoyeRoot 'src/Moye/Moye.csproj') -Raw
$MoyeVersion = $MoyeProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText.Trim()
$MoyeArtifacts = Join-Path $MoyeRoot 'artifacts'
$MoyeInstaller = Join-Path $MoyeArtifacts ('Penroam-' + $MoyeVersion + '-Setup-win-x64.exe')
$MoyePayload = Join-Path $MoyeArtifacts 'Penroam-win-x64'
$MoyeDefaultInstall = Join-Path $env:LOCALAPPDATA 'Programs/Penroam'
$MoyeLegacyInstall = Join-Path $env:LOCALAPPDATA 'Programs/Moye'
$MoyeData = Join-Path $env:LOCALAPPDATA 'Moye'
$MoyeDesktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Penroam.lnk'
$MoyeStartShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Penroam.lnk'
$MoyeLegacyShortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Moye.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Moye.lnk')
)
$MoyeLegacyFiles = @('Moye.exe', 'Moye.dll', 'Moye.deps.json', 'Moye.runtimeconfig.json')
$MoyeRegistry = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\net.yikeugene.moye_is1'
foreach ($MoyeExisting in (@($MoyeDefaultInstall, $MoyeLegacyInstall, $MoyeData, (Join-Path $env:LOCALAPPDATA 'Penroam'), $MoyeDesktopShortcut, $MoyeStartShortcut, $MoyeRegistry) + $MoyeLegacyShortcuts)) {
    if (Test-Path -LiteralPath $MoyeExisting) { throw "Refusing to touch an existing installation, notebook folder or shortcut: $MoyeExisting" }
}
if (-not (Test-Path -LiteralPath $MoyeInstaller -PathType Leaf)) { throw 'Build the installer before running this test.' }
$MoyeExpectedHash = (Get-Content -LiteralPath ($MoyeInstaller + '.sha256')).Split(' ')[0]
if ((Get-FileHash -LiteralPath $MoyeInstaller -Algorithm SHA256).Hash.ToLowerInvariant() -ne $MoyeExpectedHash) { throw 'Installer checksum mismatch.' }
$MoyeSmokeRoot = [IO.Path]::GetFullPath((Join-Path $MoyeArtifacts ('installer-smoke-' + [Guid]::NewGuid().ToString('N'))))
$MoyeArtifactsPrefix = [IO.Path]::GetFullPath($MoyeArtifacts).TrimEnd('\') + '\'
if (-not $MoyeSmokeRoot.StartsWith($MoyeArtifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid installer smoke-test path.' }
$MoyeInstallPath = Join-Path $MoyeSmokeRoot 'app'
New-Item -ItemType Directory -Path $MoyeSmokeRoot, $MoyeData -Force | Out-Null
$MoyeSentinel = Join-Path $MoyeData 'installer-preservation-check.txt'
[IO.File]::WriteAllText($MoyeSentinel, 'Synthetic notebook-data preservation marker.')
$MoyeSentinelHash = (Get-FileHash -LiteralPath $MoyeSentinel -Algorithm SHA256).Hash
$MoyeCreatedLegacyShortcuts = [Collections.Generic.List[string]]::new()

function Invoke-MoyeSetup([string]$Executable, [string[]]$Arguments) {
    $MoyeSetupProcess = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($MoyeSetupProcess.ExitCode -ne 0) { throw "Installer process failed: $Executable (exit $($MoyeSetupProcess.ExitCode))." }
}
function Assert-MoyeInstalled {
    $MoyeExpectedExe = Join-Path $MoyeInstallPath 'Penroam.exe'
    if (-not (Test-Path -LiteralPath $MoyeExpectedExe -PathType Leaf)) { throw 'Penroam.exe was not installed.' }
    $MoyeShell = New-Object -ComObject WScript.Shell
    try {
        foreach ($MoyeShortcutPath in @($MoyeDesktopShortcut, $MoyeStartShortcut)) {
            if (-not (Test-Path -LiteralPath $MoyeShortcutPath -PathType Leaf)) { throw "Automatic shortcut missing: $MoyeShortcutPath" }
            $MoyeShortcut = $MoyeShell.CreateShortcut($MoyeShortcutPath)
            try {
                if ($MoyeShortcut.TargetPath -ne $MoyeExpectedExe -or $MoyeShortcut.WorkingDirectory -ne $MoyeInstallPath) { throw 'Shortcut target or working directory is incorrect.' }
            } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($MoyeShortcut) }
        }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($MoyeShell) }
    $MoyeRegistration = Get-ItemProperty -LiteralPath $MoyeRegistry
    if ($MoyeRegistration.DisplayVersion -ne $MoyeVersion -or $MoyeRegistration.DisplayName -notmatch '^Penroam\b') { throw 'Windows uninstall registration has the wrong product name or version.' }
    foreach ($MoyeLegacyPath in ($MoyeLegacyShortcuts + @($MoyeLegacyFiles | ForEach-Object { Join-Path $MoyeInstallPath $_ }))) {
        if (Test-Path -LiteralPath $MoyeLegacyPath) { throw "Legacy branding was not removed: $MoyeLegacyPath" }
    }
    foreach ($MoyeSource in Get-ChildItem -LiteralPath $MoyePayload -Recurse -File -Force) {
        $MoyeRelative = [IO.Path]::GetRelativePath($MoyePayload, $MoyeSource.FullName)
        $MoyeInstalled = Join-Path $MoyeInstallPath $MoyeRelative
        if (-not (Test-Path -LiteralPath $MoyeInstalled -PathType Leaf) -or
            (Get-FileHash -LiteralPath $MoyeSource.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $MoyeInstalled -Algorithm SHA256).Hash) { throw "Installed payload mismatch: $MoyeRelative" }
    }
    $MoyeUnexpected = @(Get-ChildItem -LiteralPath $MoyeInstallPath -Recurse -File -Force | Where-Object {
        $MoyeRelative = [IO.Path]::GetRelativePath($MoyeInstallPath, $_.FullName)
        $MoyeRelative -notmatch '^unins\d+\.(exe|dat|msg)$' -and -not (Test-Path -LiteralPath (Join-Path $MoyePayload $MoyeRelative) -PathType Leaf)
    })
    if ($MoyeUnexpected.Count -gt 0) { throw 'Unexpected files in the installed payload.' }
    if ((Get-FileHash -LiteralPath $MoyeSentinel -Algorithm SHA256).Hash -ne $MoyeSentinelHash) { throw 'Installation changed notebook data.' }
}

function New-MoyeLegacyUpgradeFixture {
    # Seed only synthetic legacy filenames/shortcuts; never run a historical app
    # against the real user's library. The stable AppId already belongs to this test.
    foreach ($MoyeLegacyFile in $MoyeLegacyFiles) {
        Copy-Item -LiteralPath (Join-Path $MoyePayload $MoyeLegacyFile.Replace('Moye.', 'Penroam.')) -Destination (Join-Path $MoyeInstallPath $MoyeLegacyFile)
    }
    $MoyeShell = New-Object -ComObject WScript.Shell
    try {
        foreach ($MoyeShortcutPath in $MoyeLegacyShortcuts) {
            $MoyeShortcut = $MoyeShell.CreateShortcut($MoyeShortcutPath)
            try {
                $MoyeShortcut.TargetPath = Join-Path $MoyeInstallPath 'Moye.exe'
                $MoyeShortcut.WorkingDirectory = $MoyeInstallPath
                $MoyeShortcut.Save()
                $MoyeCreatedLegacyShortcuts.Add($MoyeShortcutPath)
            } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($MoyeShortcut) }
        }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($MoyeShell) }
    # Product numbering restarts at Penroam 1.0.0. Exercise replacement of a
    # synthetic legacy registration whose displayed version is higher.
    Set-ItemProperty -LiteralPath $MoyeRegistry -Name DisplayName -Value 'Moye version 2.0.0'
    Set-ItemProperty -LiteralPath $MoyeRegistry -Name DisplayVersion -Value '2.0.0'
}

$MoyeInstalledOnce = $false
try {
    foreach ($MoyePass in @('install', 'legacy-brand-upgrade', 'reinstall')) {
        if ($MoyePass -eq 'legacy-brand-upgrade') { New-MoyeLegacyUpgradeFixture }
        $MoyeSetupArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', ('/LOG="' + (Join-Path $MoyeSmokeRoot ($MoyePass + '.log')) + '"'))
        # Subsequent passes must recover the original install path from the stable AppId.
        if ($MoyePass -eq 'install') { $MoyeSetupArguments += ('/DIR="' + $MoyeInstallPath + '"') }
        $MoyeInstalledOnce = $true
        Invoke-MoyeSetup $MoyeInstaller $MoyeSetupArguments
        Assert-MoyeInstalled
        & (Join-Path $PSScriptRoot 'test-package.ps1') -PackagePath $MoyeInstallPath
        Write-Host "$MoyePass passed: desktop and Start menu shortcuts, exact payload, uninstall registration, and unchanged notebook data."
    }
    $MoyeUninstaller = Join-Path $MoyeInstallPath 'unins000.exe'
    Invoke-MoyeSetup $MoyeUninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $MoyeSmokeRoot 'uninstall.log') + '"'))
    $MoyeInstalledOnce = $false
    foreach ($MoyeRemoved in @((Join-Path $MoyeInstallPath 'Penroam.exe'), $MoyeDesktopShortcut, $MoyeStartShortcut, $MoyeRegistry)) {
        if (Test-Path -LiteralPath $MoyeRemoved) { throw "Uninstall did not remove $MoyeRemoved" }
    }
    if ((Get-FileHash -LiteralPath $MoyeSentinel -Algorithm SHA256).Hash -ne $MoyeSentinelHash) { throw 'Uninstall removed or changed notebook data.' }
    Write-Host 'Uninstall passed: application and shortcuts removed; notebook data preserved.'
    [pscustomobject]@{ product = 'Penroam'; version = $MoyeVersion; installerSha256 = $MoyeExpectedHash; install = 'passed'; syntheticLegacyBrandUpgrade = 'passed'; reinstall = 'passed'; uninstall = 'passed'; desktopShortcut = 'passed'; notebookDataPreserved = $true } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $MoyeArtifacts 'installer-smoke-result.json') -Encoding UTF8
}
finally {
    if ($MoyeInstalledOnce -and (Test-Path -LiteralPath (Join-Path $MoyeInstallPath 'unins000.exe'))) {
        Invoke-MoyeSetup (Join-Path $MoyeInstallPath 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    }
    # A failed upgrade may leave these synthetic shortcuts outside the uninstaller log.
    foreach ($MoyeShortcutPath in $MoyeCreatedLegacyShortcuts) {
        if (Test-Path -LiteralPath $MoyeShortcutPath) { Remove-Item -LiteralPath $MoyeShortcutPath }
    }
    # Remove only the synthetic file this test created; never recursively delete notebook data.
    if (Test-Path -LiteralPath $MoyeSentinel) { Remove-Item -LiteralPath $MoyeSentinel }
    if ((Test-Path -LiteralPath $MoyeData) -and @(Get-ChildItem -LiteralPath $MoyeData -Force).Count -eq 0) { Remove-Item -LiteralPath $MoyeData }
}
