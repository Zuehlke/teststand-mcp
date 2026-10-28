# ============================================================================
#  BuildCommon.Tests.ps1 - plain PowerShell asserts for build/BuildCommon.psm1: the three-part failure, path and
#  text reading, step outputs, temp cleanup, and the child-process and git helpers.
#  Runs under Windows PowerShell 5.1 and PowerShell 7:
#    powershell.exe -NoProfile -ExecutionPolicy Bypass -File build\tests\BuildCommon.Tests.ps1
#  Prints PASS/FAIL per case and exits non-zero when any case fails.
#  ASCII only on purpose: 5.1 reads a BOM-less script as ANSI.
# ============================================================================
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'TestHarness.ps1')
$script:TempDirPrefix = 'BuildCommonTests_'
. (Join-Path $PSScriptRoot 'ReleaseTestSupport.ps1')

Enter-ReleaseTestEnvironment
try {
    foreach ($path in @($CommonModulePath, $ModulePath, $VerdictModulePath, $DownloadModulePath)) { Assert-True (Test-Path -LiteralPath $path) "$path does not exist" }
    Import-Module $CommonModulePath -Force

    # ============================================================================================
    #  BuildCommon
    # ============================================================================================
    Invoke-Case 'BuildCommon: Write-BuildFailure throws a message with all three labelled parts' {
        $message = $null
        try { Write-BuildFailure -What 'the thing broke' -Why 'because of X' -Fix "do Y`nthen Z" } catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'expected a throw'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'the thing broke' 'what'
        Assert-Contains $message 'because of X' 'why'
        Assert-Contains $message 'then Z' 'second fix line'
    }

    Invoke-Case 'BuildCommon: an empty part is refused, so no failure can lack one' {
        foreach ($call in @(
            { Write-BuildFailure -What '' -Why 'w' -Fix 'f' }
            { Write-BuildFailure -What 'a' -Why '' -Fix 'f' }
            { Write-BuildFailure -What 'a' -Why 'w' -Fix '' }
        )) {
            $thrown = $false
            try { & $call } catch { $thrown = $true; Assert-True ($_.Exception.Message -notmatch '(?m)^What: ') 'an empty part still produced a failure message' }
            Assert-True $thrown 'an empty part was accepted'
        }
    }

    Invoke-Case 'BuildCommon: Get-ScriptFailureText passes a build failure through and wraps any other error into three parts' {
        $built = $null
        try { Write-BuildFailure -What 'w' -Why 'y' -Fix 'f' } catch { $built = $_ }
        Assert-Equal $built.Exception.Message (Get-ScriptFailureText -ErrorRecord $built) 'pass-through'
        $plain = $null
        try { throw 'something odd' } catch { $plain = $_ }
        $text = Get-ScriptFailureText -ErrorRecord $plain
        Assert-ThreePartFailure $text
        Assert-Contains $text 'something odd' 'the original message'
    }

    Invoke-Case 'BuildCommon: Resolve-BuildPath resolves against the PowerShell location, not the process directory' {
        $dir = New-TempDir
        Push-Location -LiteralPath $dir
        try {
            Assert-Equal (Join-Path $dir 'file.txt') (Resolve-BuildPath 'file.txt') 'relative path'
            Assert-True ([Environment]::CurrentDirectory -cne $dir) 'the process directory must differ for this test to mean anything'
            Write-TextFile (Join-Path $dir 'file.txt') 'hello'
            Assert-Equal 'hello' (Read-Utf8Text 'file.txt') 'relative read'
        } finally { Pop-Location }
    }

    Invoke-Case 'BuildCommon: Read-Utf8Text keeps a leading BOM visible, rejects invalid UTF-8 and names a missing file' {
        $dir = New-TempDir
        [IO.File]::WriteAllBytes((Join-Path $dir 'bom.txt'), [byte[]](0xEF, 0xBB, 0xBF, 0x41))
        Assert-Equal ([string][char]0xFEFF + 'A') (Read-Utf8Text (Join-Path $dir 'bom.txt')) 'BOM kept'
        [IO.File]::WriteAllBytes((Join-Path $dir 'bad.txt'), [byte[]](0xC3, 0x28))
        $message = $null
        try { [void](Read-Utf8Text (Join-Path $dir 'bad.txt')) } catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'invalid UTF-8 was accepted'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'not valid UTF-8' 'reason'
        $message = $null
        try { [void](Read-Utf8Text (Join-Path $dir 'absent.txt')) } catch { $message = $_.Exception.Message }
        Assert-ThreePartFailure $message
        Assert-Contains $message 'absent.txt' 'file name'
    }

    Invoke-Case 'BuildCommon: Add-GitHubOutput appends name=value lines without a BOM and does nothing outside Actions' {
        $outFile = Join-Path (New-TempDir) 'out'
        Write-TextFile $outFile "seed=1`n"
        Use-Environment @{ GITHUB_OUTPUT = $outFile } {
            Add-GitHubOutput -Name 'release_id' -Value '42'
            Add-GitHubOutput -Name 'tag' -Value 'v1.9.0'
        }
        Assert-NoBom $outFile
        Assert-Equal "seed=1`nrelease_id=42`ntag=v1.9.0`n" ([IO.File]::ReadAllText($outFile, $Utf8NoBom)) 'file text'
        Use-Environment @{ GITHUB_OUTPUT = $null } { Add-GitHubOutput -Name 'x' -Value 'y' }
    }

    Invoke-Case 'BuildCommon: Add-GitHubOutput refuses a name or value that could inject a second output' {
        foreach ($call in @(
            { Add-GitHubOutput -Name 'bad name' -Value 'x' }
            { Add-GitHubOutput -Name 'ok' -Value "a`nevil=1" }
        )) {
            $message = $null
            try { & $call } catch { $message = $_.Exception.Message }
            Assert-True ($null -ne $message) 'expected a failure'
            Assert-ThreePartFailure $message
        }
    }

    Invoke-Case 'BuildCommon: Remove-BuildTempTree retries, then warns with the path that is left behind' {
        $dir = New-TempDir
        $inner = Join-Path $dir 'cache'
        Write-TextFile (Join-Path $inner 'locked.bin') 'x'
        $lock = [IO.File]::Open((Join-Path $inner 'locked.bin'), [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        try {
            $warnings = @(& { Remove-BuildTempTree -Path $inner -Attempts 2 -DelayMilliseconds 0 } 3>&1 | ForEach-Object { "$_" })
            Assert-True ($warnings.Count -eq 1 -and $warnings[0].Contains($inner)) "expected one warning naming $inner, got: $($warnings -join '|')"
        } finally { $lock.Dispose() }
        $quiet = @(& { Remove-BuildTempTree -Path $inner -Attempts 2 -DelayMilliseconds 0 } 3>&1)
        Assert-Equal 0 $quiet.Count 'warnings once the lock is gone'
        Assert-True (-not (Test-Path -LiteralPath $inner)) 'the directory should be gone'
    }

    # ============================================================================================
    #  Invoke-ChildProcess: stdin text and environment overrides
    # ============================================================================================
    $HostExe = (Get-Process -Id $PID).Path
    $HostFlags = @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass')

    Invoke-Case 'child process: -StdinText reaches the program as UTF-8 without a BOM, and without it the program sees EOF at once' {
        $read = Invoke-ChildProcess -FilePath $HostExe -ArgumentList ($HostFlags + @('-Command', '[Console]::In.ReadToEnd().Length')) -StdinText 'x'
        Assert-Equal 0 $read.ExitCode "exit code. Stderr was: $($read.Stderr)"
        Assert-Equal '1' $read.Stdout.Trim() 'the program read one character (a BOM would make it more)'
        $eof = Invoke-ChildProcess -FilePath $HostExe -ArgumentList ($HostFlags + @('-Command', '[Console]::In.ReadToEnd().Length'))
        Assert-Equal '0' $eof.Stdout.Trim() 'no stdin text: EOF'
        $empty = Invoke-ChildProcess -FilePath $HostExe -ArgumentList ($HostFlags + @('-Command', '[Console]::In.ReadToEnd().Length')) -StdinText ''
        Assert-Equal '0' $empty.Stdout.Trim() 'an empty stdin text: EOF'
    }

    Invoke-Case 'child process: a program that stops reading before a huge stdin text is written is its own answer, not a failure here' {
        $huge = 'x' * (8MB)
        $run = Invoke-ChildProcess -FilePath (Join-Path $env:SystemRoot 'System32\cmd.exe') -ArgumentList @('/c', 'exit 3') -StdinText $huge
        Assert-Equal 3 $run.ExitCode 'the exit code of the program'
        Assert-True (-not $run.TimedOut) 'it must not time out'
    }

    Invoke-Case 'child process: -EnvOverrides sets a variable for the child only, and a $null value removes one' {
        $name = 'BUILDCOMMON_TEST_VARIABLE'
        $script = '[Environment]::GetEnvironmentVariable(''' + $name + ''')'
        $set = Invoke-ChildProcess -FilePath $HostExe -ArgumentList ($HostFlags + @('-Command', $script)) -EnvOverrides @{ $name = 'from the override' }
        Assert-Equal 'from the override' $set.Stdout.Trim() 'the child sees the value'
        Assert-Equal $null ([Environment]::GetEnvironmentVariable($name)) 'this process is untouched'
        Use-Environment @{ $name = 'inherited' } {
            $kept = Invoke-ChildProcess -FilePath $HostExe -ArgumentList ($HostFlags + @('-Command', $script))
            Assert-Equal 'inherited' $kept.Stdout.Trim() 'without an override the child inherits it'
            $removed = Invoke-ChildProcess -FilePath $HostExe -ArgumentList ($HostFlags + @('-Command', $script)) -EnvOverrides @{ $name = $null }
            Assert-Equal '' $removed.Stdout.Trim() 'a $null override removes the variable from the child'
        }
    }

    # ============================================================================================
    #  Invoke-Git
    # ============================================================================================
    function New-GitRepository {
        $dir = New-TempDir
        $init = Invoke-ChildProcess -FilePath 'git' -ArgumentList @('-C', $dir, 'init', '-q') -TimeoutSeconds 60
        Assert-Equal 0 $init.ExitCode "git init said: $($init.Stderr)"
        return $dir
    }

    Invoke-Case 'git: a command that works returns its result' {
        $repo = New-GitRepository
        Write-TextFile (Join-Path $repo 'a.txt') 'x'
        $result = Invoke-Git -RepoRoot $repo -Arguments @('ls-files', '-z', '--others', '--exclude-standard') -Why 'w' -Fix 'f'
        Assert-Equal 0 $result.ExitCode 'exit code'
        Assert-Contains $result.Stdout 'a.txt' 'the output'
    }

    Invoke-Case 'git: a failing command is a three-part failure that names the command and carries git''s own words' {
        $notARepository = New-TempDir
        $message = $null
        try { [void](Invoke-Git -RepoRoot $notARepository -Arguments @('rev-parse', 'HEAD') -Why 'the commit is needed' -Fix 'run it from a checkout') } catch { $message = $_.Exception.Message }
        Assert-True ($null -ne $message) 'expected a failure'
        Assert-ThreePartFailure $message
        Assert-Contains $message 'git rev-parse HEAD failed' 'the command'
        Assert-Contains $message 'the commit is needed' 'the caller''s why'
        Assert-Contains $message 'run it from a checkout' 'the caller''s fix'
    }

    Invoke-Case 'git: -SoftFail returns $null for a failing command and the result for a working one, and never throws' {
        $notARepository = New-TempDir
        Assert-Equal $null (Invoke-Git -RepoRoot $notARepository -Arguments @('rev-parse', 'HEAD') -SoftFail) 'a failing command'
        $repo = New-GitRepository
        $result = Invoke-Git -RepoRoot $repo -Arguments @('status', '--porcelain') -SoftFail
        Assert-Equal 0 $result.ExitCode 'a working command'
    }

    Invoke-Case 'git: a missing git is a three-part failure that says how to install it (and $null with -SoftFail)' {
        $emptyBin = New-TempDir
        Use-Environment @{ PATH = $emptyBin } {
            $script:Missing = $null
            try { [void](Invoke-Git -RepoRoot $emptyBin -Arguments @('status') -Why 'the list is needed' -Fix 'then run it again') } catch { $script:Missing = $_.Exception.Message }
            $script:Soft = Invoke-Git -RepoRoot $emptyBin -Arguments @('status') -SoftFail
        }
        Assert-True ($null -ne $script:Missing) 'expected a failure'
        Assert-ThreePartFailure $script:Missing
        Assert-Contains $script:Missing 'git was not found on the PATH' 'what'
        Assert-Contains $script:Missing 'Install Git for Windows' 'the fix names the install'
        Assert-Contains $script:Missing 'then run it again' 'and keeps the caller''s fix'
        Assert-Equal $null $script:Soft 'soft fail'
    }

    Invoke-Case 'git: the Why and the Fix are required unless -SoftFail is given (a failure without a reason cannot be written)' {
        $command = Get-Command Invoke-Git
        Assert-True ($command.ParameterSets.Count -eq 2) 'two parameter sets: failing and soft'
        $hard = $command.ParameterSets | Where-Object { $_.Name -ceq 'Fail' }
        $mandatory = @($hard.Parameters | Where-Object { $_.IsMandatory } | ForEach-Object { $_.Name })
        foreach ($name in @('RepoRoot', 'Arguments', 'Why', 'Fix')) { Assert-True ($mandatory -ccontains $name) "$name must be mandatory in the failing form" }
    }
}
finally {
    Exit-ReleaseTestEnvironment
    Remove-TestTempDirs
}

Complete-TestRun
