# Sends one health alert, to prove the mail settings work.
#
#   .\scripts\test-mail.ps1
#   .\scripts\test-mail.ps1 -To someone@example.com
#
# WHY THIS EXISTS. HealthMail only fires when a fault appears, so the first time it runs for real
# is the moment something has broken - which is the worst possible moment to find out the password
# is wrong, the port is blocked, or the account will not relay. This exercises everything except
# the trigger: the same environment variables, the same host and port, the same STARTTLS, the same
# From address.
#
# It does NOT touch FW_ErrorLog and does not pretend a fault happened. It sends one message that
# says plainly what it is.
#
# WHO IT GOES TO. By default, the same people the real alert would: App Admins with Receives
# Health Alerts ticked and an email address. -To overrides that for a one-off check.

param(
	[string]$To
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\db-config.ps1"

function Setting {
	param([string]$Name)
	$value = [Environment]::GetEnvironmentVariable($Name)
	if ($null -eq $value) { return "" }
	return $value.Trim()
}

$mailHost = Setting "SDC_MAIL_HOST"
$user     = Setting "SDC_MAIL_USER"
$password = Setting "SDC_MAIL_PASSWORD"
$port     = Setting "SDC_MAIL_PORT"
if (-not $port) { $port = "587" }

# run-local.ps1 sets these for the application. Run this in the same shell, or it sees nothing.
if (-not $mailHost -or -not $user -or -not $password) {
	Write-Host "No mail settings in this shell."
	Write-Host ""
	Write-Host "  SDC_MAIL_HOST      $(if ($mailHost) { $mailHost } else { '(not set)' })"
	Write-Host "  SDC_MAIL_PORT      $port"
	Write-Host "  SDC_MAIL_USER      $(if ($user) { $user } else { '(not set)' })"
	Write-Host "  SDC_MAIL_PASSWORD  $(if ($password) { 'set' } else { '(not set)' })"
	Write-Host ""
	Write-Host "They are set by run-local.ps1. Dot-source it first:  . .\run-local.ps1"
	exit 2
}

# The real recipient list, so this proves the query as well as the sending.
$recipients = @()

if ($To) {
	$recipients = @($To)
} else {
	$config = Get-SdcDbConfig
	$cs = "Server=$($config.Server);Database=$($config.Database);User ID=$($config.User);" +
		"Password=$($config.Password);Encrypt=False;TrustServerCertificate=True;Connect Timeout=10"

	$conn = New-Object System.Data.SqlClient.SqlConnection $cs
	try {
		$conn.Open()
		$cmd = $conn.CreateCommand()
		$cmd.CommandText = @"
SELECT DISTINCT LTRIM(RTRIM(e.Email)) AS Email
FROM dbo.FW_Employees e
JOIN dbo.FW_EmployeeRoles er ON er.EmployeeID = e.EmployeeID AND ISNULL(er.DeletedFlag, 0) = 0
JOIN dbo.FW_Roles r ON r.ID = er.RoleID
WHERE ISNULL(e.ReceivesHealthAlerts, 0) = 1
  AND ISNULL(e.DeletedFlag, 0) = 0
  AND NULLIF(LTRIM(RTRIM(e.Email)), '') IS NOT NULL
  AND ISNULL(r.Typ_AppAdmin, 0) = 1
"@
		$reader = $cmd.ExecuteReader()
		while ($reader.Read()) { $recipients += $reader["Email"] }
		$reader.Close()
	} finally {
		$conn.Close()
	}
}

if ($recipients.Count -eq 0) {
	Write-Host "Nobody would receive a health alert."
	Write-Host "Tick Receives Health Alerts on an App Admin's employee record, and give them an email address."
	exit 1
}

Write-Host "Host:       $mailHost`:$port"
Write-Host "From:       $user"
Write-Host "Recipients: $($recipients -join ', ')"
Write-Host ""

$client = New-Object System.Net.Mail.SmtpClient($mailHost, [int]$port)
try {
	# STARTTLS on 587. Not 465, whose implicit SSL SmtpClient has never properly supported.
	$client.EnableSsl = $true
	$client.Credentials = New-Object System.Net.NetworkCredential($user, $password)
	$client.Timeout = 20000

	$message = New-Object System.Net.Mail.MailMessage
	$message.From = New-Object System.Net.Mail.MailAddress($user, "SDC Framework")
	$message.Subject = "SDC health: test message"
	$message.IsBodyHtml = $false
	$message.Body = @"
This is a test of the health alert path, sent by scripts\test-mail.ps1.

Nothing is wrong. No fault was recorded and nothing was written to the database.

A real alert arrives only when a fault appears in Needs Attention for the first time,
or when one that was marked fixed happens again. It names the machine, the page, where
it was caught and the message.

You are receiving this because Receives Health Alerts is ticked on your employee record.
"@

	foreach ($address in $recipients) { $message.To.Add($address) }

	$client.Send($message)
	Write-Host "Sent."
	exit 0

} catch {
	Write-Host "FAILED: $($_.Exception.Message)"
	if ($_.Exception.InnerException) {
		Write-Host "        $($_.Exception.InnerException.Message)"
	}
	exit 1
} finally {
	$client.Dispose()
}
