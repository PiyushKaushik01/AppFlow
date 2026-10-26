$work = Join-Path $env:TEMP ('AppFlow-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$marker = Join-Path $work 'order.txt'
$triggerExe = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$cmd = Join-Path $env:WINDIR 'System32\cmd.exe'
$helperExe = Join-Path $work 'cmd.exe'
Copy-Item -LiteralPath $cmd -Destination $helperExe
$data = @{
    Applications = @(
        @{ Id = 'trigger'; Name = 'PowerShell'; Path = $triggerExe; Arguments = ''; Source = 'Test' },
        @{ Id = 'one'; Name = 'First marker'; Path = $cmd; Arguments = "/c echo FIRST>>`"$marker`""; Source = 'Test' },
        @{ Id = 'two'; Name = 'Second marker'; Path = $cmd; Arguments = "/c echo SECOND>>`"$marker`""; Source = 'Test' }
    )
    Profiles = @(@{ Id = 'smoke'; Name = 'Smoke'; TriggerAppId = 'trigger'; ActionAppIds = @('one', 'two'); CloseAppIds = @(); Enabled = $true; DelayMs = 500; RelaunchRunning = $false; TriggerOnWindow = $false })
    Settings = @{ StartWithWindows = $false; LaunchMinimized = $false; MinimizeToTray = $true; DefaultDelayMs = 500; Notifications = $false }
}
$data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $work 'data.json') -Encoding utf8
$helper = Start-Process -FilePath $helperExe -ArgumentList '/c ping 127.0.0.1 -n 30 > nul' -WindowStyle Hidden -PassThru
$psi = [System.Diagnostics.ProcessStartInfo]::new((Join-Path (Get-Location) 'publish\AppFlow.exe'))
$psi.UseShellExecute = $false
$psi.Environment['APPFLOW_DATA_DIR'] = $work
$app = [System.Diagnostics.Process]::Start($psi)
Start-Sleep -Seconds 10
$trigger = Start-Process -FilePath $triggerExe -ArgumentList '-NoProfile', '-Command', 'Start-Sleep -Seconds 12' -WindowStyle Hidden -PassThru
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline -and -not (Test-Path -LiteralPath $marker)) { Start-Sleep -Milliseconds 250 }
Start-Sleep -Milliseconds 1500
$content = if (Test-Path -LiteralPath $marker) { Get-Content -LiteralPath $marker -Raw } else { '<no output>' }
Start-Sleep -Seconds 10
$app.Refresh()
$cpuBefore = $app.TotalProcessorTime.TotalSeconds
Start-Sleep -Seconds 10
$app.Refresh()
$cpuSeconds = [math]::Round($app.TotalProcessorTime.TotalSeconds - $cpuBefore, 3)
$memoryMB = [math]::Round($app.WorkingSet64 / 1MB, 1)
$trayProbe = @'
using System;
using System.Runtime.InteropServices;
public static class AppFlowTrayProbe {
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hwnd, int command);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@
Add-Type -TypeDefinition $trayProbe
$hwnd = $app.MainWindowHandle
[AppFlowTrayProbe]::ShowWindowAsync($hwnd, 6) | Out-Null
Start-Sleep -Seconds 1
$app.Refresh()
$minimizedHidden = -not [AppFlowTrayProbe]::IsWindowVisible($hwnd)
$aliveAfterMinimize = -not $app.HasExited
$appExe = (Resolve-Path -LiteralPath 'publish\AppFlow.exe').Path
$secondInstance = [System.Diagnostics.Process]::Start($psi)
$secondExited = $secondInstance.WaitForExit(5000)
Start-Sleep -Milliseconds 500
$app.Refresh()
$visibleAfterSecondLaunch = [AppFlowTrayProbe]::IsWindowVisible($hwnd)
$sameBuildInstances = @([System.Diagnostics.Process]::GetProcessesByName('AppFlow') | Where-Object { $_.Path -eq $appExe }).Count
$app.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 1
$app.Refresh()
[pscustomobject]@{ TriggerPid = $trigger.Id; BackgroundHelperPid = $helper.Id; MarkerOrder = $content.Trim(); CpuSecondsOver10s = $cpuSeconds; WorkingSetMB = $memoryMB; MinimizedIntoTray = $minimizedHidden; AliveAfterMinimize = $aliveAfterMinimize; SecondLaunchExited = $secondExited; ExistingWindowShownAgain = $visibleAfterSecondLaunch; AppFlowCopiesForBuild = $sameBuildInstances; AliveAfterWindowClose = (-not $app.HasExited); DataFolder = $work } | Format-List
if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit(5000) | Out-Null }
if (-not $secondInstance.HasExited) { $secondInstance.Kill(); $secondInstance.WaitForExit(5000) | Out-Null }
if (-not $helper.HasExited) { $helper.Kill(); $helper.WaitForExit(5000) | Out-Null }
$resolved = (Resolve-Path -LiteralPath $work).Path
$tempRoot = (Resolve-Path -LiteralPath $env:TEMP).Path.TrimEnd('\') + '\'
if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf) -match '^AppFlow-smoke-[a-f0-9]{32}$') { Remove-Item -LiteralPath $resolved -Recurse -Force }
if ($content.Trim() -ne "FIRST`r`nSECOND" -and $content.Trim() -ne "FIRST`nSECOND") { throw "Companion apps did not launch in order while a headless same-name helper was running: $content" }
if (-not $minimizedHidden -or -not $aliveAfterMinimize -or -not $secondExited -or -not $visibleAfterSecondLaunch -or $sameBuildInstances -ne 1) { throw 'Tray or single-instance activation check failed.' }
