# Where the scripts get their database credentials.
#
# Dot-source it:  . "$PSScriptRoot\db-config.ps1"
#
# run-sql.ps1 read run-local.ps1 directly, which was right while it was the only script that
# needed to. It is now the third, and three copies of a regex that finds a password is three
# places to get it wrong. Nothing here is new behaviour - it is the same read, in one place.
#
# run-local.ps1 is gitignored and is the same file the application itself uses, so no password is
# typed on a command line, echoed into terminal history, or written into the repository.

function Get-SdcDbConfig {
	param(
		[string]$Root = (Split-Path -Parent $PSScriptRoot)
	)

	$configPath = Join-Path $Root "run-local.ps1"

	if (-not (Test-Path $configPath)) {
		throw "run-local.ps1 not found. Copy run-local.ps1.example and fill in your values."
	}

	$config = Get-Content $configPath -Raw

	$result = @{
		Server   = [regex]::Match($config, '\$Server\s*=\s*"([^"]+)"').Groups[1].Value
		User     = [regex]::Match($config, '\$User\s*=\s*"([^"]+)"').Groups[1].Value
		Password = [regex]::Match($config, '\$Password\s*=\s*"([^"]+)"').Groups[1].Value
		Database = [regex]::Match($config, '\$Database\s*=\s*"([^"]+)"').Groups[1].Value
	}

	if (-not $result.Server -or -not $result.User -or -not $result.Password -or -not $result.Database) {
		throw "Could not read Server, User, Password and Database from run-local.ps1."
	}

	return $result
}
