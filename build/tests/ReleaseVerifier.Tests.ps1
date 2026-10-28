# ============================================================================
#  ReleaseVerifier.Tests.ps1 - plain PowerShell asserts for build/Assert-PublishedRelease.ps1 and build/ReleaseDownload.psm1:
#  the verifier in its three modes (a directory of assets, a release through the GitHub API, the public latest download),
#  its one retry, the own-downloads record it writes, and the wording of its failures.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\ReleaseVerifier.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  NO network: the verifier's HTTP layer is replaced by a scriptblock.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'ReleaseVerifierTests_'
. (Join-Path $PSScriptRoot 'ReleaseTestSupport.ps1')

Enter-ReleaseTestEnvironment
try {
    foreach ($path in @($CommonModulePath, $ModulePath, $VerdictModulePath, $DownloadModulePath)) { Assert-True (Test-Path -LiteralPath $path) "$path does not exist" }
    Import-Module $CommonModulePath -Force
    Import-Module $ModulePath -Force
    Import-Module $VerdictModulePath -Force
    Import-Module $DownloadModulePath -Force

    # ============================================================================================
    #  Verifier: local mode
    # ============================================================================================
    Invoke-Case 'verify local: four assets, bot uploader, matching digest and manifest -> exit 0' {
        $run = Invoke-LocalVerify (New-ReleaseFixture)
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'verified' 'success line'
        Assert-Contains $run.Output 'Requested 0 asset download(s)' 'local mode downloads nothing and says so'
    }

    Invoke-Case 'verify local: without a release json the uploader check is skipped and the output says so' {
        $run = Invoke-LocalVerify (New-ReleaseFixture) -NoReleaseJson
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'uploader check skipped' 'skip notice'
    }

    Invoke-Case 'verify local: -Version without a tag (the packaging step has no tag) verifies the staged assets' {
        $fixture = New-ReleaseFixture
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; LocalDir = $fixture.Dir; Version = '1.9.0' }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'staged assets of version 1.9.0' 'subject'
    }

    Invoke-Case 'verify local: a bad -Version, a tag/version disagreement and -ReleaseJson without -Tag are three-part failures' {
        $fixture = New-ReleaseFixture
        foreach ($arguments in @(
            @{ LocalDir = $fixture.Dir; Version = '1.9' }
            @{ LocalDir = $fixture.Dir; Version = 'v1.9.0' }
            @{ LocalDir = $fixture.Dir; Tag = 'v1.9.0'; Version = '2.0.0' }
            @{ LocalDir = $fixture.Dir; Version = '1.9.0'; ReleaseJson = (Join-Path $fixture.Dir 'x.json') }
            @{ LocalDir = $fixture.Dir }
        )) {
            $run = Invoke-Script $VerifierPath (@{ AssetPrefixes = 'teststand-mcp' } + $arguments)
            Assert-Equal 1 $run.ExitCode "exit code for $(($arguments.Keys | ForEach-Object { "$_=$($arguments[$_])" }) -join ' ')"
        }
    }

    Invoke-Case 'verify local: the rehearsal tag is accepted with -Rehearsal (version is the plain X.Y.Z) and rejected without it' {
        $fixture = New-ReleaseFixture
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; LocalDir = $fixture.Dir; Tag = 'v1.9.0-rehearsal'; Rehearsal = $true }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; LocalDir = $fixture.Dir; Tag = 'v1.9.0-rehearsal' }
        Assert-Equal 1 $run.ExitCode 'exit code without -Rehearsal'
        Assert-Contains $run.Output 'rejected: suffix' 'tag verdict'
    }

    Invoke-Case 'verify: an invalid tag fails with the tag rejection text (the rule lives once)' {
        $fixture = New-ReleaseFixture
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; LocalDir = $fixture.Dir; Tag = 'V1.9.0' }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'rejected: upper-case V' 'verdict'
        Assert-Contains $run.Output 'git tag -d -- V1.9.0' 'retag command'
    }

    # --- Scenario Outline: A release that must not go live --------------------------------------
    $FaultRows = @(
        @{ Fault = 'the zip was uploaded by a person'; Uploader = @{ 'teststand-mcp.zip' = 'alice' }
           Expect = @("the plugin archive teststand-mcp.zip was uploaded by 'alice', not by github-actions[bot]") }
        @{ Fault = "the zip's hash differs from the digest"
           Mutate = { param($fx) Set-ZeroDigest $fx }
           Expect = @('the plugin archive teststand-mcp.zip does not match the archive digest teststand-mcp.sha256') }
        @{ Fault = 'one manifest entry differs from the archive'
           Mutate = { param($fx) Edit-ManifestLines $fx { param($lines) $lines | ForEach-Object { if ($_.EndsWith('  bin/appsettings.json')) { Format-HashLine -Hash $ZeroHash -Path 'bin/appsettings.json' } else { $_ } } } }
           Expect = @("archive entry 'bin/appsettings.json' differs from the manifest") }
        @{ Fault = 'the archive has an entry the manifest lacks'
           Mutate = { param($fx) Edit-ManifestLines $fx { param($lines) $lines | Where-Object { -not $_.EndsWith('  VERSION.txt') } } }
           Expect = @("archive entry 'VERSION.txt' is not listed in the manifest") }
        @{ Fault = 'the manifest lists an entry the archive lacks'
           Mutate = { param($fx) Edit-ManifestLines $fx { param($lines) $lines + (Format-HashLine -Hash (Get-Sha256Of ([byte[]](9))) -Path 'bin/ghost.dll') } }
           Expect = @("the manifest lists 'bin/ghost.dll' but the archive has no such entry") }
        @{ Fault = 'the version-named copy differs from the fixed zip'
           Mutate = { param($fx)
               $path = Join-Path $fx.Dir 'teststand-mcp-v1.9.0.zip'
               $bytes = [IO.File]::ReadAllBytes($path)
               $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
               [IO.File]::WriteAllBytes($path, $bytes) }
           Expect = @('the versioned archive teststand-mcp-v1.9.0.zip differs from the plugin archive teststand-mcp.zip') }
        @{ Fault = 'teststand-mcp.zip is missing'
           Mutate = { param($fx) Remove-Item -LiteralPath (Join-Path $fx.Dir 'teststand-mcp.zip') }
           Expect = @('the plugin archive teststand-mcp.zip is missing from the release') }
        @{ Fault = 'teststand-mcp.sha256 is missing'
           Mutate = { param($fx) Remove-Item -LiteralPath (Join-Path $fx.Dir 'teststand-mcp.sha256') }
           Expect = @('the archive digest teststand-mcp.sha256 is missing from the release') }
        @{ Fault = 'teststand-mcp.manifest.sha256 is missing'
           Mutate = { param($fx) Remove-Item -LiteralPath (Join-Path $fx.Dir 'teststand-mcp.manifest.sha256') }
           Expect = @('the archive manifest teststand-mcp.manifest.sha256 is missing from the release') }
        @{ Fault = 'the version-named copy is missing'
           Mutate = { param($fx) Remove-Item -LiteralPath (Join-Path $fx.Dir 'teststand-mcp-v1.9.0.zip') }
           Expect = @('the versioned archive teststand-mcp-v1.9.0.zip is missing from the release') }
    )
    foreach ($row in $FaultRows) {
        Invoke-Case "verify local fault: $($row.Fault) -> exit 1, names the fault, says running it again will not help, gives a local fix" {
            $fixture = New-ReleaseFixture
            if ($row.ContainsKey('Mutate')) { & $row.Mutate $fixture }
            $uploaders = @{}
            if ($row.ContainsKey('Uploader')) { $uploaders = $row.Uploader }
            $run = Invoke-LocalVerify $fixture -UploaderByAsset $uploaders
            Assert-Equal 1 $run.ExitCode 'exit code'
            foreach ($phrase in $row.Expect) { Assert-Contains $run.Output $phrase 'fault text' }
            Assert-Contains $run.Output $NeverHelps 'run-again advice'
            Assert-Contains $run.Output 'Run the packaging script again' 'local fix'
            Assert-NotContains $run.Output 'draft' 'a local run never talks about a draft'
        }
    }

    Invoke-Case 'verify local fault: a missing plugin archive is reported alone, not as a cascade of dependent faults' {
        $fixture = New-ReleaseFixture
        Remove-Item -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.zip')
        $run = Invoke-LocalVerify $fixture
        Assert-NotContains $run.Output 'does not match' 'a hash comparison against a missing archive'
        Assert-NotContains $run.Output 'differs from' 'a comparison against a missing archive'
    }

    Invoke-Case 'verify local fault: two faults are both named in one failure' {
        $fixture = New-ReleaseFixture
        Remove-Item -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.sha256')
        Edit-ManifestLines $fixture { param($lines) $lines | Where-Object { -not $_.EndsWith('  VERSION.txt') } }
        $run = Invoke-LocalVerify $fixture
        Assert-Contains $run.Output 'the archive digest teststand-mcp.sha256 is missing from the release' 'first fault'
        Assert-Contains $run.Output "archive entry 'VERSION.txt' is not listed in the manifest" 'second fault'
    }

    Invoke-Case 'verify local fault: a versioned archive of another size is a listing fault' {
        $fixture = New-ReleaseFixture
        [IO.File]::WriteAllBytes((Join-Path $fixture.Dir 'teststand-mcp-v1.9.0.zip'), [byte[]](1, 2, 3))
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'the versioned archive teststand-mcp-v1.9.0.zip is 3 bytes but the plugin archive is' 'size fault'
    }

    Invoke-Case 'verify local fault: a digest that names another file, or is malformed, is its own fault' {
        $fixture = New-ReleaseFixture
        Write-TextFile (Join-Path $fixture.Dir 'teststand-mcp.sha256') ((Get-FileSha256 (Join-Path $fixture.Dir 'teststand-mcp.zip')) + "  other.zip`n")
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code for a digest naming another file'
        Assert-Contains $run.Output "the archive digest names 'other.zip'" 'digest name fault'
        Write-TextFile (Join-Path $fixture.Dir 'teststand-mcp.sha256') "garbage`n"
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code for a malformed digest'
        Assert-Contains $run.Output 'the archive digest teststand-mcp.sha256 is malformed' 'malformed digest fault, naming the digest'
    }

    Invoke-Case 'verify local fault: a malformed, empty or repeating manifest is a manifest fault' {
        $fixture = New-ReleaseFixture
        foreach ($text in @("garbage`n", "`n")) {
            Write-TextFile (Join-Path $fixture.Dir 'teststand-mcp.manifest.sha256') $text
            $run = Invoke-LocalVerify $fixture
            Assert-Equal 1 $run.ExitCode "exit code for [$text]"
            Assert-Contains $run.Output 'the archive manifest is malformed' "fault for [$text]"
        }
        $fixture = New-ReleaseFixture
        Edit-ManifestLines $fixture { param($lines) $lines + $lines[0] }
        $run = Invoke-LocalVerify $fixture
        Assert-Contains $run.Output 'repeats the path' 'duplicate manifest path'
    }

    Invoke-Case 'verify local fault: more than five differences are cut off with a count' {
        $fixture = New-ReleaseFixture
        Edit-ManifestLines $fixture { param($lines)
            $zeroed = @($lines | ForEach-Object { Format-HashLine -Hash $ZeroHash -Path $_.Substring($_.IndexOf('  ') + 2) })
            $zeroed + (Format-HashLine -Hash $ZeroHash -Path 'ghost-a.dll') + (Format-HashLine -Hash $ZeroHash -Path 'ghost-b.dll') }
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output '... and 2 more differences' 'cut-off note'
    }

    Invoke-Case 'verify local fault: a corrupt plugin archive is its own fault, not an unexpected error' {
        $fixture = New-ReleaseFixture -CorruptZip
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'the plugin archive cannot be read as a zip file' 'fault'
        Assert-NotContains $run.Output 'Unexpected error' 'unexpected-error wrapper'
    }

    Invoke-Case 'verify local fault: duplicate, case-colliding and unsafe archive entries are reported by name' {
        $fixture = New-ReleaseFixture -ExtraEntries @(
            @{ Name = 'VERSION.txt'; Bytes = [byte[]](9) }, @{ Name = 'version.TXT'; Bytes = [byte[]](9) },
            @{ Name = '../evil.txt'; Bytes = [byte[]](9) }, @{ Name = 'bin\x.txt'; Bytes = [byte[]](9) })
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output "the archive holds the entry 'VERSION.txt' more than once" 'duplicate'
        Assert-Contains $run.Output "archive entry 'version.TXT' collides in case" 'case collision'
        Assert-Contains $run.Output "archive entry '../evil.txt' has an unsafe name" 'dot-dot'
        Assert-Contains $run.Output "archive entry 'bin\x.txt' has an unsafe name" 'backslash'
    }

    Invoke-Case 'verify local fault: a hand-attached extra asset fails the uploader check, an unknown uploader does too' {
        $fixture = New-ReleaseFixture
        Write-TextFile (Join-Path $fixture.Dir 'notes.txt') 'extra'
        $names = @(Get-RequiredAssetNames -Version $fixture.Version) + 'notes.txt'
        $run = Invoke-LocalVerify $fixture -ListedNames $names -UploaderByAsset @{ 'notes.txt' = 'alice' }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output "the asset notes.txt was uploaded by 'alice'" 'extra asset fault'
        $run = Invoke-LocalVerify $fixture -ListedNames @(Get-RequiredAssetNames -Version $fixture.Version)
        Assert-Contains $run.Output "the asset notes.txt was uploaded by '(unknown)'" 'asset absent from the release metadata'
    }

    Invoke-Case 'verify local fault: an asset name that differs only in case is missing, not matched' {
        $fixture = New-ReleaseFixture
        Rename-Item -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.sha256') -NewName 'Teststand-mcp.sha256'
        $run = Invoke-LocalVerify $fixture
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'the archive digest teststand-mcp.sha256 is missing from the release' 'fault'
    }

    Invoke-Case 'verify local: a missing or foreign -ReleaseJson is a three-part failure' {
        $fixture = New-ReleaseFixture
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; Tag = $fixture.Tag; LocalDir = $fixture.Dir; ReleaseJson = (Join-Path (New-TempDir) 'absent.json') }
        Assert-Equal 1 $run.ExitCode 'exit code for a missing file'
        Assert-Contains $run.Output 'absent.json' 'file name'
        $jsonPath = Join-Path (New-TempDir) 'other.json'
        Write-TextFile $jsonPath (New-ReleaseJsonText -Tag 'v9.9.9' -AssetNames @('x'))
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; Tag = $fixture.Tag; LocalDir = $fixture.Dir; ReleaseJson = $jsonPath }
        Assert-Equal 1 $run.ExitCode 'exit code for a release that is not in the file'
        Assert-Contains $run.Output 'has 0 releases tagged v1.9.0' 'reason'
    }

    Invoke-Case 'verify local: a release json that is not JSON still gives a three-part failure naming the error' {
        $fixture = New-ReleaseFixture
        $jsonPath = Join-Path (New-TempDir) 'broken.json'
        Write-TextFile $jsonPath '{ this is not json'
        $run = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; Tag = $fixture.Tag; LocalDir = $fixture.Dir; ReleaseJson = $jsonPath }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'Unexpected error' 'unexpected-error label'
    }

    Invoke-Case 'verify: a missing LocalDir, no source, two sources and a bad -Repo are three-part failures' {
        foreach ($arguments in @(
            @{ Tag = 'v1.9.0'; LocalDir = (Join-Path (New-TempDir) 'nope') }
            @{ Tag = 'v1.9.0' }
            @{ Tag = 'v1.9.0'; LocalDir = (New-TempDir); Repo = $ExpectedRepoSlug }
            @{ Tag = 'v1.9.0'; Repo = 'not a repo' }
            @{ Tag = 'v1.9.0'; Repo = '../x' }
            @{ Tag = 'v1.9.0'; Repo = 'owner/..' }
            @{ Tag = 'v1.9.0'; Repo = 'a/b/c' }
            @{ Repo = $ExpectedRepoSlug }
        )) {
            $run = Invoke-Script $VerifierPath (@{ AssetPrefixes = 'teststand-mcp' } + $arguments)
            Assert-Equal 1 $run.ExitCode "exit code for $(($arguments.Keys | ForEach-Object { "$_=$($arguments[$_])" }) -join ' ')"
        }
    }

    Invoke-Case 'verify: a child process under -File exits 1 for a faulty release and 0 for a good one' {
        $powershell = (Get-Process -Id $PID).Path
        $good = New-ReleaseFixture
        & $powershell -NoProfile -ExecutionPolicy Bypass -File $VerifierPath -AssetPrefixes teststand-mcp -Tag $good.Tag -LocalDir $good.Dir *> $null
        Assert-Equal 0 $LASTEXITCODE 'exit code for a good release'
        Remove-Item -LiteralPath (Join-Path $good.Dir 'teststand-mcp.sha256')
        & $powershell -NoProfile -ExecutionPolicy Bypass -File $VerifierPath -AssetPrefixes teststand-mcp -Tag $good.Tag -LocalDir $good.Dir *> $null
        Assert-Equal 1 $LASTEXITCODE 'exit code for a faulty release'
    }

    # ============================================================================================
    #  Verifier: API mode through a fake HTTP boundary
    # ============================================================================================
    Invoke-Case 'verify api: a repository whose only release is the draft is read, exit 0' {
        $fixture = New-ReleaseFixture
        $only = New-ReleaseJsonText -Tag $fixture.Tag -AssetNames @(Get-RequiredAssetNames -Version $fixture.Version) -SizeByAsset (Get-AssetSizes $fixture) -NoDecoy
        Assert-Equal 1 @($only | ConvertFrom-Json | ForEach-Object { $_ }).Count 'the fixture lists exactly one release'
        $run = Invoke-ApiVerify $fixture (New-FakeApi $fixture -Pages @{ 1 = $only })
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
    }

    Invoke-Case 'verify local: a release json that lists ONE release (and a release with ONE asset, or NONE) is read as a list' {
        $fixture = New-ReleaseFixture
        $alone = Invoke-LocalVerify $fixture -NoDecoy
        Assert-Equal 0 $alone.ExitCode "exit code for a one-release list. Output was:`n$($alone.Output)"
        $oneAsset = Invoke-LocalVerify $fixture -NoDecoy -ListedNames @('teststand-mcp.zip')
        Assert-Equal 1 $oneAsset.ExitCode 'exit code for a release with one listed asset'
        foreach ($name in @('teststand-mcp.sha256', 'teststand-mcp.manifest.sha256', 'teststand-mcp-v1.9.0.zip')) {
            Assert-Contains $oneAsset.Output "$name was uploaded by '(unknown)'" "the asset the release does not list: $name"
        }
        Assert-NotContains $oneAsset.Output 'Unexpected error' 'a one-asset list must not break the reader'
        $none = Invoke-LocalVerify $fixture -NoDecoy -ListNothing
        Assert-Equal 1 $none.ExitCode 'exit code for a release without assets'
        Assert-Contains $none.Output "the plugin archive teststand-mcp.zip was uploaded by '(unknown)'" 'every asset is unlisted'
        Assert-Contains $none.Output "the versioned archive teststand-mcp-v1.9.0.zip was uploaded by '(unknown)'" 'every asset is unlisted'
        Assert-NotContains $none.Output 'Unexpected error' 'an empty asset list must not break the reader'
    }

    Invoke-Case 'verify api: reads the draft with the token, downloads four assets, exit 0, writes release_id and asset_ids' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture
        $outFile = Join-Path (New-TempDir) 'github_output'
        Write-TextFile $outFile "seed=1`n"
        $run = $null
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { $script:ApiRun = Invoke-ApiVerify $fixture $api }
        $run = $script:ApiRun
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'Requested 4 asset download(s) in 1 attempt' 'download count'
        $listing = @($api.State.Calls | Where-Object { $_.Uri -match '/releases\?' })[0]
        Assert-True ($listing.Uri.StartsWith('https://api.github.com/repos/Zuehlke/teststand-mcp/releases?')) "listing url was $($listing.Uri)"
        Assert-Equal "Bearer $FakeToken" $listing.Headers['Authorization'] 'listing authorization'
        Assert-True ($listing.TimeoutSeconds -gt 0) 'the listing call carries a timeout'
        $requests = Get-AssetRequests $api
        Assert-Equal 4 $requests.Count 'asset requests'
        foreach ($request in $requests) {
            Assert-Equal 'application/octet-stream' $request.Headers['Accept'] "accept header of $($request.Uri)"
            Assert-Equal "Bearer $FakeToken" $request.Headers['Authorization'] "authorization of $($request.Uri)"
            Assert-True ($request.TimeoutSeconds -ge 120) 'every download carries at least the base timeout'
        }
        Assert-NotContains $run.Output $FakeToken 'output (the token must never be printed)'
        Assert-NoBom $outFile
        $lines = @([IO.File]::ReadAllText($outFile, $Utf8NoBom) -split "`n" | Where-Object { $_ })
        Assert-Equal 'seed=1' $lines[0] 'the existing line stays'
        Assert-Equal "release_id=$ReleaseId" $lines[1] 'release_id'
        $ids = ($lines[2].Substring('asset_ids='.Length)) | ConvertFrom-Json
        Assert-Equal 'teststand-mcp.zip,teststand-mcp.sha256,teststand-mcp.manifest.sha256,teststand-mcp-v1.9.0.zip' (@($ids.PSObject.Properties | ForEach-Object { $_.Name }) -join ',') 'asset ids by name'
        Assert-Equal $FirstAssetId $ids.'teststand-mcp.zip' 'zip asset id'
    }

    Invoke-Case 'verify api: the timeout of a big asset follows its size (1 MB/s)' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -ReleaseOptions @{ SizeByAsset = @{ 'teststand-mcp.zip' = 900MB; 'teststand-mcp-v1.9.0.zip' = 900MB } }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        $zipRequest = @(Get-AssetRequests $api | Where-Object { $_.Uri.EndsWith("/$FirstAssetId") })[0]
        Assert-Equal 900 $zipRequest.TimeoutSeconds 'timeout of a 900 MB asset'
    }

    Invoke-Case 'verify api: without a token no Authorization header is sent; GITHUB_TOKEN is the fallback' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture
        $run = Invoke-ApiVerify $fixture $api -Extra @{ Token = '' }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        foreach ($call in $api.State.Calls) { Assert-True (-not $call.Headers.ContainsKey('Authorization')) "Authorization sent to $($call.Uri)" }
        $fallbackApi = New-FakeApi $fixture
        Use-Environment @{ GITHUB_TOKEN = 'ghs_FROM_ENVIRONMENT' } {
            $script:FallbackRun = Invoke-ApiVerify $fixture $fallbackApi -OmitToken
        }
        Assert-Equal 0 $script:FallbackRun.ExitCode "exit code. Output was:`n$($script:FallbackRun.Output)"
        Assert-Equal 'Bearer ghs_FROM_ENVIRONMENT' $fallbackApi.State.Calls[0].Headers['Authorization'] 'fallback authorization'
        Assert-NotContains $script:FallbackRun.Output 'ghs_FROM_ENVIRONMENT' 'output'
    }

    Invoke-Case 'verify api: an asset uploaded by a person fails BEFORE anything is downloaded and is not retried' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -ReleaseOptions @{ UploaderByAsset = @{ 'teststand-mcp.zip' = 'alice' } }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output "the plugin archive teststand-mcp.zip was uploaded by 'alice'" 'fault'
        Assert-Contains $run.Output 'Start a new release run (delete the draft first)' 'api fix'
        Assert-Contains $run.Output 'Never attach assets by hand' 'extra advice'
        Assert-Equal 1 ([regex]::Matches($run.Output, 'Start a new release run').Count) 'the restart step is said once'
        Assert-Equal 0 (Get-AssetRequests $api).Count 'asset requests'
        Assert-Equal 0 $run.Delays.Count 'a retry delay was used for a non-download fault'
    }

    Invoke-Case 'verify api: a hand-attached extra asset and an unknown uploader fail before any download' {
        $fixture = New-ReleaseFixture
        $names = @(Get-RequiredAssetNames -Version $fixture.Version) + 'notes.txt'
        $api = New-FakeApi $fixture -ReleaseOptions @{ AssetNames = $names; UploaderByAsset = @{ 'notes.txt' = 'alice' }; NoUploaderFor = @('teststand-mcp.sha256') }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output "the asset notes.txt was uploaded by 'alice'" 'extra asset'
        Assert-Contains $run.Output "the archive digest teststand-mcp.sha256 was uploaded by '(unknown)'" 'unknown uploader'
        Assert-Equal 0 (Get-AssetRequests $api).Count 'asset requests'
    }

    Invoke-Case 'verify api: a versioned archive of another listed size is reported before any download' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -ReleaseOptions @{ SizeByAsset = @{ 'teststand-mcp.zip' = 1000; 'teststand-mcp-v1.9.0.zip' = 999 } }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'the versioned archive teststand-mcp-v1.9.0.zip is 999 bytes but the plugin archive is 1000 bytes' 'fault'
        Assert-Equal 0 (Get-AssetRequests $api).Count 'asset requests'
    }

    Invoke-Case 'verify api: an asset name that differs only in case is missing, and an unusable asset id fails before any download' {
        $fixture = New-ReleaseFixture
        $names = @('teststand-mcp.zip', 'Teststand-mcp.sha256', 'teststand-mcp.manifest.sha256', 'teststand-mcp-v1.9.0.zip')
        $run = Invoke-ApiVerify $fixture (New-FakeApi $fixture -ReleaseOptions @{ AssetNames = $names })
        Assert-Contains $run.Output 'the archive digest teststand-mcp.sha256 is missing from the release' 'case-only name'
        $badId = New-FakeApi $fixture -ReleaseOptions @{ IdByAsset = @{ 'teststand-mcp.sha256' = 'abc' } }
        $run = Invoke-ApiVerify $fixture $badId
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'unusable asset id' 'reason'
        Assert-Equal 0 (Get-AssetRequests $badId).Count 'asset requests'
    }

    Invoke-Case 'verify api: no release for the tag explains that a draft needs -Token, and is not retried' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -ReleaseOptions @{ Tag = 'v9.9.9' }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'v1.9.0' 'tag'
        Assert-Contains $run.Output '-Token' 'draft hint'
        Assert-Equal 0 (Get-AssetRequests $api).Count 'asset requests'
    }

    Invoke-Case 'verify api: the tag on the second page of the releases list is found' {
        $fixture = New-ReleaseFixture
        $real = New-ReleaseJsonText -Tag $fixture.Tag -AssetNames (Get-RequiredAssetNames -Version $fixture.Version) -SizeByAsset (Get-AssetSizes $fixture)
        $api = New-FakeApi $fixture -Pages @{ 1 = (New-FullPageJson); 2 = $real }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        $listings = @($api.State.Calls | Where-Object { $_.Uri -match '/releases\?' })
        Assert-Equal 2 $listings.Count 'listing requests'
        Assert-Contains $listings[1].Uri 'page=2' 'second page'
    }

    Invoke-Case 'verify api: a second page that holds exactly ONE release (the one with the tag) is found' {
        $fixture = New-ReleaseFixture
        $real = New-ReleaseJsonText -Tag $fixture.Tag -AssetNames (Get-RequiredAssetNames -Version $fixture.Version) -SizeByAsset (Get-AssetSizes $fixture) -NoDecoy
        $api = New-FakeApi $fixture -Pages @{ 1 = (New-FullPageJson); 2 = $real }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 2 @($api.State.Calls | Where-Object { $_.Uri -match '/releases\?' }).Count 'listing requests (a full page, then the page of one)'
    }

    Invoke-Case 'verify api: a release with ONE listed asset, or NONE, reports the others as missing before any download' {
        $fixture = New-ReleaseFixture
        $one = New-FakeApi $fixture -ReleaseOptions @{ AssetNames = @('teststand-mcp.zip'); NoDecoy = $true }
        $run = Invoke-ApiVerify $fixture $one
        Assert-Equal 1 $run.ExitCode 'exit code for one asset'
        Assert-Contains $run.Output 'the archive digest teststand-mcp.sha256 is missing from the release' 'missing asset'
        Assert-NotContains $run.Output 'Unexpected error' 'a one-asset list must not break the reader'
        Assert-Equal 0 (Get-AssetRequests $one).Count 'asset requests'
        $none = New-FakeApi $fixture -ReleaseOptions @{ AssetNames = @(); NoDecoy = $true }
        $run = Invoke-ApiVerify $fixture $none
        Assert-Equal 1 $run.ExitCode 'exit code for no asset'
        Assert-Contains $run.Output 'the plugin archive teststand-mcp.zip is missing from the release' 'missing asset'
        Assert-NotContains $run.Output 'Unexpected error' 'an empty asset list must not break the reader'
    }

    Invoke-Case 'verify api: a release id and asset ids above 2^31 are read, downloaded by id and reported as numbers' {
        $fixture = New-ReleaseFixture
        $big = [int64]3000000001
        $ids = @{ 'teststand-mcp.zip' = $big; 'teststand-mcp.sha256' = ($big + 1); 'teststand-mcp.manifest.sha256' = ($big + 2); 'teststand-mcp-v1.9.0.zip' = ($big + 3) }
        $api = New-FakeApi $fixture -ReleaseOptions @{ ReleaseNumber = ($big + 10); IdByAsset = $ids }
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { $script:BigRun = Invoke-ApiVerify $fixture $api }
        Assert-Equal 0 $script:BigRun.ExitCode "exit code. Output was:`n$($script:BigRun.Output)"
        Assert-True (@(Get-AssetRequests $api | Where-Object { $_.Uri.EndsWith("/$big") }).Count -eq 1) 'the archive is requested by its large id'
        $lines = @([IO.File]::ReadAllText($outFile, $Utf8NoBom) -split "`n" | Where-Object { $_ })
        Assert-Equal "release_id=$($big + 10)" $lines[0] 'release_id'
        $reported = ($lines[1].Substring('asset_ids='.Length)) | ConvertFrom-Json
        Assert-Equal "$big" "$($reported.'teststand-mcp.zip')" 'the archive id is reported as the number it is'
        Assert-Equal "$($big + 3)" "$($reported.'teststand-mcp-v1.9.0.zip')" 'the versioned archive id'
    }

    Invoke-Case 'verify api: when the search runs out of pages the failure says so' {
        $fixture = New-ReleaseFixture
        $pages = @{}
        foreach ($page in 1..$MaxListPages) { $pages[$page] = (New-FullPageJson) }
        $api = New-FakeApi $fixture -Pages $pages
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output "stopped after $MaxListPages pages" 'exhaustion note'
        Assert-Equal $MaxListPages @($api.State.Calls | Where-Object { $_.Uri -match '/releases\?' }).Count 'listing requests'
    }

    Invoke-Case 'verify api: two releases with the same tag fail, naming both ids, and nothing is downloaded' {
        $fixture = New-ReleaseFixture
        $names = @(Get-RequiredAssetNames -Version $fixture.Version)
        $first = @{ id = 701; tag_name = $fixture.Tag; draft = $true; assets = @() }
        $second = @{ id = 702; tag_name = $fixture.Tag; draft = $true; assets = @() }
        $api = New-FakeApi $fixture -Pages @{ 1 = (ConvertTo-Json -InputObject @($first, $second) -Depth 4) }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output '2 releases of Zuehlke/teststand-mcp carry the tag v1.9.0' 'fault'
        Assert-Contains $run.Output '701, 702' 'ids'
        Assert-Equal 0 (Get-AssetRequests $api).Count 'asset requests'
    }

    Invoke-Case 'verify api: the releases list failing is a three-part failure and is not retried' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -ReleasesFailure 'The remote server returned an error: (403) rate limit exceeded.'
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'rate limit' 'cause'
        Assert-Equal 0 $run.Delays.Count 'retry delays'
    }

    # --- Scenario Outline: One retry for upload settling --------------------------------------
    Invoke-Case 'retry: first attempt cannot download (HTTP error), second passes -> exit 0 after a 30 s wait, re-downloading only what failed' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -Plan { param($Name, $Count) if ($Name -eq 'teststand-mcp.sha256' -and $Count -eq 1) { 'The remote server returned an error: (502) Bad Gateway.' } }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 1 $run.Delays.Count 'number of waits'
        Assert-Equal $DefaultRetryDelay $run.Delays[0] 'wait in seconds'
        Assert-Contains $run.Output 'in 2 attempt' 'attempt count'
        Assert-Equal 1 $api.State.Downloads['teststand-mcp.zip'] 'the archive that already arrived is not downloaded again'
        Assert-Equal 2 $api.State.Downloads['teststand-mcp.sha256'] 'the failed asset is requested again'
        Assert-Equal 1 $api.State.Downloads['teststand-mcp.manifest.sha256'] 'manifest'
        Assert-Equal 1 $api.State.Downloads['teststand-mcp-v1.9.0.zip'] 'versioned archive'
    }

    Invoke-Case 'retry: first attempt cannot download (timeout), second fails again -> exit non-zero, says running it again may help' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -Plan { param($Name, $Count) if ($Name -eq 'teststand-mcp.zip') { 'The operation has timed out.' } }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'timed out' 'cause'
        Assert-Contains $run.Output $MayHelp 'advice'
        Assert-Equal 1 $run.Delays.Count 'exactly one retry'
        Assert-Equal 2 $api.State.Downloads['teststand-mcp.zip'] 'archive request count (no third attempt)'
    }

    Invoke-Case 'retry: first attempt cannot download, second downloads but the hash differs -> exit non-zero naming the hash' {
        $fixture = New-ReleaseFixture
        Set-ZeroDigest $fixture
        $api = New-FakeApi $fixture -Plan { param($Name, $Count) if ($Name -eq 'teststand-mcp.manifest.sha256' -and $Count -eq 1) { 'timeout' } }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'does not match the archive digest' 'fault'
        Assert-Equal 1 $run.Delays.Count 'exactly one retry'
    }

    Invoke-Case 'retry: downloads but the hash differs on the first attempt -> exit non-zero WITHOUT retrying' {
        $fixture = New-ReleaseFixture
        Set-ZeroDigest $fixture
        $api = New-FakeApi $fixture
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'does not match the archive digest' 'fault'
        Assert-Contains $run.Output $NeverHelps 'advice'
        Assert-Equal 0 $run.Delays.Count 'no wait'
        Assert-Equal 1 $api.State.Downloads['teststand-mcp.zip'] 'archive downloaded once'
    }

    Invoke-Case 'retry: a missing asset is not retried either' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture -ReleaseOptions @{ AssetNames = @('teststand-mcp.zip', 'teststand-mcp.sha256', 'teststand-mcp-v1.9.0.zip') }
        $run = Invoke-ApiVerify $fixture $api
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'the archive manifest teststand-mcp.manifest.sha256 is missing from the release' 'fault'
        Assert-Equal 0 $run.Delays.Count 'no wait'
    }

    # ============================================================================================
    #  Own-downloads record written by the verifier; the -Latest mode
    # ============================================================================================
    Invoke-Case 'own-downloads: verify (first archive download fails) + retry + the public latest download equal the shared fixture' {
        $fixture = New-ReleaseFixture
        $recordPath = Join-Path (New-TempDir) 'own-downloads.json'
        $verifyApi = New-FakeApi $fixture -Plan { param($Name, $Count) if ($Name -eq 'teststand-mcp.zip' -and $Count -eq 1) { 'The remote server returned an error: (502) Bad Gateway.' } }
        $run = Invoke-ApiVerify $fixture $verifyApi -OwnDownloadsPath $recordPath -Extra @{ RunId = $FixtureRunId; Date = $FixtureVerifyDate }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        $latest = Invoke-LatestVerify (New-FakeApi $fixture) -Extra @{ OwnDownloadsPath = $recordPath; RunId = $FixtureRunId; Date = $FixtureLatestDate }
        Assert-Equal 0 $latest.ExitCode "exit code. Output was:`n$($latest.Output)"
        $record = Read-OwnDownloads -Path $recordPath
        Assert-RecordMatchesFixture $record
        Assert-Equal 3 ($record.Counts[$FixtureVerifyDate]['teststand-mcp.zip']['draft'] + $record.Counts[$FixtureLatestDate]['teststand-mcp.zip']['published']) 'zip downloads in total'
    }

    Invoke-Case 'own-downloads: the phase comes from the release draft flag; -Draft only forces draft' {
        $fixture = New-ReleaseFixture
        $publishedJson = @{ IsDraft = $false }
        $recordPath = Join-Path (New-TempDir) 'published.json'
        [void](Invoke-ApiVerify $fixture (New-FakeApi $fixture -ReleaseOptions $publishedJson) -OwnDownloadsPath $recordPath -Extra @{ RunId = '55'; Date = '2026-08-20' })
        $cell = (Read-OwnDownloads -Path $recordPath).Counts['2026-08-20']['teststand-mcp.zip']
        Assert-Equal 1 $cell['published'] 'published cell of a published release'
        Assert-Equal 0 $cell['draft'] 'draft cell of a published release'
        $forcedPath = Join-Path (New-TempDir) 'forced.json'
        [void](Invoke-ApiVerify $fixture (New-FakeApi $fixture -ReleaseOptions $publishedJson) -OwnDownloadsPath $forcedPath -ForceDraft -Extra @{ RunId = '55'; Date = '2026-08-20' })
        Assert-Equal 1 (Read-OwnDownloads -Path $forcedPath).Counts['2026-08-20']['teststand-mcp.zip']['draft'] 'draft cell when forced'
        $draftPath = Join-Path (New-TempDir) 'draft.json'
        [void](Invoke-ApiVerify $fixture (New-FakeApi $fixture) -OwnDownloadsPath $draftPath -Extra @{ RunId = '55'; Date = '2026-08-20' })
        Assert-Equal 1 (Read-OwnDownloads -Path $draftPath).Counts['2026-08-20']['teststand-mcp.zip']['draft'] 'draft cell of a draft release'
    }

    Invoke-Case 'own-downloads: each download is recorded BEFORE its request, so a failed request still counts' {
        $fixture = New-ReleaseFixture
        $recordPath = Join-Path (New-TempDir) 'own-downloads.json'
        $api = New-FakeApi $fixture -Plan { param($Name, $Count) if ($Name -eq 'teststand-mcp.zip') { 'down' } }
        [void](Invoke-ApiVerify $fixture $api -OwnDownloadsPath $recordPath -Extra @{ RunId = '55'; Date = '2026-08-20' })
        Assert-Equal 2 (Read-OwnDownloads -Path $recordPath).Counts['2026-08-20']['teststand-mcp.zip']['draft'] 'two failed requests'
    }

    Invoke-Case 'own-downloads: local mode records nothing' {
        $localRecord = Join-Path (New-TempDir) 'local-downloads.json'
        [void](Invoke-LocalVerify (New-ReleaseFixture) -Extra @{ OwnDownloadsPath = $localRecord })
        Assert-True (-not (Test-Path -LiteralPath $localRecord)) 'local mode must not create a download record'
    }

    Invoke-Case 'latest: the unauthenticated public download of the archive matches the published digest, recorded as published' {
        $fixture = New-ReleaseFixture
        $recordPath = Join-Path (New-TempDir) 'own-downloads.json'
        $api = New-FakeApi $fixture
        $run = $null
        Use-Environment @{ GITHUB_TOKEN = 'ghs_MUST_NOT_BE_SENT' } {
            $script:LatestRun = Invoke-LatestVerify $api -Extra @{ OwnDownloadsPath = $recordPath; RunId = '9'; Date = '2026-08-21' }
        }
        $run = $script:LatestRun
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 'https://github.com/Zuehlke/teststand-mcp/releases/latest/download/teststand-mcp.zip' $api.State.Calls[0].Uri 'first url'
        Assert-Equal 'https://github.com/Zuehlke/teststand-mcp/releases/latest/download/teststand-mcp.sha256' $api.State.Calls[1].Uri 'second url'
        foreach ($call in $api.State.Calls) { Assert-True (-not $call.Headers.ContainsKey('Authorization')) "a credential was sent to $($call.Uri)" }
        $cells = (Read-OwnDownloads -Path $recordPath).Counts['2026-08-21']
        Assert-Equal 1 $cells['teststand-mcp.zip']['published'] 'zip published'
        Assert-Equal 0 $cells['teststand-mcp.zip']['draft'] 'zip draft'
        Assert-Equal 1 $cells['teststand-mcp.sha256']['published'] 'digest published'
    }

    Invoke-Case 'latest: an archive that does not match the published digest fails with the three parts' {
        $fixture = New-ReleaseFixture
        Set-ZeroDigest $fixture
        $run = Invoke-LatestVerify (New-FakeApi $fixture)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'users download from releases/latest' 'fault'
        Assert-Contains $run.Output 'releases/latest falls back' 'fix'
    }

    Invoke-Case 'latest: a download failure is retried once and a persistent one says running it again may help' {
        $fixture = New-ReleaseFixture
        $once = New-FakeApi $fixture -Plan { param($Name, $Count) if ($Name -eq 'teststand-mcp.zip' -and $Count -eq 1) { 'timeout' } }
        $run = Invoke-LatestVerify $once
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 1 $run.Delays.Count 'one wait'
        $always = New-FakeApi $fixture -Plan { param($Name, $Count) 'timeout' }
        $run = Invoke-LatestVerify $always
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output $MayHelp 'advice'
    }

    # ============================================================================================
    #  Fix wording by release.draft, the expected digest of -Latest, archive_sha256
    # ============================================================================================
    Invoke-Case 'fix wording: a published release with a fault never tells the maintainer to delete the draft first' {
        $fixture = New-ReleaseFixture
        $published = @{ IsDraft = $false; UploaderByAsset = @{ 'teststand-mcp.zip' = 'alice' } }
        $run = Invoke-ApiVerify $fixture (New-FakeApi $fixture -ReleaseOptions $published)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-NotContains $run.Output 'delete the draft first' 'the fix of a published release'
        Assert-Contains $run.Output 'already published' 'the fix says the release is public'
        Assert-Contains $run.Output 'releases/latest falls back' 'the rollback advice'
        Assert-Contains $run.Output 'next patch version' 'the fix-forward advice'
    }

    Invoke-Case 'fix wording: a draft with a fault still says to delete the draft first' {
        $fixture = New-ReleaseFixture
        $draft = @{ IsDraft = $true; UploaderByAsset = @{ 'teststand-mcp.zip' = 'alice' } }
        $run = Invoke-ApiVerify $fixture (New-FakeApi $fixture -ReleaseOptions $draft)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'delete the draft first' 'the fix of a draft'
        Assert-NotContains $run.Output 'already published' 'the fix of a draft'
    }

    Invoke-Case 'fix wording: a published release whose content is faulty is worded as published too' {
        $fixture = New-ReleaseFixture
        Set-ZeroDigest $fixture
        $run = Invoke-ApiVerify $fixture (New-FakeApi $fixture -ReleaseOptions @{ IsDraft = $false })
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'does not match the archive digest' 'the fault'
        Assert-NotContains $run.Output 'delete the draft first' 'the fix of a published release'
    }

    Invoke-Case 'fix wording: the public latest download is always a published release, so its restart fix never mentions a draft' {
        $fixture = New-ReleaseFixture
        Write-TextFile (Join-Path $fixture.Dir 'teststand-mcp.sha256') ($ZeroHash + "  other.zip`n")
        $run = Invoke-LatestVerify (New-FakeApi $fixture)
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'names ''other.zip''' 'the fault'
        Assert-NotContains $run.Output 'delete the draft first' 'the fix of the latest download'
        Assert-Contains $run.Output 'already published' 'the fix says the release is public'
    }

    Invoke-Case 'fix wording: Format-ReleaseFailure in local mode still never talks about a draft or a published release' {
        $failure = Format-ReleaseFailure -Subject 'x' -Problems @(New-Problem 'asset-missing' @('the plugin archive teststand-mcp.zip')) -Mode Local
        Assert-NotContains $failure.Fix 'draft' 'local fix'
        Assert-NotContains $failure.Fix 'published' 'local fix'
    }

    Invoke-Case 'api: the verified archive hash is written as the step output archive_sha256, after release_id and asset_ids' {
        $fixture = New-ReleaseFixture
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { $script:HashRun = Invoke-ApiVerify $fixture (New-FakeApi $fixture) }
        Assert-Equal 0 $script:HashRun.ExitCode "exit code. Output was:`n$($script:HashRun.Output)"
        $lines = @([IO.File]::ReadAllText($outFile, $Utf8NoBom) -split "`n" | Where-Object { $_ })
        $expected = (Get-FileHash -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Equal "archive_sha256=$expected" $lines[2] 'archive_sha256 line'
    }

    Invoke-Case 'both plugins: a release with all eight assets passes and archive_hashes names each archive with its hash' {
        $fixture = Add-PluginAssets (New-ReleaseFixture)
        $outFile = Join-Path (New-TempDir) 'github_output'
        Use-Environment @{ GITHUB_OUTPUT = $outFile } { $script:BothRun = Invoke-ApiVerify $fixture (New-FakeApi $fixture) -Extra @{ AssetPrefixes = 'teststand-mcp,teststand-mcp-x64' } }
        Assert-Equal 0 $script:BothRun.ExitCode "exit code. Output was:`n$($script:BothRun.Output)"
        $line = @([IO.File]::ReadAllText($outFile, $Utf8NoBom) -split "`n" | Where-Object { $_ -like 'archive_hashes=*' })
        Assert-Equal 1 $line.Count 'one archive_hashes line'
        $hashes = $line[0].Substring('archive_hashes='.Length) | ConvertFrom-Json
        foreach ($archive in @('teststand-mcp.zip', 'teststand-mcp-x64.zip')) {
            $expected = (Get-FileHash -LiteralPath (Join-Path $fixture.Dir $archive) -Algorithm SHA256).Hash.ToLowerInvariant()
            Assert-Equal $expected $hashes.$archive "hash of $archive"
        }
    }

    Invoke-Case 'both plugins: a missing x64 asset, or an x64 archive that differs from its digest, fails naming that asset' {
        $missing = Add-PluginAssets (New-ReleaseFixture)
        Remove-Item -LiteralPath (Join-Path $missing.Dir 'teststand-mcp-x64.manifest.sha256')
        $run = Invoke-LocalVerify $missing -ListedNames @(Get-ChildItem -LiteralPath $missing.Dir -File | ForEach-Object { $_.Name }) -Extra @{ AssetPrefixes = 'teststand-mcp,teststand-mcp-x64' }
        Assert-Equal 1 $run.ExitCode 'exit code for a missing asset'
        Assert-Contains $run.Output 'teststand-mcp-x64.manifest.sha256' 'the failure names the missing asset'
        $tampered = Add-PluginAssets (New-ReleaseFixture)
        Write-TextFile (Join-Path $tampered.Dir 'teststand-mcp-x64.sha256') ($ZeroHash + '  teststand-mcp-x64.zip' + "`n")
        $run = Invoke-ApiVerify $tampered (New-FakeApi $tampered) -Extra @{ AssetPrefixes = 'teststand-mcp,teststand-mcp-x64' }
        Assert-Equal 1 $run.ExitCode 'exit code for a digest mismatch'
        Assert-Contains $run.Output 'teststand-mcp-x64.zip' 'the failure names the x64 archive'
    }

    Invoke-Case 'both plugins: without -AssetPrefixes a manual run checks every plugin, so a broken x64 asset still fails' {
        $tampered = Add-PluginAssets (New-ReleaseFixture)
        Write-TextFile (Join-Path $tampered.Dir 'teststand-mcp-x64.sha256') ($ZeroHash + '  teststand-mcp-x64.zip' + "`n")
        $run = Invoke-Script $VerifierPath @{ Tag = $tampered.Tag; LocalDir = $tampered.Dir }
        Assert-Equal 1 $run.ExitCode 'exit code for an x64 digest mismatch with the default prefixes'
        Assert-Contains $run.Output 'teststand-mcp-x64.zip' 'the failure names the x64 archive'
    }

    Invoke-Case 'both plugins: -ExpectedArchiveHashes checks each latest download, and a stale x64 one fails naming it' {
        $fixture = Add-PluginAssets (New-ReleaseFixture)
        $hash = (Get-FileHash -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        $both = @{ AssetPrefixes = 'teststand-mcp,teststand-mcp-x64' }
        $good = $both + @{ ExpectedArchiveHashes = (ConvertTo-Json -Compress @{ 'teststand-mcp.zip' = $hash; 'teststand-mcp-x64.zip' = $hash }) }
        $run = Invoke-LatestVerify (New-FakeApi $fixture) -Extra $good
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Contains $run.Output 'latest download of teststand-mcp-x64.zip' 'the x64 archive is checked'
        $stale = $both + @{ PropagationAttempts = 2; ExpectedArchiveHashes = (ConvertTo-Json -Compress @{ 'teststand-mcp.zip' = $hash; 'teststand-mcp-x64.zip' = $ZeroHash }) }
        $run = Invoke-LatestVerify (New-FakeApi $fixture) -Extra $stale
        Assert-Equal 1 $run.ExitCode 'exit code for a stale x64 download'
        Assert-Contains $run.Output 'not the archive of the release that was just verified' 'the fault'
        Assert-Contains $run.Output 'The latest release (teststand-mcp-x64.zip)' 'the failure names the stale plugin'
    }

    Invoke-Case 'both plugins: -ExpectedArchiveHashes without an entry per plugin, not JSON, or next to -ExpectedArchiveSha256 is refused' {
        $fixture = Add-PluginAssets (New-ReleaseFixture)
        $hash = (Get-FileHash -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        foreach ($bad in @((ConvertTo-Json -Compress @{ 'teststand-mcp.zip' = $hash }), 'not json', '')) {
            $run = Invoke-LatestVerify (New-FakeApi $fixture) -Extra @{ AssetPrefixes = 'teststand-mcp,teststand-mcp-x64'; ExpectedArchiveHashes = $bad }
            Assert-Equal 1 $run.ExitCode "exit code for '$bad'"
            Assert-Contains $run.Output 'ExpectedArchiveHashes' 'the failure names the parameter'
        }
        $run = Invoke-LatestVerify (New-FakeApi $fixture) -Extra @{ ExpectedArchiveSha256 = $hash; ExpectedArchiveHashes = (ConvertTo-Json -Compress @{ 'teststand-mcp.zip' = $hash }) }
        Assert-Equal 1 $run.ExitCode 'exit code for both expectations'
        $run = Invoke-Script $VerifierPath @{ Latest = $true; HttpGet = (New-FakeApi $fixture).Http; ExpectedArchiveSha256 = $hash }
        Assert-Equal 1 $run.ExitCode 'exit code for one hash with the default (every plugin) prefixes'
        Assert-Contains $run.Output 'ExpectedArchiveHashes' 'the failure names the parameter to use instead'
        $run = Invoke-LatestVerify (New-FakeApi $fixture) -Extra @{ AssetPrefixes = 'teststand-mcp,teststand-mcp-x64'; ExpectedArchiveSha256 = $hash }
        Assert-Equal 1 $run.ExitCode 'exit code for one hash with two plugins'
        Assert-Contains $run.Output 'Pass -ExpectedArchiveHashes' 'the failure names the per-plugin parameter'
    }

    Invoke-Case 'own-downloads: run_id is a JSON string even when the run id is all digits (the collector compares strings)' {
        $fixture = New-ReleaseFixture
        $recordPath = Join-Path (New-TempDir) 'own-downloads.json'
        [void](Invoke-ApiVerify $fixture (New-FakeApi $fixture) -OwnDownloadsPath $recordPath -Extra @{ RunId = '1234567890'; Date = '2026-08-20' })
        $text = [IO.File]::ReadAllText($recordPath, $Utf8NoBom)
        Assert-True ($text -cmatch '"run_id":\s*"1234567890"') "run_id is not written as a quoted string. File was:`n$text"
    }

    Invoke-Case 'latest: an expected digest equal to the served archive passes and says so' {
        $fixture = New-ReleaseFixture
        $expected = (Get-FileHash -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        $api = New-FakeApi $fixture
        $run = Invoke-LatestVerify $api -Extra @{ ExpectedArchiveSha256 = $expected }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 0 $run.Delays.Count 'no wait when the first attempt matches'
        Assert-Contains $run.Output 'verified release' 'the success line names the check against the verified release'
    }

    Invoke-Case 'latest: releases/latest still serving the previous release fails after the bounded retries and records every download' {
        $previous = New-ReleaseFixture -ExtraEntries @(@{ Name = 'previous-only.txt'; Bytes = [byte[]](1, 2, 3) })
        $verified = New-ReleaseFixture
        $expected = (Get-FileHash -LiteralPath (Join-Path $verified.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        $recordPath = Join-Path (New-TempDir) 'own-downloads.json'
        $run = Invoke-LatestVerify (New-FakeApi $previous) -Extra @{ ExpectedArchiveSha256 = $expected; OwnDownloadsPath = $recordPath; RunId = '9'; Date = '2026-08-21'; PropagationAttempts = 4; PropagationDelaySeconds = 7 }
        Assert-Equal 1 $run.ExitCode 'exit code'
        Assert-Contains $run.Output 'not the archive of the release that was just verified' 'the fault'
        Assert-Contains $run.Output $MayHelp 'the advice: this may be propagation'
        Assert-Equal 3 $run.Delays.Count 'three waits between four attempts'
        Assert-Equal '7,7,7' ($run.Delays -join ',') 'the wait between attempts'
        Assert-Equal 4 (Read-OwnDownloads -Path $recordPath).Counts['2026-08-21']['teststand-mcp.zip']['published'] 'every attempt is a recorded download'
    }

    Invoke-Case 'latest: the new release appearing on the third attempt passes (CDN propagation)' {
        $previous = New-ReleaseFixture -ExtraEntries @(@{ Name = 'previous-only.txt'; Bytes = [byte[]](1, 2, 3) })
        $verified = New-ReleaseFixture
        $expected = (Get-FileHash -LiteralPath (Join-Path $verified.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        $served = $previous.Dir
        $newDir = $verified.Dir
        $plan = {
            param($Name, $Count)
            if ($Count -ge 3) { Get-ChildItem -LiteralPath $newDir -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $served $_.Name) -Force } }
        }.GetNewClosure()
        $run = Invoke-LatestVerify (New-FakeApi $previous -Plan $plan) -Extra @{ ExpectedArchiveSha256 = $expected }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 2 $run.Delays.Count 'two waits before the third attempt'
    }

    Invoke-Case 'latest: an asset missing from the previous latest release (the first workflow release) is waited out, not fatal' {
        $fixture = New-ReleaseFixture
        $expected = (Get-FileHash -LiteralPath (Join-Path $fixture.Dir 'teststand-mcp.zip') -Algorithm SHA256).Hash.ToLowerInvariant()
        $plan = { param($Name, $Count) if ($Name -eq 'teststand-mcp.zip' -and $Count -le 2) { 'The remote server returned an error: (404) Not Found.' } }
        $run = Invoke-LatestVerify (New-FakeApi $fixture -Plan $plan) -Extra @{ ExpectedArchiveSha256 = $expected }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 2 $run.Delays.Count 'two waits'
    }

    Invoke-Case 'latest: without an expected digest there is exactly one attempt and the old behaviour' {
        $previous = New-ReleaseFixture
        $run = Invoke-LatestVerify (New-FakeApi $previous)
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-NotContains $run.Output 'verified release' 'no expected digest, no claim about the verified release'
        Assert-Equal 0 $run.Delays.Count 'no wait'
    }

    Invoke-Case 'latest: a malformed expected digest, or one without -Latest, is a three-part failure' {
        $fixture = New-ReleaseFixture
        foreach ($bad in @('abc', ('g' * 64), (('a' * 63) + ' '))) {
            $run = Invoke-LatestVerify (New-FakeApi $fixture) -Extra @{ ExpectedArchiveSha256 = $bad }
            Assert-Equal 1 $run.ExitCode "exit code for [$bad]"
            Assert-Contains $run.Output 'ExpectedArchiveSha256' 'the failure names the parameter'
        }
        $local = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; Tag = $fixture.Tag; LocalDir = $fixture.Dir; ExpectedArchiveSha256 = ('a' * 64) }
        Assert-Equal 1 $local.ExitCode 'exit code without -Latest'
        Assert-Contains $local.Output 'only with -Latest' 'the failure says where it applies'
    }

    Invoke-Case 'latest: an expected digest that is bound but EMPTY (an unset workflow output) is refused, never read as "not given"' {
        $fixture = New-ReleaseFixture
        foreach ($empty in @('', $null)) {
            $api = New-FakeApi $fixture
            $run = Invoke-LatestVerify $api -Extra @{ ExpectedArchiveSha256 = $empty }
            Assert-Equal 1 $run.ExitCode "exit code for [$empty]"
            Assert-Contains $run.Output 'ExpectedArchiveSha256' 'the failure names the parameter'
            Assert-Contains $run.Output 'omit the parameter' 'the fix says how to ask for the weaker check on purpose'
            Assert-Equal 0 $api.State.Calls.Count 'nothing is downloaded when the expected digest is unusable'
        }
    }

    Invoke-Case 'latest: -Repo names the repository whose releases/latest is fetched, so a fork checks itself' {
        $fixture = New-ReleaseFixture
        $api = New-FakeApi $fixture
        $run = Invoke-LatestVerify $api -Extra @{ Repo = 'someone/teststand-mcp-fork' }
        Assert-Equal 0 $run.ExitCode "exit code. Output was:`n$($run.Output)"
        Assert-Equal 'https://github.com/someone/teststand-mcp-fork/releases/latest/download/teststand-mcp.zip' $api.State.Calls[0].Uri 'first url'
        $none = New-FakeApi $fixture
        [void](Invoke-LatestVerify $none)
        Assert-Equal $ExpectedLatestUrl $none.State.Calls[0].Uri 'without -Repo the contract repository'
    }

    Invoke-Case 'latest: -Latest with -LocalDir, or a bad -Repo, is still refused' {
        $fixture = New-ReleaseFixture
        $both = Invoke-Script $VerifierPath @{ AssetPrefixes = 'teststand-mcp'; Latest = $true; LocalDir = $fixture.Dir; Version = '1.9.0' }
        Assert-Equal 1 $both.ExitCode 'exit code for two sources'
        $bad = Invoke-LatestVerify (New-FakeApi $fixture) -Extra @{ Repo = 'not a repo' }
        Assert-Equal 1 $bad.ExitCode 'exit code for a bad repo'
    }

    # ============================================================================================
    #  ReleaseDownload.psm1 on its own: the fetch layer takes everything as parameters
    # ============================================================================================
    Invoke-Case 'download: the timeout of a download is the base, or the time its size needs at 1 MB/s, whichever is longer' {
        $small = New-DownloadRequest -Name 'a' -Uri 'u' -Accept 'x' -UseToken $true -Size 10
        $big = New-DownloadRequest -Name 'b' -Uri 'u' -Accept 'x' -UseToken $true -Size 900MB
        $unknown = New-DownloadRequest -Name 'c' -Uri 'u' -Accept 'x' -UseToken $false -Size $null
        Assert-Equal 120 (Get-DownloadTimeoutSeconds $small 120) 'a small asset keeps the base timeout'
        Assert-Equal 900 (Get-DownloadTimeoutSeconds $big 120) 'a big asset gets the time its size needs'
        Assert-Equal 120 (Get-DownloadTimeoutSeconds $unknown 120) 'an unknown size keeps the base timeout'
    }

    Invoke-Case 'download: Invoke-FetchWithRetry tries at most the given number of times, waits between tries and reports the last failure' {
        $waits = New-Object System.Collections.ArrayList
        $delay = { param([int]$Seconds) [void]$waits.Add($Seconds) }.GetNewClosure()
        $tries = New-Object System.Collections.ArrayList
        $fetch = { param($attempt) [void]$tries.Add($attempt); @([pscustomobject]@{ Name = 'x.zip'; Message = 'timed out' }) }.GetNewClosure()
        $outcome = Invoke-FetchWithRetry -Fetch $fetch -Delay $delay -DelaySeconds 7 -MaxAttempts 3
        Assert-Equal '1,2,3' ($tries -join ',') 'attempts'
        Assert-Equal '7,7' ($waits -join ',') 'a wait between two tries, none after the last'
        Assert-Equal 3 $outcome.Attempts 'attempts reported'
        Assert-Equal 'x.zip' $outcome.Failed[0].Name 'the failure of the last try'
        $once = Invoke-FetchWithRetry -Fetch { param($attempt) @() } -Delay $delay -DelaySeconds 7 -MaxAttempts 3
        Assert-Equal 1 $once.Attempts 'a try that works ends the loop'
        Assert-Equal 0 $once.Failed.Count 'no failure'
    }

    Invoke-Case 'download: Save-Downloads records each request BEFORE it is made, keeps what arrived, skips what is saved and removes a partial file' {
        $dir = New-TempDir
        $calls = New-Object System.Collections.ArrayList
        $http = {
            param($Uri, $Headers, $OutFile, $TimeoutSeconds)
            [void]$calls.Add($Uri)
            if ($Uri -like '*two*') { Set-Content -LiteralPath $OutFile -Value 'half' ; throw 'connection reset' }
            Set-Content -LiteralPath $OutFile -Value 'whole'
        }.GetNewClosure()
        $connection = New-Connection -Token 't' -HttpGet $http -TimeoutSeconds 5 -ApiBaseUrl 'https://example.test'
        $requests = @('one', 'two', 'three') | ForEach-Object { New-DownloadRequest -Name $_ -Uri "https://example.test/$_" -Accept 'x' -UseToken $true -Size $null }
        $transfer = [pscustomobject]@{ Requested = 0; Saved = @{} }
        $recorded = New-Object System.Collections.ArrayList
        $recorder = { param([string]$Name) [void]$recorded.Add("$Name after $($calls.Count) calls") }.GetNewClosure()
        $failed = @(Save-Downloads $connection $requests $dir $transfer $recorder)
        Assert-Equal 1 $failed.Count 'one failure'
        Assert-Equal 'two' $failed[0].Name 'the failing download'
        Assert-Contains $failed[0].Message 'connection reset' 'its message'
        Assert-Equal 'one after 0 calls,two after 1 calls' ($recorded -join ',') 'each download is recorded immediately before its request, and nothing after the failure is requested'
        Assert-True (Test-Path -LiteralPath (Join-Path $dir 'one')) 'the file that arrived is kept'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $dir 'two'))) 'a partial file is removed'
        Assert-Equal 2 $transfer.Requested 'requests counted'
        $failed = @(Save-Downloads $connection $requests $dir $transfer $recorder)
        Assert-Equal 1 @($calls | Where-Object { $_ -like '*/one' }).Count 'a saved download is not requested again'
    }
}
finally {
    Exit-ReleaseTestEnvironment
    Remove-TestTempDirs
}

Complete-TestRun
