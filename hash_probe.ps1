Add-Type -AssemblyName System.Data

$server = if ($env:SDC_DB_SERVER) { $env:SDC_DB_SERVER } else { 'BEELINK' }
$user = if ($env:SDC_DB_USER) { $env:SDC_DB_USER } else { 'sa' }
if (-not $env:SDC_DB_PASSWORD) {
    throw "SDC_DB_PASSWORD is not set. Run run-local.ps1 or set the variable before running this probe."
}
$password = $env:SDC_DB_PASSWORD
$database = if ($env:SDC_DB_NAME) { $env:SDC_DB_NAME } else { 'WX_Framework' }
$encrypt = if ($env:SDC_DB_ENCRYPT) { $env:SDC_DB_ENCRYPT } else { 'False' }
$trustServerCertificate = if ($env:SDC_DB_TRUST_SERVER_CERT) { $env:SDC_DB_TRUST_SERVER_CERT } else { 'True' }

$cs = "Server=$server;User Id=$user;Password=$password;Encrypt=$encrypt;TrustServerCertificate=$trustServerCertificate;Initial Catalog=$database;"
$conn = New-Object System.Data.SqlClient.SqlConnection $cs
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT TOP 1 UserId, Email, PasswordHash FROM dbo.Users WHERE UserId = 2"
$reader = $cmd.ExecuteReader()
[void]$reader.Read()
$uid = [int]$reader['UserId']
$email = [string]$reader['Email']
$stored = [string]$reader['PasswordHash']
$reader.Close()
$conn.Close()

$emailNoSpace = $email.Replace(' ', '')

$encodings = @(
    [System.Text.Encoding]::Unicode,
    [System.Text.Encoding]::UTF8,
    [System.Text.Encoding]::ASCII,
    [System.Text.Encoding]::BigEndianUnicode,
    [System.Text.Encoding]::UTF32
)

$keyVariants = @{}
$keyVariants['uid-string-unicode'] = [System.Text.Encoding]::Unicode.GetBytes($uid.ToString())
$keyVariants['uid-string-utf8'] = [System.Text.Encoding]::UTF8.GetBytes($uid.ToString())
$keyVariants['uid-string-ascii'] = [System.Text.Encoding]::ASCII.GetBytes($uid.ToString())
$keyVariants['uid-int32-le'] = [System.BitConverter]::GetBytes([int]$uid)
$keyInt32Be = [System.BitConverter]::GetBytes([int]$uid)
[array]::Reverse($keyInt32Be)
$keyVariants['uid-int32-be'] = $keyInt32Be
$keyVariants['uid-int64-le'] = [System.BitConverter]::GetBytes([long]$uid)
$keyInt64Be = [System.BitConverter]::GetBytes([long]$uid)
[array]::Reverse($keyInt64Be)
$keyVariants['uid-int64-be'] = $keyInt64Be

Write-Output "Stored hash length: $($stored.Length)"
Write-Output "Email used: $emailNoSpace"

$matches = @()

$messageVariants = @{}
$messageVariants['email-nospace'] = $emailNoSpace
$messageVariants['email-lower'] = $emailNoSpace.ToLowerInvariant()
$messageVariants['email-upper'] = $emailNoSpace.ToUpperInvariant()
$messageVariants['userid-text'] = $uid.ToString()
$messageVariants['userid-text-pad3'] = $uid.ToString('000')
$messageVariants['userid-bin-int32-le-as-unicode'] = [System.Text.Encoding]::Unicode.GetString([System.BitConverter]::GetBytes([int]$uid))

$keyVariants['email-unicode'] = [System.Text.Encoding]::Unicode.GetBytes($emailNoSpace)
$keyVariants['email-utf8'] = [System.Text.Encoding]::UTF8.GetBytes($emailNoSpace)
$keyVariants['email-ascii'] = [System.Text.Encoding]::ASCII.GetBytes($emailNoSpace)

foreach ($keyName in $keyVariants.Keys) {
    foreach ($msgName in $messageVariants.Keys) {
        foreach ($enc in $encodings) {
            $keyBytes = $keyVariants[$keyName]
            $msgBytes = $enc.GetBytes($messageVariants[$msgName])
            $hmac = [System.Security.Cryptography.HMACSHA512]::new($keyBytes)
            try {
                $hashBytes = $hmac.ComputeHash($msgBytes)
                $asUnicode = [System.Text.Encoding]::Unicode.GetString($hashBytes)
                $asUnicodeTrimNull = $asUnicode.Trim([char]0)
                $asUtf8 = [System.Text.Encoding]::UTF8.GetString($hashBytes)
                $asHexLower = ([System.BitConverter]::ToString($hashBytes)).Replace('-', '').ToLowerInvariant()

                if ($asUnicode -ceq $stored) {
                    $matches += "MATCH exact unicode key=$keyName msg=$msgName msgEnc=$($enc.WebName)"
                }
                if ($asUnicodeTrimNull -ceq $stored) {
                    $matches += "MATCH trimnull unicode key=$keyName msg=$msgName msgEnc=$($enc.WebName)"
                }
                if ($asUtf8 -ceq $stored) {
                    $matches += "MATCH utf8 key=$keyName msg=$msgName msgEnc=$($enc.WebName)"
                }
                if ($asHexLower -ceq $stored.ToLowerInvariant()) {
                    $matches += "MATCH hex key=$keyName msg=$msgName msgEnc=$($enc.WebName)"
                }
            }
            finally {
                $hmac.Dispose()
            }
        }
    }
}

# Reversed perspective: message from key variant bytes interpreted as text, key from message encoding bytes
foreach ($msgName in $messageVariants.Keys) {
    foreach ($enc in $encodings) {
        $msgKeyBytes = $enc.GetBytes($messageVariants[$msgName])
        foreach ($keyName in $keyVariants.Keys) {
            $msgTextFromKeyBytes = [System.Text.Encoding]::Unicode.GetString($keyVariants[$keyName])
            $msgBytes = [System.Text.Encoding]::Unicode.GetBytes($msgTextFromKeyBytes)
            $hmac = [System.Security.Cryptography.HMACSHA512]::new($msgKeyBytes)
            try {
                $hashBytes = $hmac.ComputeHash($msgBytes)
                $asUnicode = [System.Text.Encoding]::Unicode.GetString($hashBytes)
                if ($asUnicode -ceq $stored) {
                    $matches += "MATCH reversed keyFrom=$msgName/$($enc.WebName) msgFrom=$keyName"
                }
            }
            finally {
                $hmac.Dispose()
            }
        }
    }
}

# Non-HMAC SHA512 candidates with concatenation order
foreach ($enc in $encodings) {
    $sha = [System.Security.Cryptography.SHA512]::Create()
    try {
        $pair1 = $emailNoSpace + $uid.ToString()
        $pair2 = $uid.ToString() + $emailNoSpace
        $b1 = $sha.ComputeHash($enc.GetBytes($pair1))
        $b2 = $sha.ComputeHash($enc.GetBytes($pair2))
        $u1 = [System.Text.Encoding]::Unicode.GetString($b1)
        $u2 = [System.Text.Encoding]::Unicode.GetString($b2)
        if ($u1 -ceq $stored) { $matches += "MATCH sha512 concat email+uid enc=$($enc.WebName)" }
        if ($u2 -ceq $stored) { $matches += "MATCH sha512 concat uid+email enc=$($enc.WebName)" }
    }
    finally {
        $sha.Dispose()
    }
}

if ($matches.Count -eq 0) {
    Write-Output 'NO_MATCH_FOUND'
}
else {
    $matches | ForEach-Object { Write-Output $_ }
}
