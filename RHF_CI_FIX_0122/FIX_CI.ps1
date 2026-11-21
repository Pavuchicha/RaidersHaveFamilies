$ErrorActionPreference = 'Stop'
try {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $testPath = Join-Path $root 'tests\rhf_040_contracts.py'
    $sourcePath = Join-Path $root 'RaidersHaveFamilies\Source\RaidersHaveFamilies.cs'
    if (!(Test-Path -LiteralPath $testPath) -or !(Test-Path -LiteralPath $sourcePath)) {
        throw 'Cannot find source files. Place RHF_CI_FIX_0122 in the RaidersHaveFamilies project root.'
    }
    $source = [System.IO.File]::ReadAllText($sourcePath)
    $expectedSource = @("MaxPerWave = 48", "HardMaxPerCycle = 48")
    foreach ($s in $expectedSource) {
        if (!$source.Contains($s)) { throw "Unexpected source version: $s not found. Refusing to change tests." }
    }
    $content = [System.IO.File]::ReadAllText($testPath)
    $old = "assert 'MaxPerWave = 16' in source and 'HardMaxPerCycle = 16' in source"
    $new = "assert 'MaxPerWave = 48' in source and 'HardMaxPerCycle = 48' in source"
    if ($content.Contains($new) -and !$content.Contains($old)) {
        Write-Host 'Already corrected; no changes needed.' -ForegroundColor Green
        exit 0
    }
    if (!$content.Contains($old)) { throw 'Expected old assertion not found. File was changed; no changes made.' }
    if ($content.Split([string[]]@($old),[System.StringSplitOptions]::None).Length -ne 2) {
        throw 'Expected assertion not unique; no changes made.'
    }
    $fixed = $content.Replace($old, $new)
    $backup = Join-Path $env:TEMP ('RHF_rhf040_backup_' + [DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss') + '.py')
    [System.IO.File]::Copy($testPath, $backup)
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($testPath, $fixed, $utf8)
    Write-Host 'SUCCESS: tests/rhf_040_contracts.py now checks the new limit (48).' -ForegroundColor Green
    Write-Host 'Backup outside repository:' $backup -ForegroundColor Gray
    Write-Host 'Next: GitHub Desktop -> Commit to main -> Push origin.' -ForegroundColor Cyan
} catch {
    Write-Host "FAILED: $_" -ForegroundColor Red
    exit 1
}
