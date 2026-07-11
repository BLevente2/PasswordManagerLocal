$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $root 'publish.ps1'

# Keep one Windows publishing implementation. The root command owns the
# self-contained, trimming, output-directory, cleanup, and validation settings.
& $publishScript -Windows
