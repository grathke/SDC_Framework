# Who has been signed in, for how long, and how each session ended.
#
#   .\scripts\sessions.ps1
#   .\scripts\sessions.ps1 -Days 30 -Top 50
#   .\scripts\sessions.ps1 -OpenOnly
#
# WHY A SCRIPT AND NOT A PAGE. FW_Session was built on 2026-09-20 with nothing to display it, and
# where to view it was left open on purpose. This is the answer: the question "who was on, and for
# how long" is asked occasionally by one person, and a browse page with a permission row, a role
# entry and a layout is a great deal of machinery for that. If it turns out to be asked daily, or
# asked by somebody without a terminal, it earns its page then.
#
# IT NAMES THE DATABASE, every time. The application writes to whatever database its server points
# at; this reads run-local.ps1. While they are the same this is a true report, and the day they are
# not, an empty list would otherwise look exactly like nobody having signed in.
#
# A CRASH SESSION'S CONNECTED TIME IS UNKNOWN, and is printed that way rather than as a number.
# A killed process never writes an end, so the sweep falls back to the last activity, or to the
# start where there is none - which understates rather than invents, but prints as "0s" for a
# session that really ran five minutes. Nothing short of a heartbeat can do better, and sql/151
# rejected heartbeats on purpose: every user, every interval, for ever, whether or not anything
# changed.
#
# ACTIVE IS OFTEN BLANK, and that is not a fault. It is stamped when something writes a usage
# counter - a search, a page load - so somebody who signed in and read one screen for an hour
# registers as having done nothing. Connected says the server was carrying them; Active says they
# were demonstrably working. Neither is wrong, and an average of the two would describe nothing.

param(
	[int]$Days = 7,
	[int]$Top = 40,
	[switch]$OpenOnly,
	[string]$User,
	[switch]$Summary
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\db-config.ps1"

$config = Get-SdcDbConfig

$openFilter = if ($OpenOnly) { "AND s.EndedOn IS NULL" } else { "" }

# One @User against three columns rather than a name built into the text. Somebody looking for
# "glenn" should not have to know whether that is the sign-in name or the first name.
$userFilter = if ($User) {
	"AND (u.UserName LIKE @User OR u.FirstName LIKE @User OR u.LastName LIKE @User " +
	"     OR LTRIM(RTRIM(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, ''))) LIKE @User)"
} else { "" }

$sql = @"
SET NOCOUNT ON;
SELECT TOP $Top
    s.SessionID,
    ISNULL(NULLIF(LTRIM(RTRIM(ISNULL(u.FirstName, '') + ' ' + ISNULL(u.LastName, ''))), ''),
           ISNULL(u.UserName, CONCAT('user ', s.UserID))) AS WhoName,
    s.StartedOn,
    s.EndedOn,
    ISNULL(s.EndReason, '') AS EndReason,
    s.LastActivityOn,
    ISNULL(s.SessionKind, '') AS SessionKind,
    DATEDIFF(second, s.StartedOn, ISNULL(s.EndedOn, SYSUTCDATETIME())) AS ConnectedSec,
    DATEDIFF(second, s.StartedOn, s.LastActivityOn) AS ActiveSec
FROM dbo.FW_Session s
LEFT JOIN dbo.FW_Users u ON u.UserId = s.UserID
WHERE s.StartedOn >= DATEADD(day, -$Days, SYSUTCDATETIME())
  AND ISNULL(s.DeletedFlag, 0) = 0
  $openFilter
  $userFilter
ORDER BY s.StartedOn DESC;
"@

$connectionString = "Server=$($config.Server);Database=$($config.Database);User ID=$($config.User);" +
	"Password=$($config.Password);Encrypt=False;TrustServerCertificate=True;Connect Timeout=10"

Write-Host "Database: $($config.Database) on $($config.Server)"
Write-Host ""

function Format-Duration {
	param([object]$Seconds)

	if ($null -eq $Seconds -or $Seconds -is [DBNull]) { return "" }

	$s = [int]$Seconds
	if ($s -lt 0) { return "" }
	if ($s -lt 60) { return "$($s)s" }
	if ($s -lt 3600) { return "$([int][Math]::Floor($s / 60))m $($s % 60)s" }

	$h = [int][Math]::Floor($s / 3600)
	$m = [int][Math]::Floor(($s % 3600) / 60)
	return "$($h)h $($m)m"
}

$rows = @()
$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
try {
	$conn.Open()
	$cmd = $conn.CreateCommand()
	$cmd.CommandText = $sql
	$cmd.CommandTimeout = 30

	if ($User) {
		# Wrapped here rather than in the argument, so -User glenn finds Glenn without anybody
		# having to remember the per cent signs.
		$null = $cmd.Parameters.AddWithValue("@User", "%$User%")
	}

	$reader = $cmd.ExecuteReader()

	while ($reader.Read()) {
		$ended = $reader["EndedOn"]
		$reason = [string]$reader["EndReason"]

		# A crash end is a fallback, not a measurement. Saying so is the whole reason this script
		# formats the rows rather than printing the table.
		$connected = if ($ended -is [DBNull]) {
			"open"
		} elseif ($reason -eq "Crash") {
			"unknown"
		} else {
			Format-Duration $reader["ConnectedSec"]
		}

		$rows += [PSCustomObject]@{
			ID        = $reader["SessionID"]
			Who       = $reader["WhoName"]
			Started   = ([datetime]$reader["StartedOn"]).ToLocalTime().ToString("d MMM HH:mm")
			Connected = $connected
			Active    = Format-Duration $reader["ActiveSec"]
			Ended     = if ($reason -eq "") { "still open" } else { $reason }
			Where     = $reader["SessionKind"]

			# Kept for the summary, which needs the number rather than the reading of it.
			RawSeconds = if ($ended -is [DBNull] -or $reason -eq "Crash") { $null } else { [int]$reader["ConnectedSec"] }
			RawReason  = $reason
		}
	}

	$reader.Close()
} finally {
	$conn.Close()
}

if ($rows.Count -eq 0) {
	Write-Host "No sessions in the last $Days day(s)."
	exit 0
}

if ($Summary) {
	# Crash sessions are counted and excluded from the arithmetic. Their length is unknown, and
	# feeding an understated figure into a total would quietly drag every average down.
	$rows | Group-Object Who | ForEach-Object {
		$measured = @($_.Group | Where-Object { $null -ne $_.RawSeconds })
		$unknown = $_.Count - $measured.Count

		[PSCustomObject]@{
			Who      = $_.Name
			Sessions = $_.Count
			Measured = $measured.Count
			Total    = Format-Duration (($measured | Measure-Object RawSeconds -Sum).Sum)
			Average  = if ($measured.Count -gt 0) { Format-Duration ([int](($measured | Measure-Object RawSeconds -Average).Average)) } else { "" }
			Longest  = if ($measured.Count -gt 0) { Format-Duration (($measured | Measure-Object RawSeconds -Maximum).Maximum) } else { "" }
			Unknown  = $unknown
		}
	} | Sort-Object Who | Format-Table -AutoSize

	Write-Host "Total, average and longest cover the sessions whose length is known."
	Write-Host "Unknown counts the ones that ended without writing an end."
	exit 0
}

# Named explicitly, so the two raw fields the summary needs do not appear in the reading.
$rows | Select-Object ID, Who, Started, Connected, Active, Ended, Where | Format-Table -AutoSize

$crashed = @($rows | Where-Object { $_.Ended -eq "Crash" }).Count
if ($crashed -gt 0) {
	Write-Host ""
	Write-Host "$crashed session(s) ended without writing an end - the process was killed, or the"
	Write-Host "machine went down. Their connected time is unknown, not zero."
}
