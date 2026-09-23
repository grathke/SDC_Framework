# Scripts the seed data a freshly built database needs, out to sql\schema\.
#
# scripts\dump-schema.ps1 creates an empty database. An empty database does not start the
# application: FW_Pages decides what pages exist, FW_RoleSchema/RoleDetails/RoleFields decide what
# anyone may see, and FW_Registration plus FW_Users decide who can log in at all. These files are
# the rows that make the schema usable.
#
# Read-only against the database. It reads rows and writes files.
#
# What it deliberately leaves behind:
#
#   FW_Users            10,014 rows, of which 15 carry a password hash, 15 a plaintext value in
#                       the legacy [Password] column, and 3 a TOTP key. None of it travels. The
#                       bootstrap file creates one fresh administrator instead.
#   Smarty_AuthID       Third-party address-validation credentials on FW_Registration. Excluded
#   Smarty_AuthToken    by name below - they are live keys, not configuration.
#   Smarty_EmbeddedKey
#   Registration 2      SARALAND is a real client. Only registration 1, DEVELOPMENT TEAM, seeds.
#   Business data       Employees, audit trail, sessions, messages, help desk issues, login
#                       attempts, error log, saved QBE, usage counters, generated page requests.
#   Per-user state      FW_PageZooms and the FW_TableLayouts rows that belong to a user. The
#                       user-less FW_TableLayouts rows do travel - those are the QbeDefault
#                       search-field arrangements, which belong to the page, not to a person.
#
#   .\scripts\dump-seed.ps1
#
# Regenerate rather than editing sql\schema\1*.sql by hand.

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root "sql\schema"

. "$PSScriptRoot\db-config.ps1"

$c = Get-SdcDbConfig -Root $Root
$cs = "Server=$($c.Server);User Id=$($c.User);Password=$($c.Password);Initial Catalog=$($c.Database);Encrypt=False;TrustServerCertificate=True;Connect Timeout=15"

# The registration that seeds, and the bootstrap administrator's fixed identity. The hash is
# keyed on the UserId, so the id cannot be chosen later.
$SeedRegistrationId = 1
$AdminUserId = 1
$AdminUserName = 'admin'
$AdminPassword = 'ChangeMe1'

function Get-Rows([string]$Sql) {
	$cn = New-Object System.Data.SqlClient.SqlConnection $cs
	$cn.Open()
	try {
		$cmd = $cn.CreateCommand()
		$cmd.CommandText = $Sql
		$cmd.CommandTimeout = 300
		$da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
		$dt = New-Object System.Data.DataTable
		[void]$da.Fill($dt)
		return ,$dt
	} finally {
		$cn.Close()
	}
}

# Columns worth writing: not computed, not rowversion, not excluded by name.
function Get-WritableColumns([string]$Table, [string[]]$Exclude) {
	$sql = @"
SELECT c.name
FROM sys.columns c
JOIN sys.tables t ON t.object_id = c.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE t.name = '$Table'
  AND c.is_computed = 0
  AND ty.name <> 'timestamp'
ORDER BY c.column_id;
"@
	$all = @(Get-Rows $sql | ForEach-Object { $_.name })
	return @($all | Where-Object { $Exclude -notcontains $_ })
}

function Test-HasIdentity([string]$Table) {
	$sql = "SELECT COUNT(*) AS N FROM sys.identity_columns ic JOIN sys.tables t ON t.object_id = ic.object_id WHERE t.name = '$Table';"
	return ([int](Get-Rows $sql).Rows[0].N) -gt 0
}

# One SQL literal. Anything unrecognised becomes a quoted string rather than being guessed at.
function Format-SqlValue($Value) {
	if ($null -eq $Value -or $Value -is [System.DBNull]) { return 'NULL' }
	if ($Value -is [bool]) { if ($Value) { return '1' } else { return '0' } }
	if ($Value -is [byte[]]) {
		if ($Value.Length -eq 0) { return '0x' }
		return '0x' + [System.BitConverter]::ToString($Value).Replace('-', '')
	}
	if ($Value -is [datetime]) { return "'" + $Value.ToString('yyyy-MM-ddTHH:mm:ss.fff') + "'" }
	if ($Value -is [System.DateTimeOffset]) { return "'" + $Value.ToString('yyyy-MM-ddTHH:mm:ss.fffzzz') + "'" }
	if ($Value -is [guid]) { return "'" + $Value.ToString() + "'" }
	if ($Value -is [int] -or $Value -is [long] -or $Value -is [int16] -or $Value -is [byte] -or
		$Value -is [decimal] -or $Value -is [double] -or $Value -is [single]) {
		return [string]::Format([System.Globalization.CultureInfo]::InvariantCulture, '{0}', $Value)
	}
	return "N'" + ([string]$Value).Replace("'", "''") + "'"
}

# INSERT statements for one table, in batches. 100 rows per statement keeps well inside the
# 1000-row VALUES limit and keeps any one line readable.
function Write-TableInserts([System.Text.StringBuilder]$Sb, [string]$Table, [string]$Where, [string[]]$Exclude) {
	$cols = Get-WritableColumns -Table $Table -Exclude $Exclude
	if ($cols.Count -eq 0) { throw "No writable columns found for $Table." }

	$colList = ($cols | ForEach-Object { "[$_]" }) -join ', '
	$select = ($cols | ForEach-Object { "[$_]" }) -join ', '
	$whereClause = if ($Where) { " WHERE $Where" } else { "" }
	$rows = Get-Rows "SELECT $select FROM dbo.[$Table]$whereClause;"

	[void]$Sb.AppendLine("")
	[void]$Sb.AppendLine("-- $Table  ($($rows.Rows.Count) rows)")
	if ($rows.Rows.Count -eq 0) {
		[void]$Sb.AppendLine("-- nothing to seed")
		return
	}

	$hasIdentity = Test-HasIdentity -Table $Table
	if ($hasIdentity) {
		[void]$Sb.AppendLine("SET IDENTITY_INSERT dbo.[$Table] ON;")
		[void]$Sb.AppendLine("GO")
	}

	$batch = New-Object System.Collections.Generic.List[string]
	$flush = {
		if ($batch.Count -gt 0) {
			[void]$Sb.AppendLine("INSERT INTO dbo.[$Table] ($colList) VALUES")
			[void]$Sb.AppendLine(($batch -join ",`r`n") + ";")
			[void]$Sb.AppendLine("GO")
			$batch.Clear()
		}
	}

	foreach ($row in $rows.Rows) {
		$vals = foreach ($col in $cols) { Format-SqlValue $row[$col] }
		$batch.Add("    (" + ($vals -join ', ') + ")")
		if ($batch.Count -ge 100) { & $flush }
	}
	& $flush

	if ($hasIdentity) {
		[void]$Sb.AppendLine("SET IDENTITY_INSERT dbo.[$Table] OFF;")
		[void]$Sb.AppendLine("GO")
	}
}

$stamp = (Get-Date).ToString('yyyy-MM-dd')
function New-Header([string]$What) {
	return "-- $What`r`n-- Generated from $($c.Database) on $stamp by scripts\dump-seed.ps1`r`n-- Do not edit by hand. Regenerate instead."
}

if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }

Write-Host "Reading $($c.Database) on $($c.Server) ..."

# ---------- 10_reference_data.sql ----------
# Static lookups. FW_Registration has foreign keys into four of these, so they come first.
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine((New-Header "Reference data. Run after 03_indexes.sql and before 11."))
foreach ($t in @('FW_Gender', 'FW_Format_Date', 'FW_Format_Time', 'FW_TimeZones',
		'FW_RegistrationType', 'FW_LicenseTerms', 'FW_HD_IssueCategories')) {
	Write-TableInserts -Sb $sb -Table $t -Where $null -Exclude @()
}
Write-TableInserts -Sb $sb -Table 'FW_RoleTemplate' -Where "RegistrationID IS NULL OR RegistrationID = $SeedRegistrationId" -Exclude @()
Set-Content -Path (Join-Path $Out "10_reference_data.sql") -Value $sb.ToString() -Encoding utf8

# ---------- 11_registration_and_admin.sql ----------
# The registration, then one administrator. Columns pointing at users or roles that do not exist
# yet are left out and set afterwards.
$regExclude = @(
	'Smarty_AuthID', 'Smarty_AuthToken', 'Smarty_EmbeddedKey',
	'CompanyAdminID', 'CompanyAdmin_UserID', 'CompanyAdminRoleID',
	'HDUserSupport', 'HDApplicationSupport'
)
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine((New-Header "Registration and bootstrap administrator. Run after 10."))
[void]$sb.AppendLine("--")
[void]$sb.AppendLine("-- The Smarty_* address-validation keys are deliberately absent. So are the columns")
[void]$sb.AppendLine("-- pointing at a company administrator and a help desk contact, which reference rows")
[void]$sb.AppendLine("-- that do not exist in a new database. CompanyAdmin_UserID is set at the end.")
Write-TableInserts -Sb $sb -Table 'FW_Registration' -Where "RegistrationID = $SeedRegistrationId" -Exclude $regExclude

# The stored hash is HMAC-SHA512 over the password, keyed on the UserId, with the raw bytes read
# back as a UTF-16 string - see DataAccess.ComputePasswordHashForUser. That string cannot be
# written as a SQL literal, so it is emitted as bytes and converted back.
#
# The two round trips are not the same, which is the trap here. An HMAC produces arbitrary bytes,
# and some 16-bit units land in the surrogate range with no pair to match. Encoding.Unicode
# .GetString replaces each of those with U+FFFD; SQL Server's nvarchar keeps them exactly as
# given. Emitting the raw HMAC therefore stores a different string than the application computes
# at sign-in, and the account can never log in. What goes into the file is the post-replacement
# form - the bytes of the string VB.NET actually holds - which is stable on repeat and which SQL
# Server preserves unchanged.
$hmac = New-Object System.Security.Cryptography.HMACSHA512 (, [System.Text.Encoding]::Unicode.GetBytes([string]$AdminUserId))
$rawHashBytes = $hmac.ComputeHash([System.Text.Encoding]::Unicode.GetBytes(($AdminPassword -replace ' ', '')))
$hmac.Dispose()
$storedString = [System.Text.Encoding]::Unicode.GetString($rawHashBytes)
$storedBytes = [System.Text.Encoding]::Unicode.GetBytes($storedString)
$hashHex = '0x' + [System.BitConverter]::ToString($storedBytes).Replace('-', '')

[void]$sb.AppendLine("")
[void]$sb.AppendLine("-- Bootstrap administrator: user name '$AdminUserName', password '$AdminPassword'.")
[void]$sb.AppendLine("-- CHANGE THIS PASSWORD AS THE FIRST THING YOU DO. It is published in a repository.")
[void]$sb.AppendLine("SET IDENTITY_INSERT dbo.[FW_Users] ON;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("INSERT INTO dbo.[FW_Users] ([UserId], [RegistrationID], [UserName], [FirstName], [LastName],")
[void]$sb.AppendLine("       [IsActive], [SuperAdmin], [DeletedFlag], [Password], [PasswordHash], [CreatedBy], [CreatedOn])")
[void]$sb.AppendLine("VALUES ($AdminUserId, $SeedRegistrationId, N'$AdminUserName', N'System', N'Administrator',")
[void]$sb.AppendLine("       1, 1, 0, N'#####', CONVERT(nvarchar(255), $hashHex), $AdminUserId, GETDATE());")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("SET IDENTITY_INSERT dbo.[FW_Users] OFF;")
[void]$sb.AppendLine("GO")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("UPDATE dbo.FW_Registration SET CompanyAdmin_UserID = $AdminUserId WHERE RegistrationID = $SeedRegistrationId;")
[void]$sb.AppendLine("GO")
Set-Content -Path (Join-Path $Out "11_registration_and_admin.sql") -Value $sb.ToString() -Encoding utf8

# ---------- 12_framework_definition.sql ----------
# What pages exist and who may see what. FW_RoleDetails has a foreign key into FW_RoleSchema and
# FW_Roles one into FW_Registration, so the order below is not alphabetical by accident.
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine((New-Header "Pages, roles and permissions. Run after 11."))
Write-TableInserts -Sb $sb -Table 'FW_RoleSchema' -Where $null -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_Roles' -Where "RegistrationID = $SeedRegistrationId" -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_RoleDetails' -Where "RegistrationID = $SeedRegistrationId" -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_RoleFields' -Where "RegistrationID = $SeedRegistrationId" -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_Pages' -Where "RegistrationID IS NULL OR RegistrationID = $SeedRegistrationId" -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_DashboardLayouts' -Where $null -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_UpdateTabOrder' -Where $null -Exclude @()
Write-TableInserts -Sb $sb -Table 'FW_TableLayouts' -Where "UserID IS NULL AND (RegistrationID IS NULL OR RegistrationID = $SeedRegistrationId)" -Exclude @()
Set-Content -Path (Join-Path $Out "12_framework_definition.sql") -Value $sb.ToString() -Encoding utf8

# ---------- 13_zip_codes.sql ----------
# Optional and large. Nothing references it at startup.
$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine((New-Header "US zip codes. Optional - the application starts without them."))
Write-TableInserts -Sb $sb -Table 'FW_ZipCodes' -Where $null -Exclude @()
Set-Content -Path (Join-Path $Out "13_zip_codes.sql") -Value $sb.ToString() -Encoding utf8

foreach ($f in @("10_reference_data.sql", "11_registration_and_admin.sql", "12_framework_definition.sql", "13_zip_codes.sql")) {
	$size = [math]::Round((Get-Item (Join-Path $Out $f)).Length / 1KB, 0)
	Write-Host ("  {0,-34} {1,8} KB" -f $f, $size)
}
Write-Host "Written to sql\schema\"
