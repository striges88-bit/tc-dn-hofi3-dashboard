function Join-ProcessArguments {
    param([string[]]$Arguments)

    $quoted = foreach ($argument in $Arguments) {
        if ($null -eq $argument) {
            '""'
        }
        elseif ($argument.Length -eq 0 -or $argument.IndexOfAny([char[]]@(' ', "`t", '"')) -ge 0) {
            '"' + $argument.Replace('"', '\"') + '"'
        }
        else {
            $argument
        }
    }

    return [string]::Join(' ', $quoted)
}

function Invoke-GitText {
    param(
        [string]$Root,
        [string[]]$Arguments,
        [int]$TimeoutMilliseconds = 10000
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'git'
    $startInfo.Arguments = Join-ProcessArguments (@('-C', $Root) + $Arguments)
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            return [ordered]@{
                succeeded = $false
                output = ''
                error = 'git process did not start'
                failure_code = 'git-unavailable'
                timed_out = $false
                timeout_ms = $TimeoutMilliseconds
                exit_code = $null
            }
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutMilliseconds)) {
            try {
                $process.Kill()
            }
            catch {
                # The timeout remains the primary failure evidence.
            }

            return [ordered]@{
                succeeded = $false
                output = ''
                error = 'git command timed out'
                failure_code = 'git-timeout'
                timed_out = $true
                timeout_ms = $TimeoutMilliseconds
                exit_code = $null
            }
        }

        $process.WaitForExit()
        $stdout = $stdoutTask.Result.Trim()
        $stderr = $stderrTask.Result.Trim()
        if ($process.ExitCode -ne 0) {
            return [ordered]@{
                succeeded = $false
                output = $stdout
                error = "git exit $($process.ExitCode): $stderr"
                failure_code = 'git-exit-nonzero'
                timed_out = $false
                timeout_ms = $TimeoutMilliseconds
                exit_code = $process.ExitCode
            }
        }

        return [ordered]@{
            succeeded = $true
            output = $stdout
            error = ''
            failure_code = ''
            timed_out = $false
            timeout_ms = $TimeoutMilliseconds
            exit_code = $process.ExitCode
        }
    }
    catch {
        return [ordered]@{
            succeeded = $false
            output = ''
            error = $_.Exception.Message
            failure_code = 'git-unavailable'
            timed_out = $false
            timeout_ms = $TimeoutMilliseconds
            exit_code = $null
        }
    }
    finally {
        $process.Dispose()
    }
}

function New-GitFailureEvidence {
    param(
        [string]$Operation,
        [object]$Result
    )

    return [ordered]@{
        operation = $Operation
        failure_code = [string]$Result.failure_code
        timed_out = [bool]$Result.timed_out
        timeout_ms = [int]$Result.timeout_ms
        exit_code = $Result.exit_code
        error = [string]$Result.error
    }
}

function Test-JsonPropertyFalse {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    return $null -ne $property -and $property.Value -is [bool] -and $property.Value -eq $false
}

function Test-JsonPropertyExists {
    param(
        [object]$Object,
        [string]$Name
    )

    return $null -ne $Object.PSObject.Properties[$Name]
}

function Get-JsonPropertyValue {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-JsonStringProperty {
    param(
        [object]$Object,
        [string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $value = Get-JsonPropertyValue -Object $Object -Name $Name
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    return $value
}

function Read-JsonFile {
    param([string]$Path)

    return (Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json)
}

function Test-LanceDbEvalReport {
    param(
        [object]$Report,
        [int]$MinimumEvalCases
    )

    if ($null -eq $Report) {
        return [ordered]@{ passed = $false; detail = 'eval report missing' }
    }

    $requiredProperties = @(
        'generator',
        'command',
        'status',
        'source_store',
        'cloud_enabled',
        'auto_commit_refresh_enabled',
        'direct_project_crawl_enabled',
        'commit_hook_installed',
        'passed',
        'failed_count',
        'passed_count'
    )
    $missingProperties = @($requiredProperties | Where-Object { -not (Test-JsonPropertyExists -Object $Report -Name $_) })
    if ($missingProperties.Count -gt 0) {
        $statusValue = Get-JsonPropertyValue -Object $Report -Name 'status'
        $commandValue = Get-JsonPropertyValue -Object $Report -Name 'command'
        return [ordered]@{
            passed = $false
            detail = "missing properties: $($missingProperties -join ', '); status=$statusValue; command=$commandValue"
        }
    }

    $expectedStrings = [ordered]@{
        generator = 'tools/MemorySemantic/lancedb_sidecar.py'
        command = 'eval'
        status = 'ok'
        source_store = 'sqlite-fts5'
    }
    foreach ($field in $expectedStrings.Keys) {
        $actual = Get-JsonPropertyValue -Object $Report -Name $field
        $expected = $expectedStrings[$field]
        if ($actual -isnot [string] -or $actual -cne $expected) {
            return [ordered]@{ passed = $false; detail = "eval report $field mismatch: expected $expected, got $actual" }
        }
    }

    $expectedBooleans = [ordered]@{
        cloud_enabled = $false
        auto_commit_refresh_enabled = $false
        direct_project_crawl_enabled = $false
        commit_hook_installed = $false
        passed = $true
    }
    foreach ($field in $expectedBooleans.Keys) {
        $actual = Get-JsonPropertyValue -Object $Report -Name $field
        $expected = $expectedBooleans[$field]
        if ($actual -isnot [bool] -or $actual -ne $expected) {
            return [ordered]@{ passed = $false; detail = "eval report $field must be JSON boolean $expected" }
        }
    }

    $failedCountValue = Get-JsonPropertyValue -Object $Report -Name 'failed_count'
    $passedCountValue = Get-JsonPropertyValue -Object $Report -Name 'passed_count'
    if ((-not (($failedCountValue -is [int]) -or ($failedCountValue -is [long]))) -or $failedCountValue -ne 0) {
        return [ordered]@{ passed = $false; detail = "eval report failed_count must be JSON integer 0, got $failedCountValue" }
    }
    if ((-not (($passedCountValue -is [int]) -or ($passedCountValue -is [long]))) -or $passedCountValue -lt $MinimumEvalCases) {
        return [ordered]@{ passed = $false; detail = "eval report passed_count must be a JSON integer >= $MinimumEvalCases, got $passedCountValue" }
    }

    return [ordered]@{
        passed = $true
        detail = "passed_count=$passedCountValue; failed_count=$failedCountValue"
    }
}

function Test-RefreshAllReport {
    param([object]$Report)

    $falseFlags = @(
        'cloud_enabled',
        'codex_auto_retain_enabled',
        'auto_commit_refresh_enabled',
        'commit_hook_installed',
        'installs_hooks',
        'direct_project_crawl_enabled',
        'imports_raw_jsonl',
        'imports_generated_exports',
        'uses_generated_exports_as_source',
        'imports_secrets',
        'imports_local_proxy_details',
        'imports_build_artifacts',
        'touches_raw_jsonl',
        'touches_hindsight_store',
        'touches_secret_storage',
        'touches_build_artifacts'
    )

    foreach ($flag in $falseFlags) {
        if (-not (Test-JsonPropertyFalse -Object $Report -Name $flag)) {
            return "Unexpected refresh-all flag: $flag"
        }
    }

    foreach ($step in @($Report.steps)) {
        foreach ($flag in @('uses_cloud', 'uses_hook')) {
            if (-not (Test-JsonPropertyFalse -Object $step -Name $flag)) {
                return "Unexpected refresh-all step $flag type/value: $($step.name)"
            }
        }
    }

    return ''
}

function Test-RefreshAllSteps {
    param([object]$Report)

    $expectedSteps = @(
        'legacy-json-refresh',
        'sqlite-refresh',
        'sqlite-stale-check',
        'lancedb-cleanup',
        'lancedb-rebuild',
        'lancedb-eval'
    )

    $steps = @($Report.steps)
    $actualSteps = @($steps | ForEach-Object { $_.name })
    if (($actualSteps -join '|') -ne ($expectedSteps -join '|')) {
        return "Unexpected refresh-all step order: $($actualSteps -join ', ')"
    }

    foreach ($step in $steps) {
        if ($step.status -ne 'completed') {
            return "Refresh-all step status failed: $($step.name)"
        }

        $exitCode = Get-JsonPropertyValue -Object $step -Name 'exit_code'
        if ((-not (($exitCode -is [int]) -or ($exitCode -is [long]))) -or $exitCode -ne 0) {
            return "Refresh-all step exit_code must be JSON integer 0: $($step.name)"
        }
    }

    $staleStep = $steps | Where-Object { $_.name -eq 'sqlite-stale-check' } | Select-Object -First 1
    if ($null -eq $staleStep -or ($staleStep.stdout_tail -notmatch '"issues"\s*:\s*\[\s*\]')) {
        return 'SQLite stale-check did not report an empty issue list'
    }

    return ''
}

function Test-CommitAddressedFreshness {
    param(
        [string]$Root,
        [object]$RefreshReport,
        [object]$EvalReport,
        [scriptblock]$GitInvoker = $null
    )

    if ($null -eq $GitInvoker) {
        $GitInvoker = {
            param($Root, $Arguments, $TimeoutMilliseconds)
            Invoke-GitText -Root $Root -Arguments $Arguments -TimeoutMilliseconds $TimeoutMilliseconds
        }
    }

    $gitTimeoutMilliseconds = 10000
    $headResult = & $GitInvoker `
        -Root $Root `
        -Arguments @('rev-parse', '--verify', 'HEAD') `
        -TimeoutMilliseconds $gitTimeoutMilliseconds
    if (-not $headResult.succeeded) {
        return [ordered]@{
            passed = $false
            detail = "Git HEAD unavailable: $($headResult.error)"
            evidence = New-GitFailureEvidence -Operation 'resolve-head' -Result $headResult
        }
    }

    $treeResult = & $GitInvoker `
        -Root $Root `
        -Arguments @('rev-parse', 'HEAD^{tree}') `
        -TimeoutMilliseconds $gitTimeoutMilliseconds
    if (-not $treeResult.succeeded) {
        return [ordered]@{
            passed = $false
            detail = "Git tree unavailable: $($treeResult.error)"
            evidence = New-GitFailureEvidence -Operation 'resolve-tree' -Result $treeResult
        }
    }

    $refreshCommit = Get-JsonStringProperty -Object $RefreshReport -Name 'commit_sha'
    $refreshTree = Get-JsonStringProperty -Object $RefreshReport -Name 'tree_sha'
    $evalCommit = Get-JsonStringProperty -Object $EvalReport -Name 'commit_sha'
    $evalTree = Get-JsonStringProperty -Object $EvalReport -Name 'tree_sha'
    if ($null -eq $refreshCommit -or $null -eq $refreshTree -or $null -eq $evalCommit -or $null -eq $evalTree) {
        return [ordered]@{
            passed = $false
            detail = 'Missing non-empty string commit_sha/tree_sha in refresh or eval report'
            evidence = $null
        }
    }

    $head = [string]$headResult.output
    $tree = [string]$treeResult.output
    $passed = $refreshCommit -eq $head -and
        $evalCommit -eq $head -and
        $refreshTree -eq $tree -and
        $evalTree -eq $tree
    $detail = "HEAD=$head; tree=$tree; refresh_commit=$refreshCommit; refresh_tree=$refreshTree; eval_commit=$evalCommit; eval_tree=$evalTree"
    return [ordered]@{ passed = $passed; detail = $detail; evidence = $null }
}

function Test-SemanticIndexManifest {
    param(
        [string]$DatabasePath,
        [string]$StorePath,
        [string]$ManifestPath,
        [object]$RefreshReport,
        [object]$EvalReport
    )

    if (-not (Test-Path -LiteralPath $DatabasePath -PathType Leaf)) {
        return [ordered]@{ passed = $false; detail = "Canonical SQLite store missing: $DatabasePath" }
    }
    if (-not (Test-Path -LiteralPath $StorePath -PathType Container)) {
        return [ordered]@{ passed = $false; detail = "LanceDB store missing: $StorePath" }
    }
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return [ordered]@{ passed = $false; detail = "LanceDB index manifest missing: $ManifestPath" }
    }

    try {
        $manifest = Read-JsonFile -Path $ManifestPath
    }
    catch {
        return [ordered]@{ passed = $false; detail = "Invalid LanceDB index manifest JSON: $($_.Exception.Message)" }
    }

    $schemaVersion = Get-JsonPropertyValue -Object $manifest -Name 'schema_version'
    if ((-not (($schemaVersion -is [int]) -or ($schemaVersion -is [long]))) -or $schemaVersion -ne 1) {
        return [ordered]@{ passed = $false; detail = "Index manifest schema_version mismatch: expected integer 1, got $schemaVersion" }
    }

    $expectedStringFields = [ordered]@{
        generator = 'tools/MemorySemantic/lancedb_sidecar.py'
        status = 'ready'
        source_store = 'sqlite-fts5'
        lancedb_table = 'memory_documents'
    }
    foreach ($field in $expectedStringFields.Keys) {
        $actual = Get-JsonPropertyValue -Object $manifest -Name $field
        $expected = $expectedStringFields[$field]
        if ($actual -isnot [string] -or $actual -cne $expected) {
            return [ordered]@{ passed = $false; detail = "Index manifest $field mismatch: expected $expected, got $actual" }
        }
    }

    $indexedCount = Get-JsonPropertyValue -Object $manifest -Name 'indexed_count'
    if ((-not (($indexedCount -is [int]) -or ($indexedCount -is [long]))) -or $indexedCount -lt 0) {
        return [ordered]@{ passed = $false; detail = "Index manifest indexed_count must be a non-negative JSON integer, got $indexedCount" }
    }

    $embeddingFields = @(
        'embedding_provider',
        'embedding_model',
        'embedding_runtime_model',
        'embedding_dimensions',
        'embedding_package_version',
        'embedding_package_pin',
        'embedding_pooling'
    )
    foreach ($field in $embeddingFields) {
        $manifestValue = Get-JsonPropertyValue -Object $manifest -Name $field
        $evalValue = Get-JsonPropertyValue -Object $EvalReport -Name $field
        if ($null -eq $manifestValue -or $null -eq $evalValue -or $manifestValue.GetType() -ne $evalValue.GetType() -or $manifestValue -ne $evalValue) {
            return [ordered]@{ passed = $false; detail = "Index manifest $field does not match eval report" }
        }
    }

    foreach ($field in @('commit_sha', 'tree_sha')) {
        $manifestValue = Get-JsonStringProperty -Object $manifest -Name $field
        $refreshValue = Get-JsonStringProperty -Object $RefreshReport -Name $field
        $evalValue = Get-JsonStringProperty -Object $EvalReport -Name $field
        if ($null -eq $manifestValue -or $manifestValue -ne $refreshValue -or $manifestValue -ne $evalValue) {
            return [ordered]@{
                passed = $false
                detail = "Index manifest $field=$manifestValue does not match refresh=$refreshValue and eval=$evalValue"
            }
        }
    }

    $manifestIndexedAt = Get-JsonStringProperty -Object $manifest -Name 'indexed_at'
    $evalIndexedAt = Get-JsonStringProperty -Object $EvalReport -Name 'indexed_at'
    if ($null -eq $manifestIndexedAt -or $manifestIndexedAt -ne $evalIndexedAt) {
        return [ordered]@{ passed = $false; detail = "Index manifest indexed_at=$manifestIndexedAt does not match eval=$evalIndexedAt" }
    }

    return [ordered]@{ passed = $true; detail = 'SQLite/LanceDB stores and manifest commit/tree/embedding identity match refresh and eval' }
}
