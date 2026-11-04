# ============================================================================
#  ReleaseAssets.Tests.ps1 - plain PowerShell asserts for build/ReleaseAssets.psm1 and build/Assert-ReleaseTag.ps1:
#  the asset contract, the release-tag rule and its gate script, the archive digest and manifest, and the own-downloads
#  record. The verdict on a release is ReleaseVerdict.Tests.ps1, the verifier ReleaseVerifier.Tests.ps1.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\ReleaseAssets.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'ReleaseAssetsTests_'
. (Join-Path $PSScriptRoot 'ReleaseTestSupport.ps1')

# The tag table of the Gherkin outline, verbatim.
$TagTable = @(
    @{ Tag = 'v1.9.0';     Verdict = 'valid, version 1.9.0' }
    @{ Tag = 'v1.10.0';    Verdict = 'valid, version 1.10.0' }
    @{ Tag = 'V1.9.0';     Verdict = 'rejected: upper-case V' }
    @{ Tag = 'v1.9';       Verdict = 'rejected: two components' }
    @{ Tag = 'v1.9.0.1';   Verdict = 'rejected: four components' }
    @{ Tag = 'v1.9.0-rc';  Verdict = 'rejected: suffix' }
    @{ Tag = 'v01.9.0';    Verdict = 'rejected: leading zero' }
    @{ Tag = '1.9.0';      Verdict = 'rejected: no v' }
    @{ Tag = 'latest';     Verdict = 'rejected: not a version' }
    @{ Tag = '';           Verdict = 'rejected: empty' }
)

Enter-ReleaseTestEnvironment
try {
    foreach ($path in @($CommonModulePath, $ModulePath, $VerdictModulePath, $DownloadModulePath)) { Assert-True (Test-Path -LiteralPath $path) "$path does not exist" }
    Import-Module $CommonModulePath -Force
    Import-Module $ModulePath -Force

    # ============================================================================================
    #  Asset contract
    # ============================================================================================
    Invoke-Case 'contract: the four asset names and the repo slug are the published ones' {
        Assert-Equal $ExpectedRepoSlug $ReleaseRepoSlug 'repo slug'
        Assert-Equal 'teststand-mcp.zip' $ReleaseArchiveName 'archive'
        Assert-Equal 'teststand-mcp.sha256' $ReleaseDigestName 'digest'
        Assert-Equal 'teststand-mcp.manifest.sha256' $ReleaseManifestName 'manifest'
        Assert-Equal 'github-actions[bot]' $ReleaseUploaderLogin 'uploader'
        Assert-Equal 'teststand-mcp-v1.9.0.zip' (Get-VersionedArchiveName -Version '1.9.0') 'versioned archive'
        Assert-Equal 'teststand-mcp-v0.0.0-ci.zip' (Get-VersionedArchiveName -Version '0.0.0-ci') 'versioned archive of a ci build'
        Assert-Equal 'teststand-mcp.zip|teststand-mcp.sha256|teststand-mcp.manifest.sha256|teststand-mcp-v1.9.0.zip' ((Get-RequiredAssetNames -Version '1.9.0') -join '|') 'required assets in download order'
    }

    Invoke-Case 'contract: every required asset name starts with the asset prefix, so one glob picks the assets up (and nothing else)' {
        Assert-Equal 'teststand-mcp' $ReleaseAssetPrefix 'prefix'
        foreach ($version in @('1.9.0', '0.0.0')) {
            foreach ($name in (Get-RequiredAssetNames -Version $version)) { Assert-True ($name.StartsWith($ReleaseAssetPrefix, [StringComparison]::Ordinal)) "$name does not start with $ReleaseAssetPrefix" }
        }
        Assert-Equal 4 @(Get-RequiredAssetNames -Version '1.9.0' | Select-Object -Unique).Count 'distinct required assets'
    }

    Invoke-Case 'contract: the rehearsal suffix, the commit pattern and the own-downloads phases are defined once, here' {
        Assert-Equal '-rehearsal' $ReleaseRehearsalSuffix 'rehearsal suffix'
        Assert-True ($SampleCommit -cmatch $ReleaseCommitPattern) 'a full lower-case SHA matches'
        foreach ($bad in @($SampleCommit.Substring(1), ($SampleCommit + '0'), $SampleCommit.ToUpperInvariant(), "$SampleCommit`n", '', 'main')) {
            Assert-True ($bad -cnotmatch $ReleaseCommitPattern) "[$bad] is not a full commit SHA"
        }
        Assert-Equal 'draft,published' ($OwnDownloadPhases -join ',') 'the phases'
        $validateSet = @((Get-Command Add-OwnDownload).Parameters['Phase'].Attributes | Where-Object { $_ -is [System.Management.Automation.ValidateSetAttribute] })
        Assert-Equal 1 $validateSet.Count 'Add-OwnDownload -Phase has one ValidateSet'
        Assert-Equal ($OwnDownloadPhases -join ',') (@($validateSet[0].ValidValues) -join ',') 'the ValidateSet of -Phase is the phase list (a literal copy of it would drift)'
    }

    Invoke-Case 'contract: a new own-downloads cell has exactly the phases of the list, each at zero' {
        $path = Join-Path (New-TempDir) 'o.json'
        Add-OwnDownload -Path $path -RunId '1' -Asset 'a.zip' -Phase published -Date '2026-08-20'
        $cell = (Read-OwnDownloads -Path $path).Counts['2026-08-20']['a.zip']
        Assert-Equal ($OwnDownloadPhases -join ',') (@($cell.Keys) -join ',') 'cell keys, in the order of the phase list'
        Assert-Equal 1 $cell['published'] 'the phase that was counted'
        Assert-Equal 0 $cell['draft'] 'the other phase'
    }

    Invoke-Case 'contract: the exported constants are read-only' {
        foreach ($name in @('ReleaseRepoSlug', 'ReleaseAssetPrefix', 'ReleaseArchiveName', 'ReleaseDigestName', 'ReleaseManifestName', 'ReleaseUploaderLogin', 'ArchiveProblemKinds', 'ReleaseRehearsalSuffix', 'ReleaseCommitPattern', 'OwnDownloadPhases')) {
            $before = (Get-Variable -Name $name -ValueOnly) -join ','
            $thrown = $false
            try { Set-Variable -Name $name -Value 'changed' -Scope Script } catch { $thrown = $true }
            Assert-True $thrown "$name could be overwritten"
            Assert-Equal $before ((Get-Variable -Name $name -ValueOnly) -join ',') "$name after the attempt"
        }
    }

    Invoke-Case 'contract: the latest-download URL builder gives the fixed archive URL' {
        Assert-Equal $ExpectedLatestUrl (Get-LatestDownloadUrl) 'default asset'
        Assert-Equal 'https://github.com/Zuehlke/teststand-mcp/releases/latest/download/teststand-mcp.sha256' (Get-LatestDownloadUrl -AssetName $ReleaseDigestName) 'digest asset'
    }

    Invoke-Case 'contract: Get-LatestDownloadUrl takes the repository of a fork' {
        Assert-Equal 'https://github.com/a/b/releases/latest/download/teststand-mcp.sha256' (Get-LatestDownloadUrl -AssetName 'teststand-mcp.sha256' -Repo 'a/b') 'url'
    }
    Invoke-Case 'contract: marketplace.json source.url equals the URL builder output (ties slice 2 to this owner)' {
        Assert-True (Test-Path -LiteralPath $MarketplacePath) "$MarketplacePath does not exist"
        $marketplace = [IO.File]::ReadAllText($MarketplacePath, $Utf8NoBom) | ConvertFrom-Json
        $plugin = @($marketplace.plugins | Where-Object { $_.name -ceq 'teststand-mcp' })
        Assert-Equal 1 $plugin.Count 'plugins named teststand-mcp'
        Assert-Equal (Get-LatestDownloadUrl) $plugin[0].source.url 'marketplace archive url'
    }

    # ============================================================================================
    #  Tag rule
    # ============================================================================================
    foreach ($row in $TagTable) {
        Invoke-Case "tag table: [$($row.Tag)] -> $($row.Verdict)" {
            $result = ConvertFrom-ReleaseTag -Tag $row.Tag
            Assert-Equal $row.Verdict $result.Verdict 'verdict'
            Assert-Equal $row.Verdict.StartsWith('valid') $result.IsValid 'IsValid'
            Assert-Equal $false $result.IsRehearsal 'IsRehearsal'
        }
    }

    Invoke-Case 'tag: a valid tag yields the plain version; a rejected one has none' {
        Assert-Equal '1.10.0' (ConvertFrom-ReleaseTag -Tag 'v1.10.0').Version 'version'
        Assert-Equal '0.0.0' (ConvertFrom-ReleaseTag -Tag 'v0.0.0').Version 'version of v0.0.0'
        Assert-True ($null -eq (ConvertFrom-ReleaseTag -Tag 'v1.9').Version) 'a rejected tag must have no version'
    }

    Invoke-Case 'tag: null and whitespace are empty' {
        Assert-Equal 'rejected: empty' (ConvertFrom-ReleaseTag -Tag $null).Verdict 'null'
        Assert-Equal 'rejected: empty' (ConvertFrom-ReleaseTag -Tag '   ').Verdict 'whitespace'
    }

    Invoke-Case 'tag: a trailing newline, a leading space and non-ASCII digits are not a version' {
        Assert-True (-not (ConvertFrom-ReleaseTag -Tag "v1.9.0`n").IsValid) 'trailing newline accepted'
        Assert-True (-not (ConvertFrom-ReleaseTag -Tag ' v1.9.0').IsValid) 'leading space accepted'
        Assert-True (-not (ConvertFrom-ReleaseTag -Tag "v${ArabicIndicOne}.9.0").IsValid) 'a non-ASCII digit accepted'
    }

    Invoke-Case 'tag: only a case or missing-v mistake gets a suggested tag' {
        Assert-Equal 'v1.9.0' (ConvertFrom-ReleaseTag -Tag 'V1.9.0').SuggestedTag 'upper-case V'
        Assert-Equal 'v1.9.0' (ConvertFrom-ReleaseTag -Tag '1.9.0').SuggestedTag 'no v'
        foreach ($tag in @('v1.9', 'V1.9', 'v01.9.0', 'v1.9.0-rc', 'v1.9.0.1', 'latest', '1.9', '')) {
            Assert-True ($null -eq (ConvertFrom-ReleaseTag -Tag $tag).SuggestedTag) "no version may be invented for [$tag]"
        }
    }

    Invoke-Case 'tag: -AllowRehearsal accepts only v<X.Y.Z>-rehearsal with a valid base' {
        $rehearsal = ConvertFrom-ReleaseTag -Tag 'v0.0.1-rehearsal' -AllowRehearsal
        Assert-Equal $true $rehearsal.IsValid 'IsValid'
        Assert-Equal $true $rehearsal.IsRehearsal 'IsRehearsal'
        Assert-Equal '0.0.1' $rehearsal.Version 'Version is the plain X.Y.Z'
        Assert-Equal 'v0.0.1-rehearsal' $rehearsal.Tag 'Tag'
        foreach ($tag in @('V0.0.1-rehearsal', 'v0.0-rehearsal', 'v01.0.1-rehearsal', 'v0.0.1-rehearsal2', 'v0.0.1-rc', 'v0.0.1-rehearsal-rehearsal', '-rehearsal')) {
            Assert-Equal $false (ConvertFrom-ReleaseTag -Tag $tag -AllowRehearsal).IsValid "[$tag] with -AllowRehearsal"
        }
        Assert-Equal 'rejected: suffix' (ConvertFrom-ReleaseTag -Tag 'v0.0.1-rehearsal').Verdict 'without -AllowRehearsal'
        Assert-Equal $true (ConvertFrom-ReleaseTag -Tag 'v1.9.0' -AllowRehearsal).IsValid 'a normal tag still passes'
        Assert-Equal $false (ConvertFrom-ReleaseTag -Tag 'v1.9.0' -AllowRehearsal).IsRehearsal 'a normal tag is no rehearsal'
    }

    # ============================================================================================
    #  Tag gate script
    # ============================================================================================
    Invoke-Case 'gate: a valid tag exits 0 and appends version= and tag= to GITHUB_OUTPUT' {
        $outFile = Join-Path (New-TempDir) 'github_output'
        Write-TextFile $outFile "seed=1`n"
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'v1.10.0' }
            Assert-Equal 0 $run.ExitCode 'exit code'
        }
        Assert-NoBom $outFile
        Assert-Equal "seed=1`nversion=1.10.0`ntag=v1.10.0`n" ([IO.File]::ReadAllText($outFile, $Utf8NoBom)) 'GITHUB_OUTPUT text'
    }

    Invoke-Case 'gate: a valid tag without GITHUB_OUTPUT still exits 0' {
        Use-Environment @{ GITHUB_OUTPUT = $null } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0' }
            Assert-Equal 0 $run.ExitCode 'exit code'
            Assert-Contains $run.Output '1.9.0' 'gate output'
        }
    }

    Invoke-Case 'gate: -Rehearsal accepts v0.0.1-rehearsal, writes the plain version, the tag and rehearsal=true' {
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'v0.0.1-rehearsal'; Rehearsal = $true }
            Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        }
        Assert-Equal "version=0.0.1`ntag=v0.0.1-rehearsal`nrehearsal=true`n" ([IO.File]::ReadAllText($outFile, $Utf8NoBom)) 'GITHUB_OUTPUT text'
    }

    Invoke-Case 'gate: a rehearsal tag without -Rehearsal is rejected like any suffix' {
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'v0.0.1-rehearsal' }
            Assert-Equal 1 $run.ExitCode 'exit code'
            Assert-Contains $run.Output 'rejected: suffix' 'verdict'
        }
        Assert-True (-not (Test-Path -LiteralPath $outFile)) 'GITHUB_OUTPUT must not be written for a rejected tag'
    }

    foreach ($row in @($TagTable | Where-Object { -not $_.Verdict.StartsWith('valid') })) {
        Invoke-Case "gate: [$($row.Tag)] exits 1, says '$($row.Verdict)' and writes nothing to GITHUB_OUTPUT" {
            $outFile = Join-Path (New-TempDir) 'github_output'
            Use-Environment @{ GITHUB_OUTPUT = $outFile } {
                $run = Invoke-Script $TagGatePath @{ Tag = $row.Tag }
                Assert-Equal 1 $run.ExitCode 'exit code'
                Assert-Contains $run.Output $row.Verdict 'gate output'
            }
            Assert-True (-not (Test-Path -LiteralPath $outFile)) 'GITHUB_OUTPUT must not be written for a rejected tag'
        }
    }

    # --- Scenario: A fixable tag gets the fix in a safe order -----------------------------------
    Invoke-Case 'gate: V1.9.0 says why the format is required and lists delete remote, delete local, create, push in order' {
        $run = Invoke-Script $TagGatePath @{ Tag = 'V1.9.0'; Commit = $SampleCommit }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'exactly v<major>.<minor>.<patch>' 'why the format is required'
        Assert-Contains $run.Output 'case-sensitive' 'why an upper-case V gets no answer from GitHub'
        Assert-Contains $run.Output 'PowerShell commands' 'which shell'
        Assert-Contains $run.Output 'only if the bad tag was pushed' 'the remote delete caveat'
        $steps = @(
            'git push origin --delete refs/tags/V1.9.0'
            'git tag -d -- V1.9.0'
            "git tag v1.9.0 $SampleCommit"
            'git push origin v1.9.0'
        )
        $previous = -1
        foreach ($step in $steps) {
            $index = $run.Output.IndexOf($step, [StringComparison]::Ordinal)
            Assert-True ($index -ge 0) "retag command missing: $step. Output was:`n$($run.Output)"
            Assert-True ($index -gt $previous) "retag command out of order: $step"
            $previous = $index
        }
        Assert-NotContains $run.Output 'rev-list' 'a commit lookup when the commit is known'
    }

    Invoke-Case 'gate: without a known commit the bad tag is resolved BEFORE it is deleted and the create step uses <commit>' {
        $run = Invoke-Script $TagGatePath @{ Tag = '1.9.0' }
        $lookup = $run.Output.IndexOf('git rev-list -n 1 refs/tags/1.9.0', [StringComparison]::Ordinal)
        $delete = $run.Output.IndexOf('git tag -d -- 1.9.0', [StringComparison]::Ordinal)
        Assert-True ($lookup -ge 0 -and $lookup -lt $delete) "the commit lookup must come before the delete. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'git tag v1.9.0 <commit>' 'create command'
    }

    Invoke-Case 'gate: the commit comes from GITHUB_SHA when it is a full sha' {
        Use-Environment @{ GITHUB_SHA = $SampleCommit } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'V1.9.0' }
            Assert-Contains $run.Output "git tag v1.9.0 $SampleCommit" 'create command'
        }
    }

    # --- Scenario: Only a commit on main is released ---------------------------------------------
    # A repository with one commit on main and one on a side branch that never merged.
    function New-MainFixture {
        $dir = New-TempDir
        $git = { param([string[]]$GitArgs)
            $run = Invoke-ChildProcess -FilePath 'git' -ArgumentList (@('-C', $dir, '-c', 'user.name=t', '-c', 'user.email=t@example.invalid') + $GitArgs) -TimeoutSeconds 60
            Assert-Equal 0 $run.ExitCode "git $($GitArgs -join ' ') said: $($run.Stderr)"
            return $run.Stdout.Trim()
        }
        [void](& $git @('init', '-q', '-b', 'main'))
        [void](& $git @('commit', '-q', '--allow-empty', '-m', 'on main'))
        $onMain = & $git @('rev-parse', 'HEAD')
        [void](& $git @('checkout', '-q', '-b', 'side'))
        [void](& $git @('commit', '-q', '--allow-empty', '-m', 'unmerged'))
        return @{ Dir = $dir; OnMain = $onMain; OffMain = (& $git @('rev-parse', 'HEAD')) }
    }

    Invoke-Case 'gate: -MainRef passes a commit on that branch and writes the outputs' {
        $repo = New-MainFixture
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0'; Commit = $repo.OnMain; MainRef = 'refs/heads/main'; RepoRoot = $repo.Dir }
            Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
            Assert-Contains $run.Output "is on refs/heads/main" 'the check says it ran'
        }
        Assert-Equal "version=1.9.0`ntag=v1.9.0`n" ([IO.File]::ReadAllText($outFile, $Utf8NoBom)) 'GITHUB_OUTPUT text'
    }

    Invoke-Case 'gate: -MainRef refuses a commit that is not on that branch, says how to retag and writes nothing' {
        $repo = New-MainFixture
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            $run = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0'; Commit = $repo.OffMain; MainRef = 'refs/heads/main'; RepoRoot = $repo.Dir }
            Assert-Equal 1 $run.ExitCode 'exit code'
            Assert-Contains $run.Output "points at $($repo.OffMain), which is not on refs/heads/main" 'what'
            Assert-Contains $run.Output 'git push origin --delete refs/tags/v1.9.0' 'the retag starts with the remote delete'
        }
        Assert-True (-not (Test-Path -LiteralPath $outFile)) 'GITHUB_OUTPUT must not be written for a refused commit'
    }

    Invoke-Case 'gate: -MainRef naming a branch the clone does not have fails and says to fetch the full history' {
        $repo = New-MainFixture
        $run = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0'; Commit = $repo.OnMain; MainRef = 'refs/remotes/origin/main'; RepoRoot = $repo.Dir }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'fetch-depth: 0' 'the fix'
    }

    Invoke-Case 'gate: -MainRef without a full commit sha fails before any git call' {
        $repo = New-MainFixture
        $run = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0'; Commit = 'main'; MainRef = 'refs/heads/main'; RepoRoot = $repo.Dir }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'No commit to check against refs/heads/main' 'what'
    }

    Invoke-Case 'gate: -MainRef refuses a short name, which a tag of the same name would shadow' {
        $repo = New-MainFixture
        $tag = Invoke-ChildProcess -FilePath 'git' -ArgumentList @('-C', $repo.Dir, 'tag', 'main', $repo.OffMain) -TimeoutSeconds 60
        Assert-Equal 0 $tag.ExitCode "git tag said: $($tag.Stderr)"
        $short = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0'; Commit = $repo.OffMain; MainRef = 'main'; RepoRoot = $repo.Dir }
        Assert-Equal 1 $short.ExitCode 'a short name is refused'
        Assert-Contains $short.Output 'is not a full ref' 'what'
        $full = Invoke-Script $TagGatePath @{ Tag = 'v1.9.0'; Commit = $repo.OffMain; MainRef = 'refs/heads/main'; RepoRoot = $repo.Dir }
        Assert-Equal 1 $full.ExitCode 'the full ref ignores the shadowing tag'
        Assert-Contains $full.Output 'which is not on refs/heads/main' 'what'
    }

    Invoke-Case 'gate: without -MainRef no ancestry check runs (a dry run from a branch)' {
        $run = Invoke-Script $TagGatePath @{ Tag = 'v0.0.1-rehearsal'; Rehearsal = $true; Commit = $SampleCommit; RepoRoot = (New-TempDir) }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-NotContains $run.Output 'is on ' 'no ancestry check'
    }

    # --- Scenario: An unfixable tag asks for a choice --------------------------------------------
    Invoke-Case 'gate: v1.9 asks the maintainer to choose a vX.Y.Z, shows the delete commands and the create template' {
        $run = Invoke-Script $TagGatePath @{ Tag = 'v1.9'; Commit = $SampleCommit }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'Choose the version you mean' 'asks for a choice'
        Assert-Contains $run.Output 'git push origin --delete refs/tags/v1.9' 'remote delete'
        Assert-Contains $run.Output 'git tag -d -- v1.9' 'local delete'
        Assert-True ($run.Output.IndexOf('git push origin --delete refs/tags/v1.9') -lt $run.Output.IndexOf('git tag -d -- v1.9')) 'remote delete must come first'
        Assert-Contains $run.Output "git tag vX.Y.Z $SampleCommit" 'create template'
        Assert-Contains $run.Output 'git push origin vX.Y.Z' 'push template'
        Assert-True ($run.Output -cnotmatch 'git tag v[0-9]') 'a concrete version was invented'
    }

    Invoke-Case 'gate: a tag that is not safe to paste gets an instruction, never a command' {
        foreach ($tag in @('-v1.9', 'v1.9$(x)', 'v1.9;x', "v1.9`a")) {
            $run = Invoke-Script $TagGatePath @{ Tag = $tag }
            Assert-Equal 1 $run.ExitCode "exit code for [$tag]"
            Assert-Contains $run.Output 'not safe to paste into a shell' "instruction for [$tag]"
            Assert-Contains $run.Output 'GitHub web UI' "instruction for [$tag]"
            Assert-NotContains $run.Output 'git tag -d' "delete command for [$tag]"
            Assert-NotContains $run.Output 'refs/tags/' "tag ref for [$tag]"
            Assert-True ($run.Output.IndexOf([char]7) -lt 0) 'a control character was echoed'
        }
    }

    Invoke-Case 'gate: an empty tag prints no delete commands' {
        $run = Invoke-Script $TagGatePath @{ Tag = '' }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'nothing to delete' 'explanation'
        Assert-NotContains $run.Output 'git tag -d' 'delete command'
        Assert-NotContains $run.Output '--delete' 'remote delete command'
    }

    Invoke-Case 'gate: a child process under -File exits 1 for a bad tag and 0 for a good one, appending to its own GITHUB_OUTPUT' {
        $powershell = (Get-Process -Id $PID).Path
        $outFile = Join-Path (New-TempDir) 'github_output'
        Write-TextFile $outFile "seed=1`n"
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            & $powershell -NoProfile -ExecutionPolicy Bypass -File $TagGatePath -Tag 'V1.9.0' *> $null
            Assert-Equal 1 $LASTEXITCODE 'exit code for V1.9.0'
            Assert-Equal "seed=1`n" ([IO.File]::ReadAllText($outFile, $Utf8NoBom)) 'GITHUB_OUTPUT after a rejected tag'
            & $powershell -NoProfile -ExecutionPolicy Bypass -File $TagGatePath -Tag 'v1.9.0' *> $null
            Assert-Equal 0 $LASTEXITCODE 'exit code for v1.9.0'
        }
        Assert-Equal "seed=1`nversion=1.9.0`ntag=v1.9.0`n" ([IO.File]::ReadAllText($outFile, $Utf8NoBom)) 'GITHUB_OUTPUT after a valid tag'
    }

    # ============================================================================================
    #  Digest and manifest
    # ============================================================================================
    Invoke-Case 'digest: sha256sum -c format, lower-case hash, two spaces, LF' {
        $zip = Join-Path (New-TempDir) 'teststand-mcp.zip'
        [IO.File]::WriteAllBytes($zip, [byte[]](1, 2, 3, 4))
        $expected = (Get-Sha256Of ([byte[]](1, 2, 3, 4))) + '  teststand-mcp.zip' + "`n"
        Assert-Equal $expected (New-ArchiveDigest -ArchivePath $zip) 'digest text'
        Assert-Equal (Get-Sha256Of ([byte[]](1, 2, 3, 4))) (Get-FileSha256 $zip) 'Get-FileSha256 is lower-case'
    }

    Invoke-Case 'digest: a missing archive is a three-part failure naming the path' {
        $absent = Join-Path (New-TempDir) 'absent.zip'
        $message = $null
        try { [void](New-ArchiveDigest -ArchivePath $absent) } catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'absent.zip' 'path'
    }

    Invoke-Case 'manifest: sorted, forward slashes, one "<hash>  <path>" line per file of the staged tree' {
        $staged = New-StagedTree
        $lines = @((New-ArchiveManifest -StagedDir $staged) -split "`n" | Where-Object { $_ })
        Assert-Equal 5 $lines.Count 'one line per file'
        $parsed = ConvertFrom-HashLines ($lines -join "`n")
        Assert-Equal '.claude-plugin/plugin.json|VERSION.txt|bin/TestStandMCP.exe|bin/appsettings.json|bin/scripts/with space.txt' (@($parsed.Keys) -join '|') 'ordinal path order'
        Assert-Equal (Format-HashLine -Hash (Get-Sha256Of ([byte[]](0, 1, 2, 253, 254, 255, 0, 77))) -Path 'bin/TestStandMCP.exe') ($lines | Where-Object { $_.EndsWith('  bin/TestStandMCP.exe') }) 'hash line of the exe'
    }

    Invoke-Case 'manifest: round trip - a zip of the tree compares clean' {
        $staged = New-StagedTree
        $zip = Join-Path (New-TempDir) 'archive.zip'
        New-ZipFromDirectory $staged $zip
        $problems = @(Compare-ArchiveManifest -ArchivePath $zip -ManifestText (New-ArchiveManifest -StagedDir $staged))
        Assert-Equal 0 $problems.Count "problems: $(($problems | ForEach-Object { $_.Kind + ' ' + $_.Path }) -join '; ')"
    }

    Invoke-Case 'manifest: a changed file, an extra archive entry and a missing archive entry are each named' {
        $staged = New-StagedTree
        $zip = Join-Path (New-TempDir) 'archive.zip'
        New-ZipFromDirectory $staged $zip
        $lines = @((New-ArchiveManifest -StagedDir $staged) -split "`n" | Where-Object { $_ })
        $edited = @($lines | Where-Object { -not $_.EndsWith('  VERSION.txt') })
        $edited = @($edited | ForEach-Object { if ($_.EndsWith('  bin/appsettings.json')) { Format-HashLine -Hash $ZeroHash -Path 'bin/appsettings.json' } else { $_ } })
        $edited += (Format-HashLine -Hash (Get-Sha256Of ([byte[]](9))) -Path 'bin/ghost.dll')
        $problems = @(Compare-ArchiveManifest -ArchivePath $zip -ManifestText (($edited -join "`n") + "`n"))
        $byKind = @{}
        foreach ($problem in $problems) { $byKind[$problem.Kind] = $problem.Path }
        Assert-Equal 3 $problems.Count 'problem count'
        Assert-Equal 'bin/appsettings.json' $byKind['hash-mismatch'] 'hash mismatch path'
        Assert-Equal 'VERSION.txt' $byKind['entry-not-in-manifest'] 'extra archive entry path'
        Assert-Equal 'bin/ghost.dll' $byKind['entry-missing-from-archive'] 'missing archive entry path'
    }

    Invoke-Case 'manifest: an entry named with a backslash is an unsafe-name problem on every PowerShell version' {
        $zip = Join-Path (New-TempDir) 'backslash.zip'
        New-ZipFromEntries -ZipPath $zip -Entries @(@{ Name = 'bin\x.txt'; Bytes = [byte[]](1) }, @{ Name = 'ok.txt'; Bytes = [byte[]](2) })
        $problems = @(Compare-ArchiveManifest -ArchivePath $zip -ManifestText (Format-HashLine -Hash (Get-Sha256Of ([byte[]](2))) -Path 'ok.txt'))
        Assert-Equal 1 $problems.Count "problems: $(($problems | ForEach-Object { $_.Kind + ' ' + $_.Path }) -join '; ')"
        Assert-Equal 'archive-entry-unsafe' $problems[0].Kind 'kind'
        Assert-Contains $problems[0].Detail 'backslash' 'detail'
    }

    Invoke-Case 'manifest: ".." segments, absolute paths and drive letters are unsafe entry names' {
        $zip = Join-Path (New-TempDir) 'unsafe.zip'
        New-ZipFromEntries -ZipPath $zip -Entries @(
            @{ Name = '../evil.txt'; Bytes = [byte[]](1) }, @{ Name = 'a/../../evil.txt'; Bytes = [byte[]](1) },
            @{ Name = '/abs.txt'; Bytes = [byte[]](1) }, @{ Name = 'C:evil.txt'; Bytes = [byte[]](1) }, @{ Name = 'ok.txt'; Bytes = [byte[]](2) })
        $problems = @(Compare-ArchiveManifest -ArchivePath $zip -ManifestText (Format-HashLine -Hash (Get-Sha256Of ([byte[]](2))) -Path 'ok.txt'))
        Assert-Equal 4 @($problems | Where-Object { $_.Kind -eq 'archive-entry-unsafe' }).Count 'unsafe entries'
        Assert-Equal 4 $problems.Count 'nothing else is reported for them'
    }

    Invoke-Case 'manifest: a duplicate entry and a case-colliding entry are their own problems' {
        $zip = Join-Path (New-TempDir) 'dup.zip'
        New-ZipFromEntries -ZipPath $zip -Entries @(@{ Name = 'a.txt'; Bytes = [byte[]](1) }, @{ Name = 'a.txt'; Bytes = [byte[]](2) }, @{ Name = 'A.txt'; Bytes = [byte[]](3) })
        $manifest = Format-HashLine -Hash (Get-Sha256Of ([byte[]](1))) -Path 'a.txt'
        $kinds = @(@(Compare-ArchiveManifest -ArchivePath $zip -ManifestText $manifest) | ForEach-Object { $_.Kind })
        Assert-True ($kinds -ccontains 'archive-entry-duplicate') "no duplicate problem in: $($kinds -join ',')"
        Assert-True ($kinds -ccontains 'archive-entry-case-collision') "no case-collision problem in: $($kinds -join ',')"
    }

    Invoke-Case 'manifest: asset and entry names are compared case-sensitively' {
        $zip = Join-Path (New-TempDir) 'case.zip'
        New-ZipFromEntries -ZipPath $zip -Entries @(@{ Name = 'File.txt'; Bytes = [byte[]](1) })
        $problems = @(Compare-ArchiveManifest -ArchivePath $zip -ManifestText (Format-HashLine -Hash (Get-Sha256Of ([byte[]](1))) -Path 'file.txt'))
        $kinds = @($problems | ForEach-Object { $_.Kind } | Sort-Object)
        Assert-Equal 'entry-missing-from-archive,entry-not-in-manifest' ($kinds -join ',') 'kinds'
    }

    Invoke-Case 'manifest: a malformed line, an empty manifest, a repeated path and an unreadable archive are reported, not skipped' {
        $zip = Join-Path (New-TempDir) 'archive.zip'
        New-ZipFromDirectory (New-StagedTree) $zip
        $line = Format-HashLine -Hash $ZeroHash -Path 'a.txt'
        foreach ($text in @("not a hash line`n", '', "$line`n$line`n")) {
            $problems = @(Compare-ArchiveManifest -ArchivePath $zip -ManifestText $text)
            Assert-True ($problems.Count -eq 1 -and $problems[0].Kind -eq 'manifest-malformed') "expected one manifest-malformed for [$text], got: $(($problems | ForEach-Object { $_.Kind }) -join ',')"
        }
        $garbage = Join-Path (New-TempDir) 'garbage.zip'
        [IO.File]::WriteAllBytes($garbage, [byte[]](80, 75, 3, 4, 1, 2, 3))
        $problems = @(Compare-ArchiveManifest -ArchivePath $garbage -ManifestText "$line`n")
        Assert-True ($problems.Count -eq 1 -and $problems[0].Kind -eq 'archive-unreadable') "expected archive-unreadable, got: $(($problems | ForEach-Object { $_.Kind }) -join ',')"
    }

    Invoke-Case 'manifest: an empty staged tree is refused with a three-part failure' {
        $empty = Join-Path (New-TempDir) 'empty'
        [void](New-Item -ItemType Directory -Path $empty)
        $message = $null
        try { [void](New-ArchiveManifest -StagedDir $empty) } catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
    }

    # ============================================================================================
    #  Own-downloads record
    # ============================================================================================
    Invoke-Case 'own-downloads: counts land in the draft or published cell of the right date and asset' {
        $path = Join-Path (New-TempDir) 'own-downloads.json'
        $common = @{ Path = $path; RunId = '77' }
        Add-OwnDownload @common -Asset 'teststand-mcp.zip' -Phase draft -Date '2026-08-20'
        Add-OwnDownload @common -Asset 'teststand-mcp.zip' -Phase draft -Date '2026-08-20'
        Add-OwnDownload @common -Asset 'teststand-mcp.zip' -Phase published -Date '2026-08-20'
        Add-OwnDownload @common -Asset 'teststand-mcp.sha256' -Phase draft -Date '2026-08-20'
        $record = Read-OwnDownloads -Path $path
        Assert-Equal '77' $record.RunId 'run id'
        Assert-Equal 2 $record.Counts['2026-08-20']['teststand-mcp.zip']['draft'] 'zip draft'
        Assert-Equal 1 $record.Counts['2026-08-20']['teststand-mcp.zip']['published'] 'zip published'
        Assert-Equal 1 $record.Counts['2026-08-20']['teststand-mcp.sha256']['draft'] 'digest draft'
        Assert-Equal 0 $record.Counts['2026-08-20']['teststand-mcp.sha256']['published'] 'digest published'
    }

    Invoke-Case 'own-downloads: a run that crosses midnight records each download under its own date' {
        $path = Join-Path (New-TempDir) 'own-downloads.json'
        Add-OwnDownload -Path $path -RunId '77' -Asset 'teststand-mcp.zip' -Phase draft -Date '2026-08-20'
        Add-OwnDownload -Path $path -RunId '77' -Asset 'teststand-mcp.zip' -Phase published -Date '2026-08-21'
        $record = Read-OwnDownloads -Path $path
        Assert-Equal '2026-08-20,2026-08-21' (@($record.Counts.Keys) -join ',') 'dates'
        Assert-Equal 1 $record.Counts['2026-08-20']['teststand-mcp.zip']['draft'] 'day one'
        Assert-Equal 0 $record.Counts['2026-08-20']['teststand-mcp.zip']['published'] 'day one published'
        Assert-Equal 1 $record.Counts['2026-08-21']['teststand-mcp.zip']['published'] 'day two'
    }

    Invoke-Case 'own-downloads: the file has exactly run_id and counts, no BOM, integer cells, and no temp file is left behind' {
        $dir = New-TempDir
        $path = Join-Path $dir 'own-downloads.json'
        1..5 | ForEach-Object { Add-OwnDownload -Path $path -RunId '77' -Asset 'teststand-mcp.zip' -Phase draft -Date '2026-01-02' }
        $json = [IO.File]::ReadAllText($path, $Utf8NoBom) | ConvertFrom-Json
        Assert-Equal 'counts,run_id' ((@($json.PSObject.Properties | ForEach-Object { $_.Name }) | Sort-Object) -join ',') 'keys'
        Assert-True ($json.run_id -is [string]) 'run_id must be a string'
        $cell = $json.counts.'2026-01-02'.'teststand-mcp.zip'
        Assert-Equal 'draft,published' ((@($cell.PSObject.Properties | ForEach-Object { $_.Name }) | Sort-Object) -join ',') 'cell keys'
        Assert-Equal 5 $cell.draft 'draft count'
        Assert-Equal 0 $cell.published 'published count'
        Assert-NoBom $path
        Assert-Equal 'own-downloads.json' ((Get-ChildItem -LiteralPath $dir -Force | ForEach-Object { $_.Name }) -join ',') 'files in the directory (atomic write leaves no temp file)'
    }

    Invoke-Case 'own-downloads: the shared fixture parses and has the documented shape' {
        $record = Read-OwnDownloads -Path $OwnDownloadsFixturePath
        Assert-Equal $FixtureRunId $record.RunId 'run id'
        Assert-Equal "$FixtureVerifyDate,$FixtureLatestDate" (@($record.Counts.Keys) -join ',') 'dates'
    }

    Invoke-Case 'own-downloads: a record of another run is refused instead of being merged' {
        $path = Join-Path (New-TempDir) 'own-downloads.json'
        Add-OwnDownload -Path $path -RunId '1' -Asset 'teststand-mcp.zip' -Phase draft
        $message = $null
        try { Add-OwnDownload -Path $path -RunId '2' -Asset 'teststand-mcp.zip' -Phase draft } catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
    }

    Invoke-Case 'own-downloads: without a run id (parameter or GITHUB_RUN_ID) or with a bad date the call fails' {
        $path = Join-Path (New-TempDir) 'o.json'
        foreach ($call in @(
            { Add-OwnDownload -Path $path -Asset 'teststand-mcp.zip' -Phase draft }
            { Add-OwnDownload -Path $path -RunId '1' -Asset 'teststand-mcp.zip' -Phase draft -Date '2026-13-40' }
            { Add-OwnDownload -Path $path -RunId '1' -Asset 'teststand-mcp.zip' -Phase draft -Date '20.08.2026' }
        )) {
            $message = $null
            try { & $call } catch { $message = $_.Exception.Message }
            Assert-True ($null -ne $message) 'expected a failure'
            Assert-ThreePartFailure $message
        }
        Assert-True (-not (Test-Path -LiteralPath $path)) 'a failed call must not create the file'
    }

    Invoke-Case 'own-downloads: the run id defaults to GITHUB_RUN_ID and the date to today (UTC, invariant format)' {
        $path = Join-Path (New-TempDir) 'o.json'
        $before = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        Use-Environment @{ GITHUB_RUN_ID = '4242' } { Add-OwnDownload -Path $path -Asset 'teststand-mcp.zip' -Phase published }
        $after = [DateTime]::UtcNow.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        $record = Read-OwnDownloads -Path $path
        Assert-Equal '4242' $record.RunId 'run id'
        $date = @($record.Counts.Keys)[0]
        Assert-True (($date -ceq $before) -or ($date -ceq $after)) "date $date is not today"
    }

    Invoke-Case 'own-downloads: a missing, non-JSON or malformed record is a three-part failure naming the file' {
        $dir = New-TempDir
        $files = @{
            'absent.json'    = $null
            'notjson.json'   = '{ nope'
            'norun.json'     = '{"counts":{}}'
            'nocounts.json'  = '{"run_id":"1"}'
            'baddate.json'   = '{"run_id":"1","counts":{"20.08.2026":{}}}'
            'nophase.json'   = '{"run_id":"1","counts":{"2026-08-20":{"a":{"draft":1}}}}'
            'negative.json'  = '{"run_id":"1","counts":{"2026-08-20":{"a":{"draft":-1,"published":0}}}}'
            'flag.json'      = '{"run_id":"1","counts":{"2026-08-20":{"a":{"draft":true,"published":0}}}}'
        }
        foreach ($name in $files.Keys) {
            $path = Join-Path $dir $name
            if ($null -ne $files[$name]) { Write-TextFile $path $files[$name] }
            $message = $null
            try { [void](Read-OwnDownloads -Path $path) } catch { $message = $_.Exception.Message }
            Assert-True ($null -ne $message) "$name : expected a failure"
            Assert-ThreePartFailure $message
            Assert-Contains $message $name "$name : file name"
        }
    }
}
finally {
    Exit-ReleaseTestEnvironment
    Remove-TestTempDirs
}

Complete-TestRun
