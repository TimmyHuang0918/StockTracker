param([switch]$RefreshSite)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$build = Join-Path $repo 'artifacts\market-overview\bin'
$msbuild = 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe'
& $msbuild (Join-Path $repo 'StockTracker.slnx') /t:Build /p:Configuration=Debug /p:Platform=x64 "/p:OutputPath=$build\" "/p:OutDir=$build\" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.dll','System.Core.dll','System.Data.dll','System.Net.Http.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll','System.Xaml.dll') | ForEach-Object { "/r:$(Join-Path $framework $_)" }
$refs += @("/r:$build\StockTracker.exe", "/r:$build\Newtonsoft.Json.dll", "/r:$build\System.Data.SQLite.dll")
& (Join-Path (Split-Path $msbuild) 'Roslyn\csc.exe') /nologo /langversion:7.3 /target:exe /platform:x64 "/out:$build\MarketOverviewChecks.exe" $refs (Join-Path $PSScriptRoot 'MarketOverviewChecks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
$arguments = @($repo)
if ($RefreshSite) { $arguments += '--refresh-site' }
& "$build\MarketOverviewChecks.exe" @arguments
if ($LASTEXITCODE -ne 0) { throw 'Market overview checks failed' }
