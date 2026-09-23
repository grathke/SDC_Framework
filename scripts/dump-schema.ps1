# Scripts the whole database schema out to sql\schema\ as a from-scratch build.
#
# The repository had 160 incremental migrations and no way to create the database they assume.
# A new developer could clone the code and had nowhere to point SDC_DB_SERVER. These four files
# are that missing starting point.
#
# Read-only. It reads system catalogs and writes files; it changes nothing in the database.
#
# Credentials come from run-local.ps1 through db-config.ps1, the same gitignored file the
# application uses, so no password is typed on a command line or written into the repository.
#
#   .\scripts\dump-schema.ps1
#
# Regenerate it rather than editing sql\schema\ by hand.

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root "sql\schema"

. "$PSScriptRoot\db-config.ps1"

$c = Get-SdcDbConfig -Root $Root
$cs = "Server=$($c.Server);User Id=$($c.User);Password=$($c.Password);Initial Catalog=$($c.Database);Encrypt=False;TrustServerCertificate=True;Connect Timeout=15"

function Get-Rows([string]$Sql) {
	$cn = New-Object System.Data.SqlClient.SqlConnection $cs
	$cn.Open()
	try {
		$cmd = $cn.CreateCommand()
		$cmd.CommandText = $Sql
		$cmd.CommandTimeout = 180
		$da = New-Object System.Data.SqlClient.SqlDataAdapter $cmd
		$dt = New-Object System.Data.DataTable
		[void]$da.Fill($dt)
		return ,$dt
	} finally {
		$cn.Close()
	}
}

if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Path $Out -Force | Out-Null }

$colSql = @"
SELECT  t.name AS TableName,
        c.column_id AS ColumnId,
        Definition =
          QUOTENAME(c.name) + ' ' +
          CASE
            WHEN cc.definition IS NOT NULL
              THEN 'AS ' + cc.definition + CASE WHEN cc.is_persisted = 1 THEN ' PERSISTED' ELSE '' END
            ELSE
              CASE WHEN ty.name = 'timestamp' THEN 'rowversion' ELSE ty.name END +
              CASE
                WHEN ty.name IN ('varchar','char','varbinary','binary')
                  THEN '(' + IIF(c.max_length = -1, 'MAX', CONVERT(varchar(10), c.max_length)) + ')'
                WHEN ty.name IN ('nvarchar','nchar')
                  THEN '(' + IIF(c.max_length = -1, 'MAX', CONVERT(varchar(10), c.max_length / 2)) + ')'
                WHEN ty.name IN ('decimal','numeric')
                  THEN '(' + CONVERT(varchar(10), c.precision) + ',' + CONVERT(varchar(10), c.scale) + ')'
                WHEN ty.name IN ('datetime2','time','datetimeoffset') AND c.scale <> 7
                  THEN '(' + CONVERT(varchar(10), c.scale) + ')'
                ELSE ''
              END +
              CASE WHEN c.is_identity = 1
                   THEN ' IDENTITY(' + CONVERT(varchar(20), CONVERT(bigint, ic.seed_value)) + ',' +
                                       CONVERT(varchar(20), CONVERT(bigint, ic.increment_value)) + ')'
                   ELSE '' END +
              CASE WHEN ty.name = 'timestamp' THEN ''
                   WHEN c.is_nullable = 1 THEN ' NULL'
                   ELSE ' NOT NULL' END
          END +
          -- A default belongs on its column here. 'DEFAULT (x) FOR [col]' is ALTER TABLE syntax
          -- and is a parse error inside CREATE TABLE.
          ISNULL(' CONSTRAINT ' + QUOTENAME(dc.name) + ' DEFAULT ' + dc.definition, '')
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.computed_columns cc ON cc.object_id = c.object_id AND cc.column_id = c.column_id
LEFT JOIN sys.identity_columns ic ON ic.object_id = c.object_id AND ic.column_id = c.column_id
LEFT JOIN sys.default_constraints dc ON dc.parent_object_id = c.object_id AND dc.parent_column_id = c.column_id
WHERE t.is_ms_shipped = 0
ORDER BY t.name, c.column_id;
"@

$keySql = @"
SELECT  t.name AS TableName,
        Definition =
          'CONSTRAINT ' + QUOTENAME(kc.name) + ' ' +
          CASE kc.type WHEN 'PK' THEN 'PRIMARY KEY' ELSE 'UNIQUE' END +
          CASE WHEN i.type = 1 THEN ' CLUSTERED' ELSE ' NONCLUSTERED' END + ' (' +
          STUFF((SELECT ', ' + QUOTENAME(col.name) + CASE WHEN ixc.is_descending_key = 1 THEN ' DESC' ELSE '' END
                 FROM sys.index_columns ixc
                 JOIN sys.columns col ON col.object_id = ixc.object_id AND col.column_id = ixc.column_id
                 WHERE ixc.object_id = i.object_id AND ixc.index_id = i.index_id AND ixc.is_included_column = 0
                 ORDER BY ixc.key_ordinal
                 FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')'
FROM sys.key_constraints kc
JOIN sys.tables t ON t.object_id = kc.parent_object_id
JOIN sys.indexes i ON i.object_id = kc.parent_object_id AND i.index_id = kc.unique_index_id
WHERE t.is_ms_shipped = 0
ORDER BY t.name, kc.type DESC;
"@

$chkSql = @"
SELECT t.name AS TableName,
       Definition = 'CONSTRAINT ' + QUOTENAME(cc.name) + ' CHECK ' + cc.definition
FROM sys.check_constraints cc
JOIN sys.tables t ON t.object_id = cc.parent_object_id
WHERE t.is_ms_shipped = 0
ORDER BY t.name, cc.name;
"@

$fkSql = @"
SELECT  ParentTable = pt.name,
        Statement =
          'ALTER TABLE ' + QUOTENAME(SCHEMA_NAME(pt.schema_id)) + '.' + QUOTENAME(pt.name) +
          ' ADD CONSTRAINT ' + QUOTENAME(fk.name) + ' FOREIGN KEY (' +
          STUFF((SELECT ', ' + QUOTENAME(pc.name)
                 FROM sys.foreign_key_columns fkc
                 JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
                 WHERE fkc.constraint_object_id = fk.object_id
                 ORDER BY fkc.constraint_column_id
                 FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') +
          ') REFERENCES ' + QUOTENAME(SCHEMA_NAME(rt.schema_id)) + '.' + QUOTENAME(rt.name) + ' (' +
          STUFF((SELECT ', ' + QUOTENAME(rc.name)
                 FROM sys.foreign_key_columns fkc
                 JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
                 WHERE fkc.constraint_object_id = fk.object_id
                 ORDER BY fkc.constraint_column_id
                 FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')' +
          CASE fk.delete_referential_action
               WHEN 1 THEN ' ON DELETE CASCADE'
               WHEN 2 THEN ' ON DELETE SET NULL'
               WHEN 3 THEN ' ON DELETE SET DEFAULT'
               ELSE '' END +
          CASE fk.update_referential_action
               WHEN 1 THEN ' ON UPDATE CASCADE'
               WHEN 2 THEN ' ON UPDATE SET NULL'
               WHEN 3 THEN ' ON UPDATE SET DEFAULT'
               ELSE '' END + ';'
FROM sys.foreign_keys fk
JOIN sys.tables pt ON pt.object_id = fk.parent_object_id
JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
ORDER BY pt.name, fk.name;
"@

$ixSql = @"
SELECT  TableName = t.name,
        Statement =
          'CREATE ' + CASE WHEN i.is_unique = 1 THEN 'UNIQUE ' ELSE '' END +
          CASE WHEN i.type = 1 THEN 'CLUSTERED ' ELSE 'NONCLUSTERED ' END +
          'INDEX ' + QUOTENAME(i.name) + ' ON ' + QUOTENAME(SCHEMA_NAME(t.schema_id)) + '.' + QUOTENAME(t.name) + ' (' +
          STUFF((SELECT ', ' + QUOTENAME(col.name) + CASE WHEN ixc.is_descending_key = 1 THEN ' DESC' ELSE '' END
                 FROM sys.index_columns ixc
                 JOIN sys.columns col ON col.object_id = ixc.object_id AND col.column_id = ixc.column_id
                 WHERE ixc.object_id = i.object_id AND ixc.index_id = i.index_id AND ixc.is_included_column = 0
                 ORDER BY ixc.key_ordinal
                 FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')' +
          ISNULL(' INCLUDE (' +
            STUFF((SELECT ', ' + QUOTENAME(col.name)
                   FROM sys.index_columns ixc
                   JOIN sys.columns col ON col.object_id = ixc.object_id AND col.column_id = ixc.column_id
                   WHERE ixc.object_id = i.object_id AND ixc.index_id = i.index_id AND ixc.is_included_column = 1
                   ORDER BY ixc.index_column_id
                   FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, '') + ')', '') +
          ISNULL(' WHERE ' + i.filter_definition, '') + ';'
FROM sys.indexes i
JOIN sys.tables t ON t.object_id = i.object_id
WHERE t.is_ms_shipped = 0
  AND i.type IN (1, 2)
  AND i.is_primary_key = 0
  AND i.is_unique_constraint = 0
  AND i.name IS NOT NULL
ORDER BY t.name, i.name;
"@

$modSql = @"
SELECT  ObjectId = o.object_id,
        ObjType = o.type_desc,
        ObjName = o.name,
        Definition = m.definition
FROM sys.sql_modules m
JOIN sys.objects o ON o.object_id = m.object_id
WHERE o.is_ms_shipped = 0
ORDER BY o.name;
"@

# Which module depends on which. Neither CREATE VIEW nor CREATE FUNCTION tolerates a forward
# reference - only stored procedures get deferred name resolution - so grouping by type and
# sorting by name is not enough. fn_GenerateRandomCode reads vw_RandomGuid, and FW_UserPeople
# reads vw_FW_CurrentUser; both came out ahead of what they need.
$depSql = @"
SELECT DISTINCT Referencing = d.referencing_id, Referenced = d.referenced_id
FROM sys.sql_expression_dependencies d
JOIN sys.objects o ON o.object_id = d.referencing_id AND o.is_ms_shipped = 0
JOIN sys.sql_modules mr ON mr.object_id = d.referencing_id
JOIN sys.sql_modules md ON md.object_id = d.referenced_id
WHERE d.referenced_id IS NOT NULL
  AND d.referencing_id <> d.referenced_id;
"@

Write-Host "Reading $($c.Database) on $($c.Server) ..."

$cols = Get-Rows $colSql
$keys = Get-Rows $keySql
$chks = Get-Rows $chkSql
$fks = Get-Rows $fkSql
$ixs = Get-Rows $ixSql
$mods = Get-Rows $modSql
$deps = Get-Rows $depSql

# Kahn's algorithm: emit a module only once everything it references has been emitted.
# Alphabetical tiebreak keeps the output identical from one run to the next.
$byId = @{}
foreach ($r in $mods) { $byId[[int]$r.ObjectId] = $r }
$needs = @{}
foreach ($id in $byId.Keys) { $needs[$id] = New-Object System.Collections.Generic.HashSet[int] }
foreach ($d in $deps) {
	$from = [int]$d.Referencing
	$to = [int]$d.Referenced
	if ($byId.ContainsKey($from) -and $byId.ContainsKey($to)) { [void]$needs[$from].Add($to) }
}

$ordered = New-Object System.Collections.Generic.List[object]
$emitted = New-Object System.Collections.Generic.HashSet[int]
while ($emitted.Count -lt $byId.Count) {
	$ready = @($byId.Keys | Where-Object { -not $emitted.Contains($_) } |
		Where-Object { @($needs[$_] | Where-Object { -not $emitted.Contains($_) }).Count -eq 0 } |
		Sort-Object { $byId[$_].ObjName })
	if ($ready.Count -eq 0) {
		# A genuine cycle, or a dependency we cannot order. Emit the rest alphabetically and say so.
		$rest = @($byId.Keys | Where-Object { -not $emitted.Contains($_) } | Sort-Object { $byId[$_].ObjName })
		Write-Warning "Circular or unresolvable dependency among: $(($rest | ForEach-Object { $byId[$_].ObjName }) -join ', ')"
		foreach ($id in $rest) { $ordered.Add($byId[$id]); [void]$emitted.Add($id) }
		break
	}
	foreach ($id in $ready) { $ordered.Add($byId[$id]); [void]$emitted.Add($id) }
}

$stamp = (Get-Date).ToString('yyyy-MM-dd')
$hdr = "-- Generated from $($c.Database) on $stamp by scripts\dump-schema.ps1`r`n-- Do not edit by hand. Regenerate instead."

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine($hdr)
$tables = $cols | Select-Object -ExpandProperty TableName -Unique | Sort-Object
foreach ($tn in $tables) {
	[void]$sb.AppendLine("")
	[void]$sb.AppendLine("IF OBJECT_ID('dbo.[$tn]', 'U') IS NULL")
	[void]$sb.AppendLine("CREATE TABLE dbo.[$tn] (")
	$lines = @()
	foreach ($r in ($cols | Where-Object { $_.TableName -eq $tn } | Sort-Object ColumnId)) { $lines += "    " + $r.Definition }
	foreach ($r in ($keys | Where-Object { $_.TableName -eq $tn })) { $lines += "    " + $r.Definition }
	foreach ($r in ($chks | Where-Object { $_.TableName -eq $tn })) { $lines += "    " + $r.Definition }
	[void]$sb.AppendLine(($lines -join ",`r`n"))
	[void]$sb.AppendLine(");")
	[void]$sb.AppendLine("GO")
}
Set-Content -Path (Join-Path $Out "01_tables.sql") -Value $sb.ToString() -Encoding utf8

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine($hdr)
[void]$sb.AppendLine("")
foreach ($r in $fks) { [void]$sb.AppendLine($r.Statement) }
[void]$sb.AppendLine("GO")
Set-Content -Path (Join-Path $Out "02_foreign_keys.sql") -Value $sb.ToString() -Encoding utf8

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine($hdr)
[void]$sb.AppendLine("")
foreach ($r in $ixs) { [void]$sb.AppendLine($r.Statement) }
[void]$sb.AppendLine("GO")
Set-Content -Path (Join-Path $Out "03_indexes.sql") -Value $sb.ToString() -Encoding utf8

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine($hdr)
foreach ($r in $ordered) {
	[void]$sb.AppendLine("")
	[void]$sb.AppendLine("-- $($r.ObjType): $($r.ObjName)")
	[void]$sb.AppendLine($r.Definition.TrimEnd())
	[void]$sb.AppendLine("GO")
}
Set-Content -Path (Join-Path $Out "04_programmability.sql") -Value $sb.ToString() -Encoding utf8

Write-Host "tables=$($tables.Count)  foreign keys=$($fks.Rows.Count)  indexes=$($ixs.Rows.Count)  views/procs/functions=$($mods.Rows.Count)"
Write-Host "Written to sql\schema\"
