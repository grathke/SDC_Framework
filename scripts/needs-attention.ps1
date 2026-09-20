# Prints the faults the health page is showing under NEEDS ATTENTION.
#
#   .\scripts\needs-attention.ps1
#   .\scripts\needs-attention.ps1 -Days 30 -Top 20
#   .\scripts\needs-attention.ps1 -NoStack
#
# WHO THIS IS FOR. Not the administrator - they have the page, which is better. This exists so a
# developer asked to "fix what needs attention" reads exactly the rows the page is showing,
# rather than writing a fresh query each time and getting a different list. Two readers, one
# list; the page and this are the same SELECT, filtered the same way.
#
# IT NAMES THE DATABASE, every time, at the top. The application runs on a server and writes to
# whatever database that server points at; this runs here and reads run-local.ps1. While they are
# the same database this is a true report. The day they are not, an empty list would otherwise
# look exactly like nothing being wrong, which is the worst way for a diagnostic to be wrong.
#
# RESOLVED FAULTS ARE ABSENT, matching the page. A fault that comes back clears Resolved in
# Telemetry, which brings it back here carrying whatever fix was recorded last time.

param(
	[int]$Days = 7,
	[int]$Top = 10,
	[switch]$NoStack
)

$ErrorActionPreference = "Stop"

. "$PSScriptRoot\db-config.ps1"

$config = Get-SdcDbConfig

$stackLine = if ($NoStack) {
	"''"
} else {
	"CONCAT('  STACK', CHAR(13), CHAR(10), '    ', REPLACE(ISNULL(e.StackTrace, '(none recorded)'), CHAR(10), CHAR(10) + '    '), CHAR(13), CHAR(10))"
}

$sql = @"
SET NOCOUNT ON;
DECLARE @Cutoff datetime2(0) = DATEADD(day, -$Days, SYSUTCDATETIME());

SELECT CONCAT('Open faults in the last $Days day(s): ',
              (SELECT COUNT(*) FROM dbo.FW_ErrorLog c
               WHERE c.LastSeen >= @Cutoff AND ISNULL(c.DeletedFlag, 0) = 0
                 AND ISNULL(c.Resolved, 0) = 0));

SELECT CONCAT(
    '===============================================================', CHAR(13), CHAR(10),
    '#', e.ErrorLogID, '  ', e.ExceptionType,
    CASE WHEN ISNULL(e.Acknowledged, 0) = 1 THEN '   [acknowledged]' ELSE '' END,
    CASE WHEN ISNULL(e.RecurredAfterResolved, 0) = 1 THEN '   [RECURRED after a fix]' ELSE '' END,
    CHAR(13), CHAR(10),
    '  Page          ', ISNULL(NULLIF(e.PageName, ''), '(none)'), CHAR(13), CHAR(10),
    '  Where caught  ', ISNULL(NULLIF(e.Context, ''), '(none)'), CHAR(13), CHAR(10),
    '  Origin        ', ISNULL(NULLIF(e.Origin, ''), '(none)'), CHAR(13), CHAR(10),
    '  Occurrences   ', e.OccurrenceCount,
    '   first ', CONVERT(varchar(19), e.FirstSeen, 120),
    '   last ', CONVERT(varchar(19), e.LastSeen, 120), CHAR(13), CHAR(10),
    '  Build         ', ISNULL(NULLIF(e.AppVersion, ''), '(none)'),
    '   ', ISNULL(NULLIF(e.SessionKind, ''), ''), CHAR(13), CHAR(10),
    '  Fingerprint   ', ISNULL(e.Fingerprint, ''), CHAR(13), CHAR(10),
    CASE WHEN ISNULL(e.Resolution, '') = '' THEN ''
         ELSE CONCAT('  Last fix tried ', e.Resolution, CHAR(13), CHAR(10)) END,
    '  MESSAGE', CHAR(13), CHAR(10),
    '    ', REPLACE(ISNULL(e.Message, ''), CHAR(10), CHAR(10) + '    '), CHAR(13), CHAR(10),
    $stackLine
) AS Fault
FROM (
    SELECT TOP $Top *
    FROM dbo.FW_ErrorLog
    WHERE LastSeen >= @Cutoff AND ISNULL(DeletedFlag, 0) = 0 AND ISNULL(Resolved, 0) = 0
    ORDER BY ISNULL(Acknowledged, 0), LastSeen DESC
) e
ORDER BY ISNULL(e.Acknowledged, 0), e.LastSeen DESC;
"@

# sqlcmd formats; this needs the text exactly as the query built it. -y 0 keeps long values
# whole but cannot be combined with -h or -W, and the header it then prints is a screenful of
# dashes. Reading it directly avoids the argument altogether.
$connectionString = "Server=$($config.Server);Database=$($config.Database);User ID=$($config.User);" +
	"Password=$($config.Password);Encrypt=False;TrustServerCertificate=True;Connect Timeout=10"

Write-Host "Database: $($config.Database) on $($config.Server)"
Write-Host ""

$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
try {
	$conn.Open()
	$cmd = $conn.CreateCommand()
	$cmd.CommandText = $sql
	$cmd.CommandTimeout = 30
	$reader = $cmd.ExecuteReader()

	do {
		while ($reader.Read()) {
			Write-Output $reader.GetString(0)
		}
	} while ($reader.NextResult())

	$reader.Close()
} finally {
	$conn.Close()
}
