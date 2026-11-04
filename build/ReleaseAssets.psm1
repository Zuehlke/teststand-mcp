# ============================================================================
#  ReleaseAssets.psm1 - the release asset contract, shared by every build script:
#    - asset names, the repository slug and the "latest download" URL;
#    - the release-tag format rule and its diagnosis texts (ONE map);
#    - the archive digest (sha256sum -c format), the archive manifest and the
#      comparison of an archive against it;
#    - the own-downloads record (what this repository's workflows downloaded),
#      which the stats collector subtracts from the public download counts.
#  The verdict on a release (fault catalog, judge, rollback policy, asset-set check) lives in ReleaseVerdict.psm1.
#  Failures go through Write-BuildFailure (BuildCommon.psm1).
#  Compatible with Windows PowerShell 5.1 and PowerShell 7.
# ============================================================================
Set-StrictMode -Version 2.0

Import-Module (Join-Path $PSScriptRoot 'BuildCommon.psm1')

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# ----------------------------------------------------------------------------
#  Asset contract (exported, read-only)
# ----------------------------------------------------------------------------
Set-Variable -Name ReleaseRepoSlug -Value 'Zuehlke/teststand-mcp' -Option ReadOnly -Scope Script
# The x86 plugin's asset prefix. Each plugin's assets start with its own prefix (PluginIdentity.psm1
# assetPrefix: teststand-mcp, teststand-mcp-x64), so one glob ($ReleaseAssetPrefix*) picks up both.
# The Release*Name variables are the x86 names; Get-PluginAssetNames gives any plugin's.
Set-Variable -Name ReleaseAssetPrefix -Value 'teststand-mcp' -Option ReadOnly -Scope Script
Set-Variable -Name ReleaseArchiveName -Value "$ReleaseAssetPrefix.zip" -Option ReadOnly -Scope Script
Set-Variable -Name ReleaseDigestName -Value "$ReleaseAssetPrefix.sha256" -Option ReadOnly -Scope Script
Set-Variable -Name ReleaseManifestName -Value "$ReleaseAssetPrefix.manifest.sha256" -Option ReadOnly -Scope Script
Set-Variable -Name ReleaseUploaderLogin -Value 'github-actions[bot]' -Option ReadOnly -Scope Script

$script:LatestDownloadUrlFormat = 'https://github.com/{0}/releases/latest/download/{1}'
$script:AssetPrefixPattern = '\A[a-z0-9][a-z0-9-]*\z'

# The four asset names of one plugin. The prefix becomes part of file names and URLs, so it is
# restricted to what a plugin name may be.
function Get-PluginAssetNames {
    [CmdletBinding()]
    param([string]$Prefix = $ReleaseAssetPrefix, [Parameter(Mandatory)][string]$Version)
    if ($Prefix -cnotmatch $script:AssetPrefixPattern) { throw "The asset prefix '$Prefix' is not a plugin name (lowercase letters, digits and dashes)." }
    return [pscustomobject]@{
        Archive   = "$Prefix.zip"
        Digest    = "$Prefix.sha256"
        Manifest  = "$Prefix.manifest.sha256"
        Versioned = "$Prefix-v$Version.zip"
    }
}

# The prefixes a script was given (-AssetPrefixes): split at commas, since powershell -File passes an array
# as one string, and refused unless each is a plugin name. None given: the x86 plugin alone.
function ConvertTo-AssetPrefixList {
    [CmdletBinding()]
    param([AllowEmptyCollection()][AllowNull()][string[]]$Prefixes)
    $list = @($Prefixes | ForEach-Object { "$_" -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($list.Count -eq 0) { return , [string[]]@($ReleaseAssetPrefix) }
    foreach ($prefix in $list) {
        if ($prefix -cnotmatch $script:AssetPrefixPattern) {
            Write-BuildFailure -What "The asset prefix '$prefix' is not a plugin name (lowercase letters, digits and dashes)." `
                -Why 'Each prefix names the assets of one plugin and becomes part of file names and download URLs.' `
                -Fix 'Pass the plugin names, for example -AssetPrefixes teststand-mcp,teststand-mcp-x64 (Get-PluginAssetPrefixes in build\PluginIdentity.psm1 lists them).'
        }
    }
    if (@($list | Select-Object -Unique).Count -ne $list.Count) {
        Write-BuildFailure -What "The asset prefixes $($list -join ', ') name a plugin twice." -Why 'Each plugin is one set of four assets.' -Fix 'Name each plugin once.'
    }
    return , [string[]]$list
}

function Get-VersionedArchiveName {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Version, [string]$Prefix = $ReleaseAssetPrefix)
    return (Get-PluginAssetNames -Prefix $Prefix -Version $Version).Versioned
}

# Per plugin, in download order: the plugin archive first (every other check needs it), the small
# digest and manifest next, and the second full-size archive last so a cheap fault stops the run before it.
function Get-RequiredAssetNames {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Version, [string[]]$Prefixes = @($ReleaseAssetPrefix))
    return @(foreach ($prefix in $Prefixes) {
        $names = Get-PluginAssetNames -Prefix $prefix -Version $Version
        $names.Archive, $names.Digest, $names.Manifest, $names.Versioned
    })
}

# A folder of release assets holds exactly the required files of the version: no missing one, no extra one
# (an extra file would be uploaded as a release asset nobody verifies). Subfolders are not assets.
function Assert-ReleaseAssetFiles {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][string]$Version, [string[]]$Prefixes = @($ReleaseAssetPrefix))
    $resolved = Resolve-BuildPath $Directory
    $expected = @(Get-RequiredAssetNames -Version $Version -Prefixes $Prefixes)
    $present = @()
    if (Test-Path -LiteralPath $resolved -PathType Container) { $present = @(Get-ChildItem -LiteralPath $resolved -File | ForEach-Object { $_.Name }) }
    $missing = @($expected | Where-Object { $present -cnotcontains $_ })
    $unexpected = @($present | Where-Object { $expected -cnotcontains $_ })
    if ($missing.Count -eq 0 -and $unexpected.Count -eq 0) { return }
    $detail = @()
    if ($missing.Count -gt 0) { $detail += "missing: $($missing -join ', ')" }
    if ($unexpected.Count -gt 0) { $detail += "unexpected: $($unexpected -join ', ')" }
    Write-BuildFailure -What "The asset folder $Directory does not hold exactly the $($expected.Count) release assets of version $Version ($($detail -join '; '))." `
        -Why 'Every file of the folder would be uploaded to the release, so a missing asset makes an incomplete release and an extra one ships a file nobody verified. Running it again will not help.' `
        -Fix "Check what the package job uploaded (the release-assets artifact): it must hold exactly $($expected -join ', ')."
}

# owner/name, as the GitHub API and gh take it; it becomes part of a URL, so only letters, digits, dot, dash and underscore.
function Assert-RepoSlug {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Repo)
    $segments = @($Repo -split '/')
    if ($Repo -cnotmatch '\A[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+\z' -or @($segments | Where-Object { $_ -cmatch '\A\.+\z' }).Count -gt 0) {
        Write-BuildFailure -What "The repository '$Repo' is not owner/name." -Why 'It becomes part of the API URL, so it is restricted to letters, digits, dot, dash and underscore.' -Fix "Pass -Repo $ReleaseRepoSlug."
    }
}

# -Repo names another repository (a fork rehearsing the release); the default is the contract repository.
function Get-LatestDownloadUrl {
    [CmdletBinding()]
    param([string]$AssetName = $ReleaseArchiveName, [string]$Repo = $ReleaseRepoSlug)
    return ($script:LatestDownloadUrlFormat -f $Repo, $AssetName)
}

# ----------------------------------------------------------------------------
#  Release tag: ONE ordered rule map. The first rule whose pattern matches names
#  the problem; a rule with a Candidate script also offers the unambiguous fix.
#  [0-9] and \A..\z on purpose: \d matches non-ASCII digits and $ matches before a final newline.
# ----------------------------------------------------------------------------
$script:ValidTagPattern = '\Av(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z'
Set-Variable -Name ReleaseRehearsalSuffix -Value '-rehearsal' -Option ReadOnly -Scope Script   # the dry-run tag is v<X.Y.Z>-rehearsal
$script:TagFormatReason = 'Release tags must be exactly v<major>.<minor>.<patch>: a lower-case v and three numbers without leading zeros. ' +
    'The release workflow, the asset name teststand-mcp-vX.Y.Z.zip and the version stamped into the plugin all derive from the tag.'

$script:TagRules = @(
    @{ Label = 'empty';             Pattern = '\A\s*\z';                           Why = 'The tag name is empty.' }
    @{ Label = 'upper-case V';      Pattern = '\AV[0-9]';                          Why = "The tag starts with an upper-case 'V'. GitHub's v* tag filter is case-sensitive, so only the all-tags trigger sees it.";
                                    Candidate = { param($Tag) 'v' + $Tag.Substring(1) } }
    @{ Label = 'no v';              Pattern = '\A[0-9]';                           Why = "The tag has no leading 'v'.";
                                    Candidate = { param($Tag) 'v' + $Tag } }
    @{ Label = 'two components';    Pattern = '\Av[0-9]+\.[0-9]+\z';               Why = 'The tag has two numeric components; three are required.' }
    @{ Label = 'four components';   Pattern = '\Av[0-9]+(\.[0-9]+){3,}\z';         Why = 'The tag has more than three numeric components; exactly three are required.' }
    @{ Label = 'suffix';            Pattern = '\Av[0-9]+\.[0-9]+\.[0-9]+[^0-9]';   Why = 'The tag has a suffix after the three numbers (for example -rc); none is allowed.' }
    @{ Label = 'leading zero';      Pattern = '\Av[0-9]+\.[0-9]+\.[0-9]+\z';       Why = 'A number in the tag has a leading zero; write 1, not 01.' }
    @{ Label = 'not a version';     Pattern = '(?s)\A.*\z';                        Why = 'The tag does not look like a version at all.' }
)

# -AllowRehearsal additionally accepts v<X.Y.Z>-rehearsal (the dry-run tag): Version is X.Y.Z, IsRehearsal is true.
function ConvertFrom-ReleaseTag {
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$Tag, [switch]$AllowRehearsal)

    if ($null -eq $Tag) { $Tag = '' }
    $valid = {
        param([string]$Version, [bool]$IsRehearsal)
        $verdict = "valid, version $Version"
        if ($IsRehearsal) { $verdict += ' (rehearsal)' }
        return [pscustomobject]@{
            Tag = $Tag; IsValid = $true; IsRehearsal = $IsRehearsal; Version = $Version; Verdict = $verdict
            Label = $null; Why = $null; SuggestedTag = $null
        }
    }

    if ([regex]::IsMatch($Tag, $script:ValidTagPattern)) { return (& $valid $Tag.Substring(1) $false) }
    if ($AllowRehearsal -and $Tag.EndsWith($ReleaseRehearsalSuffix, [StringComparison]::Ordinal)) {
        $baseTag = $Tag.Substring(0, $Tag.Length - $ReleaseRehearsalSuffix.Length)
        if ([regex]::IsMatch($baseTag, $script:ValidTagPattern)) { return (& $valid $baseTag.Substring(1) $true) }
    }

    $rule = $script:TagRules | Where-Object { [regex]::IsMatch($Tag, $_.Pattern) } | Select-Object -First 1
    $suggested = $null
    if ($rule.ContainsKey('Candidate')) {
        $candidate = & $rule.Candidate $Tag
        if ([regex]::IsMatch($candidate, $script:ValidTagPattern)) { $suggested = $candidate }   # never invent a version
    }
    return [pscustomobject]@{
        Tag = $Tag; IsValid = $false; IsRehearsal = $false; Version = $null; Verdict = "rejected: $($rule.Label)"
        Label = $rule.Label; Why = $rule.Why; SuggestedTag = $suggested
    }
}

# Control characters never reach the output (the tag is user input).
function Get-PrintableTag {
    param([string]$Tag)
    return [regex]::Replace($Tag, '[\x00-\x1f\x7f]', '?')
}

function Format-NumberedSteps {
    param([string[]]$Steps)
    return ((1..$Steps.Count | ForEach-Object { "  $_. $($Steps[$_ - 1])" }) -join "`n")
}

# A tag is printed inside runnable commands only when it is made of these characters and does not
# start with '-' (git would read it as an option). Anything else gets an instruction, never a command.
$script:SafeTagPattern = '\A[A-Za-z0-9._/+]+[A-Za-z0-9._/+-]*\z'
Set-Variable -Name ReleaseCommitPattern -Value '\A[0-9a-f]{40}\z' -Option ReadOnly -Scope Script   # a full commit SHA, lower-case
$script:CommitPlaceholder = '<commit>'
$script:ChosenTagPlaceholder = 'vX.Y.Z'

# The What/Why/Fix of a rejected tag (splat into Write-BuildFailure).
function Get-TagRejection {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Result, [string]$Commit)

    $shownTag = Get-PrintableTag $Result.Tag
    $isEmpty = [string]::IsNullOrWhiteSpace($Result.Tag)
    $isSafeToPrint = [regex]::IsMatch($Result.Tag, $script:SafeTagPattern)
    $commitKnown = [regex]::IsMatch([string]$Commit, $ReleaseCommitPattern)
    $commitText = if ($commitKnown) { $Commit } else { $script:CommitPlaceholder }
    $newTag = if ($null -ne $Result.SuggestedTag) { $Result.SuggestedTag } else { $script:ChosenTagPlaceholder }

    $createSteps = @("git tag $newTag $commitText", "git push origin $newTag")
    $steps = @()
    if ($isSafeToPrint) {
        if (-not $commitKnown) { $steps += "git rev-list -n 1 refs/tags/$($Result.Tag)    # prints the commit the bad tag points at: use it as <commit> below" }
        $steps += @("git push origin --delete refs/tags/$($Result.Tag)    # only if the bad tag was pushed", "git tag -d -- $($Result.Tag)")
    }

    if ($isEmpty) {
        $fix = "No tag name was given (in CI the TAG variable was empty), so there is nothing to delete. Pass the tag: -Tag vX.Y.Z"
    } elseif (-not $isSafeToPrint) {
        $fix = "The tag name contains characters that are not safe to paste into a shell, so no commands are printed for it. " +
            "Delete it in the GitHub web UI (repository > Tags) or with the gh CLI, and delete the local tag the same way (git tag --list shows the exact name). " +
            "Note the commit it points at first.`nThen choose the version you mean, write it as vX.Y.Z, and create and push the new tag (PowerShell):`n" +
            (Format-NumberedSteps $createSteps)
    } elseif ($null -ne $Result.SuggestedTag) {
        $fix = "Retag in this order (delete the remote tag, delete the local tag, create the new tag, push it). These are PowerShell commands:`n" +
            (Format-NumberedSteps ($steps + $createSteps))
    } else {
        $fix = "Choose the version you mean and write it as vX.Y.Z; this script does not guess one. First remove the bad tag (remote first); these are PowerShell commands:`n" +
            (Format-NumberedSteps $steps) +
            "`nThen create and push the new tag, replacing $($script:ChosenTagPlaceholder) with your choice:`n" +
            (Format-NumberedSteps $createSteps)
    }

    return @{
        What = "The tag '$shownTag' cannot be released ($($Result.Verdict))."
        Why  = "$($Result.Why) $script:TagFormatReason No asset was built or published."
        Fix  = $fix
    }
}

# ----------------------------------------------------------------------------
#  Hashing and hash lines ("<hash>  <path>": sha256sum text mode)
# ----------------------------------------------------------------------------
$script:Sha256HexLength = 64
$script:HashLineSeparator = '  '
$script:HashLinePattern = '\A([0-9a-f]{' + $script:Sha256HexLength + '})' + [regex]::Escape($script:HashLineSeparator) + '(\S.*)\z'

function Format-HashLine {
    param([Parameter(Mandatory)][string]$Hash, [Parameter(Mandatory)][string]$Path)
    return ($Hash + $script:HashLineSeparator + $Path)
}

function ConvertTo-HexString {
    param([byte[]]$Bytes)
    return ([BitConverter]::ToString($Bytes)).Replace('-', '').ToLowerInvariant()
}

function Get-StreamSha256 {
    param([Parameter(Mandatory)][IO.Stream]$Stream)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return (ConvertTo-HexString $sha.ComputeHash($Stream)) } finally { $sha.Dispose() }
}

function Get-FileSha256 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    return (Get-FileHash -LiteralPath (Resolve-BuildPath $Path) -Algorithm SHA256).Hash.ToLowerInvariant()
}

function New-OrdinalOrderedMap {
    return New-Object System.Collections.Specialized.OrderedDictionary ([StringComparer]::Ordinal)
}

# The text of a digest or manifest as an ordinal, ordered path -> hash map. Throws a plain string on a bad line.
function ConvertFrom-HashLines {
    param([AllowEmptyString()][string]$Text)
    $entries = New-OrdinalOrderedMap
    $lineNumber = 0
    foreach ($line in ($Text -split "\n")) {
        $lineNumber++
        $line = $line.TrimEnd("`r")
        if ($line.Length -eq 0) { continue }
        $match = [regex]::Match($line, $script:HashLinePattern)
        if (-not $match.Success) { throw "line $lineNumber is not '<$($script:Sha256HexLength) hex digits>$($script:HashLineSeparator)<path>'" }
        $path = $match.Groups[2].Value
        if ($entries.Contains($path)) { throw "line $lineNumber repeats the path '$path'" }
        $entries[$path] = $match.Groups[1].Value
    }
    return $entries
}

function ConvertFrom-DigestText {
    [CmdletBinding()]
    param([AllowEmptyString()][string]$Text)
    $entries = ConvertFrom-HashLines $Text
    if ($entries.Count -ne 1) { throw "a digest holds exactly one '<hash>  <name>' line but this one holds $($entries.Count)" }
    $name = @($entries.Keys)[0]
    return [pscustomobject]@{ Name = $name; Hash = $entries[$name] }
}

# One hash line + LF: what `sha256sum -c` reads when run next to the archive.
function New-ArchiveDigest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ArchivePath, [string]$Name)
    $resolved = Resolve-BuildPath $ArchivePath
    if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
        Write-BuildFailure -What "Cannot compute the digest: $ArchivePath does not exist." `
            -Why 'The digest is written next to the archive it describes.' -Fix 'Build the archive first, then pass its path.'
    }
    if ([string]::IsNullOrEmpty($Name)) { $Name = Split-Path -Leaf $resolved }
    return ((Format-HashLine -Hash (Get-FileSha256 $resolved) -Path $Name) + "`n")
}

# One hash line per file of the staged tree, ordinal-sorted by path, forward slashes.
function New-ArchiveManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$StagedDir)
    $resolved = Resolve-BuildPath $StagedDir
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        Write-BuildFailure -What "Cannot build the manifest: $StagedDir is not a directory." `
            -Why 'The manifest lists every file of the staged tree that becomes the archive.' -Fix 'Stage the plugin first, then pass the staging directory.'
    }
    $names = Get-RelativeFileList -Dir $resolved
    if ($names.Count -eq 0) {
        Write-BuildFailure -What "Cannot build the manifest: $StagedDir holds no files." `
            -Why 'An empty manifest would pass any archive.' -Fix 'Check that the staging step copied the plugin files.'
    }
    $lines = @($names | ForEach-Object { Format-HashLine -Hash (Get-FileSha256 (Join-Path $resolved $_)) -Path $_ })
    return (($lines -join "`n") + "`n")
}

# ----------------------------------------------------------------------------
#  Archive problems: every Kind is a member of ONE exported set; the verdict module's fault
#  map must cover each of them (a test asserts it).
# ----------------------------------------------------------------------------
Set-Variable -Name ArchiveProblemKinds -Option ReadOnly -Scope Script -Value ([string[]]@(
    'archive-unreadable', 'manifest-malformed', 'hash-mismatch', 'entry-not-in-manifest', 'entry-missing-from-archive',
    'archive-entry-duplicate', 'archive-entry-case-collision', 'archive-entry-unsafe'
))

function New-ArchiveProblem {
    param([string]$Kind, [string]$Path, [string]$Detail)
    if ($ArchiveProblemKinds -cnotcontains $Kind) { throw "Unknown archive problem kind '$Kind'" }
    return [pscustomobject]@{ Kind = $Kind; Path = $Path; Detail = $Detail }
}

# A zip entry name that would extract outside the target, or is not a portable path.
function Get-UnsafeEntryReason {
    param([string]$Name)
    if ($Name.Length -eq 0) { return 'the name is empty' }
    if ($Name.Contains('\')) { return "the name uses a backslash; zip entry names must use '/'" }
    if ($Name.StartsWith('/', [StringComparison]::Ordinal)) { return 'the name is an absolute path' }
    if ([regex]::IsMatch($Name, '\A[A-Za-z]:')) { return 'the name starts with a drive letter' }
    if (($Name.TrimEnd('/') -split '/') -ccontains '..') { return "the name contains a '..' segment" }
    return $null
}

# The file entries of a zip as an ordinal name -> hash map, plus the problems found while reading them
# (duplicate, case-colliding and unsafe names). Throws InvalidDataException when the zip is unreadable.
function Get-ArchiveEntryTable {
    param([Parameter(Mandatory)][string]$ArchivePath)
    $hashes = New-OrdinalOrderedMap
    $problems = New-Object System.Collections.Generic.List[object]
    $seenIgnoringCase = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
    $zip = [IO.Compression.ZipFile]::OpenRead((Resolve-BuildPath $ArchivePath))
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName
            $unsafeReason = Get-UnsafeEntryReason $name
            if ($null -ne $unsafeReason) { $problems.Add((New-ArchiveProblem 'archive-entry-unsafe' $name $unsafeReason)); continue }
            $isDirectory = $name.EndsWith('/', [StringComparison]::Ordinal)
            if ($isDirectory) { continue }
            if ($hashes.Contains($name)) { $problems.Add((New-ArchiveProblem 'archive-entry-duplicate' $name 'the archive holds this entry name more than once')); continue }
            if ($seenIgnoringCase.ContainsKey($name)) {
                $problems.Add((New-ArchiveProblem 'archive-entry-case-collision' $name "it differs only in case from '$($seenIgnoringCase[$name])', which collide when extracted on Windows"))
            } else {
                $seenIgnoringCase[$name] = $name
            }
            $stream = $entry.Open()
            try { $hashes[$name] = Get-StreamSha256 $stream } finally { $stream.Dispose() }
        }
    } finally { $zip.Dispose() }
    return [pscustomobject]@{ Hashes = $hashes; Problems = $problems.ToArray() }
}

# Compares the entries of a zip with a manifest. Returns one problem object per difference
# (Kind, Path, Detail); an empty result means they agree.
function Compare-ArchiveManifest {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ArchivePath, [Parameter(Mandatory)][AllowEmptyString()][string]$ManifestText)

    try {
        $expected = ConvertFrom-HashLines $ManifestText
    } catch {
        return @(New-ArchiveProblem 'manifest-malformed' '' $_.Exception.Message)
    }
    if ($expected.Count -eq 0) { return @(New-ArchiveProblem 'manifest-malformed' '' 'the manifest lists no files') }

    try {
        $table = Get-ArchiveEntryTable -ArchivePath $ArchivePath
    } catch [System.IO.InvalidDataException] {
        return @(New-ArchiveProblem 'archive-unreadable' '' $_.Exception.Message)
    }

    $problems = New-Object System.Collections.Generic.List[object]
    foreach ($problem in $table.Problems) { $problems.Add($problem) }
    foreach ($path in $expected.Keys) {
        if (-not $table.Hashes.Contains($path)) {
            $problems.Add((New-ArchiveProblem 'entry-missing-from-archive' $path 'the manifest lists it but the archive has no such entry'))
        } elseif ($table.Hashes[$path] -cne $expected[$path]) {
            $problems.Add((New-ArchiveProblem 'hash-mismatch' $path "manifest $($expected[$path]) but archive $($table.Hashes[$path])"))
        }
    }
    foreach ($path in $table.Hashes.Keys) {
        if (-not $expected.Contains($path)) {
            $problems.Add((New-ArchiveProblem 'entry-not-in-manifest' $path 'the archive has the entry but the manifest does not list it'))
        }
    }
    return @($problems | Sort-Object -Property Path, Kind)
}

# ----------------------------------------------------------------------------
#  Own-downloads record: what THIS repository's workflows downloaded, per date, asset and phase.
#  { "run_id": "...", "counts": { "<YYYY-MM-DD>": { "<asset>": { "draft": n, "published": m } } } }
#  The stats collector subtracts these from the public download counts (spec AC-18).
# ----------------------------------------------------------------------------
$script:OwnDownloadsDateFormat = 'yyyy-MM-dd'
Set-Variable -Name OwnDownloadPhases -Value ([string[]]@('draft', 'published')) -Option ReadOnly -Scope Script   # also the ValidateSet of Add-OwnDownload (a test ties them)
$script:OwnDownloadsDateKeyPattern = '\A[0-9]{4}-[0-9]{2}-[0-9]{2}\z'

function Test-OwnDownloadsDate {
    param([string]$Date)
    $parsed = [DateTime]::MinValue
    return ([regex]::IsMatch($Date, $script:OwnDownloadsDateKeyPattern) -and
        [DateTime]::TryParseExact($Date, $script:OwnDownloadsDateFormat, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsed))
}

function New-OwnDownloadsRecord {
    param([string]$RunId)
    return [pscustomobject]@{ RunId = $RunId; Counts = (New-OrdinalOrderedMap) }
}

function Read-OwnDownloads {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $fail = {
        param($Reason)
        Write-BuildFailure -What "The own-downloads record $Path is unusable: $Reason." `
            -Why 'The stats collector subtracts these counts from the public download counts, so a wrong record skews the install numbers.' `
            -Fix 'Delete the file and let the workflow run write a fresh one.'
    }
    $text = Read-Utf8Text $Path
    try { $json = $text | ConvertFrom-Json } catch { & $fail "it is not valid JSON ($($_.Exception.Message))" }
    $runId = Get-JsonProperty $json 'run_id'
    if ([string]::IsNullOrWhiteSpace([string]$runId)) { & $fail "the key 'run_id' is missing or empty" }
    $counts = Get-JsonProperty $json 'counts'
    if ($null -eq $counts) { & $fail "the key 'counts' is missing" }

    $record = New-OwnDownloadsRecord -RunId ([string]$runId)
    foreach ($dateProperty in $counts.PSObject.Properties) {
        if (-not (Test-OwnDownloadsDate $dateProperty.Name)) { & $fail "'$($dateProperty.Name)' is not a YYYY-MM-DD date" }
        $assets = New-OrdinalOrderedMap
        foreach ($assetProperty in $dateProperty.Value.PSObject.Properties) {
            $cell = [ordered]@{}
            foreach ($phase in $OwnDownloadPhases) {
                $value = Get-JsonProperty $assetProperty.Value $phase
                if ($null -eq $value -or $value -is [bool] -or $value -isnot [ValueType] -or [int64]$value -lt 0) { & $fail "$($dateProperty.Name) / $($assetProperty.Name) has no non-negative '$phase' count" }
                $cell[$phase] = [int]$value
            }
            $assets[$assetProperty.Name] = $cell
        }
        $record.Counts[$dateProperty.Name] = $assets
    }
    return $record
}

# Written beside the target and moved over it, so a reader (or a crash) never sees half a file.
function Save-OwnDownloads {
    param([Parameter(Mandatory)]$Record, [Parameter(Mandatory)][string]$Path)
    $target = [IO.Path]::GetFullPath((Resolve-BuildPath $Path))
    $parent = Split-Path -Parent $target
    if (-not (Test-Path -LiteralPath $parent)) { [void](New-Item -ItemType Directory -Path $parent -Force) }
    $document = [ordered]@{ run_id = $Record.RunId; counts = $Record.Counts }
    $temp = Join-Path $parent ('.' + [IO.Path]::GetFileName($target) + '.' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($temp, ((ConvertTo-Json -InputObject $document -Depth 6) + "`n"), (Get-Utf8NoBomEncoding))
        if (Test-Path -LiteralPath $target) { [IO.File]::Replace($temp, $target, [NullString]::Value) } else { [IO.File]::Move($temp, $target) }
    } finally {
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue }
    }
}

# Counts one download of one asset, in the phase it happened in ('draft' release or 'published').
# Call it for every download a workflow makes, immediately before the request.
function Add-OwnDownload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Asset,
        [Parameter(Mandatory)][ValidateSet('draft', 'published')][string]$Phase,
        [string]$RunId = $env:GITHUB_RUN_ID,
        [string]$Date = [DateTime]::UtcNow.ToString($script:OwnDownloadsDateFormat, [Globalization.CultureInfo]::InvariantCulture)
    )
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        Write-BuildFailure -What 'Cannot record a download: no run id.' `
            -Why 'Each record belongs to one workflow run so the collector can match it to that run.' `
            -Fix 'Pass -RunId, or run inside GitHub Actions where GITHUB_RUN_ID is set.'
    }
    if (-not (Test-OwnDownloadsDate $Date)) {
        Write-BuildFailure -What "Cannot record a download: '$Date' is not a YYYY-MM-DD date." `
            -Why 'The collector subtracts the counts per calendar date.' -Fix 'Pass a date such as 2026-08-20.'
    }
    if (Test-Path -LiteralPath (Resolve-BuildPath $Path) -PathType Leaf) {
        $record = Read-OwnDownloads -Path $Path
        if ($record.RunId -cne $RunId) {
            Write-BuildFailure -What "Cannot record a download: $Path already belongs to run $($record.RunId), not run $RunId." `
                -Why 'Mixing runs in one record would make the per-run counts wrong.' `
                -Fix 'Use one own-downloads file per workflow run.'
        }
    } else {
        $record = New-OwnDownloadsRecord -RunId $RunId
    }
    if (-not $record.Counts.Contains($Date)) { $record.Counts[$Date] = New-OrdinalOrderedMap }
    $assets = $record.Counts[$Date]
    if (-not $assets.Contains($Asset)) {
        $cell = [ordered]@{}
        foreach ($known in $OwnDownloadPhases) { $cell[$known] = 0 }
        $assets[$Asset] = $cell
    }
    $assets[$Asset][$Phase] = $assets[$Asset][$Phase] + 1
    Save-OwnDownloads -Record $record -Path $Path
}

Export-ModuleMember -Variable ReleaseRepoSlug, ReleaseAssetPrefix, ReleaseArchiveName, ReleaseDigestName, ReleaseManifestName, ReleaseUploaderLogin, ArchiveProblemKinds,
    ReleaseRehearsalSuffix, ReleaseCommitPattern, OwnDownloadPhases
Export-ModuleMember -Function Get-PluginAssetNames, ConvertTo-AssetPrefixList, Get-VersionedArchiveName, Get-RequiredAssetNames, Assert-ReleaseAssetFiles, Assert-RepoSlug, Get-LatestDownloadUrl,
    Get-FileSha256, Get-StreamSha256, Format-HashLine, ConvertFrom-HashLines,
    ConvertFrom-ReleaseTag, Get-TagRejection, Get-PrintableTag, New-OrdinalOrderedMap,
    New-ArchiveDigest, ConvertFrom-DigestText, New-ArchiveManifest, Compare-ArchiveManifest,
    Read-OwnDownloads, Add-OwnDownload
