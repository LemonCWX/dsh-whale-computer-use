param([string]$Profile = 'desktop', [string]$DshCommand = 'dsh.cmd')
$ErrorActionPreference = 'Stop'
if ($Profile -eq 'desktop' -and (Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue)) { throw '请先完全退出 DeepSeek Harness 客户端，再运行安装。安装不会关闭你的程序。' }
$entry = Join-Path $PSScriptRoot 'native\WhaleComputer.exe'
if (-not (Test-Path -LiteralPath $entry)) { & (Join-Path $PSScriptRoot 'build.ps1') }
& $DshCommand plugin --profile $Profile add "link:$PSScriptRoot"
if ($LASTEXITCODE -ne 0) { throw 'DSH 安装命令失败，未执行启用步骤。' }
$dshStateDir = if ($env:DSH_HOME) { $env:DSH_HOME } else { Join-Path $env:USERPROFILE '.dsh' }
$manifestPath = Join-Path $dshStateDir "profiles\$Profile\package.json"
$manifestText = [System.IO.File]::ReadAllText($manifestPath)
$manifest = $manifestText | ConvertFrom-Json
$bundle = '@local/dsh-whale-computer-use'
if (-not $manifest.dsh.profile) { throw '未找到 DSH profile 配置。' }
if ($manifest.dsh.profile.bundles -notcontains $bundle) {
  [System.IO.File]::WriteAllText("$manifestPath.before-whale.bak", $manifestText, [System.Text.UTF8Encoding]::new($false))
  $manifest.dsh.profile.bundles = @($manifest.dsh.profile.bundles) + $bundle
  $temporaryManifest = "$manifestPath.whale.tmp"
  [System.IO.File]::WriteAllText($temporaryManifest, ($manifest | ConvertTo-Json -Depth 100), [System.Text.UTF8Encoding]::new($false))
  [System.IO.File]::Replace($temporaryManifest, $manifestPath, $null)
}
Write-Output '安装完成。重新打开 DSH，在新对话中让它打开鲸鱼娘桌面操控设置。请把插件保留在这个目录。'
