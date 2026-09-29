param([Parameter(Mandatory=$true)][string]$Archive)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Resolve-Path $Archive))
try {
    $entry=$zip.GetEntry('V-Guard/extended-core.json')
    if(-not $entry){throw 'This branch requires an extended Core runtime. Run Build-SingBox-Extended.cmd first.'}
    $reader=New-Object IO.StreamReader($entry.Open())
    try{$manifest=$reader.ReadToEnd()|ConvertFrom-Json}finally{$reader.Dispose()}
    if($manifest.engine -ne 'sing-box-extended'){throw 'Wrong runtime engine'}
    foreach($name in @('SGuard32.exe','SGuard64.exe','SGuard732.exe','SGuard764.exe')){
        $entry=$zip.GetEntry("V-Guard/$name");if(-not $entry){throw "Missing $name"}
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$hash=([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
        if($hash -ne $manifest.files.$name){throw "Runtime hash mismatch: $name"}
    }
}finally{$zip.Dispose()}
Write-Host 'Extended runtime payload verified (x86/x64, modern/Windows 7).'
