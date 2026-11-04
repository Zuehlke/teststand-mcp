# ============================================================================
#  ReleaseDraft.Tests.ps1 - plain PowerShell asserts for build/ReleaseDraft.psm1 and its three scripts:
#  build/New-ReleaseDraft.ps1, build/Publish-Release.ps1, build/Remove-RehearsalRelease.ps1, plus the
#  fetch mode of build/Assert-ReleaseAssetSet.ps1. Everything the release workflow does to GitHub
#  releases that is not verification: clearing the leftover draft of a re-run, creating the draft and
#  naming ITS id, publishing by id with a make_latest decision that keeps an out-of-order tag from moving "latest" backwards, and the
#  dry run's cleanup that deletes only what its own run created.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\ReleaseDraft.Tests.ps1
#  NO network and no gh: the gh boundary is replaced by a fake that keeps a list of releases and tags.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$BuildDir = Join-Path $RepoRoot 'build'
$NewDraftPath = Join-Path $BuildDir 'New-ReleaseDraft.ps1'
$PublishPath = Join-Path $BuildDir 'Publish-Release.ps1'
$CleanupPath = Join-Path $BuildDir 'Remove-RehearsalRelease.ps1'
$SetScriptPath = Join-Path $BuildDir 'Assert-ReleaseAssetSet.ps1'
$Repo = 'Zuehlke/teststand-mcp'
$SampleCommit = ('0123456789abcdef' * 2) + '01234567'
$BigId = 3000000001   # above 2^31: a release or asset id must never be squeezed into an int
$NotFound = 'gh: Not Found (HTTP 404)'

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'ReleaseDraftTests_'

# --- The fake gh ------------------------------------------------------------------------------------------
# State: Releases (ordered, newest first: { Id; Tag; Draft; Assets }), Tags, Latest (the tag releases/latest
# names, or $null), Calls (every argument list) and Patches (the PATCH arguments). -Fail maps a regex over the
# joined arguments to the stderr text of a failed call; -PageSize is what one listing page holds.
function New-FakeGh {
    param([object[]]$Releases = @(), [string[]]$Tags = @(), [string]$Latest = $null, [hashtable]$Fail = @{}, [int]$PageSize = 100, [string]$PatchedDraft = 'false')
    $notFoundText = $NotFound   # a closure sees this function's variables, not the script's
    $state = @{
        Releases = (New-Object System.Collections.ArrayList); Tags = (New-Object System.Collections.ArrayList)
        Latest = $Latest; Calls = (New-Object System.Collections.ArrayList); Patches = (New-Object System.Collections.ArrayList)
        NextId = $BigId; Creates = (New-Object System.Collections.ArrayList)
    }
    foreach ($release in $Releases) { [void]$state.Releases.Add($release) }
    foreach ($tag in $Tags) { [void]$state.Tags.Add($tag) }
    $gh = {
        param($Arguments, $TimeoutSeconds)
        $Arguments = @($Arguments)
        [void]$state.Calls.Add($Arguments)
        $joined = $Arguments -join ' '
        foreach ($pattern in $Fail.Keys) {
            if ($joined -match $pattern) { return (New-GhResult -ExitCode 1 -Stderr $Fail[$pattern]) }
        }
        if ($Arguments[0] -ceq 'release' -and $Arguments[1] -ceq 'create') {
            $tag = $Arguments[2]
            [void]$state.Creates.Add($Arguments)
            $state.NextId++
            [void]$state.Releases.Insert(0, @{ Id = $state.NextId; Tag = $tag; Draft = $true; Assets = @() })
            return (New-GhResult -Stdout "https://github.com/x/y/releases/tag/$tag")
        }
        if ($Arguments[0] -cne 'api') { return (New-GhResult -ExitCode 1 -Stderr "unexpected gh command $joined") }
        $isMethod = ($Arguments[1] -ceq '-X')
        $method = if ($isMethod) { $Arguments[2] } else { 'GET' }
        $path = if ($isMethod) { $Arguments[3] } else { $Arguments[1] }
        $notFound = New-GhResult -ExitCode 1 -Stderr $notFoundText
        if ($method -ceq 'GET' -and $path -match '/releases\?per_page=(\d+)&page=(\d+)\z') {
            $page = [int]$Matches[2]
            $all = @($state.Releases | ForEach-Object { $_ })
            $slice = @($all | Select-Object -Skip (($page - 1) * $PageSize) -First $PageSize)
            $lines = @($slice | ForEach-Object { ConvertTo-Json -InputObject ([ordered]@{ id = $_.Id; tag_name = $_.Tag; draft = $_.Draft }) -Compress })
            return (New-GhResult -Stdout ($lines -join "`n"))
        }
        if ($method -ceq 'GET' -and $path -match '/releases/latest\z') {
            if ($null -eq $state.Latest) { return $notFound }
            return (New-GhResult -Stdout $state.Latest)
        }
        if ($path -match '/releases/([0-9]+)\z') {
            $id = [int64]$Matches[1]
            $found = @($state.Releases | Where-Object { $_.Id -eq $id })
            if ($found.Count -eq 0) { return $notFound }
            if ($method -ceq 'GET') {
                $body = ConvertTo-Json -InputObject ([ordered]@{ id = $found[0].Id; tag_name = $found[0].Tag; draft = $found[0].Draft; assets = @($found[0].Assets) }) -Depth 4
                return (New-GhResult -Stdout $body)
            }
            if ($method -ceq 'DELETE') { $state.Releases.Remove($found[0]); return (New-GhResult) }
            if ($method -ceq 'PATCH') {
                [void]$state.Patches.Add($Arguments)
                $found[0].Draft = ($PatchedDraft -ceq 'true')
                return (New-GhResult -Stdout $PatchedDraft)
            }
        }
        if ($path -match '/git/ref/tags/(.+)\z') {
            if ($state.Tags -contains $Matches[1]) { return (New-GhResult -Stdout '{}') }
            return $notFound
        }
        if ($method -ceq 'DELETE' -and $path -match '/git/refs/tags/(.+)\z') {
            if ($state.Tags -notcontains $Matches[1]) { return $notFound }
            $state.Tags.Remove($Matches[1])
            return (New-GhResult)
        }
        return (New-GhResult -ExitCode 1 -Stderr "unexpected api call $joined")
    }.GetNewClosure()
    return @{ Gh = $gh; State = $state }
}

function New-Release {
    param($Id, [string]$Tag, [bool]$Draft = $true)
    return @{ Id = $Id; Tag = $Tag; Draft = $Draft; Assets = @() }
}

# A folder with the four assets of a version (any bytes: the draft code only needs the names).
function New-AssetFolder {
    param([string]$Version = '1.9.0', [string[]]$Extra = @(), [string[]]$Without = @(), [string[]]$Prefixes = @($ReleaseAssetPrefix))
    $dir = Join-Path (New-TempDir) 'assets'
    foreach ($name in @(Get-RequiredAssetNames -Version $Version -Prefixes $Prefixes) + $Extra) {
        if ($Without -notcontains $name) { Write-TextFile (Join-Path $dir $name) $name }
    }
    if (-not (Test-Path -LiteralPath $dir)) { [void](New-Item -ItemType Directory -Path $dir) }
    return $dir
}

function Get-Failure {
    param([scriptblock]$Body)
    try { & $Body } catch { return $_.Exception.Message }
    return $null
}

function Get-DeleteCalls {
    param($Fake)
    return @($Fake.State.Calls | Where-Object { $_[0] -ceq 'api' -and $_[1] -ceq '-X' -and $_[2] -ceq 'DELETE' })
}

function Get-TagsOfReleases {
    param($Fake)
    return @($Fake.State.Releases | ForEach-Object { "$($_.Tag)/$(if ($_.Draft) { 'draft' } else { 'published' })/$($_.Id)" })
}

try {
    Import-Module (Join-Path $BuildDir 'BuildCommon.psm1') -Force
    Import-Module (Join-Path $BuildDir 'ReleaseAssets.psm1') -Force
    Import-Module (Join-Path $BuildDir 'ReleaseDraft.psm1') -Force
    $savedOutput = [Environment]::GetEnvironmentVariable('GITHUB_OUTPUT')

    # ============================================================================================
    #  Assert-ReleaseAssetFiles: the asset set is a script check, not a count in the YAML
    # ============================================================================================
    Invoke-Case 'asset files: exactly the required names of the version pass, whatever their number' {
        Assert-ReleaseAssetFiles -Directory (New-AssetFolder) -Version '1.9.0'
    }

    Invoke-Case 'asset files: a missing asset and an extra file are both named' {
        $missing = Get-Failure { Assert-ReleaseAssetFiles -Directory (New-AssetFolder -Without @($ReleaseDigestName)) -Version '1.9.0' }
        Assert-ThreePartFailure $missing
        Assert-Contains $missing "missing: $ReleaseDigestName" 'the missing asset'
        $extra = Get-Failure { Assert-ReleaseAssetFiles -Directory (New-AssetFolder -Extra @('notes.txt')) -Version '1.9.0' }
        Assert-ThreePartFailure $extra
        Assert-Contains $extra 'unexpected: notes.txt' 'the extra file'
    }

    Invoke-Case 'asset files: assets of ANOTHER version are not the assets of this one, and a missing folder fails too' {
        $other = Get-Failure { Assert-ReleaseAssetFiles -Directory (New-AssetFolder -Version '1.9.1') -Version '1.9.0' }
        Assert-Contains $other (Get-VersionedArchiveName -Version '1.9.1') 'the unexpected versioned archive'
        Assert-Contains $other (Get-VersionedArchiveName -Version '1.9.0') 'the missing versioned archive'
        $absent = Get-Failure { Assert-ReleaseAssetFiles -Directory (Join-Path (New-TempDir) 'nope') -Version '1.9.0' }
        Assert-ThreePartFailure $absent
    }

    # ============================================================================================
    #  New-ReleaseDraft: a re-run clears its own leftover, and the id of what it created is reported
    # ============================================================================================
    Invoke-Case 'draft: a real release is created as a draft of the pushed tag with every asset, and its id is the step output' {
        $fake = New-FakeGh -Releases @((New-Release 11 'v0.9.0' $false))
        $dir = New-AssetFolder
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { $script:Created = New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir $dir -Gh $fake.Gh }
        Assert-Equal "$($BigId + 1)" "$script:Created" 'the returned id'
        Assert-Equal "release_id=$($BigId + 1)`n" ([IO.File]::ReadAllText($outFile)) 'the step output'
        $create = @($fake.State.Creates[0])
        Assert-Equal 'v1.9.0' $create[2] 'tag'
        foreach ($flag in @('--draft', '--verify-tag')) { Assert-True ($create -ccontains $flag) "gh release create lacks $flag" }
        Assert-True ($create -cnotcontains '--target') 'a real release names no target commit'
        Assert-Equal $Repo $create[($create.IndexOf('--repo') + 1)] 'repository'
        foreach ($name in (Get-RequiredAssetNames -Version '1.9.0')) { Assert-True (@($create | Where-Object { $_ -like "*\$name" }).Count -eq 1) "asset $name is not an argument of gh release create" }
        Assert-Equal 'v1.9.0/draft' ((Get-TagsOfReleases $fake)[0] -replace '/\d+\z', '') 'the new draft is the newest release'
    }

    Invoke-Case 'draft: a dry run creates the draft of the rehearsal tag at the given commit and never insists on a pushed tag' {
        $fake = New-FakeGh
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0-rehearsal' -AssetDir (New-AssetFolder) -Target $SampleCommit -DryRun -Gh $fake.Gh)
        $create = @($fake.State.Creates[0])
        Assert-Equal 'v1.9.0-rehearsal' $create[2] 'tag'
        Assert-True ($create -ccontains '--draft') 'a draft'
        Assert-True ($create -cnotcontains '--verify-tag') 'a rehearsal tag does not exist, so it cannot be verified'
        Assert-Equal $SampleCommit $create[($create.IndexOf('--target') + 1)] 'target commit'
    }

    Invoke-Case 'draft: a dry run without a full commit SHA, a dry run of a real tag and a real run of a rehearsal tag are refused before anything is created' {
        $fake = New-FakeGh
        foreach ($call in @(
            { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0-rehearsal' -AssetDir (New-AssetFolder) -DryRun -Gh $fake.Gh }
            { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0-rehearsal' -AssetDir (New-AssetFolder) -Target 'main' -DryRun -Gh $fake.Gh }
            { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0-rehearsal' -AssetDir (New-AssetFolder) -Target $SampleCommit.Substring(0, 12) -DryRun -Gh $fake.Gh }
            { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Target $SampleCommit -DryRun -Gh $fake.Gh }
            { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0-rehearsal' -AssetDir (New-AssetFolder) -Target $SampleCommit -Gh $fake.Gh }
            { New-ReleaseDraft -Repo 'not a repo' -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $fake.Gh }
        )) {
            Assert-ThreePartFailure (Get-Failure $call)
        }
        Assert-Equal 0 $fake.State.Calls.Count 'gh calls'
    }

    Invoke-Case 'draft: a leftover draft of the same tag is deleted BEFORE the new one is created, and other releases are left alone' {
        $fake = New-FakeGh -Releases @((New-Release 21 'v1.9.0'), (New-Release 22 'v1.9.1'), (New-Release 23 'v1.8.0' $false))
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $fake.Gh)
        $deletes = @(Get-DeleteCalls $fake)
        Assert-Equal 1 $deletes.Count 'deleted releases'
        Assert-Equal "repos/$Repo/releases/21" $deletes[0][3] 'the leftover draft of the tag'
        $order = @($fake.State.Calls | ForEach-Object { if ($_[0] -ceq 'release') { 'create' } elseif ($_[1] -ceq '-X') { 'delete' } else { 'list' } })
        Assert-True ($order.IndexOf('delete') -lt $order.IndexOf('create')) "the delete must come before the create: $($order -join ',')"
        $left = @(Get-TagsOfReleases $fake | ForEach-Object { $_ -replace '/\d+\z', '' } | Sort-Object)
        Assert-Equal 'v1.8.0/published,v1.9.0/draft,v1.9.1/draft' ($left -join ',') 'what is left: one draft of the tag, the others untouched'
    }

    Invoke-Case 'draft: re-running a failed run twice leaves exactly one draft of the tag (the second draft the verifier refuses never exists)' {
        $fake = New-FakeGh
        $dir = New-AssetFolder
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir $dir -Gh $fake.Gh)
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir $dir -Gh $fake.Gh)
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir $dir -Gh $fake.Gh)
        Assert-Equal 1 @($fake.State.Releases | Where-Object { $_.Tag -ceq 'v1.9.0' }).Count 'drafts of the tag'
    }

    Invoke-Case 'draft: a PUBLISHED release with the tag fails with the fix-forward advice and nothing is deleted or created' {
        $fake = New-FakeGh -Releases @((New-Release 31 'v1.9.0'), (New-Release 32 'v1.9.0' $false))
        $failure = Get-Failure { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $fake.Gh }
        Assert-ThreePartFailure $failure
        Assert-Contains $failure 'A published release of Zuehlke/teststand-mcp already carries the tag v1.9.0 (id 32)' 'what'
        Assert-Contains $failure 'next patch version' 'the fix'
        Assert-Equal 0 @(Get-DeleteCalls $fake).Count 'deleted releases (a draft must not be deleted on the way to a refusal)'
        Assert-Equal 0 $fake.State.Creates.Count 'created releases'
    }

    Invoke-Case 'draft: a leftover draft on the SECOND page of the list (a page holding one release) is found' {
        $many = @(1..100 | ForEach-Object { New-Release (1000 + $_) "v0.0.$_" $false }) + @(New-Release 300 'v1.9.0')
        $fake = New-FakeGh -Releases $many
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $fake.Gh)
        Assert-Equal "repos/$Repo/releases/300" (@(Get-DeleteCalls $fake))[0][3] 'the draft on page 2'
    }

    Invoke-Case 'draft: exactly one full page of releases (the next page is empty) and a single release are both read completely' {
        $full = New-FakeGh -Releases @(1..100 | ForEach-Object { New-Release (1000 + $_) "v0.0.$_" $false })
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $full.Gh)
        Assert-Equal 4 @($full.State.Calls | Where-Object { $_[1] -like '*releases?per_page=100&page=*' }).Count 'list pages read (a full page and the empty one after it, then 100 + the new draft)'
        $one = New-FakeGh -Releases @((New-Release 400 'v1.9.0'))
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $one.Gh)
        Assert-Equal 1 @($one.State.Releases | Where-Object { $_.Tag -ceq 'v1.9.0' }).Count 'drafts of the tag after replacing the single one'
    }

    Invoke-Case 'draft: with -AssetPrefixes the eight assets of both plugins are uploaded, and one missing x64 asset fails before gh' {
        $both = @('teststand-mcp', 'teststand-mcp-x64')
        $fake = New-FakeGh
        [void](New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder -Prefixes $both) -AssetPrefixes $both -Gh $fake.Gh)
        $create = @($fake.State.Creates[0])
        foreach ($name in (Get-RequiredAssetNames -Version '1.9.0' -Prefixes $both)) { Assert-True (@($create | Where-Object { $_ -like "*\$name" }).Count -eq 1) "asset $name is not an argument of gh release create" }
        $idle = New-FakeGh
        $failure = Get-Failure { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder -Prefixes $both -Without @('teststand-mcp-x64.sha256')) -AssetPrefixes $both -Gh $idle.Gh }
        Assert-ThreePartFailure $failure
        Assert-Contains $failure 'teststand-mcp-x64.sha256' 'the failure names the missing asset'
        Assert-Equal 0 $idle.State.Calls.Count 'gh calls'
    }

    Invoke-Case 'draft: a folder with a missing or an extra asset fails before gh is called at all' {
        $fake = New-FakeGh
        foreach ($dir in @((New-AssetFolder -Without @($ReleaseManifestName)), (New-AssetFolder -Extra @('stray.txt')))) {
            Assert-ThreePartFailure (Get-Failure { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir $dir -Gh $fake.Gh })
        }
        Assert-Equal 0 $fake.State.Calls.Count 'gh calls'
    }

    Invoke-Case 'draft: a failed listing is a three-part failure and nothing is created' {
        $fake = New-FakeGh -Fail @{ 'releases\?per_page' = 'gh: Resource not accessible by integration (HTTP 403)' }
        $failure = Get-Failure { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $fake.Gh }
        Assert-ThreePartFailure $failure
        Assert-Contains $failure 'HTTP 403' 'the cause'
        Assert-Equal 0 $fake.State.Creates.Count 'created releases'
    }

    Invoke-Case 'draft: a creation that fails half-way still reports the id of the draft it left, so the cleanup can delete exactly it' {
        $fake = New-FakeGh
        $inner = $fake.Gh
        $state = $fake.State
        $halfWay = {
            param($Arguments, $TimeoutSeconds)
            if (@($Arguments)[0] -ceq 'release') {
                $state.NextId++
                [void]$state.Releases.Insert(0, @{ Id = $state.NextId; Tag = @($Arguments)[2]; Draft = $true; Assets = @() })
                return (New-GhResult -ExitCode 1 -Stderr 'gh: upload failed')
            }
            return (& $inner $Arguments $TimeoutSeconds)
        }.GetNewClosure()
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { $script:Half = Get-Failure { New-ReleaseDraft -Repo $Repo -Tag 'v1.9.0' -AssetDir (New-AssetFolder) -Gh $halfWay } }
        Assert-ThreePartFailure $script:Half
        Assert-Contains $script:Half 'upload failed' 'the cause'
        Assert-Equal "release_id=$($BigId + 1)`n" ([IO.File]::ReadAllText($outFile)) 'the id of the half-created draft is still reported'
    }

    # ============================================================================================
    #  The script: New-ReleaseDraft.ps1
    # ============================================================================================
    Invoke-Case 'draft script: exits 0 and writes release_id; exits 1 with the three parts for a bad tag, a published tag and an empty folder name' {
        $fake = New-FakeGh
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            $script:Good = Invoke-Script $NewDraftPath @{ Repo = $Repo; Tag = 'v1.9.0'; AssetDir = (New-AssetFolder); Gh = $fake.Gh }
        }
        Assert-Equal 0 $script:Good.ExitCode "exit code. Output was:`n$($script:Good.Output)"
        Assert-Contains ([IO.File]::ReadAllText($outFile)) 'release_id=' 'the step output'
        $badTag = Invoke-Script $NewDraftPath @{ Repo = $Repo; Tag = 'V1.9.0'; AssetDir = (New-AssetFolder); Gh = $fake.Gh }
        Assert-Equal 1 $badTag.ExitCode 'exit code for a bad tag'
        $published = New-FakeGh -Releases @((New-Release 41 'v1.9.0' $false))
        $clash = Invoke-Script $NewDraftPath @{ Repo = $Repo; Tag = 'v1.9.0'; AssetDir = (New-AssetFolder); Gh = $published.Gh }
        Assert-Equal 1 $clash.ExitCode 'exit code for a published tag'
        $noFolder = Invoke-Script $NewDraftPath @{ Repo = $Repo; Tag = 'v1.9.0'; AssetDir = ''; Gh = $fake.Gh }
        Assert-Equal 1 $noFolder.ExitCode 'exit code without a folder'
    }

    # ============================================================================================
    #  Get-MakeLatestDecision and Publish-ReleaseById: an out-of-order tag never moves latest backwards
    # ============================================================================================
    $DecisionRows = @(
        @{ Tag = 'v1.9.0';  Latest = $null;                 Expect = 'true';  Why = 'no latest release yet' }
        @{ Tag = 'v1.9.0';  Latest = '';                    Expect = 'true';  Why = 'an empty latest tag' }
        @{ Tag = 'v1.9.1';  Latest = 'v1.9.0';              Expect = 'true';  Why = 'a higher patch' }
        @{ Tag = 'v1.10.0'; Latest = 'v1.9.9';              Expect = 'true';  Why = 'a higher minor (numeric, not text order)' }
        @{ Tag = 'v2.0.0';  Latest = 'v1.99.99';            Expect = 'true';  Why = 'a higher major' }
        @{ Tag = 'v1.9.0';  Latest = 'v1.9.0';              Expect = 'true';  Why = 'the same version' }
        @{ Tag = 'v1.9.0';  Latest = 'v1.9.1';              Expect = 'false'; Why = 'a lower patch' }
        @{ Tag = 'v1.9.9';  Latest = 'v1.10.0';             Expect = 'false'; Why = 'a lower minor (1.9.9 < 1.10.0)' }
        @{ Tag = 'v1.99.0'; Latest = 'v2.0.0';              Expect = 'false'; Why = 'a lower major' }
        @{ Tag = 'v9.9.9';  Latest = 'v10.0.0';             Expect = 'false'; Why = '9 < 10 as numbers' }
        @{ Tag = 'v1.0.0';  Latest = 'v1.0.99999999999999999999'; Expect = 'false'; Why = 'a component longer than any integer type' }
        @{ Tag = 'v1.9.0';  Latest = 'nightly';             Expect = 'true';  Why = 'a latest tag that is no version cannot outrank it' }
        @{ Tag = 'v1.9.0';  Latest = 'v1.9.0-rc1';          Expect = 'true';  Why = 'a pre-release tag is no vX.Y.Z' }
    )
    foreach ($row in $DecisionRows) {
        Invoke-Case "make_latest: $($row.Tag) over latest [$($row.Latest)] -> $($row.Expect) ($($row.Why))" {
            $decision = Get-MakeLatestDecision -Tag $row.Tag -LatestTag $row.Latest
            Assert-Equal $row.Expect $decision.MakeLatest 'make_latest'
            Assert-True ($decision.Reason.Length -gt 0) 'the decision says why'
        }
    }

    Invoke-Case 'make_latest: a control character in the latest tag is never echoed' {
        $decision = Get-MakeLatestDecision -Tag 'v1.9.0' -LatestTag "bad`atag"
        Assert-True ($decision.Reason.IndexOf([char]7) -lt 0) 'a control character was echoed'
    }

    Invoke-Case 'publish: by release id with draft=false as a JSON boolean, and a higher version over the latest one becomes latest' {
        $fake = New-FakeGh -Releases @((New-Release $BigId 'v1.9.1')) -Latest 'v1.9.0'
        $output = @(& { Publish-ReleaseById -ReleaseId "$BigId" -Repo $Repo -Gh $fake.Gh } *>&1 | ForEach-Object { "$_" }) -join "`n"
        Assert-Equal 1 $fake.State.Patches.Count 'PATCH calls'
        $patch = @($fake.State.Patches[0])
        Assert-Equal "repos/$Repo/releases/$BigId" $patch[3] 'addressed by id'
        Assert-Contains ($patch -join ' ') '-F draft=false' 'draft=false as a JSON boolean (-F)'
        Assert-Contains ($patch -join ' ') '-f make_latest=true' 'make_latest'
        Assert-Contains $output 'make_latest=true' 'the chosen value is printed'
        Assert-NotContains ($patch -join ' ') 'v1.9.1' 'the tag name never addresses the release'
    }

    Invoke-Case 'publish: a LOWER version than the latest release is published with make_latest=false, and the decision is printed' {
        $fake = New-FakeGh -Releases @((New-Release 51 'v1.9.0')) -Latest 'v1.10.0'
        $output = @(& { Publish-ReleaseById -ReleaseId '51' -Repo $Repo -Gh $fake.Gh } *>&1 | ForEach-Object { "$_" }) -join "`n"
        Assert-Contains (@($fake.State.Patches[0]) -join ' ') '-f make_latest=false' 'make_latest'
        Assert-Contains $output 'make_latest=false' 'the chosen value is printed'
        Assert-Contains $output 'lower version' 'and why'
    }

    Invoke-Case 'publish: the chosen make_latest is written as a step output, so the final latest check runs only for a release that became the latest one' {
        $outFile = Join-Path (New-TempDir) 'github_output'
        $higher = New-FakeGh -Releases @((New-Release 61 'v1.9.1')) -Latest 'v1.9.0'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { Publish-ReleaseById -ReleaseId '61' -Repo $Repo -Gh $higher.Gh *> $null }
        Assert-Equal "make_latest=true`n" ([IO.File]::ReadAllText($outFile)) 'the output of a release that became the latest one'
        $lower = New-FakeGh -Releases @((New-Release 62 'v1.8.0')) -Latest 'v1.9.0'
        $lowerOut = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $lowerOut } { Publish-ReleaseById -ReleaseId '62' -Repo $Repo -Gh $lower.Gh *> $null }
        Assert-Equal "make_latest=false`n" ([IO.File]::ReadAllText($lowerOut)) 'the output of a lower version'
        $stuck = New-FakeGh -Releases @((New-Release 63 'v1.9.0')) -PatchedDraft 'true'
        $failedOut = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $failedOut } { try { Publish-ReleaseById -ReleaseId '63' -Repo $Repo -Gh $stuck.Gh *> $null } catch { } }
        Assert-True (-not (Test-Path -LiteralPath $failedOut)) 'a release that did not go live reports no make_latest'
    }

    Invoke-Case 'publish: a repository without a latest release (404) makes the release the latest one' {
        $fake = New-FakeGh -Releases @((New-Release 52 'v1.9.0')) -Latest $null
        Publish-ReleaseById -ReleaseId '52' -Repo $Repo -Gh $fake.Gh *> $null
        Assert-Contains (@($fake.State.Patches[0]) -join ' ') '-f make_latest=true' 'make_latest'
    }

    Invoke-Case 'publish: an id that is not a number stops BEFORE any gh call' {
        $fake = New-FakeGh -Releases @((New-Release 53 'v1.9.0'))
        foreach ($bad in @('', 'abc', '12 34', '53/../54', '-1', '5.3')) {
            Assert-ThreePartFailure (Get-Failure { Publish-ReleaseById -ReleaseId $bad -Repo $Repo -Gh $fake.Gh })
        }
        Assert-Equal 0 $fake.State.Calls.Count 'gh calls'
    }

    Invoke-Case 'publish: a release that is not a draft, a rehearsal draft and an unknown id are refused and nothing is patched' {
        $fake = New-FakeGh -Releases @((New-Release 54 'v1.9.0' $false), (New-Release 55 'v1.9.0-rehearsal'), (New-Release 56 'nightly'))
        foreach ($id in @('54', '55', '56', '999')) {
            Assert-ThreePartFailure (Get-Failure { Publish-ReleaseById -ReleaseId $id -Repo $Repo -Gh $fake.Gh })
        }
        Assert-Equal 0 $fake.State.Patches.Count 'PATCH calls'
    }

    Invoke-Case 'publish: a latest lookup that fails for another reason than a 404 stops before publishing' {
        $fake = New-FakeGh -Releases @((New-Release 57 'v1.9.0')) -Fail @{ 'releases/latest' = 'gh: Bad Gateway (HTTP 502)' }
        $failure = Get-Failure { Publish-ReleaseById -ReleaseId '57' -Repo $Repo -Gh $fake.Gh }
        Assert-ThreePartFailure $failure
        Assert-Contains $failure 'HTTP 502' 'the cause'
        Assert-Equal 0 $fake.State.Patches.Count 'PATCH calls'
    }

    Invoke-Case 'publish: a PATCH that fails, or answers that the release is still a draft, is a failure' {
        $broken = New-FakeGh -Releases @((New-Release 58 'v1.9.0')) -Fail @{ 'PATCH' = 'gh: Server Error (HTTP 500)' }
        Assert-ThreePartFailure (Get-Failure { Publish-ReleaseById -ReleaseId '58' -Repo $Repo -Gh $broken.Gh })
        $stuck = New-FakeGh -Releases @((New-Release 59 'v1.9.0')) -PatchedDraft 'true'
        $failure = Get-Failure { Publish-ReleaseById -ReleaseId '59' -Repo $Repo -Gh $stuck.Gh }
        Assert-ThreePartFailure $failure
        Assert-Contains $failure 'still a draft' 'what'
    }

    Invoke-Case 'publish script: exit 0 for a verified draft, exit 1 with the three parts for an unset id' {
        $fake = New-FakeGh -Releases @((New-Release 60 'v1.9.0'))
        $good = Invoke-Script $PublishPath @{ Repo = $Repo; ReleaseId = '60'; Gh = $fake.Gh }
        Assert-Equal 0 $good.ExitCode "exit code. Output was:`n$($good.Output)"
        Assert-Contains $good.Output 'make_latest=true' 'the chosen value'
        $unset = Invoke-Script $PublishPath @{ Repo = $Repo; ReleaseId = ''; Gh = $fake.Gh }
        Assert-Equal 1 $unset.ExitCode 'exit code for an unset id'
    }

    # ============================================================================================
    #  Remove-RehearsalRelease: only what THIS run created
    # ============================================================================================
    $RehearsalTag = 'v1.9.0-rehearsal'

    Invoke-Case 'cleanup: deletes the draft this run created and NOT another draft of the same tag (a second dry run in flight)' {
        $fake = New-FakeGh -Releases @((New-Release $BigId $RehearsalTag), (New-Release 70 $RehearsalTag))
        Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId "$BigId" -Gh $fake.Gh *> $null
        Assert-Equal "repos/$Repo/releases/$BigId" (@(Get-DeleteCalls $fake))[0][3] 'the deleted draft'
        Assert-Equal 1 @(Get-DeleteCalls $fake).Count 'deleted releases'
        Assert-Equal "$RehearsalTag/draft/70" (@(Get-TagsOfReleases $fake)[0]) 'the other run draft survives'
    }

    Invoke-Case 'cleanup: an empty id means this run created no draft: no draft is deleted, and the tag check still runs' {
        $fake = New-FakeGh -Releases @((New-Release 71 $RehearsalTag))
        Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '' -Gh $fake.Gh *> $null
        Assert-Equal 0 @(Get-DeleteCalls $fake).Count 'deleted releases'
    }

    Invoke-Case 'cleanup: a published release, or a draft of ANOTHER tag, behind the id is left alone and reported' {
        $fake = New-FakeGh -Releases @((New-Release 72 $RehearsalTag $false), (New-Release 73 'v1.9.5'))
        foreach ($id in @('72', '73')) {
            $failure = Get-Failure { Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId $id -Gh $fake.Gh *> $null }
            Assert-ThreePartFailure $failure
            Assert-Contains $failure "release $id is not a draft of $RehearsalTag" 'the refusal'
        }
        Assert-Equal 0 @(Get-DeleteCalls $fake).Count 'deleted releases'
    }

    Invoke-Case 'cleanup: a draft that is already gone (404) is fine, an id that is not a number is not' {
        $fake = New-FakeGh
        Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '999' -Gh $fake.Gh *> $null
        Assert-ThreePartFailure (Get-Failure { Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '1; rm' -Gh $fake.Gh *> $null })
    }

    Invoke-Case 'cleanup: the rehearsal tag is deleted when it exists, and a 404 means absent' {
        $fake = New-FakeGh -Tags @($RehearsalTag)
        Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '' -Gh $fake.Gh *> $null
        Assert-Equal 1 @($fake.State.Calls | Where-Object { $_[2] -ceq 'DELETE' -and $_[3] -like '*/git/refs/tags/*' }).Count 'tag deletions'
        Assert-Equal 0 $fake.State.Tags.Count 'tags left'
        $absent = New-FakeGh
        Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '' -Gh $absent.Gh *> $null
        Assert-Equal 0 @(Get-DeleteCalls $absent).Count 'a 404 deletes nothing and is no failure'
    }

    Invoke-Case 'cleanup: a tag probe that fails for another reason than a 404 is a failure, and the draft is still deleted' {
        $fake = New-FakeGh -Releases @((New-Release 74 $RehearsalTag)) -Fail @{ 'git/ref/tags' = 'gh: Server Error (HTTP 500)' }
        $failure = Get-Failure { Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '74' -Gh $fake.Gh *> $null }
        Assert-ThreePartFailure $failure
        Assert-Contains $failure 'could not check whether the rehearsal tag' 'the failed probe'
        Assert-Equal 0 $fake.State.Releases.Count 'the draft is deleted although the tag probe failed'
    }

    Invoke-Case 'cleanup: a failed draft delete is reported and the tag is still handled' {
        $fake = New-FakeGh -Releases @((New-Release 75 $RehearsalTag)) -Tags @($RehearsalTag) -Fail @{ 'DELETE repos/[^ ]*/releases/75' = 'gh: Server Error (HTTP 500)' }
        $failure = Get-Failure { Remove-RehearsalRelease -Repo $Repo -Tag $RehearsalTag -ReleaseId '75' -Gh $fake.Gh *> $null }
        Assert-Contains $failure 'could not delete the rehearsal draft 75' 'the failed delete'
        Assert-Equal 0 $fake.State.Tags.Count 'the tag was handled after the failed delete'
    }

    Invoke-Case 'cleanup: a tag that is not a rehearsal tag is refused before any gh call' {
        $fake = New-FakeGh -Releases @((New-Release 76 'v1.9.0'))
        foreach ($tag in @('v1.9.0', 'V1.9.0-rehearsal', '', 'main')) {
            Assert-ThreePartFailure (Get-Failure { Remove-RehearsalRelease -Repo $Repo -Tag $tag -ReleaseId '76' -Gh $fake.Gh *> $null })
        }
        Assert-Equal 0 $fake.State.Calls.Count 'gh calls'
    }

    Invoke-Case 'cleanup script: ends with an explicit exit 0 after a tolerated 404, and exit 1 with the three parts after a real failure' {
        $clean = New-FakeGh -Releases @((New-Release 77 $RehearsalTag))
        $ok = Invoke-Script $CleanupPath @{ Repo = $Repo; Tag = $RehearsalTag; ReleaseId = '77'; Gh = $clean.Gh }
        Assert-Equal 0 $ok.ExitCode "exit code after a tolerated 404 of the tag probe. Output was:`n$($ok.Output)"
        Assert-Contains $ok.Output "No tag $RehearsalTag exists" 'the 404 is reported as absent'
        $broken = New-FakeGh -Fail @{ 'git/ref/tags' = 'gh: Server Error (HTTP 500)' }
        $bad = Invoke-Script $CleanupPath @{ Repo = $Repo; Tag = $RehearsalTag; ReleaseId = ''; Gh = $broken.Gh }
        Assert-Equal 1 $bad.ExitCode 'exit code after a failed probe'
    }

    # ============================================================================================
    #  Assert-ReleaseAssetSet.ps1: -Repo reads the release by its (numeric) id
    # ============================================================================================
    $Verified = [ordered]@{ 'teststand-mcp.zip' = $BigId; 'teststand-mcp.sha256' = ($BigId + 1); 'teststand-mcp.manifest.sha256' = ($BigId + 2); 'teststand-mcp-v1.9.0.zip' = ($BigId + 3) }
    $VerifiedJson = ConvertTo-Json -InputObject $Verified -Compress
    $VerifiedAssets = @($Verified.Keys | ForEach-Object { @{ id = $Verified[$_]; name = $_ } })

    Invoke-Case 'asset set -Repo: reads the draft by id through gh and passes for the verified assets (ids above 2^31)' {
        $release = @{ Id = 80; Tag = 'v1.9.0'; Draft = $true; Assets = $VerifiedAssets }
        $fake = New-FakeGh -Releases @($release)
        $run = Invoke-Script $SetScriptPath @{ Repo = $Repo; ReleaseId = '80'; ExpectedAssetIds = $VerifiedJson; Stage = 'BeforePublish'; Gh = $fake.Gh }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'exactly the 4 verified assets' 'the success line'
        Assert-Equal "repos/$Repo/releases/80" $fake.State.Calls[0][1] 'the release is read by its id'
    }

    Invoke-Case 'asset set -Repo: an unset or malformed release id fails BEFORE any gh call (the list of all releases must never be read instead)' {
        $fake = New-FakeGh -Releases @((New-Release 81 'v1.9.0'))
        foreach ($bad in @('', 'abc', '81/../x', '8 1')) {
            $run = Invoke-Script $SetScriptPath @{ Repo = $Repo; ReleaseId = $bad; ExpectedAssetIds = $VerifiedJson; Stage = 'BeforePublish'; Gh = $fake.Gh }
            Assert-Equal 1 $run.ExitCode "exit code for [$bad]"
        }
        Assert-Equal 0 $fake.State.Calls.Count 'gh calls'
    }

    Invoke-Case 'asset set -Repo: a release that changed after the verification is refused, with the rollback text once it is live' {
        $extra = @($VerifiedAssets) + @(@{ id = ($BigId + 4); name = 'surprise.zip' })
        $fake = New-FakeGh -Releases @(@{ Id = 82; Tag = 'v1.9.0'; Draft = $false; Assets = $extra })
        $run = Invoke-Script $SetScriptPath @{ Repo = $Repo; ReleaseId = '82'; ExpectedAssetIds = $VerifiedJson; Stage = 'AfterPublish'; Gh = $fake.Gh }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'surprise.zip' 'the unverified asset'
        Assert-Contains $run.Output 'already live' 'the release is public'
    }

    [Environment]::SetEnvironmentVariable('GITHUB_OUTPUT', $savedOutput)
}
finally {
    Remove-TestTempDirs
}

Complete-TestRun
