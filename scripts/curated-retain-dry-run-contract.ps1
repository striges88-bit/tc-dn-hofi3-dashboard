function Test-JsonBooleanProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Expected
    )

    $property = $Object.PSObject.Properties[$Name]
    return $null -ne $property -and
        $property.Value -is [bool] -and
        [bool]$property.Value -eq $Expected
}

function Test-JsonArrayProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    return $null -ne $property -and $property.Value -is [System.Array]
}

function Test-JsonNonNegativeIntegerValue {
    param([object]$Value)

    return ($Value -is [byte] -or
            $Value -is [int16] -or
            $Value -is [int32] -or
            $Value -is [int64] -or
            $Value -is [uint16] -or
            $Value -is [uint32] -or
            $Value -is [uint64]) -and [int64]$Value -ge 0
}

function Test-JsonNonNegativeIntegerProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    return $null -ne $property -and (Test-JsonNonNegativeIntegerValue -Value $property.Value)
}

function Test-JsonCountMap {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [System.Collections.IDictionary]$Expected
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $null -eq $property.Value) {
        return $false
    }

    $actualProperties = @($property.Value.PSObject.Properties)
    if ($actualProperties.Count -ne $Expected.Count) {
        return $false
    }

    foreach ($entry in $Expected.GetEnumerator()) {
        $actual = $property.Value.PSObject.Properties[[string]$entry.Key]
        if ($null -eq $actual -or
            -not (Test-JsonNonNegativeIntegerValue -Value $actual.Value) -or
            [int64]$actual.Value -ne [int64]$entry.Value) {
            return $false
        }
    }

    return $true
}

function Test-DryRunReportSummary {
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [object[]]$Files = @(),
        [object[]]$Findings = @()
    )

    $summaryProperty = $Report.PSObject.Properties['summary']
    if ($null -eq $summaryProperty -or $null -eq $summaryProperty.Value) {
        return $false
    }

    $summary = $summaryProperty.Value
    foreach ($propertyName in @('file_count', 'finding_count', 'files_requiring_redaction_review')) {
        if (-not (Test-JsonNonNegativeIntegerProperty -Object $summary -Name $propertyName)) {
            return $false
        }
    }

    $filesRequiringReview = @($Files | Where-Object { [int64]$_.finding_count -gt 0 }).Count
    if ([int64]$summary.file_count -ne $Files.Count -or
        [int64]$summary.finding_count -ne $Findings.Count -or
        [int64]$summary.files_requiring_redaction_review -ne $filesRequiringReview) {
        return $false
    }

    $severityCounts = [ordered]@{ critical = 0; review = 0; info = 0 }
    $typeCounts = [ordered]@{}
    foreach ($finding in $Findings) {
        $severity = [string]$finding.severity
        $type = [string]$finding.type
        $severityCounts[$severity]++
        if (-not $typeCounts.Contains($type)) {
            $typeCounts[$type] = 0
        }

        $typeCounts[$type]++
    }

    return (Test-JsonCountMap -Object $summary -Name 'findings_by_severity' -Expected $severityCounts) -and
        (Test-JsonCountMap -Object $summary -Name 'findings_by_type' -Expected $typeCounts)
}

function Test-DryRunReportContract {
    param(
        [Parameter(Mandatory = $true)][object]$Report,
        [Parameter(Mandatory = $true)][object]$Scan
    )

    $knownFindingTypes = @(
        'secret_reference',
        'env_reference',
        'absolute_local_path',
        'local_proxy_detail',
        'raw_jsonl_or_dump',
        'generated_export_reference'
    )
    $knownSeverities = @('critical', 'review', 'info')

    foreach ($propertyName in @('schema_version', 'generator', 'mode', 'status', 'summary', 'files', 'findings', 'blocking_reasons')) {
        if ($null -eq $Report.PSObject.Properties[$propertyName]) {
            return $false
        }
    }

    if (-not (Test-JsonNonNegativeIntegerProperty -Object $Report -Name 'schema_version') -or
        [int64]$Report.schema_version -ne 1 -or
        [string]$Report.generator -cne 'scripts/curated-retain-dry-run.ps1' -or
        [string]$Report.mode -cne 'dry-run' -or
        @('ready_for_review', 'review_required') -cnotcontains [string]$Report.status -or
        -not (Test-JsonArrayProperty -Object $Report -Name 'files') -or
        -not (Test-JsonArrayProperty -Object $Report -Name 'findings') -or
        -not (Test-JsonArrayProperty -Object $Report -Name 'blocking_reasons') -or
        @($Report.blocking_reasons).Count -ne 0) {
        return $false
    }

    foreach ($propertyName in @('external_retain_enabled', 'codex_auto_retain_enabled', 'cloud_enabled', 'calls_hindsight', 'calls_codex_retain', 'installs_hooks', 'runs_refresh_all', 'rebuilds_memory', 'imports_denylist')) {
        if (-not (Test-JsonBooleanProperty -Object $Report -Name $propertyName -Expected $false)) {
            return $false
        }
    }

    foreach ($propertyName in @('output_is_generated', 'output_should_be_ignored', 'writes_report_only')) {
        if (-not (Test-JsonBooleanProperty -Object $Report -Name $propertyName -Expected $true)) {
            return $false
        }
    }

    $files = @($Report.files)
    $findings = @($Report.findings)
    $scannedFiles = @($Scan.files)
    if (([string]$Report.status -eq 'review_required') -ne ($findings.Count -gt 0) -or
        $files.Count -ne $scannedFiles.Count) {
        return $false
    }

    $scannedFilesByPath = [System.Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($scannedFile in $scannedFiles) {
        if ($scannedFilesByPath.ContainsKey([string]$scannedFile.path)) {
            return $false
        }

        $scannedFilesByPath.Add([string]$scannedFile.path, $scannedFile)
    }

    $filePaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $freshFilePaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $reportedFindingCount = 0
    foreach ($file in $files) {
        $scannedFile = $null
        if ($null -eq $file -or
            [string]::IsNullOrWhiteSpace([string]$file.path) -or
            -not $scannedFilesByPath.TryGetValue([string]$file.path, [ref]$scannedFile) -or
            -not $filePaths.Add([string]$file.path) -or
            -not (Test-JsonNonNegativeIntegerProperty -Object $file -Name 'finding_count') -or
            -not (Test-JsonNonNegativeIntegerProperty -Object $file -Name 'size_bytes')) {
            return $false
        }

        $findingCount = @($findings | Where-Object { ([string]$_.source_path) -ceq ([string]$file.path) }).Count
        $reportedFindingCount += [int64]$file.finding_count
        $expectedRedactionStatus = if ($findingCount -eq 0) { 'candidate' } else { 'review_required' }
        if ($findingCount -ne [int64]$file.finding_count -or
            [string]$file.redaction_status -cne $expectedRedactionStatus -or
            [string]::IsNullOrWhiteSpace([string]$file.hash)) {
            return $false
        }

        if ([string]$file.hash -ceq [string]$scannedFile.hash -and
            [int64]$file.size_bytes -eq [int64]$scannedFile.size_bytes) {
            [void]$freshFilePaths.Add([string]$file.path)
        }
    }

    if ($reportedFindingCount -ne $findings.Count) {
        return $false
    }

    $scannedFindingKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($finding in @($Scan.findings)) {
        if ($freshFilePaths.Contains([string]$finding.source_path)) {
            [void]$scannedFindingKeys.Add("$([string]$finding.type)|$([string]$finding.severity)|$([bool]$finding.policy_reference)|$([string]$finding.source_path)|$([string]$finding.line)|$([string]$finding.rule)")
        }
    }

    $findingKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $freshFindingCount = 0
    foreach ($finding in $findings) {
        $policyReferenceProperty = if ($null -eq $finding) {
            $null
        }
        else {
            $finding.PSObject.Properties['policy_reference']
        }

        if ($null -eq $finding -or
            -not $filePaths.Contains([string]$finding.source_path) -or
            $knownFindingTypes -cnotcontains [string]$finding.type -or
            $knownSeverities -cnotcontains [string]$finding.severity -or
            $null -eq $policyReferenceProperty -or
            $policyReferenceProperty.Value -isnot [bool] -or
            [string]::IsNullOrWhiteSpace([string]$finding.rule) -or
            -not (Test-JsonNonNegativeIntegerProperty -Object $finding -Name 'line') -or
            [int64]$finding.line -lt 1) {
            return $false
        }

        $findingKey = "$([string]$finding.type)|$([string]$finding.source_path)|$([string]$finding.line)|$([string]$finding.rule)"
        $exactFindingKey = "$([string]$finding.type)|$([string]$finding.severity)|$([bool]$policyReferenceProperty.Value)|$([string]$finding.source_path)|$([string]$finding.line)|$([string]$finding.rule)"
        $isFreshFinding = $freshFilePaths.Contains([string]$finding.source_path)
        if ($isFreshFinding) {
            $freshFindingCount++
        }

        if (-not $findingKeys.Add($findingKey) -or
            ($isFreshFinding -and -not $scannedFindingKeys.Contains($exactFindingKey))) {
            return $false
        }
    }

    return $freshFindingCount -eq $scannedFindingKeys.Count -and
        (Test-DryRunReportSummary -Report $Report -Files $files -Findings $findings)
}
