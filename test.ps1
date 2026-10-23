param([switch]$NativeOnly)
$ErrorActionPreference = 'Stop'
if (-not $NativeOnly) {
  $testPaths = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.test.mjs' | ForEach-Object FullName)
  & node --test @testPaths
  if ($LASTEXITCODE -ne 0) { throw 'JavaScript checks failed.' }
}
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Windows .NET Framework 4.8 is required.' }
$scratchPath = Join-Path ([IO.Path]::GetTempPath()) ('DSHWhaleTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratchPath | Out-Null
$testExecutable = Join-Path $scratchPath 'NativeTests.exe'
$nativePath = Join-Path $PSScriptRoot 'native'
try {
  & $compilerPath /nologo /target:exe /platform:x64 /optimize+ /utf8output /main:NativeTests "/out:$testExecutable" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "$nativePath\Native.cs" "$nativePath\Settings.cs" "$nativePath\Desktop.cs" "$nativePath\Program.cs" (Join-Path $PSScriptRoot 'tests\NativeTests.cs')
  if ($LASTEXITCODE -ne 0) { throw 'Native test compilation failed.' }
  & $testExecutable $scratchPath
  if ($LASTEXITCODE -ne 0) { throw 'Native logic checks failed.' }
} finally {
  foreach ($leaf in @('NativeTests.exe','settings-test.json','settings-test.json.tmp')) {
    $temporaryPath = Join-Path $scratchPath $leaf
    if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
  }
  Remove-Item -LiteralPath $scratchPath
}
