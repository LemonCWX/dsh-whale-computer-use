$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '需要 Windows .NET Framework 4.8。' }
$nativeDir = Join-Path $PSScriptRoot 'native'
& $compilerPath /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$nativeDir\WhaleComputer.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "$nativeDir\Native.cs" "$nativeDir\Settings.cs" "$nativeDir\Desktop.cs" "$nativeDir\Program.cs"
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output '鲸鱼娘桌面操控：编译完成。'
