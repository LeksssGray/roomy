param(
    [Parameter(Mandatory)][string]$InnoCompiler,
    [string]$OutputDirectory = 'artifacts/windows',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '0.2.0'
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$compiler = (Resolve-Path -LiteralPath $InnoCompiler).Path
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$installed = Join-Path $output 'installed'
$portable = Join-Path $output 'portable'
$project = Join-Path $repo 'src/Roomy'

foreach ($edition in @(@{ Path = $installed; Portable = 'false' }, @{ Path = $portable; Portable = 'true' })) {
    dotnet publish $project -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false "-p:Version=$Version" "-p:RoomyPortable=$($edition.Portable)" -o $edition.Path
    if ($LASTEXITCODE -ne 0) { throw "Roomy publish failed (code $LASTEXITCODE)." }
}

Copy-Item -LiteralPath (Join-Path $portable 'Roomy.exe') -Destination (Join-Path $output 'Roomy-Portable.exe')
& $compiler "/DAppVersion=$Version" "/DPayloadDir=$installed" "/O$output" (Join-Path $repo 'installer/roomy.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed (code $LASTEXITCODE)." }
Get-Item -LiteralPath (Join-Path $output 'Roomy-Portable.exe'), (Join-Path $output 'Roomy-Setup.exe')
