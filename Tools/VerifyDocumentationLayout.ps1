[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $root 'Docs'

if (-not (Test-Path -LiteralPath $docs -PathType Container)) {
    throw 'The root-level Docs directory is missing.'
}

$rootReadme = Join-Path $root 'README.md'
$outsideDocs = @(
    Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.md' |
        Where-Object {
            $_.FullName -ne $rootReadme -and
            $_.FullName -notlike "$docs\*" -and
            $_.FullName -notmatch '[\/](bin|obj)[\/]'
        }
)
if ($outsideDocs.Count -ne 0) {
    $paths = $outsideDocs.FullName -join [Environment]::NewLine
    throw "Markdown files other than the repository README remain outside Docs:$([Environment]::NewLine)$paths"
}

$requiredPhaseReports = @(
    'PHASE5_2_ENDPOINT_CORRELATION_RESTART_STATIC_VERIFICATION.md',
    'PHASE5_2_IMPLEMENTATION_CHECKS.md',
    'PHASE5_2_MODIFIED_FILES.md',
    'PHASE5_4_PARTIAL_COMMIT_RESTART_RECOVERY_HARDENING_STATIC_VERIFICATION.md',
    'PHASE5_4_IMPLEMENTATION_CHECKS.md',
    'PHASE5_4_MODIFIED_FILES.md',
    'PHASE6_WINDOWS_AGENT_RUNTIME_OWNERSHIP_CUTOVER_STATIC_VERIFICATION.md',
    'PHASE6_IMPLEMENTATION_CHECKS.md',
    'PHASE6_MODIFIED_FILES.md',
    'PHASE6_1_SHUTDOWN_OWNERSHIP_INTENTIONAL_EXIT_HARDENING_STATIC_VERIFICATION.md',
    'PHASE6_1_IMPLEMENTATION_CHECKS.md',
    'PHASE6_1_MODIFIED_FILES.md',
    'PHASE6_2_FAILED_STATE_ADMISSION_RECOVERY_WINDOW_EXIT_STATIC_VERIFICATION.md',
    'PHASE6_2_IMPLEMENTATION_CHECKS.md',
    'PHASE6_2_MODIFIED_FILES.md',
    'PHASE7_WINDOWS_BACKGROUND_SYNC_STARTUP_STATIC_VERIFICATION.md',
    'PHASE7_IMPLEMENTATION_CHECKS.md',
    'PHASE7_MODIFIED_FILES.md',
    'PHASE7_1_BACKGROUND_WRITE_CERTAINTY_REASON_FRESHNESS_STATIC_VERIFICATION.md',
    'PHASE7_1_IMPLEMENTATION_CHECKS.md',
    'PHASE7_1_MODIFIED_FILES.md',
    'PHASE8_DYNAMIC_EXECUTION_PROFILES_ARCHITECTURE.md',
    'PHASE8_DYNAMIC_EXECUTION_PROFILES_LIFECYCLE_STATIC_VERIFICATION.md',
    'PHASE8_IMPLEMENTATION_CHECKS.md',
    'PHASE8_MODIFIED_FILES.md',
    'PHASE9_WINDOWS_INTEGRATION_DEPLOYMENT_HARDENING_STATIC_VERIFICATION.md',
    'PHASE9_IMPLEMENTATION_CHECKS.md',
    'PHASE9_MODIFIED_FILES.md',
    'PHASE9_AUTOMATED_WINDOWS_TEST_MATRIX.md',
    'PHASE9_USER_RUN_CHECKLIST.md',
    'PHASE10_ANDROID_FOREGROUND_SERVICE_RUNTIME_OWNERSHIP_STATIC_VERIFICATION.md',
    'PHASE10_IMPLEMENTATION_CHECKS.md',
    'PHASE10_MODIFIED_FILES.md',
    'PHASE10_ANDROID_SERVICE_ARCHITECTURE.md',
    'PHASE10_ANDROID_MANUAL_TEST_CHECKLIST.md'
)
foreach ($name in $requiredPhaseReports) {
    if (-not (Test-Path -LiteralPath (Join-Path $docs $name) -PathType Leaf)) {
        throw "Required phase documentation is missing: $name"
    }
}

$obsoletePhase6Reports = @(
    'PHASE6_PARTIAL_HANDOFF.md',
    'PHASE6_PROGRESS_CHECKPOINT_2026-07-23.md'
)
foreach ($name in $obsoletePhase6Reports) {
    if (Test-Path -LiteralPath (Join-Path $docs $name) -PathType Leaf) {
        throw "Obsolete Phase 6 checkpoint documentation remains: $name"
    }
}

$documents = @(Get-ChildItem -LiteralPath $docs -Recurse -File -Filter '*.md')
$duplicateContent = @(
    $documents |
        ForEach-Object {
            [pscustomobject]@{
                Path = $_.FullName
                Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
            }
        } |
        Group-Object Hash |
        Where-Object Count -gt 1
)
if ($duplicateContent.Count -ne 0) {
    $paths = $duplicateContent.Group.Path -join [Environment]::NewLine
    throw "Duplicate Markdown contents were found:$([Environment]::NewLine)$paths"
}

foreach ($document in $documents) {
    $content = Get-Content -LiteralPath $document.FullName -Raw
    foreach ($match in [regex]::Matches($content, '(?<!!)\[[^\]]*\]\((?<target>[^)]+)\)')) {
        $target = $match.Groups['target'].Value.Trim().Trim('<', '>')
        if ($target.Length -eq 0 -or
            $target.StartsWith('#') -or
            $target -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
            continue
        }

        $pathPart = $target.Split('#', 2)[0]
        $resolved = Join-Path $document.DirectoryName $pathPart
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "Broken local Markdown link in '$($document.FullName)': $target"
        }
    }
}

Write-Host 'Documentation layout is valid.'
