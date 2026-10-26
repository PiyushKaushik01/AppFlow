$work = Join-Path $env:TEMP ('AppFlow-idle-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
@{ Applications = @(); Profiles = @(); Settings = @{ StartWithWindows = $false; LaunchMinimized = $false; MinimizeToTray = $true; DefaultDelayMs = 1000; Notifications = $false } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'data.json') -Encoding utf8
$psi = [System.Diagnostics.ProcessStartInfo]::new((Join-Path (Get-Location) 'publish\AppFlow.exe'))
$psi.UseShellExecute = $false; $psi.Environment['APPFLOW_DATA_DIR'] = $work
$app = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Seconds 35
$app.Refresh(); $before = $app.TotalProcessorTime.TotalSeconds
Start-Sleep -Seconds 10
$app.Refresh(); $cpu = [math]::Round($app.TotalProcessorTime.TotalSeconds - $before, 3)
$memory = [math]::Round($app.WorkingSet64 / 1MB, 1)
$app.CloseMainWindow() | Out-Null; Start-Sleep -Seconds 1; $app.Refresh()
[pscustomobject]@{ CpuSecondsOver10s = $cpu; WorkingSetMB = $memory; AliveAfterWindowClose = (-not $app.HasExited); DataFolder = $work } | Format-List
if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit(5000) | Out-Null }
$resolved = (Resolve-Path -LiteralPath $work).Path
$tempRoot = (Resolve-Path -LiteralPath $env:TEMP).Path.TrimEnd('\') + '\'
if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -match '^AppFlow-idle-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force }
