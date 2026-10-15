Param()

Write-Host "Scanning iConsole source for public classes and constructors..."

$root = Resolve-Path "..\iConsole"
if (-not (Test-Path $root)) {
    Write-Error "iConsole project folder not found"
    exit 1
}

$files = Get-ChildItem -Path $root -Recurse -Include *.cs -File

$results = @()

foreach ($f in $files) {
    $text = Get-Content $f.FullName -Raw
    # find namespace
    $ns = ''
    if ($text -match 'namespace\s+([\w\.]+)') { $ns = $matches[1] }

    $classMatches = [regex]::Matches($text, 'public\s+(?:sealed\s+|static\s+)?class\s+(\w+)(?:\s*[:\(])?')
    foreach ($m in $classMatches) {
        $className = $m.Groups[1].Value
        # find constructors: look for "public ClassName(" lines
        $ctorPattern = "public\s+{0}\s*\(([^)]*)\)" -f [regex]::Escape($className)
        $ctors = [regex]::Matches($text, $ctorPattern) | ForEach-Object { $_.Groups[1].Value.Trim() }

        $results += [PSCustomObject]@{
            File = $f.FullName.Substring($PWD.Path.Length+1)
            Namespace = $ns
            Class = $className
            Constructors = $ctors -join ' || '
        }
    }
}

$out = Join-Path $PWD 'tools\iConsole_class_map.json'
$results | ConvertTo-Json -Depth 5 | Out-File -FilePath $out -Encoding utf8
Write-Host "Wrote $out with $($results.Count) entries"
