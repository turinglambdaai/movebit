$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$output = Join-Path $repo '.rivet\acceptance\windows'
$data = Join-Path $output 'data'
New-Item -ItemType Directory -Force -Path $output,$data | Out-Null
'{"SchemaVersion":2,"WelcomeShown":true,"Language":"en","AutoCheckUpdates":false}' | Set-Content -LiteralPath (Join-Path $data 'config.json') -Encoding utf8
$exe = Get-ChildItem -LiteralPath (Join-Path $repo 'dist') -Filter RivetHost.exe -File -Recurse | Select-Object -First 1
if (-not $exe) { throw 'packaged MoveBit executable was not found' }
function Wait-Until([string]$Description,[scriptblock]$Probe,[int]$TimeoutSeconds=90) { $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds); while([DateTime]::UtcNow -lt $deadline){$v=& $Probe;if($null -ne $v -and $v -ne $false){return $v};Start-Sleep -Milliseconds 100};throw "timed out waiting for $Description" }
function Contains-Text($Root,[string]$Fragment) { $nodes=$Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition); for($i=0;$i -lt $nodes.Count;$i++){if($nodes[$i].Current.Name -like "*$Fragment*"){return $true}};return $false }
function Save-Snapshot($Root,[string]$Path) { $nodes=$Root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition); $snapshot=for($i=0;$i -lt [Math]::Min($nodes.Count,800);$i++){$n=$nodes[$i];[ordered]@{name=$n.Current.Name;controlType=$n.Current.ControlType.ProgrammaticName;automationId=$n.Current.AutomationId}}; ConvertTo-Json -InputObject @($snapshot) -Depth 4 | Set-Content -LiteralPath $Path -Encoding utf8 }
$process=$null;$oldData=$env:MOVEBIT_DATA_DIR;$oldBootstrap=$env:MOVEBIT_BOOTSTRAP_DIR;$oldAcceptance=$env:MOVEBIT_ACCEPTANCE
try { $env:MOVEBIT_DATA_DIR=$data;$env:MOVEBIT_BOOTSTRAP_DIR=$data;$env:MOVEBIT_ACCEPTANCE='1';$process=Start-Process -FilePath $exe.FullName -PassThru -WorkingDirectory $repo
  $desktop=[System.Windows.Automation.AutomationElement]::RootElement;$window=Wait-Until 'MoveBit window' { $c=[System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id);$desktop.FindFirst([System.Windows.Automation.TreeScope]::Children,$c) }
  # The v1 app owns several windows (the pointer-through Mubi card, the main
  # window, transient cue cards); probe EVERY process window for the backend
  # state instead of assuming the first one is the main one.
  try { [void](Wait-Until 'backend activity state' { $all=[System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children,[System.Windows.Automation.Condition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty,$process.Id)); for($i=0;$i -lt $all.Count;$i++){ if(Contains-Text $all.Item($i) 'Active today'){ return $true } }; return $false }) } catch { Save-Snapshot $window (Join-Path $output 'accessibility-failure.json');throw }
  Save-Snapshot $window (Join-Path $output 'accessibility.json');[ordered]@{platform='windows';actions=@('launch:isolated-config');assertions=@('packaged WinUI window activated','RVT1 backend activity state reached native accessibility tree')} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'interaction-trace.json') -Encoding utf8
  $pattern=$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern);([System.Windows.Automation.WindowPattern]$pattern).Close();[void](Wait-Until 'process exit' {$process.Refresh();$process.HasExited} 30)
} finally { $env:MOVEBIT_DATA_DIR=$oldData;$env:MOVEBIT_BOOTSTRAP_DIR=$oldBootstrap;$env:MOVEBIT_ACCEPTANCE=$oldAcceptance;if($process -and -not $process.HasExited){Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue};if($process){$process.Dispose()} }
