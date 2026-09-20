# Marks a fault fixed, which is what takes it off the health page's NEEDS ATTENTION list.
#
#   .\scripts\resolve-fault.ps1 -ErrorLogID 1 -Resolution "HealthGauge now sets SupportsTransparentBackColor"
#   .\scripts\resolve-fault.ps1 -ErrorLogID 1 -Resolution "..." -WhatIf
#
# THE COUNTERPART TO needs-attention.ps1. That one is how a developer reads the list; this is how
# they close a row on it once the code is changed. Between them the loop runs without anybody
# having to file anything: the fault appears, it gets fixed, it disappears.
#
# ResolvedSource IS 'Claude', NOT 'User'. The health page writes 'User' when somebody presses
# Fixed, which means "I have decided this is acceptable". This means "the code was changed", and
# only the second one predicts the fault stops happening. Keeping them apart is what makes a
# recurrence readable - a fault that comes back after a code change is a far worse sign than one
# that comes back after somebody shrugged at it.
#
# ResolvedBy stays NULL, deliberately. It is a FW_Users id and no developer running a script is
# signed in. Writing any id here would be inventing one.
#
# THE RESOLUTION TEXT IS NOT OPTIONAL. A row marked fixed with nothing written down is worse than
# one left open: it is off the list, and when it returns nobody knows what was already tried.
# Telemetry clears Resolved on a repeat while KEEPING this text, which is the whole point of it.

param(
	[Parameter(Mandatory = $true)][int]$ErrorLogID,
	[Parameter(Mandatory = $true)][string]$Resolution,
	[switch]$WhatIf
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\db-config.ps1"

if ([string]::IsNullOrWhiteSpace($Resolution)) {
	Write-Error "-Resolution cannot be empty. Say what was changed."
	exit 2
}

$config = Get-SdcDbConfig

$connectionString = "Server=$($config.Server);Database=$($config.Database);User ID=$($config.User);" +
	"Password=$($config.Password);Encrypt=False;TrustServerCertificate=True;Connect Timeout=10"

Write-Host "Database: $($config.Database) on $($config.Server)"

$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
try {
	$conn.Open()

	# Read it back first, so the text below names what is about to change rather than an id.
	$look = $conn.CreateCommand()
	$look.CommandText = "SELECT ExceptionType, ISNULL(PageName, ''), ISNULL(Resolved, 0), OccurrenceCount " +
		"FROM dbo.FW_ErrorLog WHERE ErrorLogID = @ID"
	$null = $look.Parameters.AddWithValue("@ID", $ErrorLogID)
	$reader = $look.ExecuteReader()

	if (-not $reader.Read()) {
		$reader.Close()
		Write-Error "No fault with ErrorLogID $ErrorLogID."
		exit 2
	}

	$type = $reader.GetString(0)
	$page = $reader.GetString(1)
	$already = $reader.GetBoolean(2)
	$count = $reader.GetInt32(3)
	$reader.Close()

	Write-Host "Fault #${ErrorLogID}: $type  page $page  seen $count time(s)"

	if ($already) {
		Write-Host "Already marked fixed. Nothing to do."
		exit 0
	}

	if ($WhatIf) {
		Write-Host "-WhatIf: would mark it fixed with:"
		Write-Host "  $Resolution"
		exit 0
	}

	# Matches HealthDataAccess.Resolve exactly - resolving also acknowledges, and the original
	# acknowledgement is kept where there was one. Two paths writing the same row must leave it
	# in the same shape.
	$cmd = $conn.CreateCommand()
	$cmd.CommandText = "UPDATE dbo.FW_ErrorLog " +
		"SET Resolved = 1, ResolvedOn = SYSUTCDATETIME(), Resolution = @Resolution, " +
		"    ResolvedSource = 'Claude', " +
		"    Acknowledged = 1, " +
		"    AcknowledgedOn = ISNULL(AcknowledgedOn, SYSUTCDATETIME()) " +
		"WHERE ErrorLogID = @ID AND ISNULL(Resolved, 0) = 0"
	$null = $cmd.Parameters.AddWithValue("@ID", $ErrorLogID)
	$null = $cmd.Parameters.AddWithValue("@Resolution", $Resolution)

	$changed = $cmd.ExecuteNonQuery()

	if ($changed -eq 1) {
		Write-Host "Marked fixed. It is off the list."
		Write-Host "  $Resolution"
	} else {
		Write-Error "Nothing was updated. Something else changed the row first."
		exit 1
	}
} finally {
	$conn.Close()
}
