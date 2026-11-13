<#
.SYNOPSIS
  Fetches the native BASS runtime and its codec add-ons from un4seen.com into libs\bass\, which is ignored.

.DESCRIPTION
  Why a script and not a package: the ManagedBass NuGet is deliberately the managed wrapper only (AnyCPU, no
  binaries - Un4seen Developments does not let anyone redistribute its libraries), and the community package
  BASS.Native that does carry binaries is stale and has no ARM at all. So the binaries come from the only
  place they are published, the developer's own site, and they are never committed: BASS is free to use in a
  non-commercial product but it is not open source (see LICENSE in the repository and the note in
  MediaPortal\Source\Frontend\MediaPortal.Frontend.Player\NativeBass.cs). A contributor who wants to play
  sound with the BASS engine runs this script once; anyone who does not, keeps the libVLC engine and notices
  nothing but one sentence in the settings.

  What the archives look like, measured on the archives themselves (not from a memory):
    files\bass24.zip            bass.dll (x86) at the root, x64\bass.dll. Nothing for ARM64.
    files\bass24-linux.zip      libs\aarch64\, libs\armhf\, libs\x86\, libs\x86_64\ - each libbass.so
    files\bass24-osx.zip        ONE libbass.dylib, and its fat header names x86_64, i386 and arm64, so the
                                single file serves both Apple architectures.
  The add-on archives follow the same law with their own file name (bass_aac.dll, libs\x86_64\libbass_aac.so).
  Two gaps are the publisher's, not ours: WMA is shipped for Windows only, AAC and ALAC not for macOS.
  There is also no Windows/ARM64 build anywhere on the site (checked against the archive list of
  https://www.un4seen.com/bass.html). Windows on ARM runs the x64 library under emulation, which is why
  NativeBass falls back from win-arm64 to win-x64 instead of giving up.

  The layout written here is the layout NativeBass looks for:
    libs\bass\win-x64\bass.dll          libs\bass\linux-x64\libbass.so       libs\bass\osx\libbass.dylib
  Re-running is cheap: archives are kept in libs\bass\.archives\ and a file that is already in place is left
  alone unless -Force says otherwise.

.EXAMPLE
  .\get-bass.ps1
  Every add-on, every platform.

.EXAMPLE
  .\get-bass.ps1 -Platform win -Only bass,bass_aac,bass_opus
  Just what a Windows developer needs to hear AAC and Opus.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
  [ValidateSet('all', 'win', 'linux', 'osx')] [string] $Platform = 'all',
  # Comma separated add-on names ("bass_aac,bass_mpc"); empty means every add-on in the table below.
  [string] $Only = '',
  # Re-download the archives and overwrite a library that is already in place.
  [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# One progress bar per archive is unreadable in a pipeline log, and a stalled download still throws.
$ProgressPreference = 'SilentlyContinue'
# Windows PowerShell 5.1 negotiates TLS 1.0 by default and un4seen does not speak it; PowerShell 7 already
# offers the modern protocols and accepts this line without noticing.
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch { }
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bassRoot = Join-Path $repoRoot 'libs\bass'
$archiveRoot = Join-Path $bassRoot '.archives'
$site = 'https://www.un4seen.com/'

# The core first, then the codecs the front end promised in its settings page. A $null platform means
# "un4seen ships nothing for that platform" - the script says so out loud instead of failing quietly.
$engines = @()
$engines += [pscustomobject]@{ Name = 'bass'; Title = 'BASS core'; Core = $true;
  Win = 'files/bass24.zip'; Linux = 'files/bass24-linux.zip'; Osx = 'files/bass24-osx.zip' }
$addOns = @(
  [pscustomobject]@{ Name = 'bass_aac';   Title = 'AAC and MP4';      Win = 'files/z/2/bass_aac24.zip'; Linux = 'files/z/2/bass_aac24-linux.zip'; Osx = $null },
  [pscustomobject]@{ Name = 'bass_ac3';   Title = 'AC-3 (Dolby)';     Win = 'files/z/2/bass_ac324.zip'; Linux = 'files/z/2/bass_ac324-linux.zip'; Osx = $null },
  [pscustomobject]@{ Name = 'bass_mpc';   Title = 'Musepack';         Win = 'files/z/2/bass_mpc24.zip'; Linux = 'files/z/2/bass_mpc24-linux.zip'; Osx = 'files/z/2/bass_mpc24-osx.zip' },
  [pscustomobject]@{ Name = 'bass_spx';   Title = 'Speex';            Win = 'files/z/2/bass_spx24.zip'; Linux = 'files/z/2/bass_spx24-linux.zip'; Osx = 'files/z/2/bass_spx24-osx.zip' },
  [pscustomobject]@{ Name = 'bass_tta';   Title = 'TTA';              Win = 'files/z/2/bass_tta24.zip'; Linux = 'files/z/2/bass_tta24-linux.zip'; Osx = 'files/z/2/bass_tta24-osx.zip' },
  [pscustomobject]@{ Name = 'bassalac';   Title = 'Apple Lossless';   Win = 'files/bassalac24.zip';     Linux = 'files/bassalac24-linux.zip';     Osx = $null },
  [pscustomobject]@{ Name = 'bassape';    Title = "Monkey's Audio";  Win = 'files/bassape24.zip';      Linux = 'files/bassape24-linux.zip';      Osx = 'files/bassape24-osx.zip' },
  [pscustomobject]@{ Name = 'bassdsd';    Title = 'DSD (DSF/DFF/SACD)'; Win = 'files/bassdsd24.zip';    Linux = 'files/bassdsd24-linux.zip';      Osx = 'files/bassdsd24-osx.zip' },
  [pscustomobject]@{ Name = 'basshls';    Title = 'HLS radio';        Win = 'files/basshls24.zip';      Linux = 'files/basshls24-linux.zip';      Osx = 'files/basshls24-osx.zip' },
  [pscustomobject]@{ Name = 'bassmidi';   Title = 'MIDI';             Win = 'files/bassmidi24.zip';     Linux = 'files/bassmidi24-linux.zip';     Osx = 'files/bassmidi24-osx.zip' },
  [pscustomobject]@{ Name = 'bassopus';   Title = 'Opus';             Win = 'files/bassopus24.zip';     Linux = 'files/bassopus24-linux.zip';     Osx = 'files/bassopus24-osx.zip' },
  [pscustomobject]@{ Name = 'basswv';     Title = 'WavPack';          Win = 'files/basswv24.zip';       Linux = 'files/basswv24-linux.zip';       Osx = 'files/basswv24-osx.zip' },
  [pscustomobject]@{ Name = 'basswma';    Title = 'WMA';              Win = 'files/basswma24.zip';      Linux = $null;                            Osx = $null }
)

$wanted = $engines + $addOns
if ($Only) {
  $names = $Only -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
  $wanted = @($wanted | Where-Object { $names -contains $_.Name })
  foreach ($name in $names) {
    if (-not ($wanted | Where-Object { $_.Name -eq $name })) {
      throw "Unknown add-on '$name'. The table knows: $(($engines + $addOns | ForEach-Object { $_.Name }) -join ', ')"
    }
  }
}

function Get-Archive([string]$relative) {
<#
  The archive on disk, downloaded into libs\bass\.archives when it is not there yet. Pure on purpose: it
  reports by returning @{ Path; State; Note } and never writes to the success stream itself, because a
  function that mixes its log into its result hands the caller an array instead of a path - this folder has
  already written that mistake down (README, "A PowerShell function returns your build log").
#>
  $name = [IO.Path]::GetFileName($relative)
  $local = Join-Path $archiveRoot $name
  if ([IO.File]::Exists($local) -and -not $Force) {
    return [pscustomobject]@{ Path = $local; State = 'cached'; Note = '' }
  }
  if ($WhatIfPreference) {
    # -WhatIf promises to touch nothing, and reaching for eight megabytes over the network is touching something.
    return [pscustomobject]@{ Path = $null; State = 'would download'; Note = '' }
  }
  if (-not ([IO.Directory]::Exists($archiveRoot))) { [IO.Directory]::CreateDirectory($archiveRoot) | Out-Null }
  $temporary = "$local.tmp"
  try {
    Invoke-WebRequest -Uri ($site + $relative) -OutFile $temporary -UseBasicParsing -TimeoutSec 300
  } catch {
    if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    return [pscustomobject]@{ Path = $null; State = 'unreachable'; Note = $_.Exception.Message }
  }
  $magic = [byte[]](0, 0)
  $stream = [IO.File]::OpenRead($temporary)
  try { $null = $stream.Read($magic, 0, 2) } finally { $stream.Close() }
  if ($magic[0] -ne 0x50 -or $magic[1] -ne 0x4B) {
    # A site that cannot find a file tends to answer with its own page rather than with nothing, and trusting
    # that would report "no runtimes in this archive" instead of "the archive is not there".
    [IO.File]::Delete($temporary)
    return [pscustomobject]@{ Path = $null; State = 'unreachable'; Note = 'the answer was not an archive' }
  }
  if ([IO.File]::Exists($local)) { [IO.File]::Delete($local) }
  [IO.File]::Move($temporary, $local)
  return [pscustomobject]@{ Path = $local; State = 'downloaded'; Note = '' }
}

function Get-RuntimeFolder([string]$entry) {
<#
  The runtime folder of one archive entry, or $null when the entry is not a runtime binary. The law is the
  one measured above: an archive puts a Windows x86 library at its root and the x64 one under x64/, and the
  Linux archives carry libs\<cpu>\ with the Debian spelling of the CPUs.
#>
  $path = $entry -replace '\\', '/'
  $name = [IO.Path]::GetFileName($path)
  if ($path -match '^libs/(?<cpu>aarch64|armhf|x86|x86_64)/(?<f>[^/]+\.so)$') {
    switch ($Matches.cpu) {
      'aarch64' { return @{ Folder = 'linux-arm64'; File = $Matches.f } }
      'armhf' { return @{ Folder = 'linux-arm'; File = $Matches.f } }
      'x86' { return @{ Folder = 'linux-x86'; File = $Matches.f } }
      'x86_64' { return @{ Folder = 'linux-x64'; File = $Matches.f } }
    }
  }
  if ($name -like '*.dylib') { return @{ Folder = 'osx'; File = $name } }
  if ($name -like '*.dll') {
    if ($path -like 'x64/*') { return @{ Folder = 'win-x64'; File = $name } }
    if ($path -like '*/x64/*') { return @{ Folder = 'win-x64'; File = $name } }
    if ($path -notmatch '/') { return @{ Folder = 'win-x86'; File = $name } }
  }
  return $null
}

function Copy-Runtimes([string]$archive) {
<#
  Places every runtime binary the archive carries and returns one row per file:
  @{ Folder; File; Bytes; State } with State 'placed', 'kept' (already in place, left alone) or 'would place'.
#>
  $rows = @()
  $zip = [IO.Compression.ZipFile]::OpenRead($archive)
  try {
    foreach ($entry in $zip.Entries) {
      if ($entry.Length -eq 0) { continue }
      $target = Get-RuntimeFolder $entry.FullName
      if ($null -eq $target) { continue }
      if ($Platforms -notcontains $target.Folder.Split('-')[0]) { continue }
      $folder = Join-Path $bassRoot $target.Folder
      $path = Join-Path $folder $target.File
      $state = 'placed'
      if ([IO.File]::Exists($path) -and -not $Force) {
        $state = 'kept'
      } elseif ($WhatIfPreference) {
        $state = 'would place'
      } else {
        if (-not ([IO.Directory]::Exists($folder))) { [IO.Directory]::CreateDirectory($folder) | Out-Null }
        # Streams rather than ExtractToFile: that one is an extension method of
        # System.IO.Compression.FileSystem, and Windows PowerShell resolves ZipFile without loading it, so
        # the same line worked on PowerShell 7 and threw "no method ExtractToFile" on 5.1.
        $sourceStream = $entry.Open()
        try {
          $targetStream = [IO.File]::Create($path)
          try { $sourceStream.CopyTo($targetStream) } finally { $targetStream.Dispose() }
        } finally {
          $sourceStream.Dispose()
        }
      }
      $rows += [pscustomobject]@{ Folder = $target.Folder; File = $target.File; Bytes = $entry.Length; State = $state }
    }
  } finally {
    $zip.Dispose()
  }
  return $rows
}

$Platforms = @()
if ($Platform -eq 'all' -or $Platform -eq 'win') { $Platforms += 'win' }
if ($Platform -eq 'all' -or $Platform -eq 'linux') { $Platforms += 'linux' }
if ($Platform -eq 'all' -or $Platform -eq 'osx') { $Platforms += 'osx' }

$placed = 0
$kept = 0
$absent = 0
$failed = @()

Write-Output ("BASS natives into {0}" -f $bassRoot)
foreach ($engine in $wanted) {
  Write-Output ("{0}  ({1})" -f $engine.Name, $engine.Title)
  $legs = @(
    @{ Kind = 'win'; Relative = $engine.Win },
    @{ Kind = 'linux'; Relative = $engine.Linux },
    @{ Kind = 'osx'; Relative = $engine.Osx }
  )
  foreach ($leg in $legs) {
    if ($Platforms -notcontains $leg.Kind) { continue }
    if (-not $leg.Relative) {
      $absent++
      Write-Output ("  none        un4seen ships no {0} build of {1}" -f $leg.Kind, $engine.Name)
      continue
    }
    $archive = Get-Archive $leg.Relative
    if ($archive.State -eq 'would download') {
      Write-Output ("  would download {0}" -f $leg.Relative)
      continue
    }
    if ($archive.State -eq 'unreachable') {
      $failed += ("{0} ({1})" -f $leg.Relative, $leg.Kind)
      Write-Output ("  unreachable {0}  ({1})" -f $leg.Relative, $archive.Note)
      continue
    }
    if ($archive.State -eq 'downloaded') {
      Write-Output ("  downloaded  {0}" -f $leg.Relative)
    }
    foreach ($row in Copy-Runtimes $archive.Path) {
      if ($row.State -eq 'kept') { $kept++ } else { $placed++ }
      Write-Output ("  {0,-11} {1,-12} {2,-22} {3}" -f $row.State, $row.Folder, $row.File,
        ('{0:N0} kB' -f ($row.Bytes / 1024.0)))
    }
  }
}

# The stamp is the version of the Windows core library when it is here, because that is the one this host
# can read; on a Linux host the Linux library is there instead and says the same thing.
$stamp = Join-Path $bassRoot '.version'
$core = $null
foreach ($candidate in @('win-x64\bass.dll', 'linux-x64\libbass.so', 'osx\libbass.dylib', 'win-x86\bass.dll')) {
  $path = Join-Path $bassRoot $candidate
  if ([IO.File]::Exists($path)) { $core = $path; break }
}
$versionText = 'unknown'
if ($core) {
  try {
    $information = [Diagnostics.FileVersionInfo]::GetVersionInfo($core)
    if ($information -and $information.FileVersion) { $versionText = $information.FileVersion }
  } catch { }
}
if (-not $WhatIfPreference) {
  if (-not ([IO.Directory]::Exists($bassRoot))) { [IO.Directory]::CreateDirectory($bassRoot) | Out-Null }
  $bassLine = 'bass      {0}' -f $versionText
  $fetchedLine = 'fetched   {0} UTC' -f ([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm'))
  $lines = @(
    '# written by scripts\ci\get-bass.ps1 - this folder is ignored on purpose, BASS is not redistributable',
    '# get it again with that script; the licence is the one on https://www.un4seen.com/license/',
    $bassLine,
    $fetchedLine
  )
  [IO.File]::WriteAllText($stamp, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
  Write-Output ("  stamped     {0}  (bass {1})" -f $stamp, $versionText)
}

Write-Output ''
$lead = if ($WhatIfPreference) { 'would place' } else { 'placed' }
Write-Output ("{0} {1}, {2} already there, {3} not offered by the publisher, {4} archive(s) unavailable" -f
  $lead, $placed, $kept, $absent, $failed.Count)
if ($failed.Count) {
  Write-Output ('unavailable: ' + ($failed -join ', '))
  exit 1
}
exit 0
