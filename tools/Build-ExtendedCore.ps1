param([switch]$CoreOnly, [string]$RuntimeZip)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$core = Join-Path $root 'core-extended'
$tools = Join-Path $core 'toolchains'
$dist = Join-Path $core 'dist'
$source = Join-Path $core 'upstream'
$commit = '55faa763f986f4ca8a492d9b2719bc6330d2bef5'
$tag = 'v1.14.1-extended-2.7.2'
$goTag = 'patched-1.26.6'
$goHash = '928735bf03a72037963f8f4f86d6773eefe07731a50d37b8eced085370c113bb'
$tags = 'with_quic,with_utls,with_wireguard,with_clash_api,with_v2ray_api'
function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE)" }
}
function Assert-Pe([string]$file, [int]$machine) {
    $b = [IO.File]::ReadAllBytes($file)
    $pe = [BitConverter]::ToInt32($b, 0x3c)
    if ([BitConverter]::ToUInt32($b,$pe) -ne 0x4550 -or [BitConverter]::ToUInt16($b,$pe+4) -ne $machine) { throw "Wrong PE architecture: $file" }
    $major = [BitConverter]::ToUInt16($b,$pe+24+48)
    $minor = [BitConverter]::ToUInt16($b,$pe+24+50)
    if ($major -gt 6 -or ($major -eq 6 -and $minor -gt 1)) { throw "PE requires a newer Windows version: $file ($major.$minor)" }
}
New-Item -ItemType Directory -Force $tools,$dist | Out-Null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$archive = Join-Path $tools 'go-win7.zip'
$goDir = Join-Path $tools $goTag
$go = Join-Path $goDir 'bin\go.exe'
if (-not (Test-Path $go)) {
    Invoke-WebRequest -UseBasicParsing "https://github.com/XTLS/go-win7/releases/download/$goTag/go-for-win7-windows-amd64.zip" -OutFile $archive
    if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $goHash) { throw 'Go archive checksum mismatch' }
    Expand-Archive $archive $goDir -Force
}
if (-not (Test-Path $source)) {
    Run 'git' @('-c','core.autocrlf=false','clone','--depth','1','--branch',$tag,'https://github.com/shtorm-7/sing-box-extended.git',$source)
}
$head = (& git -C $source rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $head -ne $commit) { throw 'Unexpected upstream checkout; use a fresh core-extended/upstream directory' }
$patch = Join-Path $core 'patches\certificate-pin.patch'
$oldPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'Continue'
    & git -C $source apply --reverse --check $patch 2>$null
    $alreadyPatched = $LASTEXITCODE -eq 0
} finally { $ErrorActionPreference = $oldPreference }
if (-not $alreadyPatched) { Run 'git' @('-C',$source,'apply','--check',$patch); Run 'git' @('-C',$source,'apply',$patch) }
$previous = @{}
foreach ($key in @('GOOS','GOARCH','CGO_ENABLED','GOTOOLCHAIN','GOAMD64','GO386','GOPROXY')) { $previous[$key] = [Environment]::GetEnvironmentVariable($key,'Process') }
Push-Location $core
try {
    $env:GOTOOLCHAIN='local';$env:CGO_ENABLED='0';$env:GOOS='windows';$env:GOARCH='amd64';$env:GOAMD64='v1';$env:GO386='sse2'
    # The Go default uses a comma and stops on HTTP 403. Only adjust the public
    # default; retain explicit custom/off settings and all checksum verification.
    $moduleProxy = (& $go env GOPROXY)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read Go module proxy configuration' }
    if ($moduleProxy.Trim() -eq 'https://proxy.golang.org,direct') {
        $env:GOPROXY = 'https://proxy.golang.org|direct'
        Write-Host 'Public Go module mirror: direct-source fallback enabled for download errors.'
    }
    Run $go @('version')
    # Windows executes the integration test that restricted Linux cannot run.
    Run $go @('test','-mod=readonly','-tags',$tags,'.')
    foreach ($arch in @('386','amd64')) {
        $env:GOARCH=$arch
        $suffix = if ($arch -eq '386') { '32' } else { '64' }
        $name = "SGuard7$suffix.exe"
        Run $go @('build','-mod=readonly','-trimpath','-tags',$tags,'-ldflags=-s -w','-o',(Join-Path $dist $name),'.')
        Assert-Pe (Join-Path $dist $name) $(if ($arch -eq '386') {0x14c} else {0x8664})
        Copy-Item (Join-Path $dist $name) (Join-Path $dist "SGuard$suffix.exe") -Force
    }
    $manifest = [ordered]@{ engine='sing-box-extended'; version=$tag; source=$commit; go=$goTag; patchSha256=(Get-FileHash $patch -Algorithm SHA256).Hash; files=@{} }
    foreach($name in @('SGuard32.exe','SGuard64.exe','SGuard732.exe','SGuard764.exe')) { $manifest.files[$name]=(Get-FileHash (Join-Path $dist $name) -Algorithm SHA256).Hash }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $dist 'extended-core.json') -Encoding UTF8
} finally {
    Pop-Location
    foreach($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key,$previous[$key],'Process') }
}
if ($CoreOnly) { Write-Host "Core files: $dist"; exit 0 }
if (-not $RuntimeZip) { $RuntimeZip = Join-Path $root 'IRSpeedyVPN\Resources\Files.zip' }
if (-not (Test-Path $RuntimeZip)) { throw 'Supply the existing complete runtime archive with -RuntimeZip. Proxy, SNI, geo and other assets must be retained.' }
# Compose a new archive atomically; never replace unrelated runtime resources.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$tempZip = Join-Path $dist 'Files.extended.zip'
if(Test-Path $tempZip) { Remove-Item $tempZip }
$inputZip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $RuntimeZip))
$outputZip=[IO.Compression.ZipFile]::Open($tempZip,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach($entry in $inputZip.Entries) {
        $name=$entry.FullName.Replace('\','/')
        if($name -match '(^|/)V-Guard/(SGuard|EGuard)(7)?(32|64)\.exe$' -or $name -eq 'V-Guard/extended-core.json') { continue }
        $dest=$outputZip.CreateEntry($name,[IO.Compression.CompressionLevel]::Optimal)
        if($entry.Name -ne '') { $srcStream=$entry.Open();$dstStream=$dest.Open();try{$srcStream.CopyTo($dstStream)}finally{$srcStream.Dispose();$dstStream.Dispose()} }
    }
    foreach($name in @('SGuard32.exe','SGuard64.exe','SGuard732.exe','SGuard764.exe','extended-core.json')) {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($outputZip,(Join-Path $dist $name),"V-Guard/$name",[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $inputZip.Dispose();$outputZip.Dispose() }
Copy-Item $tempZip (Join-Path $root 'IRSpeedyVPN\Resources\Files.zip') -Force
Write-Host 'Embedded runtime prepared. Build the Windows application with Costura enabled.'

Push-Location $root
try { Run 'dotnet' @('build','IRSpeedyVPN\IRSpeedyVPN.csproj','-c','Release','-p:UseCosturaSingleFile=true') } finally { Pop-Location }
