# Build the Clawd desktop pet using the .NET Framework compiler that ships with Windows (nothing to install).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$src = Join-Path $here 'ClawdPet.cs'
$exe = Join-Path $here 'ClawdPet.exe'
$tmp = Join-Path $here 'ClawdPet.build.exe'
$ico = Join-Path $here 'clawd.ico'
$common = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/nowarn:649', '/nowarn:169', '/nowarn:414','/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')

# Pass 1 (only when clawd.ico is missing): build without an icon, then use it to generate clawd.ico
if (-not (Test-Path $ico)) {
    & $csc @common "/out:$tmp" $src
    if ($LASTEXITCODE -ne 0) { throw 'Compile failed' }
    & $tmp --icon $ico | Out-Null
    Remove-Item $tmp -Force -ErrorAction SilentlyContinue
}

# Pass 2: build the real exe with the icon embedded
& $csc @common "/win32icon:$ico" "/out:$exe" $src
if ($LASTEXITCODE -ne 0) { throw 'Compile failed (if ClawdPet.exe is running, right-click it and choose Exit first)' }
Write-Host "OK -> $exe"
