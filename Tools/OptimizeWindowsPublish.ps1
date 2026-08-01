[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

function Remove-JsonProperty {
    param(
        [AllowNull()]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    if ($null -ne $Object) {
        [void]$Object.PSObject.Properties.Remove($Name)
    }
}

function Get-JsonPropertyValue {
    param(
        [AllowNull()]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Predicate
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties |
        Where-Object { & $Predicate $_.Name } |
        Select-Object -First 1

    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Remove-JsonPropertiesMatching {
    param(
        [AllowNull()]
        [object]$Object,

        [Parameter(Mandatory = $true)]
        [scriptblock]$Predicate
    )

    if ($null -eq $Object) {
        return
    }

    $names = @(
        $Object.PSObject.Properties |
            Where-Object { & $Predicate $_.Name } |
            ForEach-Object { $_.Name }
    )

    foreach ($name in $names) {
        Remove-JsonProperty -Object $Object -Name $name
    }
}

function Remove-PublishedFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    # Designer/remote protocol assemblies are only used by Avalonia's IDE previewer.
    $exactFiles = @(
        'Avalonia.DesignerSupport.dll',
        'Avalonia.Remote.Protocol.dll',

        # CoreCLR dump/debugger payload. The app has no crash-dump collection,
        # debugger attachment, symbol-reader, profiling, or post-mortem analysis flow.
        'createdump.exe',
        'mscordaccore.dll',
        'mscordbi.dll',
        'Microsoft.DiaSymReader.Native.amd64.dll'
    )

    foreach ($fileName in $exactFiles) {
        $path = Join-Path $PublishDirectory $fileName
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    # The version-qualified DAC filename changes with the runtime patch version.
    Get-ChildItem -LiteralPath $PublishDirectory -File -Filter 'mscordaccore_*.dll' -ErrorAction SilentlyContinue |
        Remove-Item -Force
}

function Update-DependencyManifest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$PublishDirectory
    )

    $depsPath = Join-Path $PublishDirectory 'PasswordManagerLocal.deps.json'
    if (-not (Test-Path -LiteralPath $depsPath)) {
        throw "Windows dependency manifest was not found: $depsPath"
    }

    $dependencyContext = Get-Content -LiteralPath $depsPath -Raw | ConvertFrom-Json
    $runtimeTarget = Get-JsonPropertyValue -Object $dependencyContext.targets -Predicate {
        param($name)
        $name -like '*/win-x64'
    }

    if ($null -eq $runtimeTarget) {
        throw "The win-x64 runtime target could not be found in '$depsPath'."
    }

    $avalonia = Get-JsonPropertyValue -Object $runtimeTarget -Predicate {
        param($name)
        $name -like 'Avalonia/*'
    }

    if ($null -eq $avalonia) {
        throw "The Avalonia runtime entry could not be found in '$depsPath'."
    }

    # Remove the designer assembly wherever Avalonia may place it, and remove every
    # dependency edge to the remote protocol so this remains correct if package
    # internals change in a later Avalonia 11 patch.
    foreach ($runtimeLibrary in @($runtimeTarget.PSObject.Properties)) {
        Remove-JsonPropertiesMatching -Object $runtimeLibrary.Value.runtime -Predicate {
            param($name)
            $name -like '*Avalonia.DesignerSupport.dll'
        }
        Remove-JsonProperty -Object $runtimeLibrary.Value.dependencies -Name 'Avalonia.Remote.Protocol'
    }

    Remove-JsonPropertiesMatching -Object $runtimeTarget -Predicate {
        param($name)
        $name -like 'Avalonia.Remote.Protocol/*'
    }
    Remove-JsonPropertiesMatching -Object $dependencyContext.libraries -Predicate {
        param($name)
        $name -like 'Avalonia.Remote.Protocol/*'
    }

    $runtimePack = Get-JsonPropertyValue -Object $runtimeTarget -Predicate {
        param($name)
        $name -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*'
    }

    if ($null -eq $runtimePack) {
        throw "The self-contained win-x64 runtime pack entry could not be found in '$depsPath'."
    }

    foreach ($fileName in @(
        'Microsoft.DiaSymReader.Native.amd64.dll',
        'createdump.exe',
        'mscordaccore.dll',
        'mscordbi.dll'
    )) {
        Remove-JsonProperty -Object $runtimePack.native -Name $fileName
    }

    Remove-JsonPropertiesMatching -Object $runtimePack.native -Predicate {
        param($name)
        $name -like 'mscordaccore_*.dll'
    }

    $json = $dependencyContext | ConvertTo-Json -Depth 100
    [System.IO.File]::WriteAllText(
        $depsPath,
        $json,
        [System.Text.UTF8Encoding]::new($false)
    )
}

# MSBuild publish directories commonly end with a directory separator. When such a
# value is embedded in a quoted native command line, Windows argument parsing can
# leave the closing quote attached to the value (for example: C:\path\win-x64").
# Normalize both the normal and malformed forms before calling GetFullPath.
$cleanOutputPath = $OutputPath.Trim().Trim([char]34)
$directorySeparators = [char[]]@(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
)
$cleanOutputPath = $cleanOutputPath.TrimEnd($directorySeparators)

if ([string]::IsNullOrWhiteSpace($cleanOutputPath)) {
    throw 'The Windows publish directory argument was empty.'
}

$resolvedOutputPath = [System.IO.Path]::GetFullPath($cleanOutputPath)
if (-not (Test-Path -LiteralPath $resolvedOutputPath -PathType Container)) {
    throw "Windows publish directory does not exist: $resolvedOutputPath"
}

Remove-PublishedFiles -PublishDirectory $resolvedOutputPath
Update-DependencyManifest -PublishDirectory $resolvedOutputPath

Write-Host 'Removed Avalonia designer/remote protocol assets from the Windows publish.'
Write-Host 'Removed unused CoreCLR crash-dump and managed-debugger assets from the Windows publish.'
