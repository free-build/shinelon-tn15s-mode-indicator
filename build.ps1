[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $repoRoot 'src\ModeIndicator.cs'
$outputDirectory = Join-Path $repoRoot 'dist'
$output = Join-Path $outputDirectory 'Shinelon-ModeIndicator.exe'
$checksumFile = Join-Path $repoRoot 'SHA256SUMS.txt'

if (-not [Environment]::Is64BitOperatingSystem) {
    throw 'This project requires 64-bit Windows.'
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '.NET Framework 4.x x64 C# compiler was not found.'
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $compiler /nologo /target:winexe /platform:x64 /optimize+ `
    /out:$output `
    /reference:System.Management.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

$hash = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $checksumFile -Encoding ASCII `
    -Value "$hash  dist/Shinelon-ModeIndicator.exe"

Write-Host "Built: $output"
Write-Host "SHA-256: $hash"
