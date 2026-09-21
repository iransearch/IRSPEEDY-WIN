param([Parameter(Mandatory = $true)][string]$AssemblyPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
$resolved = (Resolve-Path -LiteralPath $AssemblyPath).Path
# Loading bytes avoids locking the final EXE. Do not invoke application code.
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($resolved))
$required = @('IRSpeedyHotspotHelper.exe', 'IRSpeedyHotspotHelper.dll',
    'IRSpeedyHotspotHelper.deps.json', 'IRSpeedyHotspotHelper.runtimeconfig.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'THIRD-PARTY-NOTICES.txt')
foreach ($rid in @('win-x64', 'win-x86')) {
    $name = "IRSpeedyVPN.Hotspot.$rid.zip"
    $stream = $assembly.GetManifestResourceStream($name)
    if ($null -eq $stream) { throw "Final executable is missing embedded resource: $name" }
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read)
        try {
            foreach ($file in $required) {
                $entry = $zip.GetEntry($file)
                if ($null -eq $entry -or $entry.Length -eq 0) { throw "$rid payload is missing $file" }
            }
            $entry = $zip.GetEntry('IRSpeedyHotspotHelper.exe')
            $reader = [IO.BinaryReader]::new($entry.Open())
            try {
                $bytes = $reader.ReadBytes([int]$entry.Length)
                $offset = [BitConverter]::ToInt32($bytes, 0x3c)
                if ([BitConverter]::ToUInt16($bytes, 0) -ne 0x5a4d -or
                    [BitConverter]::ToUInt32($bytes, $offset) -ne 0x4550) { throw "Invalid $rid helper executable" }
                $machine = [BitConverter]::ToUInt16($bytes, $offset + 4)
                $expected = if ($rid -eq 'win-x64') { 0x8664 } else { 0x14c }
                if ($machine -ne $expected) { throw "Wrong helper architecture for $rid" }
            } finally { $reader.Dispose() }
            Write-Host "$rid embedded payload verified ($($zip.Entries.Count) entries)."
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
}
Write-Host 'Both hotspot payloads are present in the EXE; no adjacent Hotspot folder is needed.'
