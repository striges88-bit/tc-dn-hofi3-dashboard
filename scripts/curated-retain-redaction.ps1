function New-CuratedRetainRedactedText {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object[]]$Findings
    )

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $Path).Path)
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    try {
        $text = $utf8.GetString($bytes)
    }
    catch [System.Text.DecoderFallbackException] {
        return [pscustomobject][ordered]@{
            status = 'invalid_source_encoding'
            text = $null
        }
    }

    $parts = [System.Text.RegularExpressions.Regex]::Split($text, '(\r\n|\r|\n)')
    $lineCount = [int][System.Math]::Ceiling($parts.Length / 2.0)
    $findingsByLine = @{}
    foreach ($finding in $Findings) {
        $line = [int]$finding.line
        if ($line -lt 1 -or $line -gt $lineCount) {
            return [pscustomobject][ordered]@{
                status = 'invalid_finding_line'
                text = $null
            }
        }

        if (-not $findingsByLine.ContainsKey($line)) {
            $findingsByLine[$line] = [System.Collections.Generic.List[string]]::new()
        }

        $type = [string]$finding.type
        if (-not $findingsByLine[$line].Contains($type)) {
            $findingsByLine[$line].Add($type)
        }
    }

    $builder = [System.Text.StringBuilder]::new($text.Length)
    for ($partIndex = 0; $partIndex -lt $parts.Length; $partIndex += 2) {
        $lineNumber = ($partIndex / 2) + 1
        $lineText = $parts[$partIndex]
        if ($findingsByLine.ContainsKey($lineNumber)) {
            if ($lineNumber -eq 1 -and $lineText.StartsWith([char]0xFEFF)) {
                [void]$builder.Append([char]0xFEFF)
            }

            $types = @($findingsByLine[$lineNumber] | Sort-Object -Unique)
            [void]$builder.Append("[REDACTED:$([string]::Join(',', $types))]")
        }
        else {
            [void]$builder.Append($lineText)
        }

        if ($partIndex + 1 -lt $parts.Length) {
            [void]$builder.Append($parts[$partIndex + 1])
        }
    }

    return [pscustomobject][ordered]@{
        status = 'redacted'
        text = $builder.ToString()
    }
}
