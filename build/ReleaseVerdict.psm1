# ============================================================================
#  ReleaseVerdict.psm1 - the verdict on a release, with no network and no process state.
#    Get-ReleaseProblems   the judge: plain asset data in, a list of problems out;
#    New-Problem           builds one problem from the fault catalog (ONE map);
#    Format-ReleaseFailure the What/Why/Fix of a list of problems;
#    Assert-ReleaseAssetSet the asset set that was verified is the asset set that goes live.
#  An asset is a plain object { Name; Uploader; Size; Path }. A release is judged in two scopes so
#  nothing is downloaded for a fault the listing already shows:
#    Listing  presence, who uploaded EVERY asset, versioned-vs-plugin archive size;
#    Content  digest, manifest (archive entries), versioned archive bytes.
#  Terms: plugin archive (teststand-mcp.zip), versioned archive, archive digest, archive manifest.
#  A release may carry several plugins (one per asset prefix); each one's four assets are judged on their own.
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')
Import-Module (Join-Path $PSScriptRoot 'ReleaseAssets.psm1')

$script:MaxManifestProblemsShown = 5
$script:UnknownUploader = '(unknown)'
$script:NeverRetriesWhy = 'Running the verifier again will not help'
$script:MayRetryWhy = 'Running the verifier again may help'

# THE rollback policy: what a maintainer does about a bad release that is already LIVE. Every report of such a
# release (a faulty published release, a mismatching latest download, a changed asset set after publishing)
# ends in this one sentence; CONTRIBUTING quotes it.
$script:RollbackAdvice = 'delete the release so releases/latest falls back to the previous release (safe only once an earlier workflow-built release exists), or fix forward by releasing the next patch version (see CONTRIBUTING).'

# What the maintainer does after a fault, by where the assets came from AND by release.draft: only a draft is
# deleted first; a published release is rolled back or fixed forward. Local mode never talks about either.
$script:RestartFix = @{
    Api          = 'Start a new release run (delete the draft first).'
    ApiPublished = "The release is already published, so there is no draft to delete: $script:RollbackAdvice"
    Local        = ('Run the packaging script again so it produces all {0} assets.' -f (Get-RequiredAssetNames -Version '0.0.0').Count)
}

# ----------------------------------------------------------------------------
#  The fault catalog: one entry per problem code. Fault is a format string over the problem's values;
#  Fix is EXTRA advice after the common restart step (OwnFix: it replaces that step).
#  Every Kind of Compare-ArchiveManifest has an entry (a test asserts it).
#  Retryable marks a fault that is expected to heal by itself (a CDN still serving the previous
#  release); the report then says running the verifier again MAY help instead of WILL NOT.
# ----------------------------------------------------------------------------
$script:Faults = @{
    'asset-missing'                = @{ Fault = '{0} is missing from the release'
                                        Why = 'the asset is not part of the release, so waiting will not add it'
                                        Fix = '' }
    'hand-uploaded-asset'          = @{ Fault = ("{0} was uploaded by '{1}', not by $ReleaseUploaderLogin")
                                        Why = 'only assets built and uploaded by the release workflow may go live'
                                        Fix = 'Never attach assets by hand.' }
    'versioned-size-differs'       = @{ Fault = '{0} is {1} bytes but the plugin archive is {2} bytes'
                                        Why = 'the two archives must be the same bytes, or users get a different build depending on the URL'
                                        Fix = '' }
    'digest-malformed'             = @{ Fault = 'the archive digest {1} is malformed: {0}'
                                        Why = 'the uploaded digest cannot be read, so the plugin archive cannot be checked against it'
                                        Fix = 'The digest must be one "<sha256>  <plugin archive name>" line.' }
    'digest-name'                  = @{ Fault = "the archive digest names '{0}', not {1}"
                                        Why = '"sha256sum -c" would check the wrong file'
                                        Fix = '' }
    'digest-mismatch'              = @{ Fault = 'the plugin archive {2} does not match the archive digest {3} (digest {0}, archive {1})'
                                        Why = 'the uploaded archive is not the file the digest was made for'
                                        Fix = '' }
    'versioned-differs'            = @{ Fault = 'the versioned archive {0} differs from the plugin archive {1}'
                                        Why = 'the two archives must be the same bytes, or users get a different build depending on the URL'
                                        Fix = '' }
    'archive-unreadable'           = @{ Fault = 'the plugin archive cannot be read as a zip file: {1}'
                                        Why = 'its entries cannot be checked against the archive manifest, and users could not install it'
                                        Fix = '' }
    'manifest-malformed'           = @{ Fault = 'the archive manifest is malformed: {1}'
                                        Why = 'the uploaded manifest cannot be read, so the archive entries cannot be checked'
                                        Fix = "The manifest must list `"<sha256>  <path>`" per line." }
    'hash-mismatch'                = @{ Fault = "archive entry '{0}' differs from the manifest"
                                        Why = 'the archive does not contain what the build staged'
                                        Fix = '' }
    'entry-not-in-manifest'        = @{ Fault = "archive entry '{0}' is not listed in the manifest"
                                        Why = 'the archive holds a file the build did not stage'
                                        Fix = '' }
    'entry-missing-from-archive'   = @{ Fault = "the manifest lists '{0}' but the archive has no such entry"
                                        Why = 'the archive lacks a file the build staged'
                                        Fix = '' }
    'archive-entry-duplicate'      = @{ Fault = "the archive holds the entry '{0}' more than once"
                                        Why = 'an extractor keeps either copy, so the installed file is not what was verified'
                                        Fix = '' }
    'archive-entry-case-collision' = @{ Fault = "archive entry '{0}' collides in case with another entry ({1})"
                                        Why = 'two entries that differ only in case overwrite each other when extracted on Windows'
                                        Fix = '' }
    'archive-entry-unsafe'         = @{ Fault = "archive entry '{0}' has an unsafe name ({1})"
                                        Why = 'an entry like this can extract outside the plugin folder'
                                        Fix = '' }
    'manifest-more'                = @{ Fault = '... and {0} more differences between the archive and the manifest'
                                        Why = 'the archive does not contain what the build staged'
                                        Fix = '' }
    'latest-mismatch'              = @{ Fault = "the plugin archive users download from releases/latest (hash {0}) does not match the published archive digest ({1})"
                                        Why = 'users would install a different file than the one the release was verified for'
                                        OwnFix = $true
                                        Fix = "Check what releases/latest points at. If a bad release is live, $script:RollbackAdvice" }
    'latest-not-verified'          = @{ Fault = "the plugin archive users download from releases/latest (hash {0}) is not the archive of the release that was just verified (hash {1})"
                                        Why = 'releases/latest still serves another release, or the download has not propagated yet, so users would not get the release that was just published'
                                        OwnFix = $true
                                        Retryable = $true
                                        Fix = 'Wait a minute and run the check again. If it keeps failing, open the Releases page and check which release is marked Latest, and that the publish step marked this one as the latest release.' }
}

function Get-ReleaseFaultCodes {
    [CmdletBinding()]
    param()
    return @($script:Faults.Keys)
}

# An unknown code is a bug in the verifier, so it throws instead of producing a silent blank fault.
function New-Problem {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Code, [object[]]$Values = @())
    if (-not $script:Faults.ContainsKey($Code)) { throw "Unknown release fault code '$Code'" }
    $entry = $script:Faults[$Code]
    return [pscustomobject]@{
        Code = $Code; Fault = ([string]::Format($entry.Fault, $Values)); Why = $entry.Why; Fix = $entry.Fix
        OwnFix = $entry.ContainsKey('OwnFix'); Retryable = $entry.ContainsKey('Retryable')
    }
}

# ----------------------------------------------------------------------------
#  Small checks. Each takes plain data and returns an array of problems.
# ----------------------------------------------------------------------------
# $Prefixes: the asset prefixes of the plugins the release carries.
function Get-AssetLabel {
    param([string]$Name, [string]$Version, [string[]]$Prefixes)
    foreach ($prefix in $Prefixes) {
        $names = Get-PluginAssetNames -Prefix $prefix -Version $Version
        if ($Name -ceq $names.Archive) { return "the plugin archive $Name" }
        if ($Name -ceq $names.Digest) { return "the archive digest $Name" }
        if ($Name -ceq $names.Manifest) { return "the archive manifest $Name" }
        if ($Name -ceq $names.Versioned) { return "the versioned archive $Name" }
    }
    return "the asset $Name"
}

function Get-PresenceProblems {
    param($AssetsByName, [string]$Version, [string[]]$Prefixes)
    $problems = @()
    foreach ($name in (Get-RequiredAssetNames -Version $Version -Prefixes $Prefixes)) {
        if (-not $AssetsByName.ContainsKey($name)) { $problems += New-Problem 'asset-missing' @((Get-AssetLabel $name $Version $Prefixes)) }
    }
    return $problems
}

# EVERY asset of the release, including ones this workflow never builds: a hand-attached extra is also a fault.
function Get-UploaderProblems {
    param([object[]]$Assets, [string]$Version, [string[]]$Prefixes)
    $problems = @()
    foreach ($asset in $Assets) {
        if ($asset.Uploader -cne $ReleaseUploaderLogin) { $problems += New-Problem 'hand-uploaded-asset' @((Get-AssetLabel $asset.Name $Version $Prefixes), $asset.Uploader) }
    }
    return $problems
}

# $Names: one plugin's names (Get-PluginAssetNames); the checks below each judge one plugin.
function Get-VersionedSizeProblems {
    param($AssetsByName, $Names)
    if (-not ($AssetsByName.ContainsKey($Names.Versioned) -and $AssetsByName.ContainsKey($Names.Archive))) { return @() }
    $versioned = $AssetsByName[$Names.Versioned]
    $plugin = $AssetsByName[$Names.Archive]
    if ($null -eq $versioned.Size -or $null -eq $plugin.Size -or $versioned.Size -eq $plugin.Size) { return @() }
    return @(New-Problem 'versioned-size-differs' @("the versioned archive $($Names.Versioned)", $versioned.Size, $plugin.Size))
}

function Get-DigestProblems {
    param($AssetsByName, $Names, [string]$ArchiveHash)
    if (-not $AssetsByName.ContainsKey($Names.Digest)) { return @() }
    try {
        $digest = ConvertFrom-DigestText -Text (Read-Utf8Text $AssetsByName[$Names.Digest].Path)
    } catch {
        return @(New-Problem 'digest-malformed' @($_.Exception.Message, $Names.Digest))
    }
    if ($digest.Name -cne $Names.Archive) { return @(New-Problem 'digest-name' @($digest.Name, $Names.Archive)) }
    if ($digest.Hash -cne $ArchiveHash) { return @(New-Problem 'digest-mismatch' @($digest.Hash, $ArchiveHash, $Names.Archive, $Names.Digest)) }
    return @()
}

function Get-ManifestProblems {
    param($AssetsByName, $Names, [string]$ArchivePath)
    if (-not $AssetsByName.ContainsKey($Names.Manifest)) { return @() }
    $differences = @(Compare-ArchiveManifest -ArchivePath $ArchivePath -ManifestText (Read-Utf8Text $AssetsByName[$Names.Manifest].Path))
    $problems = @()
    foreach ($difference in ($differences | Select-Object -First $script:MaxManifestProblemsShown)) {
        $problems += New-Problem $difference.Kind @($difference.Path, $difference.Detail)
    }
    if ($differences.Count -gt $script:MaxManifestProblemsShown) { $problems += New-Problem 'manifest-more' @($differences.Count - $script:MaxManifestProblemsShown) }
    return $problems
}

function Get-VersionedArchiveProblems {
    param($AssetsByName, $Names, [string]$ArchiveHash)
    if (-not $AssetsByName.ContainsKey($Names.Versioned)) { return @() }
    if ((Get-FileSha256 $AssetsByName[$Names.Versioned].Path) -ceq $ArchiveHash) { return @() }
    return @(New-Problem 'versioned-differs' @($Names.Versioned, $Names.Archive))
}

# ----------------------------------------------------------------------------
#  The judge
# ----------------------------------------------------------------------------
function Get-ReleaseProblems {
    [CmdletBinding()]
    param(
        [object[]]$Assets = @(),
        [Parameter(Mandatory)][string]$Version,
        [bool]$CheckUploader = $true,
        [ValidateSet('Listing', 'Content', 'All')][string]$Scope = 'All',
        [string[]]$Prefixes = @($ReleaseAssetPrefix)
    )
    # Asset names match exactly: 'Teststand-MCP.zip' is not 'teststand-mcp.zip'.
    $assetsByName = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($asset in $Assets) { $assetsByName[$asset.Name] = $asset }

    $problems = @()
    if ($Scope -ne 'Content') {
        $problems += @(Get-PresenceProblems $assetsByName $Version $Prefixes)
        if ($CheckUploader) { $problems += @(Get-UploaderProblems $Assets $Version $Prefixes) }
    }
    foreach ($prefix in $Prefixes) {
        $names = Get-PluginAssetNames -Prefix $prefix -Version $Version
        if ($Scope -ne 'Content') { $problems += @(Get-VersionedSizeProblems $assetsByName $names) }
        if ($Scope -eq 'Listing' -or -not $assetsByName.ContainsKey($names.Archive)) { continue }
        $archivePath = $assetsByName[$names.Archive].Path
        $archiveHash = Get-FileSha256 $archivePath
        $problems += @(Get-DigestProblems $assetsByName $names $archiveHash)
        $problems += @(Get-ManifestProblems $assetsByName $names $archivePath)
        $problems += @(Get-VersionedArchiveProblems $assetsByName $names $archiveHash)
    }
    return $problems
}

# ----------------------------------------------------------------------------
#  Report
# ----------------------------------------------------------------------------
# -Mode Api (a draft on GitHub), ApiPublished (a release that is already public) or Local (staged assets):
# the fix differs, the faults do not.
function Format-ReleaseFailure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Subject, [Parameter(Mandatory)][object[]]$Problems, [ValidateSet('Api', 'ApiPublished', 'Local')][string]$Mode = 'Api')
    $reasons = @($Problems | ForEach-Object { $_.Why } | Select-Object -Unique)
    # The common step (start over) once, then each fault's own extra advice; a fault with OwnFix needs no restart.
    $fixes = @()
    if (@($Problems | Where-Object { -not $_.OwnFix }).Count -gt 0) { $fixes += $script:RestartFix[$Mode] }
    $fixes += @($Problems | ForEach-Object { $_.Fix } | Where-Object { $_ } | Select-Object -Unique)
    $whyPrefix = if (@($Problems | Where-Object { -not $_.Retryable }).Count -eq 0) { $script:MayRetryWhy } else { $script:NeverRetriesWhy }
    return @{
        What = "$Subject must not go live: " + ((@($Problems | ForEach-Object { $_.Fault })) -join '; ') + '.'
        Why  = "${whyPrefix}: " + ($reasons -join '; ') + '.'
        Fix  = ($fixes -join "`n")
    }
}

# ----------------------------------------------------------------------------
#  The asset set that was verified is the asset set that goes live. The release workflow calls this
#  (through Assert-ReleaseAssetSet.ps1) on the body of GET releases/{id} just BEFORE it publishes the
#  verified draft and again just AFTER: a person or a stray job could add, replace or remove an asset
#  between the verification and the publish call, and the verification would then say nothing about what
#  users download. Asset ids are compared (a replaced asset gets a new id), as a set: order is irrelevant.
#  -ExpectedAssetIds is the verifier's asset_ids output, {"<asset name>": <id>, ...}.
# ----------------------------------------------------------------------------
$script:AssetIdPattern = '\A[0-9]+\z'
$script:AssetSetFixes = @{
    Restart = 'Delete the draft (gh release delete <tag>, or in the web UI) and start a new release run: the verified assets are no longer what the release holds.'
    NotLive = 'The release is still a draft, so nothing is live: start a new release run (delete the draft first).'
    Live    = "The release is already live with assets nobody verified: $script:RollbackAdvice"
}

function ConvertFrom-ExpectedAssetIds {
    param([AllowEmptyString()][string]$Text)
    $fail = {
        param($Reason)
        Write-BuildFailure -What "The verified asset ids are unusable: $Reason." `
            -Why 'They are the verifier''s asset_ids output, the only record of what was verified; without them the release cannot be compared with it. Running it again will not help.' `
            -Fix 'Check that the verification step wrote asset_ids to its step output, then start a new release run.'
    }
    if ([string]::IsNullOrWhiteSpace($Text)) { & $fail 'the value is empty' }
    try { $json = $Text | ConvertFrom-Json } catch { & $fail 'it is not valid JSON' }
    $expected = New-OrdinalOrderedMap   # id -> asset name
    foreach ($property in @(Get-PropertyNames $json)) {
        $id = [string](Get-JsonProperty $json $property)
        if ($id -cnotmatch $script:AssetIdPattern) { & $fail "'$property' has the id '$id', which is not a number" }
        $expected[$id] = $property
    }
    if ($expected.Count -eq 0) { & $fail 'it lists no asset' }
    return $expected
}

function Assert-ReleaseAssetSet {
    [CmdletBinding()]
    param(
        [AllowEmptyString()][string]$ReleaseJson,
        [AllowEmptyString()][string]$ExpectedAssetIds,
        [AllowEmptyString()][string]$ReleaseId,
        [ValidateSet('BeforePublish', 'AfterPublish')][string]$Stage
    )
    $after = ($Stage -ceq 'AfterPublish')
    $when = if ($after) { 'after the publish call' } else { 'before publishing' }
    $expected = ConvertFrom-ExpectedAssetIds $ExpectedAssetIds

    $release = $null
    if (-not [string]::IsNullOrWhiteSpace($ReleaseJson)) { try { $release = $ReleaseJson | ConvertFrom-Json } catch { $release = $null } }
    if ($null -eq $release -or $null -eq $release.PSObject.Properties['assets']) {   # an empty list is still a list
        Write-BuildFailure -What "The release response $when is not a release (empty, not JSON, or without an asset list)." `
            -Why 'The asset set cannot be compared without it. Running it again may help if the API call failed transiently.' `
            -Fix $(if ($after) { $script:AssetSetFixes.Live } else { $script:AssetSetFixes.Restart })
    }

    $problems = @()
    $actualId = [string](Get-JsonProperty $release 'id')
    if ($actualId -cne $ReleaseId) { $problems += "the API answered with release $actualId, not release $ReleaseId" }
    $draft = Get-JsonProperty $release 'draft'
    $isLive = $false
    if ($draft -isnot [bool]) {
        $problems += 'the response has no draft flag'
    } else {
        $isLive = (-not $draft)
        if (-not $after -and -not $draft) { $problems += 'the release is not a draft any more' }
        if ($after -and $draft) { $problems += 'the release is still a draft' }
    }

    $actual = New-OrdinalOrderedMap   # id -> asset name
    foreach ($asset in @(Get-JsonProperty $release 'assets')) {
        if ($null -eq $asset) { continue }
        $actual[[string](Get-JsonProperty $asset 'id')] = [string](Get-JsonProperty $asset 'name')
    }
    foreach ($id in $expected.Keys) { if (-not $actual.Contains($id)) { $problems += "$($expected[$id]) (id $id) is missing" } }
    foreach ($id in $actual.Keys) { if (-not $expected.Contains($id)) { $problems += "$($actual[$id]) (id $id) was not verified" } }

    if ($problems.Count -eq 0) {
        Write-Host "Release $ReleaseId holds exactly the $($expected.Count) verified assets ($(if ($isLive) { 'published' } else { 'draft' }))."
        return
    }
    $fix = if ($isLive) { $script:AssetSetFixes.Live } elseif ($after) { $script:AssetSetFixes.NotLive } else { $script:AssetSetFixes.Restart }
    $state = if ($isLive) { 'The release is live: it' } else { 'The release' }
    Write-BuildFailure -What "$state does not match what was verified ($when): $($problems -join '; ')." `
        -Why 'Publishing, or having published, assets the verification never saw would make its result meaningless. Running it again will not help: the release changed after it was verified.' `
        -Fix $fix
}

Export-ModuleMember -Function Get-ReleaseProblems, New-Problem, Get-ReleaseFaultCodes, Format-ReleaseFailure, Assert-ReleaseAssetSet
