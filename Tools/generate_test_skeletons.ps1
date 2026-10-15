Param()

Write-Host "Generating MSTest skeletons for public classes..."

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$solutionRoot = Resolve-Path "$root\..\"

$projects = @{
    'iTerminal.Core' = 'iTerminal.Core.Test'
    'iConsole' = 'iConsole.Test'
    'iConsole.Inputs' = 'iConsole.Test.Core'
}

function Find-PublicClasses($path) {
    Get-ChildItem -Path $path -Recurse -Include *.cs -File |
        Select-String -Pattern 'namespace\s+([\w\.]+)|public\s+class\s+(\w+)' -AllMatches |
        ForEach-Object {
            $_.Line
        }
}

function Get-ClassInfos($srcDir) {
    $files = Get-ChildItem -Path $srcDir -Recurse -Include *.cs -File
    foreach ($f in $files) {
        $content = Get-Content $f.FullName -Raw
        if ($content -match 'namespace\s+([\w\.]+)') { $ns = $matches[1] } else { $ns = $null }
        $classMatches = [regex]::Matches($content, 'public\s+class\s+(\w+)')
        foreach ($m in $classMatches) {
            [PSCustomObject]@{
                File = $f.FullName
                Namespace = $ns
                Class = $m.Groups[1].Value
            }
        }
    }
}

foreach ($projPrefix in $projects.Keys) {
    $srcPath = Join-Path $solutionRoot $projPrefix
    if (-Not (Test-Path $srcPath)) { continue }

    $testProj = $projects[$projPrefix]
    $testRoot = Join-Path $solutionRoot $testProj
    if (-Not (Test-Path $testRoot)) {
        Write-Host "Test project directory not found: $testRoot - skipping"
        continue
    }

    $classInfos = Get-ClassInfos $srcPath
    foreach ($ci in $classInfos) {
        $className = $ci.Class
        $ns = $ci.Namespace
        if (-not $ns) { continue }

        # Determine test folder path mirroring namespace under test project
        $relPath = $ns.Replace($projPrefix, '').TrimStart('.') -replace '\.', '\\'
        if ($relPath -eq '') { $relPath = 'Generated' }
        $destDir = Join-Path $testRoot $relPath
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }

        $testFile = Join-Path $destDir ($className + 'Test.cs')
        if (Test-Path $testFile) { continue }

        $testNamespace = $ns + '.Test'
        $template = @"
using Microsoft.VisualStudio.TestTools.UnitTesting;
using $ns;

namespace $testNamespace;

[TestClass]
public class ${className}Test
{
    [TestMethod]
    public void Constructor_Default_ShouldCreateInstance()
    {
        // Arrange

        // Act
        var ex = Assert.ThrowsException<Exception>(() => throw new System.NotImplementedException());

        // Assert
        Assert.Inconclusive("Implement tests for $ns.$className");
    }
}
"@

        $template | Out-File -FilePath $testFile -Encoding utf8
        Write-Host "Created test skeleton: $testFile"
    }
}

Write-Host "Generation complete. Review and implement the generated tests." 
