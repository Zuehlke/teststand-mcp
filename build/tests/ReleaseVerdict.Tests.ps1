# ============================================================================
#  ReleaseVerdict.Tests.ps1 - plain PowerShell asserts for build/ReleaseVerdict.psm1: the fault catalog, the one
#  rollback policy, and Assert-ReleaseAssetSet (the asset set that was verified is the asset set that goes live), with
#  build/Assert-ReleaseAssetSet.ps1.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\ReleaseVerdict.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'ReleaseVerdictTests_'
. (Join-Path $PSScriptRoot 'ReleaseTestSupport.ps1')

Enter-ReleaseTestEnvironment
try {
    foreach ($path in @($CommonModulePath, $ModulePath, $VerdictModulePath, $DownloadModulePath)) { Assert-True (Test-Path -LiteralPath $path) "$path does not exist" }
    Import-Module $CommonModulePath -Force
    Import-Module $ModulePath -Force
    Import-Module $VerdictModulePath -Force

    # ============================================================================================
    #  The fault catalog
    # ============================================================================================
    Invoke-Case 'fault catalog: every archive problem kind has a fault entry, and an unknown code throws' {
        $codes = @(Get-ReleaseFaultCodes)
        foreach ($kind in $ArchiveProblemKinds) { Assert-True ($codes -ccontains $kind) "no fault entry for the archive problem kind '$kind'" }
        $thrown = $false
        try { [void](New-Problem -Code 'no-such-code') } catch { $thrown = $true }
        Assert-True $thrown 'New-Problem accepted an unknown code'
    }

    Invoke-Case 'fault catalog: a propagation fault may be retried, every other fault never helps' {
        $propagation = New-Problem 'latest-not-verified' @('aaa', 'bbb')
        Assert-True $propagation.Retryable 'latest-not-verified is retryable'
        $alone = Format-ReleaseFailure -Subject 'The latest release' -Problems @($propagation) -Mode ApiPublished
        Assert-Contains $alone.Why $MayHelp 'why of a lone propagation fault'
        Assert-NotContains $alone.Why $NeverHelps 'why of a lone propagation fault'
        $mixed = Format-ReleaseFailure -Subject 'x' -Problems @($propagation, (New-Problem 'digest-mismatch' @('a', 'b', 'c', 'd'))) -Mode ApiPublished
        Assert-Contains $mixed.Why $NeverHelps 'one non-retryable fault decides'
        foreach ($code in @(Get-ReleaseFaultCodes | Where-Object { $_ -cne 'latest-not-verified' })) {
            Assert-True (-not (New-Problem $code @('a', 'b', 'c', 'd')).Retryable) "$code must not be marked retryable"
        }
    }

    # ============================================================================================
    #  Assert-ReleaseAssetSet: the asset set that was verified is the asset set that goes live
    # ============================================================================================
    $VerifiedAssets = [ordered]@{ 'teststand-mcp.zip' = 1000; 'teststand-mcp.sha256' = 1001; 'teststand-mcp.manifest.sha256' = 1002; 'teststand-mcp-v1.9.0.zip' = 1003 }
    $SetScriptPath = Join-Path $BuildDir 'Assert-ReleaseAssetSet.ps1'

    # A GET releases/{id} body: id, draft flag and assets (name -> id).
    function New-AssetSetJson {
        param([bool]$Draft = $true, $Assets = $VerifiedAssets, $Id = $ReleaseId)
        $list = @(foreach ($name in $Assets.Keys) { @{ id = $Assets[$name]; name = $name; uploader = @{ login = $BotLogin } } })
        return (ConvertTo-Json -InputObject @{ id = $Id; tag_name = 'v1.9.0'; draft = $Draft; assets = $list } -Depth 5)
    }

    function Copy-AssetMap {
        param($Map)
        $copy = [ordered]@{}
        foreach ($key in $Map.Keys) { $copy[$key] = $Map[$key] }
        return $copy
    }

    function Get-AssetSetFailure {
        param([hashtable]$Arguments)
        $message = $null
        try { Assert-ReleaseAssetSet @Arguments } catch { $message = $_.Exception.Message }
        return $message
    }

    $ExpectedIdsJson = ConvertTo-Json -InputObject $VerifiedAssets -Compress

    Invoke-Case 'rollback policy: every report of a bad release that is already live ends in the SAME sentence' {
        $policy = 'releases/latest falls back to the previous release (safe only once an earlier workflow-built release exists), or fix forward by releasing the next patch version (see CONTRIBUTING).'
        $published = (Format-ReleaseFailure -Subject 'x' -Problems @(New-Problem 'digest-mismatch' @('a', 'b', 'c', 'd')) -Mode ApiPublished).Fix
        $latest = (Format-ReleaseFailure -Subject 'x' -Problems @(New-Problem 'latest-mismatch' @('a', 'b')) -Mode ApiPublished).Fix
        $assets = ConvertTo-Json -InputObject ([ordered]@{ 'a.zip' = 1 }) -Compress
        $live = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Draft $false -Assets ([ordered]@{ 'a.zip' = 2 })); ExpectedAssetIds = $assets; ReleaseId = "$ReleaseId"; Stage = 'AfterPublish' }
        foreach ($report in @($published, $latest, $live)) { Assert-Contains $report $policy 'a report of a live release' }
        $draft = (Format-ReleaseFailure -Subject 'x' -Problems @(New-Problem 'digest-mismatch' @('a', 'b', 'c', 'd')) -Mode Api).Fix
        Assert-NotContains $draft 'releases/latest falls back' 'the fix of a DRAFT (nothing is live, nothing to roll back)'
    }

    Invoke-Case 'asset set: release and asset ids above 2^31 compare as the numbers they are, and a replaced one is named with its id' {
        $big = [int64]3000000001
        $bigAssets = [ordered]@{ 'teststand-mcp.zip' = $big; 'teststand-mcp.sha256' = ($big + 1); 'teststand-mcp.manifest.sha256' = ($big + 2); 'teststand-mcp-v1.9.0.zip' = ($big + 3) }
        $bigIds = ConvertTo-Json -InputObject $bigAssets -Compress
        Assert-ReleaseAssetSet -ReleaseJson (New-AssetSetJson -Assets $bigAssets -Id ($big + 10)) -ExpectedAssetIds $bigIds -ReleaseId "$($big + 10)" -Stage BeforePublish
        $replaced = Copy-AssetMap $bigAssets
        $replaced['teststand-mcp.zip'] = ($big + 99)
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Assets $replaced -Id ($big + 10)); ExpectedAssetIds = $bigIds; ReleaseId = "$($big + 10)"; Stage = 'BeforePublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-Contains $message "teststand-mcp.zip (id $big) is missing" 'the verified id'
        Assert-Contains $message "teststand-mcp.zip (id $($big + 99)) was not verified" 'the new id'
    }

    Invoke-Case 'asset set: the verified assets of a draft, before publishing, pass' {
        Assert-ReleaseAssetSet -ReleaseJson (New-AssetSetJson) -ExpectedAssetIds $ExpectedIdsJson -ReleaseId "$ReleaseId" -Stage BeforePublish
    }

    Invoke-Case 'asset set: the same assets listed in another order pass' {
        $reordered = [ordered]@{ 'teststand-mcp-v1.9.0.zip' = 1003; 'teststand-mcp.manifest.sha256' = 1002; 'teststand-mcp.zip' = 1000; 'teststand-mcp.sha256' = 1001 }
        Assert-ReleaseAssetSet -ReleaseJson (New-AssetSetJson -Assets $reordered) -ExpectedAssetIds $ExpectedIdsJson -ReleaseId "$ReleaseId" -Stage BeforePublish
    }

    Invoke-Case 'asset set: after the publish call the same assets with draft false pass' {
        Assert-ReleaseAssetSet -ReleaseJson (New-AssetSetJson -Draft $false) -ExpectedAssetIds $ExpectedIdsJson -ReleaseId "$ReleaseId" -Stage AfterPublish
    }

    Invoke-Case 'asset set: a missing asset is named with its id and the draft must be deleted' {
        $fewer = [ordered]@{ 'teststand-mcp.zip' = 1000; 'teststand-mcp.sha256' = 1001; 'teststand-mcp-v1.9.0.zip' = 1003 }
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Assets $fewer); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'teststand-mcp.manifest.sha256 (id 1002) is missing' 'the missing asset'
        Assert-Contains $message 'Delete the draft' 'the fix before publishing'
        Assert-NotContains $message 'already live' 'the fix before publishing'
    }

    Invoke-Case 'asset set: an extra asset, even one uploaded by the bot, is named and refused' {
        $more = Copy-AssetMap $VerifiedAssets
        $more['surprise.zip'] = 1004
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Assets $more); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'surprise.zip (id 1004) was not verified' 'the extra asset'
    }

    Invoke-Case 'asset set: an asset replaced under the same name has a new id and counts as missing AND extra' {
        $replaced = Copy-AssetMap $VerifiedAssets
        $replaced['teststand-mcp.zip'] = 2000
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Assets $replaced); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-Contains $message 'teststand-mcp.zip (id 1000) is missing' 'the verified id is gone'
        Assert-Contains $message 'teststand-mcp.zip (id 2000) was not verified' 'the new id was never verified'
    }

    Invoke-Case 'asset set: a release that is already public is refused before publishing' {
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Draft $false); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'is not a draft' 'the fault'
    }

    Invoke-Case 'asset set: a release still a draft after the publish call fails, and nothing is live' {
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Draft $true); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'AfterPublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'is still a draft' 'the fault'
        Assert-Contains $message 'nothing is live' 'the fix says nothing went live'
        Assert-NotContains $message 'already live' 'the fix of a release that is not live'
    }

    Invoke-Case 'asset set: a mismatch after the publish call says the release is live and names the rollback' {
        $more = Copy-AssetMap $VerifiedAssets
        $more['surprise.zip'] = 1004
        $message = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Draft $false -Assets $more); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'AfterPublish' }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'already live' 'the release is public'
        Assert-Contains $message 'releases/latest falls back' 'the rollback'
        Assert-Contains $message 'next patch version' 'the fix forward'
    }

    Invoke-Case 'asset set: a response for another release id, a missing draft flag and unusable input are three-part failures' {
        $other = Get-AssetSetFailure @{ ReleaseJson = (New-AssetSetJson -Id 999); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-Contains $other 'release 999' 'the wrong release'
        $noFlag = Get-AssetSetFailure @{ ReleaseJson = '{"id":777,"assets":[]}'; ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-Contains $noFlag 'draft flag' 'a body without a draft flag'
        foreach ($bad in @(
            @{ ReleaseJson = 'not json'; ExpectedAssetIds = $ExpectedIdsJson }
            @{ ReleaseJson = ''; ExpectedAssetIds = $ExpectedIdsJson }
            @{ ReleaseJson = (New-AssetSetJson); ExpectedAssetIds = '' }
            @{ ReleaseJson = (New-AssetSetJson); ExpectedAssetIds = '{}' }
            @{ ReleaseJson = (New-AssetSetJson); ExpectedAssetIds = '{"a.zip":"x"}' }
            @{ ReleaseJson = (New-AssetSetJson); ExpectedAssetIds = 'nope' }
        )) {
            $arguments = @{ ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
            foreach ($key in $bad.Keys) { $arguments[$key] = $bad[$key] }
            $message = Get-AssetSetFailure $arguments
            Assert-True ($null -ne $message) "expected a failure for [$($bad.ReleaseJson)] / [$($bad.ExpectedAssetIds)]"
            Assert-ThreePartFailure $message
        }
    }

    Invoke-Case 'asset set: the script exits 0 for the verified set and 1 with the three parts for any other' {
        $good = Invoke-Script $SetScriptPath @{ ReleaseJson = (New-AssetSetJson); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'BeforePublish' }
        Assert-Equal 0 $good.ExitCode "exit code. Output was:`n$($good.Output)"
        Assert-Contains $good.Output 'exactly the 4 verified assets' 'the success line'
        $fewer = [ordered]@{ 'teststand-mcp.zip' = 1000 }
        $bad = Invoke-Script $SetScriptPath @{ ReleaseJson = (New-AssetSetJson -Assets $fewer); ExpectedAssetIds = $ExpectedIdsJson; ReleaseId = "$ReleaseId"; Stage = 'AfterPublish' }
        Assert-Equal 1 $bad.ExitCode 'exit code'
        $empty = Invoke-Script $SetScriptPath @{ ReleaseJson = ''; ExpectedAssetIds = ''; ReleaseId = ''; Stage = 'BeforePublish' }
        Assert-Equal 1 $empty.ExitCode 'exit code for empty inputs (an unset step output must not prompt)'
    }
}
finally {
    Exit-ReleaseTestEnvironment
    Remove-TestTempDirs
}

Complete-TestRun
