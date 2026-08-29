# Silent Windows toast for Claude Code notifications.
# Reads the hook payload on stdin and shows a visual-only notification (no sound).
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$message = 'Waiting for you in Claude Code.'
try {
    $raw = [Console]::In.ReadToEnd()
    if ($raw) {
        $payload = $raw | ConvertFrom-Json
        if ($payload.message) { $message = [string]$payload.message }
    }
} catch { }

# Toast XML declares audio silent="true" so nothing is ever played.
$xmlText = @"
<toast activationType="none">
  <visual>
    <binding template="ToastGeneric">
      <text>Claude Code</text>
      <text>$([System.Security.SecurityElement]::Escape($message))</text>
    </binding>
  </visual>
  <audio silent="true"/>
</toast>
"@

try {
    [void][Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime]
    [void][Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom, ContentType = WindowsRuntime]

    $xml = New-Object Windows.Data.Xml.Dom.XmlDocument
    $xml.LoadXml($xmlText)

    $appId = '{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\WindowsPowerShell\v1.0\powershell.exe'
    $toast = New-Object Windows.UI.Notifications.ToastNotification $xml
    [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier($appId).Show($toast)
    exit 0
} catch {
    # Fall back to a balloon tip. BalloonTipIcon 'None' is the silent variant.
    try {
        Add-Type -AssemblyName System.Windows.Forms
        $icon = New-Object System.Windows.Forms.NotifyIcon
        $icon.Icon = [System.Drawing.SystemIcons]::Information
        $icon.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::None
        $icon.BalloonTipTitle = 'Claude Code'
        $icon.BalloonTipText = $message
        $icon.Visible = $true
        $icon.ShowBalloonTip(5000)
        Start-Sleep -Milliseconds 400
        $icon.Dispose()
    } catch { }
    exit 0
}
