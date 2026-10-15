Param()

Write-Host "Fixing generated test skeletons: removing Assert.ThrowsException usage..."

$testRoots = @("iConsole.Test", "iTerminal.Core.Test", "iConsole.Test.Core")
$solutionRoot = Resolve-Path "..\" -Relative | Resolve-Path

foreach ($root in $testRoots) {
    $dir = Join-Path $PWD $root
    if (-not (Test-Path $dir)) { continue }

    $files = Get-ChildItem -Path $dir -Recurse -Include *Test.cs -File
    foreach ($f in $files) {
        $content = Get-Content $f.FullName -Raw
        if ($content -match 'Assert\.ThrowsException<Exception>\(.*NotImplementedException\(\)\)') {
            $new = $content -replace 'var\s+ex\s*=\s*Assert\.ThrowsException<Exception>\(.*NotImplementedException\(\)\)\s*;\s*', '// TODO: implement test\n            '
            if ($new -notmatch 'Assert\.Inconclusive') {
                $new = $new -replace '\}\s*$','    \n        // TODO: add assertions\n        Assert.Inconclusive("Generated test skeleton - implement");\n}\n'
            }
            $new | Set-Content -Path $f.FullName -Encoding utf8
            Write-Host "Fixed $($f.FullName)"
        }
    }
}

Write-Host "Fix complete."
