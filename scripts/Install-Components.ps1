# Run from TwinLink or manually. Installers remain interactive; no account tokens are stored.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = 'TwinLink - 安装必要组件'
$form.Width = 740
$form.Height = 520
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10)
$text = New-Object System.Windows.Forms.TextBox
$text.Multiline = $true
$text.ReadOnly = $true
$text.ScrollBars = 'Vertical'
$text.Dock = 'Fill'
$text.Text = "准备依次安装 Sunshine、Moonlight、Tailscale。`r`n系统可能弹出安装及管理员窗口，请完成安装。`r`n账户登录和 PIN 配对需要你在两台电脑上完成。`r`n"
$button = New-Object System.Windows.Forms.Button
$button.Text = '开始安装'
$button.Dock = 'Bottom'
$button.Height = 45
$form.Controls.Add($text)
$form.Controls.Add($button)
$button.Add_Click({
    $button.Enabled = $false
    try {
        $winget = Get-Command winget.exe -ErrorAction Stop
        foreach ($item in @(
            @{ Name = 'Sunshine'; Id = 'LizardByte.Sunshine' },
            @{ Name = 'Moonlight'; Id = 'MoonlightGameStreamingProject.Moonlight' },
            @{ Name = 'Tailscale'; Id = 'Tailscale.Tailscale' }
        )) {
            $text.AppendText("`r`n正在安装 $($item.Name)，请留意系统提示…`r`n")
            [System.Windows.Forms.Application]::DoEvents()
            $info = New-Object System.Diagnostics.ProcessStartInfo
            $info.FileName = $winget.Source
            # IDs are constants, never user input. Winget checks package hashes.
            $info.Arguments = 'install --id ' + $item.Id + ' --exact --source winget --accept-source-agreements --accept-package-agreements --disable-interactivity'
            $info.UseShellExecute = $false
            $info.CreateNoWindow = $true
            $info.RedirectStandardOutput = $true
            $info.RedirectStandardError = $true
            $process = [System.Diagnostics.Process]::Start($info)
            $outTask = $process.StandardOutput.ReadToEndAsync()
            $errTask = $process.StandardError.ReadToEndAsync()
            while (-not $process.WaitForExit(150)) { [System.Windows.Forms.Application]::DoEvents() }
            $text.AppendText($outTask.Result + $errTask.Result + "`r`n退出代码：$($process.ExitCode)`r`n")
            if ($process.ExitCode -eq 0) { $text.AppendText("$($item.Name) 安装完成。`r`n") }
            else { $text.AppendText("请检查上面的结果；如已安装最新版也可能返回非零代码。若失败，使用 README 中的官方下载链接。`r`n") }
            $process.Dispose()
        }
        $text.AppendText("`r`n安装流程结束。回到 TwinLink 点击【查看准备状态】。`r`n若提示重启，请先保存工作再自行重启。")
    } catch {
        $text.AppendText("`r`n安装未完成：$($_.Exception.Message)`r`n可通过 README 的官方链接手动安装。Windows 缺少 winget 时，请先从 Microsoft Store 安装【应用安装程序】。")
    } finally { $button.Text = '重新检查 / 安装'; $button.Enabled = $true }
})
[void]$form.ShowDialog()
