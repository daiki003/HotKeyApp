param()

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$projectPath = Join-Path $projectRoot 'HotKeyCommandApp.csproj'
$appPath = Join-Path $projectRoot 'bin\Debug\net8.0-windows\HotKeyCommandApp.exe'

if (-not (Test-Path -LiteralPath $appPath)) {
    throw 'HotKeyApp のビルドが見つかりません。先にビルドしてください。'
}

# 操作用の実行セッションで待機し続けず、独立した dotnet run を起動する。
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
$runner = Start-Process -FilePath $dotnetPath `
    -ArgumentList @('run', '--project', ('"' + $projectPath + '"'), '--no-build') `
    -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru

$deadline = [DateTime]::UtcNow.AddSeconds(10)
do {
    $app = Get-CimInstance Win32_Process -Filter "Name='HotKeyCommandApp.exe'" |
        Where-Object { $_.ExecutablePath -eq $appPath } |
        Sort-Object CreationDate |
        Select-Object -First 1
    if ($null -ne $app) {
        [PSCustomObject]@{ ProcessId = $app.ProcessId; RunnerId = $runner.Id; ExecutablePath = $app.ExecutablePath }
        return
    }
    if ($runner.HasExited -and $runner.ExitCode -ne 0) {
        throw ('HotKeyApp の起動に失敗しました。終了コード: ' + $runner.ExitCode)
    }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)

throw 'HotKeyApp のプロセスを確認できませんでした。'
