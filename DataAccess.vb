Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Globalization
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports Microsoft.Data.SqlClient

Namespace HelloWorld
    Public NotInheritable Class DataAccess
        Public NotInheritable Class CrudButtonCaptions
            Public Property CreateCaption As String
            Public Property ReadCaption As String
            Public Property UpdateCaption As String
            Public Property DeleteCaption As String

            Public Shared Function DefaultCaptions() As CrudButtonCaptions
                Return New CrudButtonCaptions With {
                    .CreateCaption = "New",
                    .ReadCaption = "Read",
                    .UpdateCaption = "Modify",
                    .DeleteCaption = "Delete"
                }
            End Function
        End Class

        Public NotInheritable Class PageInitMetadata
            Public Property CrudCaptions As CrudButtonCaptions
            Public Property RoleAccess As RoleTableAccessEntry
            Public Property FieldCaptions As Dictionary(Of String, String)
            Public Property InvisibleFields As HashSet(Of String)
            Public Property StartEmpty As Boolean
            Public Property RoleTableAlias As String
            Public Property RoleOverrideCaption As String

            Public Sub New()
                CrudCaptions = CrudButtonCaptions.DefaultCaptions()
                RoleAccess = New RoleTableAccessEntry()
                FieldCaptions = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                InvisibleFields = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                StartEmpty = False
                RoleTableAlias = String.Empty
                RoleOverrideCaption = String.Empty
            End Sub
        End Class

        Private Sub New()
        End Sub

        ' Resolved on first use rather than at type load, so credentials entered in the startup
        ' configuration dialog take effect without restarting the application.
        Private Shared cachedConnectionString As String

        Private Shared ReadOnly Property ConnectionString As String
            Get
                If cachedConnectionString Is Nothing Then
                    cachedConnectionString = BuildConnectionString()
                End If

                Return cachedConnectionString
            End Get
        End Property

        ''' <summary>Discards the resolved connection string so the next use picks up new settings.</summary>
        Public Shared Sub RefreshConnectionString()
            cachedConnectionString = Nothing
        End Sub
        Private Shared ReadOnly metadataCacheLock As New Object()
        Private Shared ReadOnly crudCaptionCache As New Dictionary(Of Integer, CrudButtonCaptions)()
        Private Shared ReadOnly roleOverrideCaptionCache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly roleStartEmptyCache As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly roleFieldCaptionCache As New Dictionary(Of String, Dictionary(Of String, String))(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly pageInitMetadataCache As New Dictionary(Of String, PageInitMetadata)(StringComparer.OrdinalIgnoreCase)

        Public Shared Sub InvalidateRoleMetadataCache()
            SyncLock metadataCacheLock
                crudCaptionCache.Clear()
                roleOverrideCaptionCache.Clear()
                roleStartEmptyCache.Clear()
                roleFieldCaptionCache.Clear()
                pageInitMetadataCache.Clear()
            End SyncLock
        End Sub

        Private Shared Function CloneCrudCaptions(source As CrudButtonCaptions) As CrudButtonCaptions
            If source Is Nothing Then
                Return CrudButtonCaptions.DefaultCaptions()
            End If

            Return New CrudButtonCaptions With {
                .CreateCaption = source.CreateCaption,
                .ReadCaption = source.ReadCaption,
                .UpdateCaption = source.UpdateCaption,
                .DeleteCaption = source.DeleteCaption
            }
        End Function

        Private Shared Function BuildRoleDetailCacheKey(roleId As Integer, registrationId As Integer, dbTable As String) As String
            Return roleId.ToString(CultureInfo.InvariantCulture) & "|" &
                   registrationId.ToString(CultureInfo.InvariantCulture) & "|" &
                   dbTable.Trim().ToUpperInvariant()
        End Function

        Private Shared Function CloneCaptionMap(source As Dictionary(Of String, String)) As Dictionary(Of String, String)
            If source Is Nothing Then
                Return New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            End If

            Return New Dictionary(Of String, String)(source, StringComparer.OrdinalIgnoreCase)
        End Function

        Public Shared Function GetPageInitMetadata(roleId As Integer, registrationId As Integer, tableName As String) As PageInitMetadata
            Dim result As New PageInitMetadata()
            
            If roleId <= 0 OrElse registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return result
            End If

            Dim cacheKey = BuildRoleDetailCacheKey(roleId, registrationId, tableName)
            SyncLock metadataCacheLock
                Dim cached As PageInitMetadata = Nothing
                If pageInitMetadataCache.TryGetValue(cacheKey, cached) Then
                    Return ClonePageInitMetadata(cached)
                End If
            End SyncLock

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()

                    ' Batch 1: Registration captions
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 " &
                        "ISNULL(LTRIM(RTRIM(BTN_Create_Caption)), '') AS BTN_Create_Caption, " &
                        "ISNULL(LTRIM(RTRIM(BTN_Read_Caption)), '') AS BTN_Read_Caption, " &
                        "ISNULL(LTRIM(RTRIM(BTN_Update_Caption)), '') AS BTN_Update_Caption, " &
                        "ISNULL(LTRIM(RTRIM(BTN_Delete_Caption)), '') AS BTN_Delete_Caption " &
                        "FROM dbo.FW_Registration WHERE ID = @RegID", conn)
                        cmd.Parameters.AddWithValue("@RegID", registrationId)

                        Using reader = cmd.ExecuteReader()
                            If reader.Read() Then
                                Dim captions = CrudButtonCaptions.DefaultCaptions()
                                Dim create = SafeString(reader("BTN_Create_Caption"))
                                Dim read = SafeString(reader("BTN_Read_Caption"))
                                Dim update = SafeString(reader("BTN_Update_Caption"))
                                Dim delete = SafeString(reader("BTN_Delete_Caption"))

                                If create <> String.Empty Then captions.CreateCaption = create
                                If read <> String.Empty Then captions.ReadCaption = read
                                If update <> String.Empty Then captions.UpdateCaption = update
                                If delete <> String.Empty Then captions.DeleteCaption = delete

                                result.CrudCaptions = captions
                            End If
                        End Using
                    End Using

                    ' Batch 2: Role-table access + StartEmpty + table alias
                    Using cmd As New SqlCommand(
                        "SELECT " &
                        "ISNULL(Can_Create, 0) AS Can_Create, " &
                        "ISNULL(Can_Read, 0) AS Can_Read, " &
                        "ISNULL(Can_Update, 0) AS Can_Update, " &
                        "ISNULL(Can_Delete, 0) AS Can_Delete, " &
                        "ISNULL(Can_Import, 0) AS Can_Import, " &
                        "ISNULL(Can_UseQBE, 0) AS Can_UseQBE, " &
                        "ISNULL(Can_ViewAllRecords, 0) AS Can_ViewAllRecords, " &
                        "ISNULL(Can_ViewOnlyMyRecords, 0) AS Can_ViewOnlyMyRecords, " &
                        "ISNULL(Expand_QBE, 0) AS Expand_QBE, " &
                        "ISNULL(StartEmpty, 0) AS StartEmpty, " &
                        "ISNULL(LTRIM(RTRIM(Table_Alias)), '') AS Table_Alias, " &
                        "ISNULL(LTRIM(RTRIM(OverrideCaption)), '') AS OverrideCaption " &
                        "FROM dbo.FW_RoleDetails " &
                        "WHERE RoleID = @RoleID AND RegistrationID = @RegID AND DB_Table = @DBTable " &
                        "AND ISNULL(IsActive, 1) = 1", conn)
                        cmd.Parameters.AddWithValue("@RoleID", roleId)
                        cmd.Parameters.AddWithValue("@RegID", registrationId)
                        cmd.Parameters.AddWithValue("@DBTable", tableName.Trim())

                        Using reader = cmd.ExecuteReader()
                            If reader.Read() Then
                                result.RoleAccess = New RoleTableAccessEntry With {
                                    .TableName = tableName,
                                    .CanCreate = Convert.ToBoolean(reader("Can_Create")),
                                    .CanReadOnly = Convert.ToBoolean(reader("Can_Read")),
                                    .CanUpdate = Convert.ToBoolean(reader("Can_Update")),
                                    .CanDelete = Convert.ToBoolean(reader("Can_Delete")),
                                    .CanImport = Convert.ToBoolean(reader("Can_Import")),
                                    .CanUseQbe = Convert.ToBoolean(reader("Can_UseQBE")),
                                    .CanViewAllRecords = Convert.ToBoolean(reader("Can_ViewAllRecords")),
                                    .CanViewOnlyMy = Convert.ToBoolean(reader("Can_ViewOnlyMyRecords")),
                                    .CanExpandQbe = Convert.ToBoolean(reader("Expand_QBE"))
                                }
                                result.StartEmpty = Convert.ToBoolean(reader("StartEmpty"))
                                result.RoleTableAlias = SafeString(reader("Table_Alias"))
                                result.RoleOverrideCaption = SafeString(reader("OverrideCaption"))
                            End If
                        End Using
                    End Using

                    ' Batch 3: Field captions
                    Using cmd As New SqlCommand(
                        "SELECT FieldName, " &
                        "ISNULL(LTRIM(RTRIM(OverrideCaption)), '') AS OverrideCaption, " &
                        "ISNULL(LTRIM(RTRIM(FriendlyFieldName)), '') AS FriendlyFieldName, " &
                        "ISNULL(Make_Invisible, 0) AS Make_Invisible " &
                        "FROM dbo.FW_RoleFields " &
                        "WHERE RoleID = @RoleID AND RegistrationID = @RegID " &
                        "AND UPPER(LTRIM(RTRIM(TableName))) = UPPER(@TableName) " &
                        "AND ISNULL(IsActive, 1) = 1", conn)
                        cmd.Parameters.AddWithValue("@RoleID", roleId)
                        cmd.Parameters.AddWithValue("@RegID", registrationId)
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim fieldName = SafeString(reader("FieldName"))
                                If fieldName = String.Empty Then
                                    Continue While
                                End If

                                Dim overrideCaption = SafeString(reader("OverrideCaption"))
                                Dim friendlyName = SafeString(reader("FriendlyFieldName"))
                                Dim caption = If(overrideCaption <> String.Empty, overrideCaption, friendlyName)

                                If caption <> String.Empty Then
                                    result.FieldCaptions(fieldName) = caption
                                End If

                                If Convert.ToBoolean(reader("Make_Invisible")) Then
                                    result.InvisibleFields.Add(fieldName)
                                End If
                            End While
                        End Using
                    End Using
                End Using
            Catch
                ' Return defaults on error
            End Try

            SyncLock metadataCacheLock
                pageInitMetadataCache(cacheKey) = ClonePageInitMetadata(result)
            End SyncLock

            Return result
        End Function

        Private Shared Function ClonePageInitMetadata(source As PageInitMetadata) As PageInitMetadata
            If source Is Nothing Then
                Return New PageInitMetadata()
            End If

            Return New PageInitMetadata With {
                .CrudCaptions = CloneCrudCaptions(source.CrudCaptions),
                .RoleAccess = New RoleTableAccessEntry With {
                    .TableName = source.RoleAccess.TableName,
                    .CanCreate = source.RoleAccess.CanCreate,
                    .CanReadOnly = source.RoleAccess.CanReadOnly,
                    .CanUpdate = source.RoleAccess.CanUpdate,
                    .CanDelete = source.RoleAccess.CanDelete,
                    .CanImport = source.RoleAccess.CanImport,
                    .CanUseQbe = source.RoleAccess.CanUseQbe,
                    .CanViewAllRecords = source.RoleAccess.CanViewAllRecords,
                    .CanViewOnlyMy = source.RoleAccess.CanViewOnlyMy,
                    .CanExpandQbe = source.RoleAccess.CanExpandQbe
                },
                .FieldCaptions = CloneCaptionMap(source.FieldCaptions),
                .InvisibleFields = New HashSet(Of String)(source.InvisibleFields, StringComparer.OrdinalIgnoreCase),
                .StartEmpty = source.StartEmpty,
                .RoleTableAlias = source.RoleTableAlias,
                .RoleOverrideCaption = source.RoleOverrideCaption
            }
        End Function

        Public Shared Function GetDefaultDatabaseName() As String
            Dim builder As New SqlConnectionStringBuilder(ConnectionString)
            Return builder.InitialCatalog
        End Function

        Public Shared Function BuildConnectionStringForDatabase(databaseName As String) As String
            Dim builder As New SqlConnectionStringBuilder(ConnectionString)

            If Not String.IsNullOrWhiteSpace(databaseName) Then
                builder.InitialCatalog = databaseName.Trim()
            End If

            Return builder.ConnectionString
        End Function

        ''' <summary>
        ''' Resolution order is environment, then the credentials the user saved through the
        ''' startup dialog, then defaults. The environment comes first so a server deployment can
        ''' override without touching anyone's saved file.
        ''' </summary>
        Private Shared Function BuildConnectionString() As String
            Dim fullConnectionString = Environment.GetEnvironmentVariable("HELLOWORLD_DB_CONNECTION")
            If Not String.IsNullOrWhiteSpace(fullConnectionString) Then
                Return fullConnectionString.Trim()
            End If

            Dim saved = GetUsableSavedSettings()
            If saved IsNot Nothing Then
                Return DatabaseConfigStore.BuildConnectionString(saved)
            End If

            Dim server = GetEnvironmentOrDefault("HELLOWORLD_DB_SERVER", "BEELINK")
            Dim userId = GetEnvironmentOrDefault("HELLOWORLD_DB_USER", "sa")
            Dim password = GetEnvironmentOrDefault("HELLOWORLD_DB_PASSWORD", String.Empty)
            Dim database = GetEnvironmentOrDefault("HELLOWORLD_DB_NAME", "WX_Framework")
            Dim encrypt = GetEnvironmentOrDefault("HELLOWORLD_DB_ENCRYPT", "False")
            Dim trustServerCertificate = GetEnvironmentOrDefault("HELLOWORLD_DB_TRUST_SERVER_CERT", "True")

            Return "Server=" & server & ";User Id=" & userId & ";Password=" & password & ";Encrypt=" & encrypt & ";TrustServerCertificate=" & trustServerCertificate & ";Initial Catalog=" & database & ";"
        End Function

        Private Shared Function GetEnvironmentOrDefault(variableName As String, defaultValue As String) As String
            Dim value = Environment.GetEnvironmentVariable(variableName)
            If String.IsNullOrWhiteSpace(value) Then
                Return defaultValue
            End If

            Return value.Trim()
        End Function

        ''' <summary>
        ''' Returns a user-facing message when required database credentials are not configured,
        ''' or String.Empty when configuration is complete. Credentials are supplied through the
        ''' environment and are deliberately not compiled into the application.
        ''' </summary>
        Public Shared Function GetMissingConfigurationMessage() As String
            If Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELLOWORLD_DB_CONNECTION")) Then
                Return String.Empty
            End If

            If String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELLOWORLD_DB_PASSWORD")) Then
                ' Credentials saved through the configuration dialog count as configured, or the
                ' dialog would reappear on every launch and saving would achieve nothing.
                If GetUsableSavedSettings() IsNot Nothing Then
                    Return String.Empty
                End If

                Return "Database credentials are not configured." & Environment.NewLine &
                       Environment.NewLine &
                       "Enter them here, or set HELLOWORLD_DB_CONNECTION to a full connection " &
                       "string. HELLOWORLD_DB_SERVER, HELLOWORLD_DB_USER, HELLOWORLD_DB_NAME and " &
                       "HELLOWORLD_DB_PASSWORD are the individual overrides." & Environment.NewLine &
                       Environment.NewLine &
                       "run-local.ps1 sets these for local development."
            End If

            Return String.Empty
        End Function

        ''' <summary>
        ''' True when the connection details come from the environment rather than from anything
        ''' the user saved. Those belong to whoever set the machine up, so the application must
        ''' never offer to replace them - a server that is merely unreachable is not a credentials
        ''' problem, and a developer running run-local.ps1 should never see a prompt at all.
        ''' </summary>
        Public Shared Function IsUsingEnvironmentCredentials() As Boolean
            Return Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELLOWORLD_DB_CONNECTION")) OrElse
                   Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELLOWORLD_DB_PASSWORD"))
        End Function

        ''' <summary>
        ''' Saved credentials, but only when the environment has not already supplied a password.
        ''' Single owner of that precedence rule, so the startup check and the connection string
        ''' cannot disagree about whether the application is configured.
        ''' </summary>
        Private Shared Function GetUsableSavedSettings() As DatabaseConfigStore.DatabaseSettings
            If Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HELLOWORLD_DB_PASSWORD")) Then
                Return Nothing
            End If

            Dim saved = DatabaseConfigStore.Load()
            If saved Is Nothing OrElse String.IsNullOrWhiteSpace(saved.Password) Then
                Return Nothing
            End If

            Return saved
        End Function

        ''' <summary>
        ''' Opens the configured connection and runs a trivial query. Returns the failure reason,
        ''' or String.Empty when the database is reachable. Used at startup so credentials that
        ''' stopped working - a password changed on the server, say - lead to the configuration
        ''' dialog rather than an unexplained failure at login.
        ''' </summary>
        ''' <summary>
        ''' Server and database of the configured connection, for display. Never includes the
        ''' password, so it is safe to show on screen or write to the log.
        ''' </summary>
        Public Shared Function GetConnectionDescription() As String
            Try
                Dim builder As New SqlConnectionStringBuilder(ConnectionString)
                Dim server = If(String.IsNullOrWhiteSpace(builder.DataSource), "(unknown server)", builder.DataSource)
                Dim database = If(String.IsNullOrWhiteSpace(builder.InitialCatalog), "(unknown database)", builder.InitialCatalog)
                Return server & " / " & database
            Catch
                Return String.Empty
            End Try
        End Function

        ''' <summary>
        ''' Tests the configured connection and says *why* it failed, not merely that it did.
        '''
        ''' The three causes need different responses: an unreachable server is fixed by starting
        ''' it and retrying, while refused credentials or a missing database are fixed by changing
        ''' the settings. Telling a user to reconfigure a connection that is perfectly correct,
        ''' because their server is simply off, wastes their time and invites them to break it.
        '''
        ''' Classified on the SQL error number rather than message text, so it is not defeated by
        ''' wording or localisation.
        ''' </summary>
        Public Shared Function GetConnectionStatus(Optional timeoutSeconds As Integer = 10) As DatabaseConnectionStatus
            Try
                Dim builder As New SqlConnectionStringBuilder(ConnectionString) With {
                    .ConnectTimeout = Math.Max(1, timeoutSeconds),
                    .ConnectRetryCount = 0
                }

                Using conn As New SqlConnection(builder.ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand("SELECT 1", conn)
                        cmd.CommandTimeout = Math.Max(1, timeoutSeconds)
                        cmd.ExecuteScalar()
                    End Using
                End Using

                Return New DatabaseConnectionStatus()
            Catch ex As SqlException
                Dim kind As DatabaseFailureKind
                Select Case ex.Number
                    Case 18456, 18452, 18470
                        ' Login failed, untrusted domain, account disabled.
                        kind = DatabaseFailureKind.BadCredentials
                    Case 911, 4060, 4063, 4064
                        ' Database does not exist, or cannot be opened by this login.
                        kind = DatabaseFailureKind.DatabaseUnavailable
                    Case -2, -1, 2, 17, 40, 53, 121, 233, 1231, 10060, 10061, 10054
                        ' Timeouts and the network family: not found, refused, reset.
                        kind = DatabaseFailureKind.ServerUnreachable
                    Case Else
                        kind = DatabaseFailureKind.Other
                End Select

                Return New DatabaseConnectionStatus With {.Kind = kind, .Message = ex.Message}
            Catch ex As Exception
                Return New DatabaseConnectionStatus With {.Kind = DatabaseFailureKind.Other, .Message = ex.Message}
            End Try
        End Function

        Public Shared Function TestConfiguredConnection(Optional timeoutSeconds As Integer = 15) As String
            Return TestConnection(ConnectionString, timeoutSeconds)
        End Function

        ''' <summary>
        ''' Opens a connection and runs a trivial query. This authenticates to SQL Server, not to
        ''' the application, so it answers "is the database alive and reachable" before anyone has
        ''' logged in. Returns the failure reason, or String.Empty when the database answered.
        ''' </summary>
        Public Shared Function TestConnection(connectionString As String, Optional timeoutSeconds As Integer = 15) As String
            If String.IsNullOrWhiteSpace(connectionString) Then Return "No connection string is configured."

            Try
                ' ConnectRetryCount = 0 matters as much as the timeout. SqlClient retries once by
                ' default with a 10 second interval, so a 5 second timeout against an unreachable
                ' address took 27 seconds to report. Retrying is the user's decision, offered by
                ' the Retry button, not something the probe should do silently.
                Dim builder As New SqlConnectionStringBuilder(connectionString) With {
                    .ConnectTimeout = Math.Max(1, timeoutSeconds),
                    .ConnectRetryCount = 0
                }

                Using conn As New SqlConnection(builder.ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand("SELECT 1", conn)
                        cmd.CommandTimeout = Math.Max(1, timeoutSeconds)
                        cmd.ExecuteScalar()
                    End Using
                End Using

                Return String.Empty
            Catch ex As Exception
                Return ex.Message
            End Try
        End Function

        Public Shared Function TryAuthenticate(emailInput As String, passwordInput As String, ByRef user As UserContext, ByRef errorMessage As String) As Boolean
            user = Nothing
            errorMessage = String.Empty

                Dim emailLookup = NormalizeEmailForLookup(emailInput).Trim()
            If emailLookup = String.Empty Then
                errorMessage = "Email is required."
                Return False
            End If

            Dim enteredPassword = If(passwordInput, String.Empty).Trim()
            If enteredPassword = String.Empty Then
                errorMessage = "Password is required."
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()

                    Dim userId As Integer
                    Dim dbEmail As String = String.Empty
                    Dim firstName As String = String.Empty
                    Dim lastName As String = String.Empty
                    Dim storedPasswordHash As String = String.Empty

                    If Not TryGetCurrentUserRecord(conn, emailLookup, userId, dbEmail, firstName, lastName, storedPasswordHash) Then
                        errorMessage = "No user found for that email."
                        Return False
                    End If

                    If String.IsNullOrWhiteSpace(storedPasswordHash) Then
                        errorMessage = "Password is not configured for this user."
                        Return False
                    End If

                    Dim hashInput = RemoveSpaces(enteredPassword)
                    Dim passwordMatches = ValidateComputedHashAgainstStored(hashInput, userId, storedPasswordHash)
                    If Not passwordMatches Then
                        errorMessage = "Invalid email or password."
                        Return False
                    End If

                    user = New UserContext With {
                        .UserId = userId,
                        .Email = dbEmail,
                        .FirstName = firstName,
                        .LastName = lastName
                    }

                    Return True
                End Using
            Catch ex As Exception
                errorMessage = "Login failed: " & ex.Message
                Return False
            End Try
        End Function

        Private Shared Function TryGetCurrentUserRecord(conn As SqlConnection, emailLookup As String, ByRef userId As Integer, ByRef dbEmail As String, ByRef firstName As String, ByRef lastName As String, ByRef storedPasswordHash As String) As Boolean
            Using cmd As New SqlCommand("SELECT TOP 1 UserId, Email, ISNULL(FirstName, ''), ISNULL(LastName, ''), ISNULL(PasswordHash, '') FROM dbo.vw_FW_CurrentUser WHERE LOWER(REPLACE(Email, ' ', '')) = @EmailLookup", conn)
                cmd.Parameters.AddWithValue("@EmailLookup", emailLookup)

                Using reader = cmd.ExecuteReader()
                    If Not reader.Read() Then
                        Return False
                    End If

                    userId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)
                    dbEmail = reader.GetString(1)
                    firstName = reader.GetString(2)
                    lastName = reader.GetString(3)
                    storedPasswordHash = reader.GetString(4)
                    Return True
                End Using
            End Using
        End Function

        Public Shared Function GetBrowseRowsByRegistration(registrationId As Integer,
                                                         Optional filters As Dictionary(Of String, String) = Nothing,
                                                         Optional baseSelectSql As String = Nothing,
                                                         Optional showDeletedOnly As Boolean = False,
                                                         Optional additionalScopePredicate As String = Nothing,
                                                         Optional scopeUserId As Integer = 0,
                                                         Optional maxRows As Integer = 0,
                                                         Optional overrideExplicitRegistrationPredicate As Boolean = False) As DataTable
            Dim table As New DataTable("FW_Entity")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                ' CUSTOM SQL PATH: Execute as-is, then apply QBE filters client-side
                If Not String.IsNullOrWhiteSpace(baseSelectSql) Then
                    Dim effectiveSql = baseSelectSql.Trim()
                    Dim hasRegistrationReference = Regex.IsMatch(
                        effectiveSql,
                        "(?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?\b",
                        RegexOptions.IgnoreCase)
                    Dim hasExplicitRegistrationPredicate = Regex.IsMatch(
                        effectiveSql,
                        "\bWHERE\b[\s\S]*?(?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?\s*=",
                        RegexOptions.IgnoreCase)
                    If registrationId > 0 AndAlso hasRegistrationReference Then
                        Dim registrationValue = registrationId.ToString(CultureInfo.InvariantCulture)

                        If hasExplicitRegistrationPredicate Then
                            effectiveSql = Regex.Replace(
                                effectiveSql,
                                "((?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?)\s*=\s*(?:@RegistrationID|\?|\d+)",
                                "$1 = " & registrationValue,
                                RegexOptions.IgnoreCase)
                        Else
                            effectiveSql = effectiveSql.Replace("@RegistrationID", registrationValue, StringComparison.OrdinalIgnoreCase)
                            effectiveSql = effectiveSql.Replace("?", registrationValue)
                            effectiveSql = AddBrowseRegistrationPredicate(effectiveSql, registrationValue)
                        End If
                    End If

                    If Not String.IsNullOrWhiteSpace(additionalScopePredicate) AndAlso scopeUserId > 0 Then
                        Dim scopedPredicate = additionalScopePredicate.Trim().Replace("@RegistrationID", registrationId.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
                        effectiveSql = AddBrowseScopePredicate(effectiveSql, scopedPredicate)
                    End If

                    Using cmd As New SqlCommand(effectiveSql, conn)
                        If scopeUserId > 0 AndAlso effectiveSql.Contains("@UserID", StringComparison.OrdinalIgnoreCase) Then
                            cmd.Parameters.AddWithValue("@UserID", scopeUserId)
                        End If
                        If effectiveSql.Contains("@RegistrationID", StringComparison.OrdinalIgnoreCase) Then
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        End If
                        Using da As New SqlDataAdapter(cmd)
                            da.Fill(table)
                        End Using
                    End Using

                    If registrationId > 0 AndAlso Not hasExplicitRegistrationPredicate AndAlso
                       table.Columns.Contains("RegistrationID") Then
                        table = FilterBrowseRowsByRegistration(table, registrationId)
                    End If

                    table = ApplyEntityDeletedFilterFallback(table, showDeletedOnly)

                    If filters IsNot Nothing AndAlso filters.Count > 0 Then
                        Dim filterExpr = BuildDataViewFilterExpression(table, filters)
                        If Not String.IsNullOrWhiteSpace(filterExpr) Then
                            Try
                                Dim view As New DataView(table)
                                view.RowFilter = filterExpr
                                Return view.ToTable()
                            Catch
                                ' Return unfiltered if expression is invalid
                            End Try
                        End If
                    End If

                    Return LimitBrowseRows(table, maxRows)
                End If

                ' DEFAULT SQL PATH: Standard query with optional QBE filters
                Dim sql As New StringBuilder()
                sql.Append("SELECT ID, RegistrationID, FirstName, MiddleName, LastName, FirstLast, LastFirst, EMail1, Phone1, IsActive, AssignedManagerID, GenderID, DeletedFlag, UpdatedOn ")
                sql.Append("FROM dbo.FW_Entity WHERE RegistrationID = @RegistrationID")

                Dim parsedFilters As New List(Of Tuple(Of String, QbeComparisonOperator, String))()

                If filters IsNot Nothing Then
                    For Each kvp In filters
                        Dim rawKey = If(kvp.Key, String.Empty).Trim()
                        Dim rawValue = If(kvp.Value, String.Empty).Trim()

                        If rawValue = String.Empty Then
                            Continue For
                        End If

                        Dim fieldName = rawKey
                        Dim comparisonOperator As QbeComparisonOperator = QbeComparisonOperator.EqualsTo

                        If rawKey.Contains("|") Then
                            Dim pieces = rawKey.Split("|"c)
                            fieldName = pieces(0)
                            If pieces.Length > 1 Then
                                [Enum].TryParse(pieces(1), True, comparisonOperator)
                            End If
                        End If

                        parsedFilters.Add(Tuple.Create(fieldName, comparisonOperator, rawValue))
                    Next
                End If

                ' Add filter clauses to SQL
                For Each parsedFilter In parsedFilters
                    Dim fieldName = parsedFilter.Item1
                    Dim comparisonOperator = parsedFilter.Item2
                    Dim value = parsedFilter.Item3

                    Select Case fieldName
                        Case "ID"
                            Dim parsed As Integer
                            If Integer.TryParse(value, parsed) Then
                                sql.Append(" AND ID").Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.NumericField)).Append(" @").Append(fieldName)
                            End If
                        Case "AssignedManagerID"
                            Dim parsed As Integer
                            If Integer.TryParse(value, parsed) Then
                                sql.Append(" AND AssignedManagerID").Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.NumericField)).Append(" @AssignedManagerID")
                            End If
                        Case "IsActive"
                            Dim parsedBit As Boolean
                            If TryParseBooleanFilter(value, parsedBit) Then
                                sql.Append(" AND IsActive").Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.BooleanField)).Append(" @IsActive")
                            End If
                        Case "FirstName", "MiddleName", "LastName", "FirstLast", "LastFirst", "EMail1", "Phone1"
                            sql.Append(" AND ").Append(fieldName).Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.TextField)).Append(" @").Append(fieldName)
                    End Select
                Next

                sql.Append(" ORDER BY ID")

                ' Execute default query with filters and parameters
                Using cmd As New SqlCommand(sql.ToString(), conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                    For Each parsedFilter In parsedFilters
                        Dim fieldName = parsedFilter.Item1
                        Dim comparisonOperator = parsedFilter.Item2
                        Dim value = parsedFilter.Item3

                        Select Case fieldName
                            Case "ID"
                                Dim parsed As Integer
                                If Integer.TryParse(value, parsed) Then
                                    cmd.Parameters.AddWithValue("@" & fieldName, parsed)
                                End If
                            Case "AssignedManagerID"
                                Dim parsed As Integer
                                If Integer.TryParse(value, parsed) Then
                                    cmd.Parameters.AddWithValue("@AssignedManagerID", parsed)
                                End If
                            Case "IsActive"
                                Dim parsedBit As Boolean
                                If TryParseBooleanFilter(value, parsedBit) Then
                                    cmd.Parameters.AddWithValue("@IsActive", parsedBit)
                                End If
                            Case "FirstName", "MiddleName", "LastName", "FirstLast", "LastFirst", "EMail1", "Phone1"
                                cmd.Parameters.AddWithValue("@" & fieldName, BuildTextFilterValue(value, comparisonOperator))
                        End Select
                    Next

                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            table = ApplyEntityDeletedFilterFallback(table, showDeletedOnly)
            Return LimitBrowseRows(table, maxRows)
        End Function

        Private Shared Function LimitBrowseRows(source As DataTable, maxRows As Integer) As DataTable
            If source Is Nothing OrElse maxRows <= 0 OrElse source.Rows.Count <= maxRows Then
                Return source
            End If

            Dim limited = source.Clone()
            limited.ExtendedProperties("BrowseRowsLimited") = True
            For index As Integer = 0 To maxRows - 1
                limited.ImportRow(source.Rows(index))
            Next

            Return limited
        End Function

        Private Shared Function AddBrowseScopePredicate(sql As String, predicate As String) As String
            If Not Regex.IsMatch(predicate, "^[A-Za-z_][A-Za-z0-9_]*\s*=\s*\d+\s+AND\s+[A-Za-z_][A-Za-z0-9_]*\s*=\s*@UserID$", RegexOptions.CultureInvariant Or RegexOptions.IgnoreCase) Then
                Return sql
            End If

            Dim orderMatch = Regex.Match(sql, "\s+ORDER\s+BY\s+", RegexOptions.IgnoreCase)
            If orderMatch.Success Then
                Return sql.Substring(0, orderMatch.Index) &
                       If(Regex.IsMatch(sql.Substring(0, orderMatch.Index), "\s+WHERE\s+", RegexOptions.IgnoreCase), " AND ", " WHERE ") &
                       predicate & sql.Substring(orderMatch.Index)
            End If

            Return sql &
                   If(Regex.IsMatch(sql, "\s+WHERE\s+", RegexOptions.IgnoreCase), " AND ", " WHERE ") &
                   predicate
        End Function

        Private Shared Function AddBrowseRegistrationPredicate(sql As String, registrationValue As String) As String
            If String.IsNullOrWhiteSpace(sql) OrElse String.IsNullOrWhiteSpace(registrationValue) OrElse
               Not Regex.IsMatch(sql, "(?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?\b", RegexOptions.IgnoreCase) OrElse
               Regex.IsMatch(sql, "(?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?\s*=", RegexOptions.IgnoreCase) Then
                Return sql
            End If

            Dim predicate = "RegistrationID = " & registrationValue
            Dim orderMatch = Regex.Match(sql, "\s+ORDER\s+BY\s+", RegexOptions.IgnoreCase)
            If orderMatch.Success Then
                Return sql.Substring(0, orderMatch.Index) &
                       If(Regex.IsMatch(sql.Substring(0, orderMatch.Index), "\s+WHERE\s+", RegexOptions.IgnoreCase), " AND ", " WHERE ") &
                       predicate & sql.Substring(orderMatch.Index)
            End If

            Return sql &
                   If(Regex.IsMatch(sql, "\s+WHERE\s+", RegexOptions.IgnoreCase), " AND ", " WHERE ") &
                   predicate
        End Function

        Private Shared Function FilterBrowseRowsByRegistration(source As DataTable, registrationId As Integer) As DataTable
            If source Is Nothing OrElse Not source.Columns.Contains("RegistrationID") OrElse registrationId <= 0 Then
                Return source
            End If

            Dim filtered = source.Clone()
            For Each row As DataRow In source.Rows
                If row.IsNull("RegistrationID") Then
                    Continue For
                End If

                Dim rowRegistrationId As Integer
                If Integer.TryParse(Convert.ToString(row("RegistrationID"), CultureInfo.InvariantCulture), rowRegistrationId) AndAlso
                   rowRegistrationId = registrationId Then
                    filtered.ImportRow(row)
                End If
            Next

            Return filtered
        End Function

        Private Shared Function ApplyDeletedFlagFilter(source As DataTable, showDeletedOnly As Boolean) As DataTable
            If source Is Nothing OrElse source.Columns Is Nothing OrElse Not source.Columns.Contains("DeletedFlag") Then
                Return source
            End If

            Dim filtered As DataTable = source.Clone()

            For Each row As DataRow In source.Rows
                Dim isDeleted As Boolean = False

                If Not row.IsNull("DeletedFlag") Then
                    Try
                        isDeleted = Convert.ToBoolean(row("DeletedFlag"), CultureInfo.InvariantCulture)
                    Catch
                        isDeleted = False
                    End Try
                End If

                If isDeleted = showDeletedOnly Then
                    filtered.ImportRow(row)
                End If
            Next

            Return filtered
        End Function

        Public Shared Function GetTableSqlFromRoleTable(registrationId As Integer, tableName As String) As String
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return String.Empty
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 Table_SQL FROM dbo.FW_RoleTables " &
                    "WHERE (RegistrationID = @RegistrationID OR RegistrationID IS NULL) AND DB_Table = @DBTable " &
                    "ORDER BY CASE WHEN RegistrationID = @RegistrationID THEN 0 ELSE 1 END, ID DESC", conn)
                    
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@DBTable", tableName.Trim())
                    
                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        Return String.Empty
                    End If
                    
                    Return result.ToString()
                End Using
            End Using
        End Function

        Public Shared Function GetRoleTableIdByTable(registrationId As Integer, tableName As String) As Integer?
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return Nothing
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ID FROM dbo.FW_RoleTables " &
                    "WHERE (RegistrationID = @RegistrationID OR RegistrationID IS NULL) AND DB_Table = @DBTable " &
                    "ORDER BY CASE WHEN RegistrationID = @RegistrationID THEN 0 ELSE 1 END, ID DESC", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@DBTable", tableName.Trim())

                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        Return Nothing
                    End If

                    Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        Public Shared Function GetTableSqlFromRoleTableByWindowOrPage(registrationId As Integer, windowOrPageName As String) As String
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return String.Empty
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 Table_SQL FROM dbo.FW_RoleTables " &
                    "WHERE WindowOrPage = @WindowOrPage " &
                    "ORDER BY ID DESC", conn)
                    
                    cmd.Parameters.AddWithValue("@WindowOrPage", windowOrPageName.Trim())
                    
                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        Return String.Empty
                    End If
                    
                    Return result.ToString()
                End Using
            End Using
        End Function

        Public Shared Function GetTableAliasFromRoleTableByWindowOrPage(registrationId As Integer, windowOrPageName As String) As String
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return String.Empty
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 Table_Alias FROM dbo.FW_RoleTables " &
                    "WHERE WindowOrPage = @WindowOrPage " &
                    "ORDER BY ID DESC", conn)
                    
                    cmd.Parameters.AddWithValue("@WindowOrPage", windowOrPageName.Trim())
                    
                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        Return String.Empty
                    End If
                    
                    Return result.ToString()
                End Using
            End Using
        End Function

        Public Shared Function GetExposedRoleTableChoices() As DataTable
            Dim choices As New DataTable("ExposedRoleTables")
            choices.Columns.Add("ID", GetType(Integer))
            choices.Columns.Add("SchemaID", GetType(Integer))
            choices.Columns.Add("Table_Alias", GetType(String))
            choices.Columns.Add("DB_Table", GetType(String))
            choices.Columns.Add("WindowOrPage", GetType(String))

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "DECLARE @ExposureColumn sysname; " &
                    "SELECT TOP 1 @ExposureColumn = c.name " &
                    "FROM sys.columns AS c " &
                    "WHERE c.object_id = OBJECT_ID(N'dbo.FW_RoleTables') " &
                    "AND c.name = N'ExposedToUser'; " &
                    "IF @ExposureColumn IS NULL THROW 52107, 'No exposed-user column exists on dbo.FW_RoleTables.', 1; " &
                    "DECLARE @Sql nvarchar(max) = " &
                    "N'SELECT MIN(rt.ID) AS ID, MIN(rs.ID) AS SchemaID, rt.Table_Alias, MIN(rt.DB_Table) AS DB_Table, MIN(rt.WindowOrPage) AS WindowOrPage ' " &
                    "+ N'FROM dbo.FW_RoleTables rt INNER JOIN dbo.FW_RoleSchema rs ON rs.DB_Table = rt.DB_Table AND ISNULL(rs.IsActive, 1) = 1 ' " &
                    "+ N'WHERE ISNULL(rt.' + QUOTENAME(@ExposureColumn) + N', 0) = 1 ' " &
                    "+ N'GROUP BY rt.Table_Alias ORDER BY rt.Table_Alias'; " &
                    "EXEC sys.sp_executesql @Sql;", conn)
                    cmd.CommandTimeout = 10
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(choices)
                    End Using
                End Using
            End Using

            Return choices
        End Function

        Public Shared Function GetDbTableFromRoleTableByWindowOrPage(registrationId As Integer, windowOrPageName As String) As String
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return String.Empty
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 DB_Table FROM dbo.FW_RoleTables " &
                    "WHERE WindowOrPage = @WindowOrPage " &
                    "ORDER BY ID DESC", conn)

                    cmd.Parameters.AddWithValue("@WindowOrPage", windowOrPageName.Trim())

                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        Return String.Empty
                    End If

                    Return result.ToString()
                End Using
            End Using
        End Function

        Public Shared Function CheckIfRoleTableRecordExists(registrationId As Integer, windowOrPageName As String) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return False
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COUNT(1) FROM dbo.FW_RoleTables " &
                    "WHERE WindowOrPage = @WindowOrPage", conn)
                    
                    cmd.Parameters.AddWithValue("@WindowOrPage", windowOrPageName.Trim())
                    
                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso IsNumeric(result), CInt(result) > 0, False)
                End Using
            End Using
        End Function

        Public Shared Function GetRoleTableMetadata(windowOrPageName As String) As DataRow
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return Nothing
            End If

            Dim table As New DataTable("FW_RoleTablesMetadata")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 WindowOrPage, DB_Table, Table_Alias, Table_SQL " &
                    "FROM dbo.FW_RoleTables WHERE WindowOrPage = @WindowOrPage " &
                    "ORDER BY ID DESC", conn)
                    cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = windowOrPageName.Trim()
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return If(table.Rows.Count = 0, Nothing, table.Rows(0))
        End Function

        Public Shared Function UpsertRoleTableRecord(registrationId As Integer, windowOrPageName As String, dbTableName As String, tableAlias As String, tableSql As String, userId As Integer) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) OrElse String.IsNullOrWhiteSpace(dbTableName) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    
                    Using cmd As New SqlCommand(
                        "IF EXISTS (SELECT 1 FROM dbo.FW_RoleTables WHERE WindowOrPage = @WindowOrPage) " &
                        "UPDATE dbo.FW_RoleTables SET RegistrationID = NULL, DB_Table = @DBTable, Table_Alias = @TableAlias, Table_SQL = @TableSQL WHERE WindowOrPage = @WindowOrPage " &
                        "ELSE INSERT INTO dbo.FW_RoleTables (RegistrationID, WindowOrPage, DB_Table, Table_Alias, Table_SQL, CreatedBy) VALUES (NULL, @WindowOrPage, @DBTable, @TableAlias, @TableSQL, @CreatedBy)", conn)
                        cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = windowOrPageName.Trim()
                        cmd.Parameters.Add("@DBTable", SqlDbType.VarChar, 100).Value = dbTableName.Trim()
                        cmd.Parameters.Add("@TableAlias", SqlDbType.VarChar, 100).Value = If(String.IsNullOrWhiteSpace(tableAlias), dbTableName.Trim(), tableAlias.Trim())
                        cmd.Parameters.Add("@TableSQL", SqlDbType.VarChar, -1).Value = DbValue(tableSql)
                        cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = userId
                        cmd.ExecuteNonQuery()
                    End Using
                    
                    ' Also save the table alias to FW_TableAliases for global reuse
                    SaveTableAlias(dbTableName.Trim(), If(String.IsNullOrWhiteSpace(tableAlias), dbTableName.Trim(), tableAlias.Trim()))
                    
                    Return True
                End Using
            Catch ex As Exception
                LogFallbackUsage("SQL_Fallback_UpsertException",
                                 "Failed to persist FW_RoleTables fallback for " & windowOrPageName & ": " & ex.Message,
                                 windowOrPageName,
                                 registrationId)
                Return False
            End Try
        End Function

        Public Shared Function UpdateRoleTableSql(windowOrPageName As String, tableSql As String) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_RoleTables SET Table_SQL = @TableSQL WHERE WindowOrPage = @WindowOrPage", conn)
                        cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = windowOrPageName.Trim()
                        cmd.Parameters.Add("@TableSQL", SqlDbType.VarChar, -1).Value = DbValue(tableSql)
                        Return cmd.ExecuteNonQuery() > 0
                    End Using
                End Using
            Catch ex As Exception
                LogFallbackUsage("SQL_Fallback_UpdateException",
                                 "Failed to update FW_RoleTables SQL for " & windowOrPageName & ": " & ex.Message,
                                 windowOrPageName,
                                 0)
                Return False
            End Try
        End Function

        Public Shared Function SaveTableAlias(tableName As String, displayAlias As String) As Boolean
            If String.IsNullOrWhiteSpace(tableName) OrElse String.IsNullOrWhiteSpace(displayAlias) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    
                    Using cmd As New SqlCommand(
                        "IF EXISTS (SELECT 1 FROM dbo.FW_TableAliases WHERE TableName = @TableName) " &
                        "UPDATE dbo.FW_TableAliases SET DisplayAlias = @DisplayAlias WHERE TableName = @TableName " &
                        "ELSE " &
                        "INSERT INTO dbo.FW_TableAliases (TableName, DisplayAlias) VALUES (@TableName, @DisplayAlias)", conn)
                        
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())
                        cmd.Parameters.AddWithValue("@DisplayAlias", displayAlias.Trim())
                        
                        cmd.ExecuteNonQuery()
                    End Using
                    
                    Return True
                End Using
            Catch
                Return False
            End Try
        End Function

        Public Shared Function GetDatabaseTables() As List(Of String)
            Dim tables As New List(Of String)()

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT UPPER(name) FROM sys.tables WHERE name LIKE 'FW_%' OR name LIKE 'AS_%' OR name LIKE 'CRM_%' ORDER BY UPPER(name)", conn)
                        
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim tableName = reader(0).ToString()
                                tables.Add(tableName)
                            End While
                        End Using
                    End Using
                End Using
            Catch
                ' Return empty list on error
            End Try

            Return tables
        End Function

        Public Shared Function FormatTableNameAsAlias(tableName As String) As String
            If String.IsNullOrWhiteSpace(tableName) Then
                Return tableName
            End If

            ' Get the actual casing from the database
            Dim actualName = GetActualTableNameFromSchema(tableName.Trim())
            Dim name = actualName.Trim()
            
            ' Strip FW_ or AS_ prefix (case-insensitive in VB.NET)
            If name.ToUpper().StartsWith("FW_") Then
                name = name.Substring(3)
            ElseIf name.ToUpper().StartsWith("AS_") Then
                name = name.Substring(3)
            End If

            name = name.Replace("_", " ").Trim()

            Dim result As New System.Text.StringBuilder()
            For i As Integer = 0 To name.Length - 1
                Dim current = name(i)
                Dim previous = If(i > 0, name(i - 1), ChrW(0))
                Dim nextCharacter = If(i + 1 < name.Length, name(i + 1), ChrW(0))
                Dim startsNewWord = i > 0 AndAlso current <> " "c AndAlso
                                    Char.IsUpper(current) AndAlso
                                    (Char.IsLower(previous) OrElse (Char.IsUpper(previous) AndAlso Char.IsLower(nextCharacter)))
                If startsNewWord AndAlso result.Length > 0 AndAlso result(result.Length - 1) <> " "c Then
                    result.Append(" "c)
                End If
                result.Append(current)
            Next

            Dim formatted = result.ToString().Trim()
            formatted = Regex.Replace(formatted, "\bHd\b", "HD", RegexOptions.IgnoreCase)
            formatted = Regex.Replace(formatted, "\bUi\b", "UI", RegexOptions.IgnoreCase)
            formatted = Regex.Replace(formatted, "\bQbe\b", "QBE", RegexOptions.IgnoreCase)
            Return formatted
        End Function

        Public Structure TableData
            Public TableName As String
            Public DisplayAlias As String

            Public Sub New(name As String, displayAlias As String)
                TableName = name
                Me.DisplayAlias = displayAlias
            End Sub
        End Structure

        Public Shared Function GetDatabaseTablesWithAliases() As List(Of TableData)
            Dim tables As New List(Of TableData)()
            Dim tableNames = GetDatabaseTables()

            For Each tableName In tableNames
                Dim displayAlias = FormatTableNameAsAlias(tableName)
                tables.Add(New TableData(tableName, displayAlias))
            Next

            Return tables
        End Function

        Public Shared Function TableHasColumn(tableName As String, columnName As String) As Boolean
            If String.IsNullOrWhiteSpace(tableName) OrElse String.IsNullOrWhiteSpace(columnName) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT COUNT(1) FROM INFORMATION_SCHEMA.COLUMNS " &
                        "WHERE TABLE_NAME = @TableName AND COLUMN_NAME = @ColumnName", conn)
                        
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())
                        cmd.Parameters.AddWithValue("@ColumnName", columnName.Trim())
                        
                        Dim result = cmd.ExecuteScalar()
                        Return If(result IsNot Nothing AndAlso IsNumeric(result), CInt(result) > 0, False)
                    End Using
                End Using
            Catch
                Return False
            End Try
        End Function

        Public Shared Function TableHasRowVersion(tableName As String) As Boolean
            If String.IsNullOrWhiteSpace(tableName) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT COUNT(1) FROM sys.columns AS c " &
                        "INNER JOIN sys.tables AS t ON t.object_id = c.object_id " &
                        "INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id " &
                        "WHERE s.name = N'dbo' AND t.name = @TableName AND c.system_type_id = 189", conn)
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())
                        Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0
                    End Using
                End Using
            Catch
                Return False
            End Try
        End Function

        Public Shared Function GetTabOrderSettings(pageName As String) As List(Of TabOrderSetting)
            Dim results As New List(Of TabOrderSetting)()
            If String.IsNullOrWhiteSpace(pageName) Then
                Return results
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT ControlName, TabOrder, IsActive FROM dbo.FW_UpdateTabOrder " &
                        "WHERE PageName = @PageName AND ISNULL(DeletedFlag, 0) = 0 ORDER BY TabOrder", conn)
                        cmd.Parameters.AddWithValue("@PageName", pageName.Trim())

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim controlName = SafeString(reader("ControlName")).Trim()
                                If controlName = String.Empty Then
                                    Continue While
                                End If

                                Dim tabOrderValue As Integer = 0
                                If Not IsDBNull(reader("TabOrder")) Then
                                    tabOrderValue = Convert.ToInt32(reader("TabOrder"), CultureInfo.InvariantCulture)
                                End If

                                Dim isActive As Boolean = True
                                If Not IsDBNull(reader("IsActive")) Then
                                    isActive = Convert.ToBoolean(reader("IsActive"), CultureInfo.InvariantCulture)
                                End If

                                results.Add(New TabOrderSetting With {
                                    .ControlName = controlName,
                                    .TabOrder = tabOrderValue,
                                    .TabStop = isActive
                                })
                            End While
                        End Using
                    End Using
                End Using
            Catch
                Return New List(Of TabOrderSetting)()
            End Try

            Return results
        End Function

        Public Shared Sub SaveTabOrderSettings(pageName As String, settings As List(Of TabOrderSetting), updatedBy As Integer)
            If String.IsNullOrWhiteSpace(pageName) Then
                Return
            End If

            If settings Is Nothing Then
                settings = New List(Of TabOrderSetting)()
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using transaction = conn.BeginTransaction()
                        Using deleteCmd As New SqlCommand(
                            "DELETE FROM dbo.FW_UpdateTabOrder WHERE PageName = @PageName", conn, transaction)
                            deleteCmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                            deleteCmd.ExecuteNonQuery()
                        End Using

                        Dim insertSql = "INSERT INTO dbo.FW_UpdateTabOrder (PageName, ControlName, TabOrder, IsActive, DeletedFlag, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                                        "VALUES (@PageName, @ControlName, @TabOrder, @IsActive, 0, @CreatedBy, GETDATE(), @UpdatedBy, GETDATE())"

                        For Each setting In settings
                            If setting Is Nothing Then
                                Continue For
                            End If

                            Dim controlName = If(setting.ControlName, String.Empty).Trim()
                            If controlName = String.Empty Then
                                Continue For
                            End If

                            Using insertCmd As New SqlCommand(insertSql, conn, transaction)
                                insertCmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                                insertCmd.Parameters.AddWithValue("@ControlName", controlName)
                                insertCmd.Parameters.AddWithValue("@TabOrder", setting.TabOrder)
                                insertCmd.Parameters.AddWithValue("@IsActive", setting.TabStop)
                                insertCmd.Parameters.AddWithValue("@CreatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                insertCmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                insertCmd.ExecuteNonQuery()
                            End Using
                        Next

                        transaction.Commit()
                    End Using
                End Using
            Catch
                ' Intentionally swallow persistence errors; page should still load.
            End Try
        End Sub

        Public Shared Function GetActualTableNameFromSchema(tableName As String) As String
            If String.IsNullOrWhiteSpace(tableName) Then
                Return tableName
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT name FROM sys.tables WHERE UPPER(name) = @UpperName", conn)
                        
                        cmd.Parameters.AddWithValue("@UpperName", tableName.Trim().ToUpper())
                        
                        Dim result = cmd.ExecuteScalar()
                        If result IsNot Nothing Then
                            Return result.ToString()
                        End If
                    End Using
                End Using
            Catch
                ' Return original if query fails
            End Try

            Return tableName
        End Function

        Public Shared Function GetRolesByRegistration(registrationId As Integer, Optional baseSelectSql As String = Nothing) As DataTable
            Dim table As New DataTable()

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                ' CUSTOM SQL PATH: Execute exactly as-is, no modifications
                If Not String.IsNullOrWhiteSpace(baseSelectSql) Then
                    Using cmd As New SqlCommand(baseSelectSql.Trim(), conn)
                        Using da As New SqlDataAdapter(cmd)
                            da.Fill(table)
                        End Using
                    End Using
                    Return table
                End If

                ' DEFAULT SQL PATH: Standard query with parameter
                Dim sql As New StringBuilder()
                sql.Append("SELECT ID, RegistrationID, RoleName ")
                sql.Append("FROM dbo.FW_Roles ")
                sql.Append("WHERE RegistrationID = @RegistrationID ")
                sql.Append("ORDER BY RoleName")

                Using cmd As New SqlCommand(sql.ToString(), conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetPageGenerationById(pageRequestId As Integer) As DataRow
            Dim table As New DataTable("PageGeneration")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT * FROM dbo.FW_PageGeneration_B_U WHERE PageRequestID = @PageRequestID", conn)
                    cmd.Parameters.Add("@PageRequestID", SqlDbType.Int).Value = pageRequestId
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            If table.Rows.Count = 0 Then Return Nothing
            Return table.Rows(0)
        End Function

        Public Shared Function GetPageGenerationId(requestName As String, browsePageName As String, maintenancePageName As String) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 PageRequestID FROM dbo.FW_PageGeneration_B_U " &
                    "WHERE RequestName = @RequestName AND BrowsePageName = @BrowsePageName AND MaintenancePageName = @MaintenancePageName " &
                    "ORDER BY PageRequestID DESC", conn)
                    cmd.Parameters.Add("@RequestName", SqlDbType.VarChar, -1).Value = If(requestName, String.Empty).Trim()
                    cmd.Parameters.Add("@BrowsePageName", SqlDbType.VarChar, -1).Value = If(browsePageName, String.Empty).Trim()
                    cmd.Parameters.Add("@MaintenancePageName", SqlDbType.VarChar, -1).Value = If(maintenancePageName, String.Empty).Trim()
                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then Return 0
                    Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        Public Shared Function SavePageGenerationMaintenanceBaseline(pageRequestId As Integer,
                                                                       source As String,
                                                                       sourceHash As String) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_PageGeneration_B_U SET GeneratedMaintenanceSource = @Source, GeneratedMaintenanceHash = @Hash, UpdatedOn = GETDATE() WHERE PageRequestID = @PageRequestID", conn)
                    cmd.Parameters.Add("@Source", SqlDbType.VarChar, -1).Value = If(source, String.Empty)
                    cmd.Parameters.Add("@Hash", SqlDbType.VarChar, 64).Value = If(sourceHash, String.Empty)
                    cmd.Parameters.Add("@PageRequestID", SqlDbType.Int).Value = pageRequestId
                    Return cmd.ExecuteNonQuery() = 1
                End Using
            End Using
        End Function

        Public Shared Function GetPageGenerationSchema() As DataTable
            Dim table As New DataTable("PageGenerationSchema")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 0 * FROM dbo.FW_PageGeneration_B_U", conn)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetTableFieldNames(tableName As String) As List(Of String)
            Dim fields As New List(Of String)()
            If String.IsNullOrWhiteSpace(tableName) Then Return fields

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION", conn)
                    cmd.Parameters.Add("@TableName", SqlDbType.VarChar, 128).Value = tableName.Trim()
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            fields.Add(reader.GetString(0))
                        End While
                    End Using
                End Using
            End Using

            Return fields
        End Function

        Public Shared Function GetPrimaryKeyFieldName(tableName As String) As String
            If String.IsNullOrWhiteSpace(tableName) Then Return String.Empty

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 c.name " &
                    "FROM sys.indexes AS i " &
                    "INNER JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id " &
                    "INNER JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id " &
                    "INNER JOIN sys.tables AS t ON t.object_id = i.object_id " &
                    "INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id " &
                    "WHERE s.name = N'dbo' AND t.name = @TableName AND i.is_primary_key = 1 " &
                    "ORDER BY ic.key_ordinal", conn)
                    cmd.Parameters.Add("@TableName", SqlDbType.VarChar, 128).Value = tableName.Trim()
                    Dim result = cmd.ExecuteScalar()
                    Return If(result Is Nothing OrElse IsDBNull(result), String.Empty, result.ToString())
                End Using
            End Using
        End Function

        ''' <summary>Whether the row still exists, ignoring its RowVersion.</summary>
        Private Shared Function GeneratedPageRecordExists(conn As SqlConnection, tableName As String, primaryKey As String, recordId As Integer) As Boolean
            Using cmd As New SqlCommand("SELECT COUNT(1) FROM dbo." & QuoteGeneratedIdentifier(tableName) &
                                        " WHERE " & QuoteGeneratedIdentifier(primaryKey) & " = @RecordID", conn)
                cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0
            End Using
        End Function

        ''' <summary>
        ''' Rows for a lookup combo, ordered by the display column. Used by generated maintenance
        ''' pages to fill a foreign-key combo through the shared ConfigureLookupCombo helper.
        ''' </summary>
        Public Shared Function GetLookupTable(tableName As String, valueColumn As String, displayColumn As String) As DataTable
            Dim result As New DataTable()

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT " & QuoteGeneratedIdentifier(valueColumn) & " AS " & QuoteGeneratedIdentifier(valueColumn) &
                                            ", " & QuoteGeneratedIdentifier(displayColumn) & " AS " & QuoteGeneratedIdentifier(displayColumn) &
                                            " FROM dbo." & QuoteGeneratedIdentifier(tableName) &
                                            " ORDER BY " & QuoteGeneratedIdentifier(displayColumn), conn)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(result)
                    End Using
                End Using
            End Using

            Return result
        End Function

        ''' <summary>
        ''' Soft-deletes a generated page's record, and writes the audit row.
        '''
        ''' Returns the reason it could not be done, or String.Empty on success. A table with no
        ''' DeletedFlag is refused rather than deleted physically: a physical delete needs explicit
        ''' approval, and quietly doing one because the column happens to be missing is exactly the
        ''' kind of surprise that guardrail exists to prevent.
        ''' </summary>
        Public Shared Function SoftDeleteGeneratedPageRecord(tableName As String,
                                                             primaryKey As String,
                                                             recordId As Integer,
                                                             userId As Integer,
                                                             pageName As String) As String
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) OrElse String.IsNullOrWhiteSpace(primaryKey) OrElse recordId <= 0 Then
                Return "The record could not be identified."
            End If

            If Not TableHasColumn(normalizedTable, "DeletedFlag") Then
                Return "This table does not support delete. It has no DeletedFlag column, and records are never removed physically."
            End If

            Try
                Dim assignments As New List(Of String)() From {"[DeletedFlag] = 1"}
                If TableHasColumn(normalizedTable, "DeletedBy") Then assignments.Add("[DeletedBy] = @UserID")
                If TableHasColumn(normalizedTable, "DeletedOn") Then assignments.Add("[DeletedOn] = SYSUTCDATETIME()")
                If TableHasColumn(normalizedTable, "IsActive") Then assignments.Add("[IsActive] = 0")
                If TableHasColumn(normalizedTable, "UpdatedBy") Then assignments.Add("[UpdatedBy] = @UserID")
                If TableHasColumn(normalizedTable, "UpdatedOn") Then assignments.Add("[UpdatedOn] = GETDATE()")

                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo." & QuoteGeneratedIdentifier(normalizedTable) &
                        " SET " & String.Join(", ", assignments) &
                        " WHERE " & QuoteGeneratedIdentifier(primaryKey) & " = @RecordID", conn)
                        cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = If(userId > 0, CType(userId, Object), DBNull.Value)

                        If cmd.ExecuteNonQuery() <> 1 Then
                            Return "The record was not found. It may already have been deleted."
                        End If
                    End Using
                End Using

                LogUpdateAudit(If(String.IsNullOrWhiteSpace(pageName), "FW_Base_B", pageName),
                               normalizedTable,
                               "Delete",
                               "AfterSave",
                               recordId.ToString(CultureInfo.InvariantCulture),
                               String.Empty,
                               True)

                Return String.Empty
            Catch ex As Exception
                Return ex.Message
            End Try
        End Function

        Public Shared Function GetGeneratedPageSchema(tableName As String) As DataTable
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 0 * FROM dbo." & QuoteGeneratedIdentifier(tableName), conn)
                    Using adapter As New SqlDataAdapter(cmd)
                        Dim table As New DataTable(tableName)
                        adapter.Fill(table)
                        Return table
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function LoadGeneratedPageRecord(tableName As String, primaryKey As String, recordId As Integer) As DataRow
            If recordId <= 0 Then Return Nothing

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT * FROM dbo." & QuoteGeneratedIdentifier(tableName) & " WHERE " & QuoteGeneratedIdentifier(primaryKey) & " = @RecordID", conn)
                    cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                    Using adapter As New SqlDataAdapter(cmd)
                        Dim table As New DataTable(tableName)
                        adapter.Fill(table)
                        Return If(table.Rows.Count = 0, Nothing, table.Rows(0))
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function SaveGeneratedPageRecord(tableName As String,
                                                        primaryKey As String,
                                                        recordId As Integer,
                                                        values As Dictionary(Of String, Object),
                                                        originalRowVersion As Byte(),
                                                        userId As Integer) As Boolean
            SaveGeneratedPageRecordWithId(tableName, primaryKey, recordId, values, originalRowVersion, userId)
            Return True
        End Function

        ''' <summary>
        ''' Original signature, kept so pages generated before TrySaveGeneratedPageRecord existed
        ''' keep behaving exactly as they did: a concurrency conflict throws.
        ''' </summary>
        Public Shared Function SaveGeneratedPageRecordWithId(tableName As String,
                                                             primaryKey As String,
                                                             recordId As Integer,
                                                             values As Dictionary(Of String, Object),
                                                             originalRowVersion As Byte(),
                                                             userId As Integer) As Integer
            Dim outcome As SaveResult
            Dim savedId = TrySaveGeneratedPageRecord(tableName, primaryKey, recordId, values, originalRowVersion, userId, outcome)

            If outcome = SaveResult.RecordChanged OrElse outcome = SaveResult.RecordDeleted Then
                Throw New InvalidOperationException("THE RECORD WAS CHANGED OR DELETED BEFORE IT COULD BE SAVED. RELOAD THE RECORD AND TRY AGAIN.")
            End If

            Return savedId
        End Function

        ''' <summary>
        ''' Saves a generated page's record and reports the outcome instead of throwing, so the
        ''' page can tell a concurrency conflict apart from a plain failure and put the choice to
        ''' the user - reload, cancel, or overwrite - as the save contract requires.
        ''' </summary>
        Public Shared Function TrySaveGeneratedPageRecord(tableName As String,
                                                          primaryKey As String,
                                                          recordId As Integer,
                                                          values As Dictionary(Of String, Object),
                                                          originalRowVersion As Byte(),
                                                          userId As Integer,
                                                          ByRef outcome As SaveResult) As Integer
            outcome = SaveResult.Succeeded
            Dim schema = GetGeneratedPageSchema(tableName)
            Dim writableValues = values.Where(Function(pair) schema.Columns.Contains(pair.Key) AndAlso
                                                       Not String.Equals(pair.Key, primaryKey, StringComparison.OrdinalIgnoreCase) AndAlso
                                                       Not String.Equals(pair.Key, "RowVersion", StringComparison.OrdinalIgnoreCase)).ToList()
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                If recordId <= 0 Then
                    If schema.Columns.Contains("RegistrationID") AndAlso
                       Not String.Equals(tableName.Trim(), "FW_Registration", StringComparison.OrdinalIgnoreCase) AndAlso
                       SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso SessionState.Current.Value.RegistrationID > 0 Then
                        Dim registrationValue = writableValues.FirstOrDefault(Function(pair) String.Equals(pair.Key, "RegistrationID", StringComparison.OrdinalIgnoreCase)).Value
                        Dim explicitRegistrationId As Integer
                        If registrationValue Is Nothing OrElse Not Integer.TryParse(Convert.ToString(registrationValue, CultureInfo.InvariantCulture), explicitRegistrationId) OrElse explicitRegistrationId <= 0 Then
                            writableValues.RemoveAll(Function(pair) String.Equals(pair.Key, "RegistrationID", StringComparison.OrdinalIgnoreCase))
                            writableValues.Add(New KeyValuePair(Of String, Object)("RegistrationID", SessionState.Current.Value.RegistrationID))
                        End If
                    End If
                    AddMissingGeneratedInsertValues(schema, writableValues)
                    Dim columns = writableValues.Select(Function(pair) QuoteGeneratedIdentifier(pair.Key)).ToList()
                    Dim parameters = writableValues.Select(Function(pair, index) "@Value" & index.ToString(CultureInfo.InvariantCulture)).ToList()
                    If schema.Columns.Contains("CreatedBy") Then
                        columns.Add("[CreatedBy]")
                        parameters.Add("@CreatedBy")
                    End If
                    If schema.Columns.Contains("CreatedOn") Then
                        columns.Add("[CreatedOn]")
                        parameters.Add("GETDATE()")
                    End If
                    Using cmd As New SqlCommand("INSERT INTO dbo." & QuoteGeneratedIdentifier(tableName) & " (" & String.Join(", ", columns) & ") VALUES (" & String.Join(", ", parameters) & ")", conn)
                        AddGeneratedParameters(cmd, writableValues, schema)
                        If schema.Columns.Contains("CreatedBy") Then cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = userId
                        cmd.CommandText &= "; SELECT CAST(SCOPE_IDENTITY() AS INT);"
                        Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                    End Using
                End If

                Dim assignments = writableValues.Select(Function(pair, index) QuoteGeneratedIdentifier(pair.Key) & " = @Value" & index.ToString(CultureInfo.InvariantCulture)).ToList()
                If schema.Columns.Contains("UpdatedBy") Then assignments.Add("[UpdatedBy] = @UpdatedBy")
                If schema.Columns.Contains("UpdatedOn") Then assignments.Add("[UpdatedOn] = GETDATE()")
                Dim whereClause = QuoteGeneratedIdentifier(primaryKey) & " = @RecordID"
                If schema.Columns.Contains("RowVersion") Then whereClause &= " AND [RowVersion] = @RowVersion"
                Using cmd As New SqlCommand("UPDATE dbo." & QuoteGeneratedIdentifier(tableName) & " SET " & String.Join(", ", assignments) & " WHERE " & whereClause, conn)
                    AddGeneratedParameters(cmd, writableValues, schema)
                    cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                    If schema.Columns.Contains("UpdatedBy") Then cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = userId
                    If schema.Columns.Contains("RowVersion") Then cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = If(originalRowVersion, New Byte() {})
                    If cmd.ExecuteNonQuery() <> 1 Then
                        ' No row matched: either the RowVersion moved on or the record is gone.
                        ' Distinguishing the two is what lets the page offer an overwrite for one
                        ' and refuse it for the other.
                        outcome = If(GeneratedPageRecordExists(conn, tableName, primaryKey, recordId),
                                     SaveResult.RecordChanged,
                                     SaveResult.RecordDeleted)
                        Return 0
                    End If

                    Return recordId
                End Using
            End Using
        End Function

        Private Shared Sub AddMissingGeneratedInsertValues(schema As DataTable, values As List(Of KeyValuePair(Of String, Object)))
            Dim existing = New HashSet(Of String)(values.Select(Function(pair) pair.Key), StringComparer.OrdinalIgnoreCase)
            Dim firstName = GetGeneratedValue(values, "FirstName")
            Dim lastName = GetGeneratedValue(values, "LastName")
            For Each column As DataColumn In schema.Columns
                     If column.AutoIncrement OrElse column.AllowDBNull OrElse
                         (column.DefaultValue IsNot Nothing AndAlso Not Convert.IsDBNull(column.DefaultValue)) OrElse
                   String.Equals(column.ColumnName, "RowVersion", StringComparison.OrdinalIgnoreCase) OrElse
                   existing.Contains(column.ColumnName) Then
                    Continue For
                End If

                Dim value As Object
                If String.Equals(column.ColumnName, "RegistrationID", StringComparison.OrdinalIgnoreCase) AndAlso
                   SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso SessionState.Current.Value.RegistrationID > 0 Then
                    value = SessionState.Current.Value.RegistrationID
                ElseIf String.Equals(column.ColumnName, "FirstLast", StringComparison.OrdinalIgnoreCase) Then
                    value = String.Join(" ", {firstName, lastName}.Where(Function(item) item <> String.Empty))
                ElseIf String.Equals(column.ColumnName, "LastFirst", StringComparison.OrdinalIgnoreCase) Then
                    value = String.Join(" ", {lastName, firstName}.Where(Function(item) item <> String.Empty))
                Else
                    value = GeneratedDefaultValue(column.DataType)
                End If
                values.Add(New KeyValuePair(Of String, Object)(column.ColumnName, value))
                existing.Add(column.ColumnName)
            Next
        End Sub

        Private Shared Function GetGeneratedValue(values As List(Of KeyValuePair(Of String, Object)), fieldName As String) As String
            Dim match = values.FirstOrDefault(Function(pair) String.Equals(pair.Key, fieldName, StringComparison.OrdinalIgnoreCase))
            Return If(match.Value Is Nothing OrElse Convert.IsDBNull(match.Value), String.Empty, Convert.ToString(match.Value, CultureInfo.InvariantCulture)).Trim()
        End Function

        Private Shared Function GeneratedDefaultValue(dataType As Type) As Object
            If dataType Is GetType(String) OrElse dataType Is GetType(Char) Then Return String.Empty
            If dataType Is GetType(Boolean) Then Return False
            If dataType Is GetType(DateTime) OrElse dataType Is GetType(DateTimeOffset) Then Return DateTime.Now
            If dataType Is GetType(Byte) Then Return CByte(0)
            If dataType Is GetType(Short) Then Return CShort(0)
            If dataType Is GetType(Integer) Then Return 0
            If dataType Is GetType(Long) Then Return CLng(0)
            If dataType Is GetType(Decimal) Then Return Decimal.Zero
            If dataType Is GetType(Double) Then Return 0.0R
            If dataType Is GetType(Single) Then Return 0.0F
            Return String.Empty
        End Function

        Private Shared Sub AddGeneratedParameters(command As SqlCommand,
                                                   values As List(Of KeyValuePair(Of String, Object)),
                                                   schema As DataTable)
            For index As Integer = 0 To values.Count - 1
                Dim column = schema.Columns(values(index).Key)
                Dim parameter = command.Parameters.Add("@Value" & index.ToString(CultureInfo.InvariantCulture), SqlTypeFor(column.DataType))
                parameter.Value = If(values(index).Value Is Nothing, DBNull.Value, values(index).Value)
            Next
        End Sub

        Private Shared Function SqlTypeFor(dataType As Type) As SqlDbType
            If dataType Is GetType(String) OrElse dataType Is GetType(Char) Then Return SqlDbType.VarChar
            If dataType Is GetType(Integer) Then Return SqlDbType.Int
            If dataType Is GetType(Short) Then Return SqlDbType.SmallInt
            If dataType Is GetType(Long) Then Return SqlDbType.BigInt
            If dataType Is GetType(Boolean) Then Return SqlDbType.Bit
            If dataType Is GetType(DateTime) Then Return SqlDbType.DateTime
            If dataType Is GetType(DateTimeOffset) Then Return SqlDbType.DateTimeOffset
            If dataType Is GetType(DateOnly) Then Return SqlDbType.Date
            If dataType Is GetType(Decimal) Then Return SqlDbType.Decimal
            If dataType Is GetType(Double) Then Return SqlDbType.Float
            If dataType Is GetType(Single) Then Return SqlDbType.Real
            If dataType Is GetType(Byte()) Then Return SqlDbType.VarBinary
            Return SqlDbType.Variant
        End Function

        Private Shared Function QuoteGeneratedIdentifier(value As String) As String
            If String.IsNullOrWhiteSpace(value) OrElse Not Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$") Then
                Throw New ArgumentException("Invalid database identifier: " & value)
            End If
            Return "[" & value.Replace("]", "]]", StringComparison.Ordinal) & "]"
        End Function

        Public Shared Function ValidatePageGenerationSql(sql As String, registrationId As Integer) As String
            If String.IsNullOrWhiteSpace(sql) OrElse Not sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) Then
                Return "BrowseSql must be a SELECT statement."
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(sql.Trim(), conn)
                        cmd.CommandTimeout = 10
                        If sql.IndexOf("@RegistrationID", StringComparison.OrdinalIgnoreCase) >= 0 Then
                            cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
                        End If

                        Using reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)
                            For index As Integer = 0 To reader.FieldCount - 1
                                If String.Equals(reader.GetName(index), "PK", StringComparison.OrdinalIgnoreCase) Then
                                    Return String.Empty
                                End If
                            Next
                        End Using
                    End Using
                End Using
                Return "BrowseSql must return a column aliased as PK."
            Catch ex As Exception
                Return "SQL validation failed: " & ex.Message
            End Try
        End Function

        Public Shared Function SavePageGeneration(isNewRecord As Boolean,
                                                   pageRequestId As Integer,
                                                   values As Dictionary(Of String, Object),
                                                   originalRowVersion As Byte()) As Boolean
            Dim writableColumns = New String() {
                "RequestName", "PageBaseName", "BrowsePageName", "MaintenancePageName", "UnderlyingTableName",
                "UseRegistrationID", "BrowseFields", "MaintenanceFields", "BrowseSql", "LookupFields", "AdminRequiredFields",
                "MenuCaller", "GenerateBrowsePage", "GenerateMaintenancePage", "UseQbeOnly"
            }

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                If isNewRecord Then
                    Dim columnNames = String.Join(", ", writableColumns)
                    Dim parameterNames = String.Join(", ", writableColumns.Select(Function(column) "@" & column))
                    Using cmd As New SqlCommand("INSERT INTO dbo.FW_PageGeneration_B_U (" & columnNames & ", CreatedBy) VALUES (" & parameterNames & ", @CreatedBy)", conn)
                        AddPageGenerationParameters(cmd, values)
                        cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                        cmd.ExecuteNonQuery()
                    End Using
                    Return True
                End If

                Dim assignments = String.Join(", ", writableColumns.Select(Function(column) column & " = @" & column))
                Using cmd As New SqlCommand("UPDATE dbo.FW_PageGeneration_B_U SET " & assignments & ", UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() WHERE PageRequestID = @PageRequestID AND RowVersion = @RowVersion", conn)
                    AddPageGenerationParameters(cmd, values)
                    cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)
                    cmd.Parameters.Add("@PageRequestID", SqlDbType.Int).Value = pageRequestId
                    cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = If(originalRowVersion, New Byte() {})
                    Return cmd.ExecuteNonQuery() = 1
                End Using
            End Using
        End Function

        Private Shared Sub AddPageGenerationParameters(command As SqlCommand, values As Dictionary(Of String, Object))
            For Each pair In values
                Dim parameter = If(String.Equals(pair.Key, "UseRegistrationID", StringComparison.OrdinalIgnoreCase) OrElse
                                   String.Equals(pair.Key, "GenerateBrowsePage", StringComparison.OrdinalIgnoreCase) OrElse
                                   String.Equals(pair.Key, "GenerateMaintenancePage", StringComparison.OrdinalIgnoreCase) OrElse
                                   String.Equals(pair.Key, "UseQbeOnly", StringComparison.OrdinalIgnoreCase),
                                   command.Parameters.Add("@" & pair.Key, SqlDbType.Bit),
                                   command.Parameters.Add("@" & pair.Key, SqlDbType.VarChar, -1))
                parameter.Value = If(pair.Value Is Nothing, DBNull.Value, pair.Value)
            Next
        End Sub

        Public Shared Function GetEntityById(entityId As Integer) As EntityRecord
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, RegistrationID, AssignedManagerID, GenderID, FirstName, MiddleName, LastName, FirstLast, LastFirst, EMail1, Phone1, IsActive, RowVersion " &
                    "FROM dbo.FW_Entity WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", entityId)
                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then
                            Return Nothing
                        End If

                        Dim genderId As Integer = 0
                        If Not IsDBNull(reader("GenderID")) Then
                            genderId = Convert.ToInt32(reader("GenderID"), CultureInfo.InvariantCulture)
                        End If

                        Return New EntityRecord With {
                            .ID = Convert.ToInt32(reader("ID"), CultureInfo.InvariantCulture),
                            .RegistrationID = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture),
                            .AssignedManagerID = Convert.ToInt32(reader("AssignedManagerID"), CultureInfo.InvariantCulture),
                            .GenderID = genderId,
                            .FirstName = SafeString(reader("FirstName")),
                            .MiddleName = SafeString(reader("MiddleName")),
                            .LastName = SafeString(reader("LastName")),
                            .FirstLast = SafeString(reader("FirstLast")),
                            .LastFirst = SafeString(reader("LastFirst")),
                            .EMail1 = SafeString(reader("EMail1")),
                            .Phone1 = SafeString(reader("Phone1")),
                            .IsActive = Convert.ToBoolean(reader("IsActive"), CultureInfo.InvariantCulture),
                            .RowVersion = DirectCast(reader("RowVersion"), Byte())
                        }
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function CreateEntity(record As EntityRecord, currentUserId As Integer) As Integer
            Dim assignedManager = If(record.AssignedManagerID > 0, record.AssignedManagerID, currentUserId)
            Dim genderIdValue As Object = If(record.GenderID > 0, CType(record.GenderID, Object), DBNull.Value)

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Entity " &
                    "(RegistrationID, AssignedManagerID, GenderID, FirstName, MiddleName, LastName, EMail1, Phone1, IsActive, BDAcknowledged, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                    "VALUES " &
                    "(@RegistrationID, @AssignedManager, @GenderID, @FirstName, @MiddleName, @LastName, @EMail1, @Phone1, @IsActive, 0, @CurrentUserId, GETDATE(), @CurrentUserId, GETDATE()); " &
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                    cmd.Parameters.AddWithValue("@AssignedManager", assignedManager)
                    cmd.Parameters.AddWithValue("@GenderID", genderIdValue)
                    cmd.Parameters.AddWithValue("@FirstName", DbValue(record.FirstName))
                    cmd.Parameters.AddWithValue("@MiddleName", DbValue(record.MiddleName))
                    cmd.Parameters.AddWithValue("@LastName", DbValue(record.LastName))
                    cmd.Parameters.AddWithValue("@EMail1", DbValue(record.EMail1))
                    cmd.Parameters.AddWithValue("@Phone1", DbValue(record.Phone1))
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId)

                    Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        Public Shared Function UpdateEntity(record As EntityRecord, currentUserId As Integer) As SaveResult
            If Not TableHasRowVersion("FW_Entity") OrElse record.RowVersion Is Nothing Then
                Return SaveResult.ConcurrencyUnavailable
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Entity SET " &
                    "RegistrationID = @RegistrationID, " &
                    "AssignedManagerID = @AssignedManager, " &
                    "GenderID = @GenderID, " &
                    "FirstName = @FirstName, " &
                    "MiddleName = @MiddleName, " &
                    "LastName = @LastName, " &
                    "EMail1 = @EMail1, " &
                    "Phone1 = @Phone1, " &
                    "IsActive = @IsActive, " &
                    "UpdatedBy = @CurrentUserId, " &
                    "UpdatedOn = GETDATE() " &
                    "WHERE ID = @ID AND RowVersion = @OriginalRowVersion", conn)

                    cmd.Parameters.AddWithValue("@ID", record.ID)
                    cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                    cmd.Parameters.AddWithValue("@AssignedManager", record.AssignedManagerID)
                    cmd.Parameters.AddWithValue("@GenderID", If(record.GenderID > 0, CType(record.GenderID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@FirstName", DbValue(record.FirstName))
                    cmd.Parameters.AddWithValue("@MiddleName", DbValue(record.MiddleName))
                    cmd.Parameters.AddWithValue("@LastName", DbValue(record.LastName))
                    cmd.Parameters.AddWithValue("@EMail1", DbValue(record.EMail1))
                    cmd.Parameters.AddWithValue("@Phone1", DbValue(record.Phone1))
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId)
                    cmd.Parameters.Add("@OriginalRowVersion", SqlDbType.Timestamp).Value = record.RowVersion

                    If cmd.ExecuteNonQuery() = 0 Then
                        Return SaveResult.RecordChanged
                    End If
                End Using
            End Using
            Return SaveResult.Succeeded
        End Function

        Public Shared Sub DeleteEntity(entityId As Integer,
                                       Optional updatedBy As Integer = 0,
                                       Optional sourcePageName As String = "FW_Base_B")
            Dim auditPageName = If(String.IsNullOrWhiteSpace(sourcePageName), "FW_Base_B", sourcePageName.Trim())

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Entity", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Entity " &
                        "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", entityId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit(auditPageName,
                                   "FW_Entity",
                                   "Delete",
                                   "AfterSave",
                                   entityId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Soft deleted entity record."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    Return
                End If

                Using cmd As New SqlCommand("DELETE FROM dbo.FW_Entity WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", entityId)
                    cmd.ExecuteNonQuery()
                End Using

                LogUpdateAudit(auditPageName,
                               "FW_Entity",
                               "Delete",
                               "AfterSave",
                               entityId.ToString(CultureInfo.InvariantCulture),
                               BuildSoftDeleteAuditSnapshotJson("Deleted entity record."),
                               True,
                               Nothing,
                               updatedBy)
            End Using
        End Sub

        Public Shared Sub DeleteUser(userId As Integer, Optional updatedBy As Integer = 0)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Users", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Users " &
                        "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE UserID = @UserID", conn)
                        cmd.Parameters.AddWithValue("@UserID", userId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit("Users_AppAdmin_B",
                                   "FW_Users",
                                   "Delete",
                                   "AfterSave",
                                   userId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Soft deleted user record."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    Return
                End If

                Using cmd As New SqlCommand("DELETE FROM dbo.FW_Users WHERE UserID = @UserID", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.ExecuteNonQuery()
                End Using

                LogUpdateAudit("Users_AppAdmin_B",
                               "FW_Users",
                               "Delete",
                               "AfterSave",
                               userId.ToString(CultureInfo.InvariantCulture),
                               BuildSoftDeleteAuditSnapshotJson("Deleted user record."),
                               True,
                               Nothing,
                               updatedBy)
            End Using
        End Sub

        Public Shared Sub RestoreEntity(entityId As Integer,
                                        Optional updatedBy As Integer = 0,
                                        Optional sourcePageName As String = "FW_Base_B")
            Dim auditPageName = If(String.IsNullOrWhiteSpace(sourcePageName), "FW_Base_B", sourcePageName.Trim())

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Entity", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Entity " &
                        "SET IsActive = 1, DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", entityId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit(auditPageName,
                                   "FW_Entity",
                                   "Restore",
                                   "AfterSave",
                                   entityId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Restored entity record."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    Return
                End If

                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Entity SET IsActive = 1, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", entityId)
                    cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                    cmd.ExecuteNonQuery()
                End Using

                LogUpdateAudit(auditPageName,
                               "FW_Entity",
                               "Restore",
                               "AfterSave",
                               entityId.ToString(CultureInfo.InvariantCulture),
                               BuildSoftDeleteAuditSnapshotJson("Restored entity record."),
                               True,
                               Nothing,
                               updatedBy)
            End Using
        End Sub

        Public Shared Sub RestoreUser(userId As Integer, Optional updatedBy As Integer = 0)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Users", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Users " &
                        "SET IsActive = 1, DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE UserID = @UserID", conn)
                        cmd.Parameters.AddWithValue("@UserID", userId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit("Users_AppAdmin_B",
                                   "FW_Users",
                                   "Restore",
                                   "AfterSave",
                                   userId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Restored user record."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    Return
                End If

                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Users SET IsActive = 1, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() WHERE UserID = @UserID", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                    cmd.ExecuteNonQuery()
                End Using

                LogUpdateAudit("Users_AppAdmin_B",
                               "FW_Users",
                               "Restore",
                               "AfterSave",
                               userId.ToString(CultureInfo.InvariantCulture),
                               BuildSoftDeleteAuditSnapshotJson("Restored user record."),
                               True,
                               Nothing,
                               updatedBy)
            End Using
        End Sub

        Public Shared Function TryGetRegistrationContextForUser(userId As Integer, ByRef registrationId As Integer, ByRef registrationName As String) As Boolean
            registrationId = 0
            registrationName = String.Empty

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Return TryGetRegistrationContextFromCurrentUserView(conn, userId, registrationId, registrationName)
                End Using
            Catch
                Return False
            End Try
        End Function

        Public Shared Function TryGetLoginRoleSelectionData(userId As Integer,
                                                           ByRef registrationId As Integer,
                                                           ByRef registrationName As String,
                                                           ByRef licenseEndDate As Nullable(Of Date),
                                                           ByRef assignedRoles As List(Of UserRoleOption)) As Boolean
            registrationId = 0
            registrationName = String.Empty
            licenseEndDate = Nothing
            assignedRoles = New List(Of UserRoleOption)()

            If userId <= 0 Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()

                    Using ctxCmd As New SqlCommand(
                        "SELECT TOP 1 cu.RegistrationID, ISNULL(cu.RegName, '') AS RegistrationName, r.LicenseExpiration_Date AS LicenseValue " &
                        "FROM dbo.vw_FW_CurrentUser cu " &
                        "LEFT JOIN dbo.FW_Registration r ON r.ID = cu.RegistrationID " &
                        "WHERE cu.UserID = @UserID", conn)
                        ctxCmd.Parameters.AddWithValue("@UserID", userId)

                        Using reader = ctxCmd.ExecuteReader()
                            If Not reader.Read() Then
                                Return False
                            End If

                            registrationId = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture)
                            registrationName = SafeString(reader("RegistrationName"))

                            If reader("LicenseValue") IsNot DBNull.Value Then
                                licenseEndDate = CDate(reader("LicenseValue"))
                            End If
                        End Using
                    End Using

                    If registrationId <= 0 Then
                        Return False
                    End If

                    Using roleCmd As New SqlCommand(
                        "SELECT DISTINCT r.ID AS RoleID, r.RoleName, ISNULL(r.DisplayOrder, 0) AS DisplayOrder, " &
                        "       ISNULL(r.Typ_AppAdmin, 0) AS Typ_AppAdmin, " &
                        "       ISNULL(r.Typ_CompanyAdmin, 0) AS Typ_CompanyAdmin, " &
                        "       ISNULL(r.Typ_RW, 0) AS Typ_RW, " &
                        "       ISNULL(r.Typ_RO, 0) AS Typ_RO, " &
                        "       ISNULL(r.Typ_User, 0) AS Typ_User, " &
                        "       ISNULL(r.Typ_OnlyMyRecords, 0) AS Typ_OnlyMyRecords, " &
                        "       ISNULL(r.Can_Create, 0) AS Can_Create, " &
                        "       ISNULL(r.Can_Read, 0) AS Can_Read, " &
                        "       ISNULL(r.Can_Update, 0) AS Can_Update, " &
                        "       ISNULL(r.Can_Delete, 0) AS Can_Delete, " &
                        "       ISNULL(r.Can_ViewAllRecords, 0) AS Can_ViewAllRecords, " &
                        "       ISNULL(r.Can_ViewOnlyMyRecords, 0) AS Can_ViewOnlyMyRecords " &
                        "FROM dbo.FW_UserRoles ur " &
                        "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                        "WHERE ur.UserID = @UserID " &
                        "  AND ur.RegistrationID = @RegistrationID " &
                        "  AND ISNULL(ur.IsActive, 1) = 1 " &
                        "  AND ISNULL(r.IsActive, 1) = 1 " &
                        "ORDER BY ISNULL(r.DisplayOrder, 0), r.RoleName", conn)

                        roleCmd.Parameters.AddWithValue("@UserID", userId)
                        roleCmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                        Using reader = roleCmd.ExecuteReader()
                            While reader.Read()
                                Dim isApplicationAdmin = Convert.ToBoolean(reader("Typ_AppAdmin"))
                                Dim isCompanyAdmin = Convert.ToBoolean(reader("Typ_CompanyAdmin"))
                                Dim isTypRw = Convert.ToBoolean(reader("Typ_RW"))
                                Dim isTypRo = Convert.ToBoolean(reader("Typ_RO"))
                                Dim isTypUser = Convert.ToBoolean(reader("Typ_User"))
                                Dim isTypOnlyMyRecords = Convert.ToBoolean(reader("Typ_OnlyMyRecords"))
                                Dim canCreate = Convert.ToBoolean(reader("Can_Create"))
                                Dim canRead = Convert.ToBoolean(reader("Can_Read"))
                                Dim canUpdate = Convert.ToBoolean(reader("Can_Update"))
                                Dim canDelete = Convert.ToBoolean(reader("Can_Delete"))
                                Dim canViewAll = Convert.ToBoolean(reader("Can_ViewAllRecords"))
                                Dim canViewOnlyMine = Convert.ToBoolean(reader("Can_ViewOnlyMyRecords"))
                                Dim roleType As String = "Custom"

                                If isCompanyAdmin Then
                                    roleType = "Company Admin"
                                ElseIf isApplicationAdmin Then
                                    roleType = "Application Admin"
                                ElseIf isTypOnlyMyRecords Then
                                    roleType = "Owner Only"
                                ElseIf isTypRw Then
                                    roleType = "RW"
                                ElseIf isTypRo Then
                                    roleType = "RO"
                                ElseIf isTypUser Then
                                    roleType = "User"
                                ElseIf canViewOnlyMine AndAlso Not canViewAll Then
                                    roleType = "Owner Only"
                                ElseIf canRead AndAlso (canCreate OrElse canUpdate OrElse canDelete) Then
                                    roleType = "RW"
                                ElseIf canRead AndAlso Not canCreate AndAlso Not canUpdate AndAlso Not canDelete Then
                                    roleType = "RO"
                                End If

                                assignedRoles.Add(New UserRoleOption With {
                                    .RoleID = Convert.ToInt32(reader("RoleID")),
                                    .RoleName = reader("RoleName").ToString(),
                                    .DisplayOrder = Convert.ToInt32(reader("DisplayOrder")),
                                    .RoleType = roleType,
                                    .IsApplicationAdmin = isApplicationAdmin,
                                    .IsCompanyAdmin = isCompanyAdmin
                                })
                            End While
                        End Using
                    End Using
                End Using

                Return True
            Catch
                assignedRoles = New List(Of UserRoleOption)()
                Return False
            End Try
        End Function

        Private Shared Function TryGetRegistrationContextFromCurrentUserView(conn As SqlConnection, userId As Integer, ByRef registrationId As Integer, ByRef registrationName As String) As Boolean
            Using cmd As New SqlCommand("SELECT TOP 1 RegistrationID, ISNULL(RegName, '') AS RegistrationName FROM dbo.vw_FW_CurrentUser WHERE UserID = @UserID", conn)
                cmd.Parameters.AddWithValue("@UserID", userId)

                Using reader = cmd.ExecuteReader()
                    If Not reader.Read() Then
                        Return False
                    End If

                    registrationId = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture)
                    registrationName = SafeString(reader("RegistrationName"))
                    Return registrationId > 0
                End Using
            End Using
        End Function

        Private Shared Function NormalizeEmailForLookup(email As String) As String
            If email Is Nothing Then
                Return String.Empty
            End If

            Return email.Replace(" ", String.Empty).Trim().ToLowerInvariant()
        End Function

        Private Shared Function ValidateComputedHashAgainstStored(emailWithoutSpaces As String, userId As Integer, storedHash As String) As Boolean
            If String.IsNullOrWhiteSpace(storedHash) Then
                Return False
            End If

            Dim candidates As New List(Of String)()
            candidates.Add(ComputeHmacHashAsUnicodeString(emailWithoutSpaces, Encoding.Unicode.GetBytes(userId.ToString(CultureInfo.InvariantCulture)), Encoding.Unicode))
            candidates.Add(ComputeHmacHashAsUnicodeString(emailWithoutSpaces, Encoding.UTF8.GetBytes(userId.ToString(CultureInfo.InvariantCulture)), Encoding.UTF8))

            Dim idBytes = BitConverter.GetBytes(userId)
            candidates.Add(ComputeHmacHashAsUnicodeString(emailWithoutSpaces, idBytes, Encoding.Unicode))
            candidates.Add(ComputeHmacHashAsUnicodeString(emailWithoutSpaces, idBytes, Encoding.UTF8))

            For Each candidate In candidates
                If String.Equals(candidate, storedHash, StringComparison.Ordinal) Then
                    Return True
                End If
            Next

            Return False
        End Function

        ''' <summary>
        ''' What is left in dbo.FW_Users.[Password] once the real password has been hashed into
        ''' PasswordHash. It is a sentinel, not a password: seeing it means "this user has a
        ''' password set and it has not been retyped". Single owner of the value - the maintenance
        ''' page shows this same constant rather than inventing its own placeholder.
        ''' </summary>
        Public Const StoredPasswordMask As String = "#####"

        Public Shared Function ComputePasswordHashForUser(rawPassword As String, userId As Integer) As String
            If userId <= 0 Then
                Return String.Empty
            End If

            Dim normalized = RemoveSpaces(rawPassword)
            If normalized = String.Empty Then
                Return String.Empty
            End If

            Return ComputeHmacHashAsUnicodeString(normalized,
                                                  Encoding.Unicode.GetBytes(userId.ToString(CultureInfo.InvariantCulture)),
                                                  Encoding.Unicode)
        End Function

        Public Shared Function UpdateUserPasswordHash(userId As Integer, rawPassword As String, updatedBy As Integer) As Boolean
            If userId <= 0 Then
                Return False
            End If

            Dim rawValue = If(rawPassword, String.Empty).Trim()
            If rawValue = String.Empty Then
                Return False
            End If

            Dim passwordHash = ComputePasswordHashForUser(rawValue, userId)
            If String.IsNullOrWhiteSpace(passwordHash) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using tx = conn.BeginTransaction()
                        Try
                            Using saveRawCmd As New SqlCommand(
                                "UPDATE dbo.FW_Users " &
                                "SET [Password] = @RawPassword, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE UserID = @UserID", conn, tx)
                                saveRawCmd.Parameters.AddWithValue("@RawPassword", rawValue)
                                saveRawCmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                                saveRawCmd.Parameters.AddWithValue("@UserID", userId)
                                saveRawCmd.ExecuteNonQuery()
                            End Using

                            Dim rowsUpdated As Integer
                            Using saveHashCmd As New SqlCommand(
                                "UPDATE dbo.FW_Users " &
                                "SET PasswordHash = @PasswordHash, [Password] = @PasswordMask, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE UserID = @UserID", conn, tx)
                                saveHashCmd.Parameters.AddWithValue("@PasswordHash", passwordHash)
                                saveHashCmd.Parameters.AddWithValue("@PasswordMask", StoredPasswordMask)
                                saveHashCmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                                saveHashCmd.Parameters.AddWithValue("@UserID", userId)
                                rowsUpdated = saveHashCmd.ExecuteNonQuery()
                            End Using

                            tx.Commit()
                            Return rowsUpdated > 0
                        Catch
                            tx.Rollback()
                            Return False
                        End Try
                    End Using
                End Using
            Catch
                Return False
            End Try
        End Function

        Private Shared Function ComputeHmacHashAsUnicodeString(emailWithoutSpaces As String, keyBytes As Byte(), messageEncoding As Encoding) As String
            Return Encoding.Unicode.GetString(ComputeHmacHashBytes(emailWithoutSpaces, keyBytes, messageEncoding))
        End Function

        ''' <summary>Single implementation of the keyed hash. Both representations below use it.</summary>
        Private Shared Function ComputeHmacHashBytes(message As String, keyBytes As Byte(), messageEncoding As Encoding) As Byte()
            Using hmac As New HMACSHA512(keyBytes)
                Return hmac.ComputeHash(messageEncoding.GetBytes(message))
            End Using
        End Function

        ''' <summary>
        ''' The same HMAC-SHA512 the user password path uses, rendered as hex.
        '''
        ''' Stored password hashes read the raw bytes back as a Unicode string, which is the
        ''' database's existing contract but cannot be written as a source literal. Hex is the same
        ''' hash in a form that can be compared against a compiled-in value.
        ''' </summary>
        Public Shared Function ComputeKeyedHashHex(value As String, key As String) As String
            Dim hashBytes = ComputeHmacHashBytes(RemoveSpaces(value),
                                                 Encoding.Unicode.GetBytes(If(key, String.Empty)),
                                                 Encoding.Unicode)
            Return Convert.ToHexString(hashBytes).ToLowerInvariant()
        End Function

        Private Shared Function RemoveSpaces(value As String) As String
            If value Is Nothing Then
                Return String.Empty
            End If

            Return value.Replace(" ", String.Empty).Trim()
        End Function

        Private Shared Function DbValue(value As String) As Object
            If value Is Nothing OrElse value.Trim() = String.Empty Then
                Return DBNull.Value
            End If

            Return value.Trim()
        End Function

        Private Shared Function DbValueBounded(value As String, maxLength As Integer) As Object
            If value Is Nothing Then
                Return DBNull.Value
            End If

            Dim trimmed = value.Trim()
            If trimmed = String.Empty Then
                Return DBNull.Value
            End If

            If maxLength > 0 AndAlso trimmed.Length > maxLength Then
                trimmed = trimmed.Substring(0, maxLength)
            End If

            Return trimmed
        End Function

        Private Shared Function SafeString(value As Object) As String
            If value Is Nothing OrElse IsDBNull(value) Then
                Return String.Empty
            End If

            Return value.ToString()
        End Function

        Private Shared Function IsNumeric(value As Object) As Boolean
            If value Is Nothing OrElse IsDBNull(value) Then
                Return False
            End If

            Return IsNumeric(value.GetType()) AndAlso Integer.TryParse(value.ToString(), Nothing)
        End Function

        Private Shared Function IsNumeric(valueType As Type) As Boolean
            If valueType Is Nothing Then
                Return False
            End If

            Return valueType Is GetType(Byte) OrElse
                   valueType Is GetType(Int16) OrElse
                   valueType Is GetType(Int32) OrElse
                   valueType Is GetType(Int64)
        End Function

        Private Shared Function BuildTextFilterValue(value As String, comparisonOperator As QbeComparisonOperator) As String
            Select Case comparisonOperator
                Case QbeComparisonOperator.StartsWith
                    Return value & "%"
                Case QbeComparisonOperator.EndsWith
                    Return "%" & value
                Case QbeComparisonOperator.Contains
                    Return "%" & value & "%"
                Case Else
                    Return value
            End Select
        End Function

        Private Shared Function GetSqlOperator(comparisonOperator As QbeComparisonOperator, fieldKind As QbeFieldKind) As String
            Select Case fieldKind
                Case QbeFieldKind.NumericField, QbeFieldKind.DateField
                    Select Case comparisonOperator
                        Case QbeComparisonOperator.NotEquals
                            Return "<>"
                        Case QbeComparisonOperator.GreaterThan
                            Return ">"
                        Case QbeComparisonOperator.GreaterThanOrEqual
                            Return ">="
                        Case QbeComparisonOperator.LessThan
                            Return "<"
                        Case QbeComparisonOperator.LessThanOrEqual
                            Return "<="
                        Case Else
                            Return "="
                    End Select
                Case QbeFieldKind.BooleanField
                    Select Case comparisonOperator
                        Case QbeComparisonOperator.NotEquals
                            Return "<>"
                        Case Else
                            Return "="
                    End Select
                Case Else
                    Select Case comparisonOperator
                        Case QbeComparisonOperator.NotEquals
                            Return "<>"
                        Case QbeComparisonOperator.Contains, QbeComparisonOperator.StartsWith, QbeComparisonOperator.EndsWith
                            Return "LIKE"
                        Case Else
                            Return "="
                    End Select
            End Select
        End Function

        Private Shared Function TryParseBooleanFilter(value As String, ByRef parsed As Boolean) As Boolean
            Dim normalized = value.Trim().ToLowerInvariant()

            Select Case normalized
                Case "1", "true", "yes", "y"
                    parsed = True
                    Return True
                Case "0", "false", "no", "n"
                    parsed = False
                    Return True
                Case Else
                    Return False
            End Select
        End Function

        Public Shared Sub EnumeratePageControls(form As System.Windows.Forms.Form, pageName As String, userId As Integer, Optional sql As String = Nothing)
            If form Is Nothing OrElse String.IsNullOrWhiteSpace(pageName) Then
                Return
            End If

            Try
                ClearPageEnumerations(pageName)

                ' Get SQL and extract column-to-table mappings
                Dim actualSql = sql
                If String.IsNullOrWhiteSpace(actualSql) Then
                    Dim sqlTextBox = form.Controls.OfType(Of System.Windows.Forms.TextBox)().FirstOrDefault(Function(tb) tb.Name = "sqlTextBox")
                    If sqlTextBox IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(sqlTextBox.Text) Then
                        actualSql = sqlTextBox.Text.Trim()
                    End If
                End If

                ' Extract column-to-field mappings from SELECT clause
                Dim columnMappings = ExtractColumnMappingsFromSql(actualSql)
                Dim primaryTableName = ExtractPrimaryTableFromSql(actualSql)

                Dim enumerations As New List(Of Tuple(Of String, String, String, String))()
                EnumerateControlsRecursive(form, enumerations, primaryTableName, columnMappings)

                For Each enumeration In enumerations
                    InsertEnumeration(pageName, enumeration.Item1, enumeration.Item2, enumeration.Item3, String.Empty, userId)
                Next
            Catch ex As Exception
                System.Windows.Forms.MessageBox.Show("Enumeration failed: " & ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error)
            End Try
        End Sub

        Public Shared Sub EnumeratePageControls_U(form As System.Windows.Forms.Form, pageName As String, Optional tableNameOverride As String = Nothing)
            If form Is Nothing OrElse String.IsNullOrWhiteSpace(pageName) Then
                Return
            End If

            Try
                Dim userId = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)

                ClearPageEnumerations_U(pageName)

                ' Derive table name from page name: Entity_U -> FW_Entity, Roles_U -> FW_Roles
                ' Use override if provided
                Dim tableName = If(Not String.IsNullOrWhiteSpace(tableNameOverride),
                    tableNameOverride,
                    If(pageName.EndsWith("_U"), "FW_" & pageName.Substring(0, pageName.Length - 2), "FW_" & pageName))

                ' Simple enumeration: just walk all controls with table name for FileLink formatting
                Dim enumerations As New List(Of Tuple(Of String, String, String, String))()
                Dim emptyMappings As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                EnumerateControlsRecursive(form, enumerations, tableName, emptyMappings, isForUPages:=True)

                For Each enumeration In enumerations
                    If Not String.IsNullOrWhiteSpace(enumeration.Item3) Then  ' Only insert if FileLink is populated
                        InsertEnumeration_U(pageName, enumeration.Item1, enumeration.Item2, enumeration.Item3, enumeration.Item4, userId)
                    End If
                Next
            Catch ex As Exception
                System.Windows.Forms.MessageBox.Show("Enumeration failed: " & ex.Message, "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error)
            End Try
        End Sub

        Private Shared Function ExtractColumnMappingsFromSql(sql As String) As Dictionary(Of String, String)
            Dim mappings As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

            If String.IsNullOrWhiteSpace(sql) Then
                Return mappings
            End If

            Try
                ' Extract SELECT clause
                Dim selectMatch = System.Text.RegularExpressions.Regex.Match(sql, "(?i)SELECT\s+(.*?)\s+FROM", System.Text.RegularExpressions.RegexOptions.Singleline)
                If Not selectMatch.Success Then
                    Return mappings
                End If

                Dim selectClause = selectMatch.Groups(1).Value
                
                ' Split by comma to get individual column selections
                Dim columnSelections = selectClause.Split(","c)
                
                For Each selection In columnSelections
                    Dim col = selection.Trim()
                    If String.IsNullOrWhiteSpace(col) Then
                        Continue For
                    End If

                    ' Parse "TableName.FieldName" or "TableName.FieldName AS ColumnName"
                    Dim parts = System.Text.RegularExpressions.Regex.Match(col, "(?i)^([a-zA-Z_]\w*)\.([a-zA-Z_]\w*)(?:\s+AS\s+([a-zA-Z_]\w*))?$")
                    
                    If parts.Success Then
                        Dim tableName = parts.Groups(1).Value.Trim()
                        Dim fieldName = parts.Groups(2).Value.Trim()
                        Dim columnAlias = If(String.IsNullOrWhiteSpace(parts.Groups(3).Value), fieldName, parts.Groups(3).Value.Trim())
                        
                        Dim fileLink = tableName & "." & fieldName
                        If Not mappings.ContainsKey(columnAlias) Then
                            mappings(columnAlias) = fileLink
                        End If
                    End If
                Next
            Catch
                ' If parsing fails, return empty mappings
            End Try

            Return mappings
        End Function

        Private Shared Function ExtractPrimaryTableFromSql(sql As String) As String
            If String.IsNullOrWhiteSpace(sql) Then
                Return String.Empty
            End If

            Try
                Dim pattern = "(?i)\bFROM\s+([a-zA-Z_]\w*(?:\.[a-zA-Z_]\w*)?)"
                Dim match = System.Text.RegularExpressions.Regex.Match(sql, pattern)
                
                If match.Success Then
                    Dim tableName = match.Groups(1).Value.Trim()
                    
                    ' Extract table name from "schema.TableName" format
                    If tableName.Contains(".") Then
                        Dim parts = tableName.Split("."c)
                        tableName = If(parts.Length > 1, parts(1), parts(0)).Trim()
                    End If
                    
                    Return tableName
                End If
            Catch
                ' If parsing fails, return empty
            End Try

            Return String.Empty
        End Function

        Private Shared Sub EnumerateControlsRecursive(container As System.Windows.Forms.Control, ByRef enumerations As List(Of Tuple(Of String, String, String, String)), primaryTableName As String, columnMappings As Dictionary(Of String, String), Optional isForUPages As Boolean = False)
            If container Is Nothing OrElse container.Controls Is Nothing Then
                Return
            End If

            For Each ctrl As System.Windows.Forms.Control In container.Controls
                If ctrl Is Nothing Then
                    Continue For
                End If

                Dim controlName = If(String.IsNullOrWhiteSpace(ctrl.Name), String.Empty, ctrl.Name)
                Dim controlCaption = If(String.IsNullOrWhiteSpace(ctrl.Text), String.Empty, ctrl.Text)
                Dim fileLink As String = String.Empty

                ' Check for DataBindings
                If ctrl.DataBindings IsNot Nothing AndAlso ctrl.DataBindings.Count > 0 Then
                    For Each binding As System.Windows.Forms.Binding In ctrl.DataBindings
                        If binding IsNot Nothing Then
                            ' Get the actual entity field name from BindingMemberInfo, not the control property name
                            Dim fieldName = If(String.IsNullOrWhiteSpace(binding.BindingMemberInfo.BindingField), String.Empty, binding.BindingMemberInfo.BindingField)
                            
                            ' Check if column has explicit mapping first, otherwise use primary table
                            If columnMappings.ContainsKey(fieldName) Then
                                fileLink = columnMappings(fieldName)
                            ElseIf Not String.IsNullOrWhiteSpace(primaryTableName) AndAlso Not String.IsNullOrWhiteSpace(fieldName) Then
                                fileLink = primaryTableName & "." & fieldName
                            Else
                                fileLink = fieldName
                            End If
                            Exit For
                        End If
                    Next
                End If

                ' Handle DataGridView columns
                Dim dgv = TryCast(ctrl, System.Windows.Forms.DataGridView)
                If dgv IsNot Nothing AndAlso dgv.Columns IsNot Nothing Then
                    For Each col As System.Windows.Forms.DataGridViewColumn In dgv.Columns
                        If col IsNot Nothing Then
                            Dim colName = If(String.IsNullOrWhiteSpace(col.Name), String.Empty, col.Name)
                            Dim colCaption = If(String.IsNullOrWhiteSpace(col.HeaderText), String.Empty, col.HeaderText)
                            Dim colDataProperty = If(String.IsNullOrWhiteSpace(col.DataPropertyName), String.Empty, col.DataPropertyName)
                            
                            ' Store table name in column Tag for later use
                            col.Tag = primaryTableName
                            
                            ' Construct FileLink - check mappings first, then use primary table
                            Dim colFileLink As String = String.Empty
                            If columnMappings.ContainsKey(colDataProperty) Then
                                colFileLink = columnMappings(colDataProperty)
                            ElseIf Not String.IsNullOrWhiteSpace(primaryTableName) AndAlso Not String.IsNullOrWhiteSpace(colDataProperty) Then
                                colFileLink = primaryTableName & "." & colDataProperty
                            Else
                                colFileLink = colDataProperty
                            End If
                            
                            ' Add the column as a separate entry with datagridname.columnname format
                            enumerations.Add(New Tuple(Of String, String, String, String)(controlName & "." & colName, colCaption, colFileLink, String.Empty))
                        End If
                    Next
                End If

                ' Fallback for _U pages: if no FileLink yet but control follows naming convention, derive from control name
                If isForUPages AndAlso String.IsNullOrWhiteSpace(fileLink) AndAlso Not String.IsNullOrWhiteSpace(primaryTableName) AndAlso Not String.IsNullOrWhiteSpace(controlName) Then
                    Dim derivedField As String = String.Empty
                    If controlName.StartsWith("TextBox_") Then : derivedField = controlName.Substring(8)
                    ElseIf controlName.StartsWith("ComboBox_") Then : derivedField = controlName.Substring(9)
                    ElseIf controlName.StartsWith("CheckBox_") Then : derivedField = controlName.Substring(9)
                    ElseIf controlName.StartsWith("DateTimePicker_") Then : derivedField = controlName.Substring(15)
                    ElseIf controlName.StartsWith("NumericUpDown_") Then : derivedField = controlName.Substring(14)
                    ElseIf controlName.StartsWith("MaskedTextBox_") Then : derivedField = controlName.Substring(14)
                    ElseIf controlName.StartsWith("RichTextBox_") Then : derivedField = controlName.Substring(12)
                    ElseIf controlName.StartsWith("RadioButton_") Then : derivedField = controlName.Substring(12)
                    End If
                    If Not String.IsNullOrWhiteSpace(derivedField) Then
                        fileLink = primaryTableName & "." & derivedField
                    End If
                End If

                ' For _U pages: consolidate Label+DataControl into one record, skip the Label row
                ' Supported pairs: Label_X / TextBox_X, ComboBox_X, CheckBox_X, DateTimePicker_X, NumericUpDown_X, MaskedTextBox_X, RichTextBox_X, RadioButton_X
                If isForUPages AndAlso Not String.IsNullOrWhiteSpace(controlName) AndAlso controlName.StartsWith("Label_") Then
                    Dim suffix = controlName.Substring(6)
                    If container.Controls("TextBox_" & suffix) IsNot Nothing OrElse
                       container.Controls("ComboBox_" & suffix) IsNot Nothing OrElse
                       container.Controls("CheckBox_" & suffix) IsNot Nothing OrElse
                       container.Controls("DateTimePicker_" & suffix) IsNot Nothing OrElse
                       container.Controls("NumericUpDown_" & suffix) IsNot Nothing OrElse
                       container.Controls("MaskedTextBox_" & suffix) IsNot Nothing OrElse
                       container.Controls("RichTextBox_" & suffix) IsNot Nothing OrElse
                       container.Controls("RadioButton_" & suffix) IsNot Nothing Then
                        GoTo NextControl
                    End If
                End If

                ' Detect matching Label for data controls (only for _U pages)
                Dim linkedControl As String = String.Empty
                If isForUPages AndAlso Not String.IsNullOrWhiteSpace(controlName) Then
                    Dim suffix As String = String.Empty
                    If controlName.StartsWith("TextBox_") Then
                        suffix = controlName.Substring(8)
                    ElseIf controlName.StartsWith("ComboBox_") Then
                        suffix = controlName.Substring(9)
                    ElseIf controlName.StartsWith("CheckBox_") Then
                        suffix = controlName.Substring(9)
                    ElseIf controlName.StartsWith("DateTimePicker_") Then
                        suffix = controlName.Substring(15)
                    ElseIf controlName.StartsWith("NumericUpDown_") Then
                        suffix = controlName.Substring(14)
                    ElseIf controlName.StartsWith("MaskedTextBox_") Then
                        suffix = controlName.Substring(14)
                    ElseIf controlName.StartsWith("RichTextBox_") Then
                        suffix = controlName.Substring(12)
                    ElseIf controlName.StartsWith("RadioButton_") Then
                        suffix = controlName.Substring(12)
                    End If
                    If Not String.IsNullOrWhiteSpace(suffix) AndAlso container.Controls("Label_" & suffix) IsNot Nothing Then
                        linkedControl = "Label_" & suffix
                    ElseIf Not String.IsNullOrWhiteSpace(suffix) Then
                        ' No paired label — point to self so caption updates always use LinkedControl
                        linkedControl = controlName
                    End If
                End If

                ' Use binding field name as caption for data-bound controls (avoids capturing live data value)
                If isForUPages AndAlso ctrl.DataBindings IsNot Nothing AndAlso ctrl.DataBindings.Count > 0 Then
                    Dim fieldName = ctrl.DataBindings(0).BindingMemberInfo.BindingField
                    If Not String.IsNullOrWhiteSpace(fieldName) Then
                        controlCaption = fieldName
                    End If
                ElseIf isForUPages AndAlso Not String.IsNullOrWhiteSpace(controlName) Then
                    ' No DataBinding — derive caption from control name suffix (e.g. TextBox_FirstName → FirstName)
                    Dim derivedCaption As String = String.Empty
                    If controlName.StartsWith("TextBox_") Then : derivedCaption = controlName.Substring(8)
                    ElseIf controlName.StartsWith("ComboBox_") Then : derivedCaption = controlName.Substring(9)
                    ElseIf controlName.StartsWith("CheckBox_") Then : derivedCaption = controlName.Substring(9)
                    ElseIf controlName.StartsWith("DateTimePicker_") Then : derivedCaption = controlName.Substring(15)
                    ElseIf controlName.StartsWith("NumericUpDown_") Then : derivedCaption = controlName.Substring(14)
                    ElseIf controlName.StartsWith("MaskedTextBox_") Then : derivedCaption = controlName.Substring(14)
                    ElseIf controlName.StartsWith("RichTextBox_") Then : derivedCaption = controlName.Substring(12)
                    End If
                    If Not String.IsNullOrWhiteSpace(derivedCaption) Then
                        controlCaption = derivedCaption
                    End If
                End If

                If Not String.IsNullOrWhiteSpace(controlName) Then
                    enumerations.Add(New Tuple(Of String, String, String, String)(controlName, controlCaption, fileLink, linkedControl))
                ElseIf Not String.IsNullOrWhiteSpace(controlCaption) Then
                    enumerations.Add(New Tuple(Of String, String, String, String)(ctrl.GetType().Name & "_" & controlCaption, controlCaption, fileLink, linkedControl))
                End If

                NextControl:

                ' Recurse for nested containers
                EnumerateControlsRecursive(ctrl, enumerations, primaryTableName, columnMappings, isForUPages)
            Next
        End Sub

        Private Shared Sub ClearPageEnumerations(pageName As String)
            If String.IsNullOrWhiteSpace(pageName) Then
                Return
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("DELETE FROM dbo.FW_Enumerations WHERE PageName = @PageName", conn)
                    cmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Shared Sub ClearPageEnumerations_U(pageName As String)
            If String.IsNullOrWhiteSpace(pageName) Then
                Return
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("DELETE FROM dbo.FW_Enumerations_U WHERE PageName = @PageName", conn)
                    cmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Function GetUsersByRegistration(registrationId As Integer) As DataTable
            Dim table As New DataTable("FW_Users")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserID AS ID, FirstLast FROM dbo.FW_Users " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "ORDER BY FirstLast", conn)
                    
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetAccessDiagnosticUsers(registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticUsers")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserID, ISNULL(FirstLast, '') AS FirstLast FROM dbo.FW_Users " &
                    "WHERE RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0 " &
                    "ORDER BY FirstLast, UserID", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticUsersWithEmail(registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticUsersWithEmail")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserID, ISNULL(Email, '') AS Email, ISNULL(FirstLast, '') AS FirstLast " &
                    "FROM dbo.FW_Users " &
                    "WHERE RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0 " &
                    "AND ISNULL(Email, '') <> '' ORDER BY Email, UserID", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticUserByEmail(email As String, registrationId As Integer) As DataRow
            If String.IsNullOrWhiteSpace(email) OrElse registrationId <= 0 Then
                Return Nothing
            End If

            Dim table As New DataTable("AccessDiagnosticUser")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 UserID, ISNULL(FirstLast, '') AS FirstLast " &
                    "FROM dbo.FW_Users " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "AND ISNULL(DeletedFlag, 0) = 0 " &
                    "AND LOWER(REPLACE(Email, ' ', '')) = LOWER(REPLACE(@Email, ' ', '')) " &
                    "ORDER BY UserID", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@Email", email.Trim())
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return If(table.Rows.Count = 0, Nothing, table.Rows(0))
        End Function

        Public Shared Function GetAccessDiagnosticRoles(userId As Integer, registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticRoles")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DISTINCT r.ID AS RoleID, r.RoleName, ISNULL(r.IsActive, 1) AS IsActive " &
                    "FROM dbo.FW_UserRoles ur INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                    "WHERE ur.UserID = @UserID AND ur.RegistrationID = @RegistrationID " &
                    "AND ISNULL(ur.IsActive, 1) = 1 ORDER BY r.RoleName", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.CommandTimeout = 10
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticRegistrationRoles(userId As Integer, registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticRegistrationRoles")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT r.ID AS RoleID, r.RoleName, ISNULL(r.DisplayOrder, 0) AS DisplayOrder, " &
                    "CASE WHEN EXISTS (SELECT 1 FROM dbo.FW_UserRoles ur " &
                    "WHERE ur.UserID = @UserID AND ur.RoleID = r.ID " &
                    "AND ur.RegistrationID = @RegistrationID AND ISNULL(ur.IsActive, 1) = 1) THEN 1 ELSE 0 END AS IsAssigned " &
                    "FROM dbo.FW_Roles r " &
                    "WHERE r.RegistrationID = @RegistrationID AND ISNULL(r.IsActive, 1) = 1 " &
                    "ORDER BY CASE WHEN ISNULL(r.DisplayOrder, 0) = 0 THEN 1 ELSE 0 END, " &
                    "ISNULL(r.DisplayOrder, 255), r.RoleName", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticPages(registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticPages")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DISTINCT WindowOrPage, DB_Table, ISNULL(Table_Alias, DB_Table) AS Table_Alias " &
                    "FROM dbo.FW_RoleTables WHERE RegistrationID = @RegistrationID OR RegistrationID IS NULL " &
                    "ORDER BY WindowOrPage, DB_Table", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticInaccessiblePages(userId As Integer, registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticInaccessiblePages")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DISTINCT rt.WindowOrPage, rt.DB_Table, ISNULL(rt.Table_Alias, rt.DB_Table) AS Table_Alias, " &
                    "CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.FW_UserRoles ur0 WHERE ur0.UserID = @UserID " &
                    "AND ur0.RegistrationID = @RegistrationID AND ISNULL(ur0.IsActive, 1) = 1) " &
                    "THEN 'MISSING ROLE' ELSE 'ROLE HAS NO READ PERMISSION' END AS DiagnosticReason " &
                    "FROM dbo.FW_RoleTables rt " &
                    "WHERE (rt.RegistrationID = @RegistrationID OR rt.RegistrationID IS NULL) " &
                    "AND NOT EXISTS (" &
                    "SELECT 1 FROM dbo.FW_UserRoles ur " &
                    "INNER JOIN dbo.FW_RoleDetails rd ON rd.RoleID = ur.RoleID " &
                    "AND rd.RegistrationID = @RegistrationID AND rd.DB_Table = rt.DB_Table " &
                    "WHERE ur.UserID = @UserID AND ur.RegistrationID = @RegistrationID " &
                    "AND ISNULL(ur.IsActive, 1) = 1 AND ISNULL(rd.Can_Read, 0) = 1) " &
                    "ORDER BY rt.WindowOrPage, rt.DB_Table", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticRoleDetails(roleId As Integer, registrationId As Integer) As DataTable
            Dim table As New DataTable("AccessDiagnosticRoleDetails")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DB_Table, Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE, " &
                    "Can_ViewAllRecords, Can_ViewOnlyMyRecords FROM dbo.FW_RoleDetails " &
                    "WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.CommandTimeout = 10
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAccessDiagnosticRoleFields(roleId As Integer, registrationId As Integer, schemaId As Integer, dbTable As String) As DataTable
            Dim table As New DataTable("AccessDiagnosticRoleFields")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, FieldName, FriendlyFieldName, OverrideCaption, " &
                    "ISNULL(Can_Create, 0) AS Can_Create, ISNULL(Can_Read, 0) AS Can_Read, ISNULL(Can_Update, 0) AS Can_Update " &
                    "FROM dbo.FW_RoleFields WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID " &
                    "AND SchemaID = @SchemaID AND TableName = @DBTable " &
                    "ORDER BY ISNULL(OrderBy, 9999), FieldName", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                    cmd.Parameters.AddWithValue("@DBTable", dbTable)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Sub UpdateDiagnosticRoleField(id As Integer, overrideCaption As String, canCreate As Boolean, canRead As Boolean, canUpdate As Boolean, updatedBy As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_RoleFields SET OverrideCaption = @OverrideCaption, Can_Create = @CanCreate, " &
                    "Can_Read = @CanRead, Can_Update = @CanUpdate, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", id)
                    cmd.Parameters.AddWithValue("@OverrideCaption", If(String.IsNullOrWhiteSpace(overrideCaption), CType(DBNull.Value, Object), overrideCaption.Trim()))
                    cmd.Parameters.AddWithValue("@CanCreate", canCreate)
                    cmd.Parameters.AddWithValue("@CanRead", canRead)
                    cmd.Parameters.AddWithValue("@CanUpdate", canUpdate)
                    cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
            InvalidateRoleMetadataCache()
        End Sub

        Public Shared Function GetAccessDiagnostic(userId As Integer,
                                                    registrationId As Integer,
                                                    dbTable As String) As DataTable
            Dim table As New DataTable("AccessDiagnostic")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("dbo.usp_FW_AccessDiagnostic", conn)
                    cmd.CommandType = CommandType.StoredProcedure
                    cmd.CommandTimeout = 10
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@DB_Table", If(dbTable, String.Empty))
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function ApplyAccessDiagnosticChanges(userId As Integer,
                                                       registrationId As Integer,
                                                       roleId As Integer,
                                                       schemaId As Integer,
                                                       dbTable As String,
                                                       permissions As IDictionary(Of String, Boolean),
                                                       actorUserId As Integer,
                                                       Optional assignRoleIfMissing As Boolean = True) As DataTable
            Dim trace As New DataTable("AccessDiagnosticApplyTrace")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("dbo.usp_FW_ApplyAccessDiagnosticChanges", conn)
                    cmd.CommandType = CommandType.StoredProcedure
                    cmd.CommandTimeout = 10
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                    cmd.Parameters.AddWithValue("@DB_Table", If(dbTable, String.Empty))
                    For Each permission In New String() {"Can_Create", "Can_Read", "Can_Update", "Can_Delete", "Can_UseQBE", "Can_ViewAllRecords", "Can_ViewOnlyMyRecords"}
                        cmd.Parameters.AddWithValue("@" & permission, permissions(permission))
                    Next
                    cmd.Parameters.AddWithValue("@AssignRoleIfMissing", assignRoleIfMissing)
                    cmd.Parameters.AddWithValue("@ActorUserID", actorUserId)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(trace)
                    End Using
                End Using
            End Using
            Return trace
        End Function

        Public Shared Function GetRoleSchemaIdByTable(dbTable As String) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 1 ID FROM dbo.FW_RoleSchema WHERE DB_Table = @DBTable AND ISNULL(IsActive, 1) = 1 ORDER BY ID", conn)
                    cmd.CommandTimeout = 10
                    cmd.Parameters.AddWithValue("@DBTable", If(dbTable, String.Empty))
                    Dim value = cmd.ExecuteScalar()
                    Return If(value Is Nothing OrElse IsDBNull(value), 0, Convert.ToInt32(value, CultureInfo.InvariantCulture))
                End Using
            End Using
        End Function

        Public Shared Function GetGendersByRegistration(registrationId As Integer) As DataTable
            Dim table As New DataTable("Gender")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, ISNULL(GenderDescription, '') AS GenderDescription " &
                    "FROM dbo.FW_Gender " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "ORDER BY GenderDescription", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetRegistrationTypes() As DataTable
            Dim table As New DataTable("FW_RegistrationType")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT RegTypeID AS ID, RegTypeName AS RegistrationType FROM dbo.FW_RegistrationType ORDER BY RegTypeName", conn)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetRegistrationById(registrationId As Integer) As RegistrationRecord
            If registrationId <= 0 Then
                Return Nothing
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ID, RegName, Smarty_AuthID, Smarty_AuthToken, Smarty_EmbeddedKey, ISNULL(Smarty_UseEmbeddedKey, 0) AS Smarty_UseEmbeddedKey, ISNULL(LTRIM(RTRIM(BusinessRuleType)), '') AS BusinessRuleType, RegTypeId, Address, Address2, City, State, Zip, MainFax, MainPhone, MainEMail, WebLandingPage, " &
                    "ISNULL(DisplayDashboardOnStartUp, 0) AS DisplayDashboardOnStartUp, " &
                    "ISNULL(AllowMessaging, 0) AS AllowMessaging, " &
                    "ISNULL(AllowMultipleRoles, 0) AS AllowMultipleRoles, " &
                    "ISNULL(AllowPasswordChangeAtLogin, 0) AS AllowPasswordChangeAtLogin, " &
                    "ISNULL(AllowUpdateMyProfile, 0) AS AllowUpdateMyProfile, " &
                    "ISNULL(AllowUpdateMyProfileEmail, 0) AS AllowUpdateMyProfileEmail, " &
                    "ISNULL(Ribbonbar_InvisibleIcons, 0) AS Ribbonbar_InvisibleIcons, " &
                    "ISNULL(Use2FA, 0) AS Use2FA, " &
                    "ISNULL(HDUserSupport, 0) AS HDUserSupport, " &
                    "ISNULL(HDApplicationSupport, 0) AS HDApplicationSupport, " &
                    "ISNULL(IsActive, 1) AS IsActive, RowVersion " &
                    "FROM dbo.FW_Registration WHERE ID = @ID", conn)

                    cmd.Parameters.AddWithValue("@ID", registrationId)

                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then
                            Return Nothing
                        End If

                        Return New RegistrationRecord With {
                            .ID = Convert.ToInt32(reader("ID"), CultureInfo.InvariantCulture),
                            .RegName = SafeString(reader("RegName")),
                            .Smarty_AuthID = SafeString(reader("Smarty_AuthID")),
                            .Smarty_AuthToken = SafeString(reader("Smarty_AuthToken")),
                            .Smarty_EmbeddedKey = SafeString(reader("Smarty_EmbeddedKey")),
                            .Smarty_UseEmbeddedKey = Convert.ToBoolean(reader("Smarty_UseEmbeddedKey"), CultureInfo.InvariantCulture),
                            .BusinessRuleType = NormalizeBusinessRuleType(SafeString(reader("BusinessRuleType"))),
                            .RegTypeId = If(IsDBNull(reader("RegTypeId")), 0, Convert.ToInt32(reader("RegTypeId"), CultureInfo.InvariantCulture)),
                            .Address = SafeString(reader("Address")),
                            .Address2 = SafeString(reader("Address2")),
                            .City = SafeString(reader("City")),
                            .State = SafeString(reader("State")),
                            .Zip = SafeString(reader("Zip")),
                            .MainFax = SafeString(reader("MainFax")),
                            .MainPhone = SafeString(reader("MainPhone")),
                            .MainEMail = SafeString(reader("MainEMail")),
                            .WebLandingPage = SafeString(reader("WebLandingPage")),
                            .DisplayDashboardOnStartUp = Convert.ToBoolean(reader("DisplayDashboardOnStartUp"), CultureInfo.InvariantCulture),
                            .AllowMessaging = Convert.ToBoolean(reader("AllowMessaging"), CultureInfo.InvariantCulture),
                            .AllowMultipleRoles = Convert.ToBoolean(reader("AllowMultipleRoles"), CultureInfo.InvariantCulture),
                            .AllowPasswordChangeAtLogin = Convert.ToBoolean(reader("AllowPasswordChangeAtLogin"), CultureInfo.InvariantCulture),
                            .AllowUpdateMyProfile = Convert.ToBoolean(reader("AllowUpdateMyProfile"), CultureInfo.InvariantCulture),
                            .AllowUpdateMyProfileEmail = Convert.ToBoolean(reader("AllowUpdateMyProfileEmail"), CultureInfo.InvariantCulture),
                            .Ribbonbar_InvisibleIcons = Convert.ToBoolean(reader("Ribbonbar_InvisibleIcons"), CultureInfo.InvariantCulture),
                            .Use2FA = Convert.ToBoolean(reader("Use2FA"), CultureInfo.InvariantCulture),
                            .HDUserSupport = Convert.ToInt32(reader("HDUserSupport"), CultureInfo.InvariantCulture),
                            .HDApplicationSupport = Convert.ToInt32(reader("HDApplicationSupport"), CultureInfo.InvariantCulture),
                            .IsActive = Convert.ToBoolean(reader("IsActive"), CultureInfo.InvariantCulture),
                            .RowVersion = DirectCast(reader("RowVersion"), Byte())
                        }
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function GetHelpDeskRouting(registrationId As Integer,
                                                  ByRef userSupportId As Integer,
                                                  ByRef applicationSupportId As Integer) As Boolean
            userSupportId = 0
            applicationSupportId = 0
            If registrationId <= 0 Then Return False

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 1 ISNULL(HDUserSupport, 0), ISNULL(HDApplicationSupport, 0) FROM dbo.FW_Registration WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", registrationId)
                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then Return False
                        userSupportId = Convert.ToInt32(reader(0), CultureInfo.InvariantCulture)
                        applicationSupportId = Convert.ToInt32(reader(1), CultureInfo.InvariantCulture)
                        Return True
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function CreateRegistration(record As RegistrationRecord, currentUserId As Integer) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim normalizedBusinessRuleType = NormalizeBusinessRuleType(record.BusinessRuleType)
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Registration " &
                    "(RegName, BusinessRuleType, RegTypeId, Address, Address2, City, State, Zip, MainFax, MainPhone, MainEMail, WebLandingPage, Smarty_AuthID, Smarty_AuthToken, Smarty_EmbeddedKey, Smarty_UseEmbeddedKey, DisplayDashboardOnStartUp, AllowMessaging, AllowMultipleRoles, AllowPasswordChangeAtLogin, AllowUpdateMyProfile, AllowUpdateMyProfileEmail, Ribbonbar_InvisibleIcons, Use2FA, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                    "VALUES " &
                    "(@RegName, @BusinessRuleType, @RegTypeId, @Address, @Address2, @City, @State, @Zip, @MainFax, @MainPhone, @MainEMail, @WebLandingPage, @Smarty_AuthID, @Smarty_AuthToken, @Smarty_EmbeddedKey, @Smarty_UseEmbeddedKey, @DisplayDashboardOnStartUp, @AllowMessaging, @AllowMultipleRoles, @AllowPasswordChangeAtLogin, @AllowUpdateMyProfile, @AllowUpdateMyProfileEmail, @Ribbonbar_InvisibleIcons, @Use2FA, @IsActive, @CurrentUserId, GETDATE(), @CurrentUserId, GETDATE()); " &
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);", conn)

                    cmd.Parameters.AddWithValue("@RegName", DbValue(record.RegName))
                    cmd.Parameters.AddWithValue("@BusinessRuleType", normalizedBusinessRuleType)
                    cmd.Parameters.AddWithValue("@RegTypeId", If(record.RegTypeId > 0, CType(record.RegTypeId, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@Address", DbValue(record.Address))
                    cmd.Parameters.AddWithValue("@Address2", DbValue(record.Address2))
                    cmd.Parameters.AddWithValue("@City", DbValue(record.City))
                    cmd.Parameters.AddWithValue("@State", DbValue(record.State))
                    cmd.Parameters.AddWithValue("@Zip", DbValue(record.Zip))
                    cmd.Parameters.AddWithValue("@MainFax", DbValue(record.MainFax))
                    cmd.Parameters.AddWithValue("@MainPhone", DbValue(record.MainPhone))
                    cmd.Parameters.AddWithValue("@MainEMail", DbValue(record.MainEMail))
                    cmd.Parameters.AddWithValue("@WebLandingPage", DbValue(record.WebLandingPage))
                    cmd.Parameters.AddWithValue("@Smarty_AuthID", DbValue(record.Smarty_AuthID))
                    cmd.Parameters.AddWithValue("@Smarty_AuthToken", DbValue(record.Smarty_AuthToken))
                    cmd.Parameters.AddWithValue("@Smarty_EmbeddedKey", DbValue(record.Smarty_EmbeddedKey))
                    cmd.Parameters.AddWithValue("@Smarty_UseEmbeddedKey", record.Smarty_UseEmbeddedKey)
                    cmd.Parameters.AddWithValue("@DisplayDashboardOnStartUp", record.DisplayDashboardOnStartUp)
                    cmd.Parameters.AddWithValue("@AllowMessaging", record.AllowMessaging)
                    cmd.Parameters.AddWithValue("@AllowMultipleRoles", record.AllowMultipleRoles)
                    cmd.Parameters.AddWithValue("@AllowPasswordChangeAtLogin", record.AllowPasswordChangeAtLogin)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfile", record.AllowUpdateMyProfile)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfileEmail", record.AllowUpdateMyProfileEmail)
                    cmd.Parameters.AddWithValue("@Ribbonbar_InvisibleIcons", record.Ribbonbar_InvisibleIcons)
                    cmd.Parameters.AddWithValue("@Use2FA", record.Use2FA)
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId)

                    Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        Public Shared Function UpdateRegistration(record As RegistrationRecord, currentUserId As Integer) As SaveResult
            If Not TableHasRowVersion("FW_Registration") OrElse record.RowVersion Is Nothing Then
                Return SaveResult.ConcurrencyUnavailable
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim normalizedBusinessRuleType = NormalizeBusinessRuleType(record.BusinessRuleType)
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Registration SET " &
                    "RegName = @RegName, " &
                    "BusinessRuleType = @BusinessRuleType, " &
                    "RegTypeId = @RegTypeId, " &
                    "Address = @Address, " &
                    "Address2 = @Address2, " &
                    "City = @City, " &
                    "State = @State, " &
                    "Zip = @Zip, " &
                    "MainFax = @MainFax, " &
                    "MainPhone = @MainPhone, " &
                    "MainEMail = @MainEMail, " &
                    "WebLandingPage = @WebLandingPage, " &
                    "Smarty_AuthID = @Smarty_AuthID, " &
                    "Smarty_AuthToken = @Smarty_AuthToken, " &
                    "Smarty_EmbeddedKey = @Smarty_EmbeddedKey, " &
                    "Smarty_UseEmbeddedKey = @Smarty_UseEmbeddedKey, " &
                    "DisplayDashboardOnStartUp = @DisplayDashboardOnStartUp, " &
                    "AllowMessaging = @AllowMessaging, " &
                    "AllowMultipleRoles = @AllowMultipleRoles, " &
                    "AllowPasswordChangeAtLogin = @AllowPasswordChangeAtLogin, " &
                    "AllowUpdateMyProfile = @AllowUpdateMyProfile, " &
                    "AllowUpdateMyProfileEmail = @AllowUpdateMyProfileEmail, " &
                    "Ribbonbar_InvisibleIcons = @Ribbonbar_InvisibleIcons, " &
                    "Use2FA = @Use2FA, " &
                    "IsActive = @IsActive, " &
                    "UpdatedBy = @CurrentUserId, " &
                    "UpdatedOn = GETDATE() " &
                    "WHERE ID = @ID AND RowVersion = @OriginalRowVersion", conn)

                    cmd.Parameters.AddWithValue("@ID", record.ID)
                    cmd.Parameters.AddWithValue("@RegName", DbValue(record.RegName))
                    cmd.Parameters.AddWithValue("@BusinessRuleType", normalizedBusinessRuleType)
                    cmd.Parameters.AddWithValue("@RegTypeId", If(record.RegTypeId > 0, CType(record.RegTypeId, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@Address", DbValue(record.Address))
                    cmd.Parameters.AddWithValue("@Address2", DbValue(record.Address2))
                    cmd.Parameters.AddWithValue("@City", DbValue(record.City))
                    cmd.Parameters.AddWithValue("@State", DbValue(record.State))
                    cmd.Parameters.AddWithValue("@Zip", DbValue(record.Zip))
                    cmd.Parameters.AddWithValue("@MainFax", DbValue(record.MainFax))
                    cmd.Parameters.AddWithValue("@MainPhone", DbValue(record.MainPhone))
                    cmd.Parameters.AddWithValue("@MainEMail", DbValue(record.MainEMail))
                    cmd.Parameters.AddWithValue("@WebLandingPage", DbValue(record.WebLandingPage))
                    cmd.Parameters.AddWithValue("@Smarty_AuthID", DbValue(record.Smarty_AuthID))
                    cmd.Parameters.AddWithValue("@Smarty_AuthToken", DbValue(record.Smarty_AuthToken))
                    cmd.Parameters.AddWithValue("@Smarty_EmbeddedKey", DbValue(record.Smarty_EmbeddedKey))
                    cmd.Parameters.AddWithValue("@Smarty_UseEmbeddedKey", record.Smarty_UseEmbeddedKey)
                    cmd.Parameters.AddWithValue("@DisplayDashboardOnStartUp", record.DisplayDashboardOnStartUp)
                    cmd.Parameters.AddWithValue("@AllowMessaging", record.AllowMessaging)
                    cmd.Parameters.AddWithValue("@AllowMultipleRoles", record.AllowMultipleRoles)
                    cmd.Parameters.AddWithValue("@AllowPasswordChangeAtLogin", record.AllowPasswordChangeAtLogin)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfile", record.AllowUpdateMyProfile)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfileEmail", record.AllowUpdateMyProfileEmail)
                    cmd.Parameters.AddWithValue("@Ribbonbar_InvisibleIcons", record.Ribbonbar_InvisibleIcons)
                    cmd.Parameters.AddWithValue("@Use2FA", record.Use2FA)
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId)
                    cmd.Parameters.Add("@OriginalRowVersion", SqlDbType.Timestamp).Value = record.RowVersion

                    If cmd.ExecuteNonQuery() = 0 Then
                        Return SaveResult.RecordChanged
                    End If
                End Using
            End Using
            Return SaveResult.Succeeded
        End Function

        Public Shared Function GetLicenseEndDate(registrationId As Integer) As Nullable(Of Date)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT LicenseExpiration_Date AS LicenseValue FROM dbo.FW_Registration WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", registrationId)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        Return CDate(result)
                    End If
                    Return Nothing
                End Using
            End Using
        End Function

        Public Shared Function GetAllRegistrations() As DataTable
            Dim table As New DataTable("FW_Registration")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT r.ID, r.RegName, ISNULL(rt.RegTypeName, '') AS RegistrationType " &
                    "FROM dbo.FW_Registration r " &
                    "LEFT JOIN dbo.FW_RegistrationType rt ON rt.RegTypeID = r.RegTypeID " &
                    "ORDER BY r.RegName", conn)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetRegistrationBusinessRuleType(registrationId As Integer) As String
            If registrationId <= 0 Then
                LogFallbackUsage("BusinessRuleType_Fallback_DefaultLegacy",
                                 "RegistrationID <= 0. Returning BR_Legacy.",
                                 "GetRegistrationBusinessRuleType",
                                 registrationId)
                Return BR_Legacy
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(LTRIM(RTRIM(BusinessRuleType)), '') FROM dbo.FW_Registration WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", registrationId)
                        Dim result = cmd.ExecuteScalar()
                        Dim rawValue = If(result Is Nothing OrElse IsDBNull(result), String.Empty, result.ToString())
                        Return NormalizeBusinessRuleType(rawValue)
                    End Using
                End Using
            Catch
                LogFallbackUsage("BusinessRuleType_Fallback_DefaultLegacy",
                                 "Exception while reading BusinessRuleType. Returning BR_Legacy.",
                                 "GetRegistrationBusinessRuleType",
                                 registrationId)
                Return BR_Legacy
            End Try
        End Function

        Public Shared Function UpdateRegistrationBusinessRuleType(registrationId As Integer, rawBusinessRuleType As String, updatedBy As Integer) As Boolean
            If registrationId <= 0 Then
                Return False
            End If

            Dim normalized = NormalizeBusinessRuleType(rawBusinessRuleType)

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Registration " &
                        "SET BusinessRuleType = @BusinessRuleType, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@BusinessRuleType", normalized)
                        cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                        cmd.Parameters.AddWithValue("@ID", registrationId)
                        Return cmd.ExecuteNonQuery() > 0
                    End Using
                End Using
            Catch
                Return False
            End Try
        End Function

        Public Shared Sub LogFallbackUsage(fallbackType As String,
                                           Optional details As String = "",
                                           Optional pageName As String = "",
                                           Optional registrationId As Integer? = Nothing,
                                           Optional userId As Integer? = Nothing)
            If String.IsNullOrWhiteSpace(fallbackType) Then
                Return
            End If

            Dim resolvedRegistrationId As Integer? = registrationId
            Dim resolvedUserId As Integer? = userId

            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Dim session = SessionState.Current.Value
                If Not resolvedRegistrationId.HasValue OrElse resolvedRegistrationId.Value <= 0 Then
                    If session.RegistrationID > 0 Then
                        resolvedRegistrationId = session.RegistrationID
                    End If
                End If

                If Not resolvedUserId.HasValue OrElse resolvedUserId.Value <= 0 Then
                    If session.UserID > 0 Then
                        resolvedUserId = session.UserID
                    End If
                End If
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "INSERT INTO dbo.FW_FallbackUsageLog (RegistrationID, UserID, PageName, FallbackType, Details) " &
                        "VALUES (@RegistrationID, @UserID, @PageName, @FallbackType, @Details)", conn)

                        cmd.Parameters.AddWithValue("@RegistrationID", If(resolvedRegistrationId.HasValue AndAlso resolvedRegistrationId.Value > 0, CType(resolvedRegistrationId.Value, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@UserID", If(resolvedUserId.HasValue AndAlso resolvedUserId.Value > 0, CType(resolvedUserId.Value, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@PageName", DbValueBounded(pageName, 100))
                        cmd.Parameters.AddWithValue("@FallbackType", DbValueBounded(fallbackType, 100))
                        cmd.Parameters.AddWithValue("@Details", DbValueBounded(details, 1000))
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch
                ' Logging must never interrupt app flow.
            End Try
        End Sub

        Public Shared Sub LogUpdateAudit(pageName As String,
                                         tableName As String,
                                         operationType As String,
                                         phase As String,
                                         Optional recordKey As String = "",
                                         Optional snapshotJson As String = "",
                                         Optional saveSucceeded As Boolean? = Nothing,
                                         Optional registrationId As Integer? = Nothing,
                                         Optional userId As Integer? = Nothing)
            If String.IsNullOrWhiteSpace(pageName) OrElse String.IsNullOrWhiteSpace(operationType) OrElse String.IsNullOrWhiteSpace(phase) Then
                Return
            End If

            Dim resolvedRegistrationId As Integer? = registrationId
            Dim resolvedUserId As Integer? = userId

            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Dim session = SessionState.Current.Value
                If Not resolvedRegistrationId.HasValue OrElse resolvedRegistrationId.Value <= 0 Then
                    If session.RegistrationID > 0 Then
                        resolvedRegistrationId = session.RegistrationID
                    End If
                End If

                If Not resolvedUserId.HasValue OrElse resolvedUserId.Value <= 0 Then
                    If session.UserID > 0 Then
                        resolvedUserId = session.UserID
                    End If
                End If
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "INSERT INTO dbo.FW_AuditTrail (RegistrationID, UserID, PageName, TableName, OperationType, Phase, RecordKey, SaveSucceeded, SnapshotJson) " &
                        "VALUES (@RegistrationID, @UserID, @PageName, @TableName, @OperationType, @Phase, @RecordKey, @SaveSucceeded, @SnapshotJson)", conn)

                        cmd.Parameters.AddWithValue("@RegistrationID", If(resolvedRegistrationId.HasValue AndAlso resolvedRegistrationId.Value > 0, CType(resolvedRegistrationId.Value, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@UserID", If(resolvedUserId.HasValue AndAlso resolvedUserId.Value > 0, CType(resolvedUserId.Value, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@PageName", DbValueBounded(pageName, 100))
                        cmd.Parameters.AddWithValue("@TableName", DbValueBounded(tableName, 100))
                        cmd.Parameters.AddWithValue("@OperationType", DbValueBounded(operationType, 30))
                        cmd.Parameters.AddWithValue("@Phase", DbValueBounded(phase, 20))
                        cmd.Parameters.AddWithValue("@RecordKey", DbValueBounded(recordKey, 100))
                        cmd.Parameters.AddWithValue("@SaveSucceeded", If(saveSucceeded.HasValue, CType(saveSucceeded.Value, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@SnapshotJson", DbValue(snapshotJson))
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch
                ' Logging must never interrupt app flow.
            End Try
        End Sub

        Private Shared Function BuildSoftDeleteAuditSnapshotJson(message As String) As String
            Dim payload As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
                {"Note", If(String.IsNullOrWhiteSpace(message), "Soft deleted. Check the table.", message.Trim())}
            }

            Return JsonSerializer.Serialize(payload)
        End Function

        Public Shared Function GetUpdateAuditEntries(Optional registrationId As Integer? = Nothing,
                                                     Optional userId As Integer? = Nothing,
                                                     Optional pageName As String = "",
                                                     Optional tableName As String = "",
                                                     Optional operationType As String = "",
                                                     Optional fromDate As DateTime? = Nothing,
                                                     Optional toDate As DateTime? = Nothing,
                                                     Optional changedOnly As Boolean = True,
                                                     Optional showDeletedOnly As Boolean = False) As DataTable
            Dim table As New DataTable("FW_UpdateAuditLogEntries")
            table.Columns.Add("AuditID", GetType(Integer))
            table.Columns.Add("CreatedOn", GetType(DateTime))
            table.Columns.Add("RegistrationID", GetType(Integer))
            table.Columns.Add("UserID", GetType(Integer))
            table.Columns.Add("UserDisplay", GetType(String))
            table.Columns.Add("PageName", GetType(String))
            table.Columns.Add("TableName", GetType(String))
            table.Columns.Add("OperationType", GetType(String))
            table.Columns.Add("RecordKey", GetType(String))
            table.Columns.Add("SaveSucceeded", GetType(Boolean))
            table.Columns.Add("ChangeCount", GetType(Integer))
            table.Columns.Add("ChangedFields", GetType(String))
            table.Columns.Add("BeforeSnapshotJson", GetType(String))
            table.Columns.Add("AfterSnapshotJson", GetType(String))
            table.Columns.Add("DeltaJson", GetType(String))

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim supportsDeletedFlag = TableHasColumn("FW_AuditTrail", "DeletedFlag")

                Dim sql As New StringBuilder()
                sql.Append("SELECT a.UpdateAuditLogID AS AuditID, ")
                sql.Append("ISNULL(a.LoggedOn, GETDATE()) AS CreatedOn, ")
                sql.Append("a.RegistrationID, a.UserID, ")
                sql.Append("ISNULL(u.LastFirst, ISNULL(u.FirstName + ' ' + u.LastName, '')) AS UserDisplay, ")
                sql.Append("ISNULL(a.PageName, '') AS PageName, ")
                sql.Append("ISNULL(a.TableName, '') AS TableName, ")
                sql.Append("ISNULL(a.OperationType, '') AS OperationType, ")
                sql.Append("ISNULL(a.RecordKey, '') AS RecordKey, ")
                sql.Append("ISNULL(a.SaveSucceeded, 0) AS SaveSucceeded, ")
                sql.Append("ISNULL(b.SnapshotJson, '') AS BeforeSnapshotJson, ")
                sql.Append("ISNULL(a.SnapshotJson, '') AS AfterSnapshotJson ")
                sql.Append("FROM dbo.FW_AuditTrail a ")
                sql.Append("LEFT JOIN dbo.FW_Users u ON u.UserID = a.UserID ")
                sql.Append("OUTER APPLY ( ")
                sql.Append("  SELECT TOP 1 x.SnapshotJson ")
                sql.Append("  FROM dbo.FW_AuditTrail x ")
                sql.Append("  WHERE x.Phase = 'BeforeSave' ")
                sql.Append("    AND x.UpdateAuditLogID < a.UpdateAuditLogID ")
                sql.Append("    AND ISNULL(x.RegistrationID, -1) = ISNULL(a.RegistrationID, -1) ")
                sql.Append("    AND ISNULL(x.UserID, -1) = ISNULL(a.UserID, -1) ")
                sql.Append("    AND ISNULL(x.PageName, '') = ISNULL(a.PageName, '') ")
                sql.Append("    AND ISNULL(x.TableName, '') = ISNULL(a.TableName, '') ")
                sql.Append("    AND ISNULL(x.OperationType, '') = ISNULL(a.OperationType, '') ")
                sql.Append("    AND ISNULL(x.RecordKey, '') = ISNULL(a.RecordKey, '') ")
                sql.Append("  ORDER BY x.UpdateAuditLogID DESC ")
                sql.Append(") b ")
                sql.Append("WHERE a.Phase = 'AfterSave' ")

                If supportsDeletedFlag Then
                    sql.Append("AND ISNULL(a.DeletedFlag, 0) = @ShowDeletedOnly ")
                End If

                If registrationId.HasValue AndAlso registrationId.Value > 0 Then
                    sql.Append("AND a.RegistrationID = @RegistrationID ")
                End If

                If userId.HasValue AndAlso userId.Value > 0 Then
                    sql.Append("AND a.UserID = @UserID ")
                End If

                If Not String.IsNullOrWhiteSpace(pageName) Then
                    sql.Append("AND a.PageName = @PageName ")
                End If

                If Not String.IsNullOrWhiteSpace(tableName) Then
                    sql.Append("AND a.TableName = @TableName ")
                End If

                If Not String.IsNullOrWhiteSpace(operationType) Then
                    sql.Append("AND a.OperationType = @OperationType ")
                End If

                If fromDate.HasValue Then
                    sql.Append("AND ISNULL(a.LoggedOn, GETDATE()) >= @FromDate ")
                End If

                If toDate.HasValue Then
                    sql.Append("AND ISNULL(a.LoggedOn, GETDATE()) < @ToDateExclusive ")
                End If

                sql.Append("ORDER BY a.UpdateAuditLogID DESC")

                Using cmd As New SqlCommand(sql.ToString(), conn)
                    If registrationId.HasValue AndAlso registrationId.Value > 0 Then
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId.Value)
                    End If

                    If userId.HasValue AndAlso userId.Value > 0 Then
                        cmd.Parameters.AddWithValue("@UserID", userId.Value)
                    End If

                    If Not String.IsNullOrWhiteSpace(pageName) Then
                        cmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                    End If

                    If Not String.IsNullOrWhiteSpace(tableName) Then
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())
                    End If

                    If Not String.IsNullOrWhiteSpace(operationType) Then
                        cmd.Parameters.AddWithValue("@OperationType", operationType.Trim())
                    End If

                    If fromDate.HasValue Then
                        cmd.Parameters.AddWithValue("@FromDate", fromDate.Value.Date)
                    End If

                    If toDate.HasValue Then
                        cmd.Parameters.AddWithValue("@ToDateExclusive", toDate.Value.Date.AddDays(1))
                    End If

                    If supportsDeletedFlag Then
                        cmd.Parameters.AddWithValue("@ShowDeletedOnly", If(showDeletedOnly, 1, 0))
                    End If

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim beforeJson = SafeString(reader("BeforeSnapshotJson"))
                            Dim afterJson = SafeString(reader("AfterSnapshotJson"))

                            Dim changedFields As New List(Of String)()
                            Dim deltaJson = BuildSnapshotDeltaJson(beforeJson, afterJson, changedFields)

                            If changedOnly AndAlso changedFields.Count = 0 Then
                                Continue While
                            End If

                            Dim row = table.NewRow()
                            row("AuditID") = Convert.ToInt32(reader("AuditID"), CultureInfo.InvariantCulture)
                            row("CreatedOn") = Convert.ToDateTime(reader("CreatedOn"), CultureInfo.InvariantCulture)
                            row("RegistrationID") = If(reader("RegistrationID") Is DBNull.Value, 0, Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture))
                            row("UserID") = If(reader("UserID") Is DBNull.Value, 0, Convert.ToInt32(reader("UserID"), CultureInfo.InvariantCulture))
                            row("UserDisplay") = SafeString(reader("UserDisplay"))
                            row("PageName") = SafeString(reader("PageName"))
                            row("TableName") = SafeString(reader("TableName"))
                            row("OperationType") = SafeString(reader("OperationType"))
                            row("RecordKey") = SafeString(reader("RecordKey"))
                            row("SaveSucceeded") = Convert.ToBoolean(reader("SaveSucceeded"), CultureInfo.InvariantCulture)
                            row("ChangeCount") = changedFields.Count
                            row("ChangedFields") = String.Join(", ", changedFields)
                            row("BeforeSnapshotJson") = beforeJson
                            row("AfterSnapshotJson") = afterJson
                            row("DeltaJson") = deltaJson
                            table.Rows.Add(row)
                        End While
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Sub SoftDeleteAuditTrailEntry(auditId As Integer, Optional updatedBy As Integer = 0)
            If auditId <= 0 Then
                Return
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_AuditTrail", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_AuditTrail " &
                        "SET DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME() " &
                        "WHERE UpdateAuditLogID = @UpdateAuditLogID AND ISNULL(DeletedFlag, 0) = 0", conn)
                        cmd.Parameters.AddWithValue("@UpdateAuditLogID", auditId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit("FW_AuditTrail_B",
                                   "FW_AuditTrail",
                                   "Delete",
                                   "AfterSave",
                                   auditId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Soft deleted audit trail row."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    Return
                End If

                Using cmd As New SqlCommand("DELETE FROM dbo.FW_AuditTrail WHERE UpdateAuditLogID = @UpdateAuditLogID", conn)
                    cmd.Parameters.AddWithValue("@UpdateAuditLogID", auditId)
                    cmd.ExecuteNonQuery()
                End Using

                LogUpdateAudit("FW_AuditTrail_B",
                               "FW_AuditTrail",
                               "Delete",
                               "AfterSave",
                               auditId.ToString(CultureInfo.InvariantCulture),
                               BuildSoftDeleteAuditSnapshotJson("Deleted audit trail row."),
                               True,
                               Nothing,
                               updatedBy)
            End Using
        End Sub

        Public Shared Sub RestoreAuditTrailEntry(auditId As Integer, Optional updatedBy As Integer = 0)
            If auditId <= 0 Then
                Return
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_AuditTrail", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_AuditTrail " &
                        "SET DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL " &
                        "WHERE UpdateAuditLogID = @UpdateAuditLogID", conn)
                        cmd.Parameters.AddWithValue("@UpdateAuditLogID", auditId)
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit("FW_AuditTrail_B",
                                   "FW_AuditTrail",
                                   "Restore",
                                   "AfterSave",
                                   auditId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Restored audit trail row."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    Return
                End If

                ' No-op on schemas without soft-delete columns.
            End Using
        End Sub

        Public Shared Function GetUpdateAuditFilterValues(Optional showDeletedOnly As Boolean = False) As DataTable
            Dim table As New DataTable("FW_UpdateAuditLogFilterValues")
            table.Columns.Add("PageName", GetType(String))
            table.Columns.Add("TableName", GetType(String))
            table.Columns.Add("OperationType", GetType(String))
            table.Columns.Add("UserID", GetType(Integer))
            table.Columns.Add("UserDisplay", GetType(String))
            table.Columns.Add("RegistrationID", GetType(Integer))
            table.Columns.Add("RegistrationDisplay", GetType(String))

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim supportsDeletedFlag = TableHasColumn("FW_AuditTrail", "DeletedFlag")
                Dim sql As String =
                    "SELECT DISTINCT " &
                    "ISNULL(a.PageName, '') AS PageName, " &
                    "ISNULL(a.TableName, '') AS TableName, " &
                    "ISNULL(a.OperationType, '') AS OperationType, " &
                    "ISNULL(a.UserID, 0) AS UserID, " &
                    "ISNULL(u.LastFirst, ISNULL(u.FirstName + ' ' + u.LastName, '')) AS UserDisplay, " &
                    "ISNULL(a.RegistrationID, 0) AS RegistrationID, " &
                    "ISNULL(r.RegName, '') AS RegistrationDisplay " &
                    "FROM dbo.FW_AuditTrail a " &
                    "LEFT JOIN dbo.FW_Users u ON u.UserID = a.UserID " &
                    "LEFT JOIN dbo.FW_Registration r ON r.ID = a.RegistrationID " &
                    "WHERE a.Phase = 'AfterSave'"

                If supportsDeletedFlag Then
                    sql &= " AND ISNULL(a.DeletedFlag, 0) = @ShowDeletedOnly"
                End If

                Using cmd As New SqlCommand(sql, conn)
                    If supportsDeletedFlag Then
                        cmd.Parameters.AddWithValue("@ShowDeletedOnly", If(showDeletedOnly, 1, 0))
                    End If
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Private Shared Function BuildSnapshotDeltaJson(beforeJson As String,
                                                       afterJson As String,
                                                       changedFields As List(Of String)) As String
            Dim beforeMap = ParseSnapshotJsonToMap(beforeJson)
            Dim afterMap = ParseSnapshotJsonToMap(afterJson)

            Dim keys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each key In beforeMap.Keys
                keys.Add(key)
            Next
            For Each key In afterMap.Keys
                keys.Add(key)
            Next

            Dim deltas As New List(Of Dictionary(Of String, String))()
            For Each key In keys.OrderBy(Function(k) k, StringComparer.OrdinalIgnoreCase)
                Dim beforeValue = If(beforeMap.ContainsKey(key), beforeMap(key), String.Empty)
                Dim afterValue = If(afterMap.ContainsKey(key), afterMap(key), String.Empty)

                If String.Equals(beforeValue, afterValue, StringComparison.Ordinal) Then
                    Continue For
                End If

                changedFields.Add(key)
                deltas.Add(New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase) From {
                    {"Field", key},
                    {"Before", beforeValue},
                    {"After", afterValue}
                })
            Next

            Return JsonSerializer.Serialize(deltas)
        End Function

        Private Shared Function ParseSnapshotJsonToMap(snapshotJson As String) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(snapshotJson) Then
                Return result
            End If

            Try
                Dim parsed = JsonSerializer.Deserialize(Of Dictionary(Of String, String))(snapshotJson)
                If parsed Is Nothing Then
                    Return result
                End If

                For Each kvp In parsed
                    result(kvp.Key) = If(kvp.Value, String.Empty)
                Next
            Catch
                ' If JSON cannot be parsed, leave map empty.
            End Try

            Return result
        End Function

        Public Shared Function GetCrudButtonCaptions(registrationId As Integer) As CrudButtonCaptions
            Dim captions = CrudButtonCaptions.DefaultCaptions()
            If registrationId <= 0 Then
                Return captions
            End If

            Dim activeSession = SessionState.Current
            Dim cachedCaptions As CrudButtonCaptions = Nothing
            SyncLock metadataCacheLock
                If crudCaptionCache.TryGetValue(registrationId, cachedCaptions) Then
                    captions = CloneCrudCaptions(cachedCaptions)
                End If
            End SyncLock

            If cachedCaptions Is Nothing Then
                Try
                    Using conn As New SqlConnection(ConnectionString)
                        conn.Open()
                        Using cmd As New SqlCommand(
                            "SELECT TOP 1 " &
                            "ISNULL(LTRIM(RTRIM(BTN_Create_Caption)), '') AS BTN_Create_Caption, " &
                            "ISNULL(LTRIM(RTRIM(BTN_Read_Caption)), '') AS BTN_Read_Caption, " &
                            "ISNULL(LTRIM(RTRIM(BTN_Update_Caption)), '') AS BTN_Update_Caption, " &
                            "ISNULL(LTRIM(RTRIM(BTN_Delete_Caption)), '') AS BTN_Delete_Caption " &
                            "FROM dbo.FW_Registration WHERE ID = @ID", conn)
                            cmd.Parameters.AddWithValue("@ID", registrationId)

                            Using reader = cmd.ExecuteReader()
                                If reader.Read() Then
                                    Dim createCaption = SafeString(reader("BTN_Create_Caption"))
                                    Dim readCaption = SafeString(reader("BTN_Read_Caption"))
                                    Dim updateCaption = SafeString(reader("BTN_Update_Caption"))
                                    Dim deleteCaption = SafeString(reader("BTN_Delete_Caption"))

                                    If createCaption <> String.Empty Then
                                        captions.CreateCaption = createCaption
                                    End If
                                    If readCaption <> String.Empty Then
                                        captions.ReadCaption = readCaption
                                    End If
                                    If updateCaption <> String.Empty Then
                                        captions.UpdateCaption = updateCaption
                                    End If
                                    If deleteCaption <> String.Empty Then
                                        captions.DeleteCaption = deleteCaption
                                    End If
                                End If
                            End Using
                        End Using
                    End Using
                Catch
                    ' Keep defaults when captions are unavailable.
                End Try

                SyncLock metadataCacheLock
                    crudCaptionCache(registrationId) = CloneCrudCaptions(captions)
                End SyncLock
            End If

            If activeSession.HasValue AndAlso activeSession.Value.RegistrationID = registrationId Then
                captions.CreateCaption = If(String.IsNullOrWhiteSpace(activeSession.Value.CrudCreateCaption), captions.CreateCaption, activeSession.Value.CrudCreateCaption)
                captions.ReadCaption = If(String.IsNullOrWhiteSpace(activeSession.Value.CrudReadCaption), captions.ReadCaption, activeSession.Value.CrudReadCaption)
                captions.UpdateCaption = If(String.IsNullOrWhiteSpace(activeSession.Value.CrudUpdateCaption), captions.UpdateCaption, activeSession.Value.CrudUpdateCaption)
                captions.DeleteCaption = If(String.IsNullOrWhiteSpace(activeSession.Value.CrudDeleteCaption), captions.DeleteCaption, activeSession.Value.CrudDeleteCaption)

                SessionState.UpdateCrudCaptions(captions.CreateCaption,
                                                captions.ReadCaption,
                                                captions.UpdateCaption,
                                                captions.DeleteCaption)
            End If

            Return captions
        End Function

        Public Shared Function GetMaxRecordsNoQBE(registrationId As Integer) As Integer
            If registrationId <= 0 Then
                Return 10
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(MaxRecordsNoQBE, 10) AS MaxRecords " &
                        "FROM dbo.FW_Registration WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", registrationId)

                        Dim result = cmd.ExecuteScalar()
                        If result Is Nothing OrElse IsDBNull(result) Then
                            Return 10
                        End If

                        Dim maxRecords = Convert.ToInt32(result)
                        If maxRecords <= 0 Then
                            Return 10
                        End If

                        Return maxRecords
                    End Using
                End Using
            Catch
                ' Return default when unavailable
                Return 10
            End Try
        End Function

        Public Shared Function GetUsersForAdmin(registrationId As Integer,
                                                Optional filters As Dictionary(Of String, String) = Nothing,
                                                Optional baseSelectSql As String = Nothing,
                                                Optional showDeletedOnly As Boolean = False) As DataTable
            Dim table As New DataTable("FW_Users")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                ' CUSTOM SQL PATH: Execute as-is, then apply QBE filters client-side
                If Not String.IsNullOrWhiteSpace(baseSelectSql) Then
                    Dim effectiveSql = baseSelectSql.Trim()
                    If registrationId > 0 Then
                        Dim registrationValue = registrationId.ToString(CultureInfo.InvariantCulture)
                        effectiveSql = Regex.Replace(effectiveSql, "RegistrationID\s*=\s*(?:@RegistrationID|\?|\d+)", "RegistrationID = " & registrationValue, RegexOptions.IgnoreCase)
                        effectiveSql = effectiveSql.Replace("@RegistrationID", registrationValue, StringComparison.OrdinalIgnoreCase)
                        effectiveSql = effectiveSql.Replace("?", registrationValue)
                    End If

                    Using cmd As New SqlCommand(effectiveSql, conn)
                        Using da As New SqlDataAdapter(cmd)
                            da.Fill(table)
                        End Using
                    End Using

                    table = ApplyDeletedFilterWithSourceHydration(table,
                                                                 showDeletedOnly,
                                                                 "FW_Users",
                                                                 "UserID",
                                                                 "PK",
                                                                 "UserID",
                                                                 "ID")

                    If filters IsNot Nothing AndAlso filters.Count > 0 Then
                        Dim filterExpr = BuildDataViewFilterExpression(table, filters)
                        If Not String.IsNullOrWhiteSpace(filterExpr) Then
                            Try
                                Dim view As New DataView(table)
                                view.RowFilter = filterExpr
                                Return view.ToTable()
                            Catch
                                ' Return unfiltered if expression is invalid
                            End Try
                        End If
                    End If

                    Return table
                End If

                Dim sql As New StringBuilder()
                sql.Append("SELECT UserID, RegistrationID, FirstName, LastName, FirstLast, LastFirst, Email, Phone, IsActive, SuperAdmin ")
                sql.Append("FROM dbo.FW_Users WHERE RegistrationID = @RegistrationID")

                Dim parsedFilters As New List(Of Tuple(Of String, QbeComparisonOperator, String))()

                If filters IsNot Nothing Then
                    For Each kvp In filters
                        Dim rawKey = If(kvp.Key, String.Empty).Trim()
                        Dim rawValue = If(kvp.Value, String.Empty).Trim()

                        If rawValue = String.Empty Then
                            Continue For
                        End If

                        Dim fieldName = rawKey
                        Dim comparisonOperator As QbeComparisonOperator = QbeComparisonOperator.EqualsTo

                        If rawKey.Contains("|") Then
                            Dim pieces = rawKey.Split("|"c)
                            fieldName = pieces(0)
                            If pieces.Length > 1 Then
                                [Enum].TryParse(pieces(1), True, comparisonOperator)
                            End If
                        End If

                        parsedFilters.Add(Tuple.Create(fieldName, comparisonOperator, rawValue))
                    Next
                End If

                For Each parsedFilter In parsedFilters
                    Dim fieldName = parsedFilter.Item1
                    Dim comparisonOperator = parsedFilter.Item2
                    Dim value = parsedFilter.Item3

                    Select Case fieldName
                        Case "UserID"
                            Dim parsed As Integer
                            If Integer.TryParse(value, parsed) Then
                                sql.Append(" AND UserID").Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.NumericField)).Append(" @UserID")
                            End If
                        Case "IsActive", "SuperAdmin"
                            Dim parsedBit As Boolean
                            If TryParseBooleanFilter(value, parsedBit) Then
                                sql.Append(" AND ").Append(fieldName).Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.BooleanField)).Append(" @").Append(fieldName)
                            End If
                        Case "FirstName", "LastName", "FirstLast", "LastFirst", "Email", "Phone"
                            sql.Append(" AND ").Append(fieldName).Append(" ").Append(GetSqlOperator(comparisonOperator, QbeFieldKind.TextField)).Append(" @").Append(fieldName)
                    End Select
                Next

                sql.Append(" ORDER BY LastFirst")

                Using cmd As New SqlCommand(sql.ToString(), conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                    For Each parsedFilter In parsedFilters
                        Dim fieldName = parsedFilter.Item1
                        Dim comparisonOperator = parsedFilter.Item2
                        Dim value = parsedFilter.Item3

                        Select Case fieldName
                            Case "UserID"
                                Dim parsed As Integer
                                If Integer.TryParse(value, parsed) Then
                                    cmd.Parameters.AddWithValue("@UserID", parsed)
                                End If
                            Case "IsActive", "SuperAdmin"
                                Dim parsedBit As Boolean
                                If TryParseBooleanFilter(value, parsedBit) Then
                                    cmd.Parameters.AddWithValue("@" & fieldName, parsedBit)
                                End If
                            Case "FirstName", "LastName", "FirstLast", "LastFirst", "Email", "Phone"
                                cmd.Parameters.AddWithValue("@" & fieldName, BuildTextFilterValue(value, comparisonOperator))
                        End Select
                    Next

                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            table = ApplyDeletedFilterWithSourceHydration(table,
                                                          showDeletedOnly,
                                                          "FW_Users",
                                                          "UserID",
                                                          "PK",
                                                          "UserID",
                                                          "ID")
            Return table
        End Function

        Private Shared Function ApplyEntityDeletedFilterFallback(source As DataTable, showDeletedOnly As Boolean) As DataTable
            Return ApplyDeletedFilterWithSourceHydration(source,
                                                         showDeletedOnly,
                                                         "FW_Entity",
                                                         "ID",
                                                         "PK",
                                                         "ID",
                                                         "EntityID")
        End Function

        Private Shared Function ApplyDeletedFilterWithSourceHydration(source As DataTable,
                                                                       showDeletedOnly As Boolean,
                                                                       sourceTableName As String,
                                                                       sourceKeyColumnName As String,
                                                                       ParamArray resultKeyColumnCandidates() As String) As DataTable
            If source Is Nothing Then
                Return source
            End If

            If source.Columns Is Nothing OrElse source.Columns.Contains("DeletedFlag") Then
                Return ApplyDeletedFlagFilter(source, showDeletedOnly)
            End If

            If String.IsNullOrWhiteSpace(sourceTableName) OrElse String.IsNullOrWhiteSpace(sourceKeyColumnName) Then
                Return source
            End If

            If Not IsSafeSqlIdentifier(sourceTableName) OrElse Not IsSafeSqlIdentifier(sourceKeyColumnName) Then
                Return source
            End If

            If Not TableHasColumn(sourceTableName, "DeletedFlag") Then
                Return source
            End If

            Dim keyColumn = ResolveResultKeyColumn(source, resultKeyColumnCandidates)
            If keyColumn Is Nothing Then
                Return source
            End If

            Dim deletedIds = GetDeletedIdSetForSourceTable(source,
                                                           keyColumn.ColumnName,
                                                           sourceTableName,
                                                           sourceKeyColumnName)
            Dim withDeletedFlag As DataTable = source.Copy()
            withDeletedFlag.Columns.Add("DeletedFlag", GetType(Boolean))

            For Each row As DataRow In withDeletedFlag.Rows
                Dim isDeleted As Boolean = False

                If row IsNot Nothing AndAlso Not row.IsNull(keyColumn.ColumnName) Then
                    Dim entityId As Integer
                    If Integer.TryParse(row(keyColumn.ColumnName).ToString(), entityId) Then
                        isDeleted = deletedIds.Contains(entityId)
                    End If
                End If

                row("DeletedFlag") = isDeleted
            Next

            Return ApplyDeletedFlagFilter(withDeletedFlag, showDeletedOnly)
        End Function

        Private Shared Function ResolveResultKeyColumn(source As DataTable,
                                                       resultKeyColumnCandidates As IEnumerable(Of String)) As DataColumn
            If source Is Nothing OrElse source.Columns Is Nothing Then
                Return Nothing
            End If

            If resultKeyColumnCandidates IsNot Nothing Then
                For Each candidate In resultKeyColumnCandidates
                    Dim key = If(candidate, String.Empty).Trim()
                    If key <> String.Empty AndAlso source.Columns.Contains(key) Then
                        Return source.Columns(key)
                    End If
                Next
            End If

            For Each column As DataColumn In source.Columns
                If column Is Nothing OrElse String.IsNullOrWhiteSpace(column.ColumnName) Then
                    Continue For
                End If

                Dim name = column.ColumnName.Trim()
                If String.Equals(name, "PK", StringComparison.OrdinalIgnoreCase) OrElse
                   name.EndsWith("ID", StringComparison.OrdinalIgnoreCase) Then
                    Return column
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function GetDeletedIdSetForSourceTable(source As DataTable,
                                                              resultKeyColumnName As String,
                                                              sourceTableName As String,
                                                              sourceKeyColumnName As String) As HashSet(Of Integer)
            Dim result As New HashSet(Of Integer)()
            If source Is Nothing OrElse source.Columns Is Nothing Then
                Return result
            End If

            If String.IsNullOrWhiteSpace(resultKeyColumnName) OrElse Not source.Columns.Contains(resultKeyColumnName) Then
                Return result
            End If

            If String.IsNullOrWhiteSpace(sourceTableName) OrElse String.IsNullOrWhiteSpace(sourceKeyColumnName) Then
                Return result
            End If

            If Not IsSafeSqlIdentifier(sourceTableName) OrElse Not IsSafeSqlIdentifier(sourceKeyColumnName) Then
                Return result
            End If

            Dim ids As New List(Of Integer)()
            For Each row As DataRow In source.Rows
                If row Is Nothing OrElse row.IsNull(resultKeyColumnName) Then
                    Continue For
                End If

                Dim id As Integer
                If Integer.TryParse(row(resultKeyColumnName).ToString(), id) AndAlso id > 0 Then
                    ids.Add(id)
                End If
            Next

            If ids.Count = 0 Then
                Return result
            End If

            Dim uniqueIds As New HashSet(Of Integer)(ids)
            ids = New List(Of Integer)(uniqueIds)

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim parameterNames As New List(Of String)()
                Using cmd As New SqlCommand()
                    cmd.Connection = conn

                    For i As Integer = 0 To ids.Count - 1
                        Dim paramName = "@ID" & i.ToString(CultureInfo.InvariantCulture)
                        parameterNames.Add(paramName)
                        cmd.Parameters.AddWithValue(paramName, ids(i))
                    Next

                    cmd.CommandText =
                        "SELECT " & sourceKeyColumnName & " FROM dbo." & sourceTableName & " WHERE ISNULL(DeletedFlag, 0) = 1 AND " & sourceKeyColumnName & " IN (" &
                        String.Join(",", parameterNames) & ")"

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            result.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture))
                        End While
                    End Using
                End Using
            End Using

            Return result
        End Function

        Private Shared Function IsSafeSqlIdentifier(value As String) As Boolean
            If String.IsNullOrWhiteSpace(value) Then
                Return False
            End If

            Return Regex.IsMatch(value.Trim(), "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)
        End Function

        Public Shared Function GetUserByID(userId As Integer) As UserAdminRecord
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserID, RegistrationID, FirstName, LastName, FirstLast, LastFirst, Email, Phone, Address1 AS Address, Address2, City, State, Zip, IsActive, SuperAdmin, RowVersion " &
                    "FROM dbo.FW_Users WHERE UserID = @UserID", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    Using reader = cmd.ExecuteReader()
                        If reader.Read() Then
                            Return New UserAdminRecord With {
                                .UserID = Convert.ToInt32(reader("UserID")),
                                .RegistrationID = Convert.ToInt32(reader("RegistrationID")),
                                .FirstName = If(reader("FirstName") Is DBNull.Value, String.Empty, reader("FirstName").ToString()),
                                .LastName = If(reader("LastName") Is DBNull.Value, String.Empty, reader("LastName").ToString()),
                                .FirstLast = If(reader("FirstLast") Is DBNull.Value, String.Empty, reader("FirstLast").ToString()),
                                .LastFirst = If(reader("LastFirst") Is DBNull.Value, String.Empty, reader("LastFirst").ToString()),
                                .Email = If(reader("Email") Is DBNull.Value, String.Empty, reader("Email").ToString()),
                                .Phone = If(reader("Phone") Is DBNull.Value, String.Empty, reader("Phone").ToString()),
                                .Address = If(reader("Address") Is DBNull.Value, String.Empty, reader("Address").ToString()),
                                .Address2 = If(reader("Address2") Is DBNull.Value, String.Empty, reader("Address2").ToString()),
                                .City = If(reader("City") Is DBNull.Value, String.Empty, reader("City").ToString()),
                                .State = If(reader("State") Is DBNull.Value, String.Empty, reader("State").ToString()),
                                .Zip = If(reader("Zip") Is DBNull.Value, String.Empty, reader("Zip").ToString()),
                                .IsActive = If(reader("IsActive") Is DBNull.Value, False, CBool(reader("IsActive"))),
                                .SuperAdmin = If(reader("SuperAdmin") Is DBNull.Value, False, CBool(reader("SuperAdmin"))),
                                .RowVersion = DirectCast(reader("RowVersion"), Byte())
                            }
                        End If
                    End Using
                End Using
            End Using
            Return New UserAdminRecord()
        End Function

        Public Shared Function GetRolesForUserAssignment(registrationId As Integer, Optional userId As Integer = 0) As DataTable
            Dim table As New DataTable("FW_Roles")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim sql As String =
                    "SELECT r.ID, r.RoleName, r.DisplayOrder " &
                    "FROM dbo.FW_Roles r " &
                    "WHERE r.RegistrationID = @RegistrationID AND r.IsActive = 1 "

                If userId > 0 Then
                    sql &= "AND NOT EXISTS (SELECT 1 FROM dbo.FW_UserRoles ur WHERE ur.UserID = @UserID AND ur.RoleID = r.ID) "
                End If

                sql &= "ORDER BY r.DisplayOrder, r.RoleName"

                Using cmd As New SqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    If userId > 0 Then
                        cmd.Parameters.AddWithValue("@UserID", userId)
                    End If
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetAssignedRolesForUser(userId As Integer, registrationId As Integer) As List(Of UserRoleOption)
            Dim roles As New List(Of UserRoleOption)()

            If userId <= 0 OrElse registrationId <= 0 Then
                Return roles
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DISTINCT r.ID AS RoleID, r.RoleName, ISNULL(r.DisplayOrder, 0) AS DisplayOrder, " &
                    "       ISNULL(r.Typ_AppAdmin, 0) AS Typ_AppAdmin, " &
                    "       ISNULL(r.Typ_CompanyAdmin, 0) AS Typ_CompanyAdmin, " &
                    "       ISNULL(r.Typ_RW, 0) AS Typ_RW, " &
                    "       ISNULL(r.Typ_RO, 0) AS Typ_RO, " &
                    "       ISNULL(r.Typ_User, 0) AS Typ_User, " &
                    "       ISNULL(r.Typ_OnlyMyRecords, 0) AS Typ_OnlyMyRecords, " &
                    "       ISNULL(r.Can_Create, 0) AS Can_Create, " &
                    "       ISNULL(r.Can_Read, 0) AS Can_Read, " &
                    "       ISNULL(r.Can_Update, 0) AS Can_Update, " &
                    "       ISNULL(r.Can_Delete, 0) AS Can_Delete, " &
                    "       ISNULL(r.Can_ViewAllRecords, 0) AS Can_ViewAllRecords, " &
                    "       ISNULL(r.Can_ViewOnlyMyRecords, 0) AS Can_ViewOnlyMyRecords " &
                    "FROM dbo.FW_UserRoles ur " &
                    "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                    "WHERE ur.UserID = @UserID " &
                    "  AND ur.RegistrationID = @RegistrationID " &
                    "  AND ISNULL(ur.IsActive, 1) = 1 " &
                    "  AND ISNULL(r.IsActive, 1) = 1 " &
                    "ORDER BY ISNULL(r.DisplayOrder, 0), r.RoleName", conn)

                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim isApplicationAdmin = Convert.ToBoolean(reader("Typ_AppAdmin"))
                            Dim isCompanyAdmin = Convert.ToBoolean(reader("Typ_CompanyAdmin"))
                            Dim isTypRw = Convert.ToBoolean(reader("Typ_RW"))
                            Dim isTypRo = Convert.ToBoolean(reader("Typ_RO"))
                            Dim isTypUser = Convert.ToBoolean(reader("Typ_User"))
                            Dim isTypOnlyMyRecords = Convert.ToBoolean(reader("Typ_OnlyMyRecords"))
                            Dim canCreate = Convert.ToBoolean(reader("Can_Create"))
                            Dim canRead = Convert.ToBoolean(reader("Can_Read"))
                            Dim canUpdate = Convert.ToBoolean(reader("Can_Update"))
                            Dim canDelete = Convert.ToBoolean(reader("Can_Delete"))
                            Dim canViewAll = Convert.ToBoolean(reader("Can_ViewAllRecords"))
                            Dim canViewOnlyMine = Convert.ToBoolean(reader("Can_ViewOnlyMyRecords"))
                            Dim roleType As String = "Custom"

                            If isCompanyAdmin Then
                                roleType = "Company Admin"
                            ElseIf isApplicationAdmin Then
                                roleType = "Application Admin"
                            ElseIf isTypOnlyMyRecords Then
                                roleType = "Owner Only"
                            ElseIf isTypRw Then
                                roleType = "RW"
                            ElseIf isTypRo Then
                                roleType = "RO"
                            ElseIf isTypUser Then
                                roleType = "User"
                            ElseIf canViewOnlyMine AndAlso Not canViewAll Then
                                roleType = "Owner Only"
                            ElseIf canRead AndAlso (canCreate OrElse canUpdate OrElse canDelete) Then
                                roleType = "RW"
                            ElseIf canRead AndAlso Not canCreate AndAlso Not canUpdate AndAlso Not canDelete Then
                                roleType = "RO"
                            End If

                            roles.Add(New UserRoleOption With {
                                .RoleID = Convert.ToInt32(reader("RoleID")),
                                .RoleName = reader("RoleName").ToString(),
                                .DisplayOrder = Convert.ToInt32(reader("DisplayOrder")),
                                .RoleType = roleType,
                                .IsApplicationAdmin = isApplicationAdmin,
                                .IsCompanyAdmin = isCompanyAdmin
                            })
                        End While
                    End Using
                End Using
            End Using

            Return roles
        End Function

        Public Shared Function GetUserRoles(userId As Integer) As DataTable
            Dim table As New DataTable("vw_FW_UserRoles")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserRoleID, RegistrationID, UserID, RoleID, RoleName, DisplayOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn " &
                    "FROM dbo.vw_FW_UserRoles WHERE UserID = @UserID " &
                    "ORDER BY (SELECT ISNULL(r.DisplayOrder, 255) FROM dbo.FW_Roles r WHERE r.ID = vw_FW_UserRoles.RoleID), RoleName", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Sub AssignRoleToUser(userId As Integer, registrationId As Integer, roleId As Integer, displayOrder As Integer, createdBy As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "IF NOT EXISTS (SELECT 1 FROM dbo.FW_UserRoles WHERE UserID = @UserID AND RoleID = @RoleID) " &
                    "INSERT INTO dbo.FW_UserRoles (RegistrationID, UserID, RoleID, DisplayOrder, IsActive, CreatedBy, CreatedOn) " &
                    "VALUES (@RegistrationID, @UserID, @RoleID, @DisplayOrder, 1, @CreatedBy, GETDATE())", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@DisplayOrder", displayOrder)
                    cmd.Parameters.AddWithValue("@CreatedBy", createdBy)
                    Dim rowsAffected = cmd.ExecuteNonQuery()
                    If rowsAffected = 0 Then
                        ' Role already exists for this user, or insert failed
                    End If
                End Using
            End Using
        End Sub

        Public Shared Sub RemoveRoleFromUser(userId As Integer, roleId As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "DELETE FROM dbo.FW_UserRoles WHERE UserID = @UserID AND RoleID = @RoleID", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Function IsUserLoginUnique(email As String, Optional excludeUserId As Integer = 0) As Boolean
            Dim normalized = NormalizeEmailForLookup(email)
            If normalized = String.Empty Then
                Return False
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COUNT(1) FROM dbo.FW_Users " &
                    "WHERE LOWER(REPLACE(ISNULL(Email, ''), ' ', '')) = @EmailLookup " &
                    "AND (@ExcludeUserId <= 0 OR UserID <> @ExcludeUserId)", conn)
                    cmd.Parameters.AddWithValue("@EmailLookup", normalized)
                    cmd.Parameters.AddWithValue("@ExcludeUserId", excludeUserId)
                    Dim countObj = cmd.ExecuteScalar()
                    Dim existingCount As Integer = 0
                    If countObj IsNot Nothing AndAlso countObj IsNot DBNull.Value Then
                        Integer.TryParse(countObj.ToString(), existingCount)
                    End If

                    Return existingCount = 0
                End Using
            End Using
        End Function

        Public Shared Function CreateUser(record As UserAdminRecord, createdBy As Integer) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using trans = conn.BeginTransaction(IsolationLevel.Serializable)
                    Try
                        Using limitCommand As New SqlCommand(
                            "DECLARE @MaxUsers smallint; " &
                            "SELECT @MaxUsers = MaxUsers FROM dbo.FW_Registration WITH (UPDLOCK, HOLDLOCK) WHERE ID = @RegistrationID; " &
                            "IF ISNULL(@MaxUsers, 0) > 0 AND " &
                            "(SELECT COUNT(*) FROM dbo.FW_Users WHERE RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0) >= @MaxUsers " &
                            "THROW 52300, 'USER LIMIT REACHED. NO ADDITIONAL USERS CAN BE CREATED FOR THIS REGISTRATION.', 1;", conn, trans)
                            limitCommand.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                            limitCommand.ExecuteNonQuery()
                        End Using

                        Using cmd As New SqlCommand(
                            "INSERT INTO dbo.FW_Users (RegistrationID, FirstName, LastName, Email, Phone, Address1, Address2, City, State, Zip, IsActive, SuperAdmin, CreatedBy, CreatedOn) " &
                            "VALUES (@RegistrationID, @FirstName, @LastName, @Email, @Phone, @Address, @Address2, @City, @State, @Zip, @IsActive, @SuperAdmin, @CreatedBy, GETDATE()); " &
                            "SELECT CAST(SCOPE_IDENTITY() as int)", conn, trans)
                            cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                            cmd.Parameters.AddWithValue("@FirstName", CType(If(String.IsNullOrWhiteSpace(record.FirstName), DBNull.Value, CObj(record.FirstName.Trim())), Object))
                            cmd.Parameters.AddWithValue("@LastName", CType(If(String.IsNullOrWhiteSpace(record.LastName), DBNull.Value, CObj(record.LastName.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Email", CType(If(String.IsNullOrWhiteSpace(record.Email), DBNull.Value, CObj(record.Email.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Phone", CType(If(String.IsNullOrWhiteSpace(record.Phone), DBNull.Value, CObj(record.Phone.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Address", CType(If(String.IsNullOrWhiteSpace(record.Address), DBNull.Value, CObj(record.Address.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Address2", CType(If(String.IsNullOrWhiteSpace(record.Address2), DBNull.Value, CObj(record.Address2.Trim())), Object))
                            cmd.Parameters.AddWithValue("@City", CType(If(String.IsNullOrWhiteSpace(record.City), DBNull.Value, CObj(record.City.Trim())), Object))
                            cmd.Parameters.AddWithValue("@State", CType(If(String.IsNullOrWhiteSpace(record.State), DBNull.Value, CObj(record.State.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Zip", CType(If(String.IsNullOrWhiteSpace(record.Zip), DBNull.Value, CObj(record.Zip.Trim())), Object))
                            cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                            cmd.Parameters.AddWithValue("@SuperAdmin", record.SuperAdmin)
                            cmd.Parameters.AddWithValue("@CreatedBy", createdBy)
                            Dim result = cmd.ExecuteScalar()
                            trans.Commit()
                            If result IsNot Nothing Then
                                Return CInt(result)
                            End If
                        End Using
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
            Return 0
        End Function

        Public Shared Function UpdateUser(record As UserAdminRecord, updatedBy As Integer) As SaveResult
            If Not TableHasRowVersion("FW_Users") OrElse record.RowVersion Is Nothing Then
                Return SaveResult.ConcurrencyUnavailable
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Users SET FirstName = @FirstName, LastName = @LastName, Email = @Email, Phone = @Phone, " &
                    "Address1 = @Address, Address2 = @Address2, City = @City, State = @State, Zip = @Zip, " &
                    "IsActive = @IsActive, SuperAdmin = @SuperAdmin, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                    "WHERE UserID = @UserID AND RowVersion = @OriginalRowVersion", conn)
                    cmd.Parameters.AddWithValue("@UserID", record.UserID)
                    cmd.Parameters.AddWithValue("@FirstName", CType(If(String.IsNullOrWhiteSpace(record.FirstName), DBNull.Value, CObj(record.FirstName.Trim())), Object))
                    cmd.Parameters.AddWithValue("@LastName", CType(If(String.IsNullOrWhiteSpace(record.LastName), DBNull.Value, CObj(record.LastName.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Email", CType(If(String.IsNullOrWhiteSpace(record.Email), DBNull.Value, CObj(record.Email.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Phone", CType(If(String.IsNullOrWhiteSpace(record.Phone), DBNull.Value, CObj(record.Phone.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Address", CType(If(String.IsNullOrWhiteSpace(record.Address), DBNull.Value, CObj(record.Address.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Address2", CType(If(String.IsNullOrWhiteSpace(record.Address2), DBNull.Value, CObj(record.Address2.Trim())), Object))
                    cmd.Parameters.AddWithValue("@City", CType(If(String.IsNullOrWhiteSpace(record.City), DBNull.Value, CObj(record.City.Trim())), Object))
                    cmd.Parameters.AddWithValue("@State", CType(If(String.IsNullOrWhiteSpace(record.State), DBNull.Value, CObj(record.State.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Zip", CType(If(String.IsNullOrWhiteSpace(record.Zip), DBNull.Value, CObj(record.Zip.Trim())), Object))
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@SuperAdmin", record.SuperAdmin)
                    cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                    cmd.Parameters.Add("@OriginalRowVersion", SqlDbType.Timestamp).Value = record.RowVersion
                    If cmd.ExecuteNonQuery() = 0 Then
                        Return SaveResult.RecordChanged
                    End If
                End Using
            End Using
            Return SaveResult.Succeeded
        End Function

        Public Shared Sub SyncRoleSchemaWithDatabase()
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim tableNames As New List(Of String)()
                Dim syncUserId = If(SessionState.IsActive AndAlso SessionState.Current.HasValue,
                                    SessionState.Current.Value.UserID,
                                    0)
                Using tableCmd As New SqlCommand("SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') AND (name LIKE 'FW[_]%' OR name LIKE 'AS[_]%') ORDER BY name", conn)
                    Using reader = tableCmd.ExecuteReader()
                        While reader.Read()
                            tableNames.Add(reader.GetString(0))
                        End While
                    End Using
                End Using

                For Each tableName In tableNames
                    Dim tableAlias = FormatTableNameAsAlias(tableName)
                    Using cmd As New SqlCommand(
                        "IF EXISTS (SELECT 1 FROM dbo.FW_RoleSchema WHERE DB_Table = @DBTable) " &
                        "UPDATE dbo.FW_RoleSchema SET Table_Alias = @TableAlias WHERE DB_Table = @DBTable " &
                        "ELSE INSERT INTO dbo.FW_RoleSchema (DB_Table, Table_Alias, IsActive, CreatedBy, CreatedOn) VALUES (@DBTable, @TableAlias, 1, @CreatedBy, GETDATE())", conn)
                        cmd.Parameters.AddWithValue("@DBTable", tableName)
                        cmd.Parameters.AddWithValue("@TableAlias", tableAlias)
                        cmd.Parameters.AddWithValue("@CreatedBy", syncUserId)
                        cmd.ExecuteNonQuery()
                    End Using
                Next
            End Using
        End Sub

        Public Shared Function GetRoleSchemaTable() As DataTable
            Dim table As New DataTable("FW_RoleSchema")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, DB_Table, Table_Alias FROM dbo.FW_RoleSchema WHERE IsActive = 1 ORDER BY DB_Table", conn)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function ExecuteCustomQuery(customSql As String,
                              Optional registrationId As Integer? = Nothing,
                              Optional showDeletedOnly As Boolean = False,
                              Optional sourceTableName As String = Nothing,
                              Optional sourceKeyColumnName As String = "ID") As DataTable
            Dim table As New DataTable()
            Dim effectiveSql = If(customSql, String.Empty).Trim()

            If registrationId.HasValue AndAlso registrationId.Value > 0 Then
                Dim registrationValue = registrationId.Value.ToString(CultureInfo.InvariantCulture)
                effectiveSql = Regex.Replace(effectiveSql,
                                             "((?:[A-Za-z_][A-Za-z0-9_]*\.)?\[?RegistrationID\]?)\s*=\s*(?:@RegistrationID|\?|\d+)",
                                             "$1 = " & registrationValue,
                                             RegexOptions.IgnoreCase)
                effectiveSql = effectiveSql.Replace("@RegistrationID", registrationValue, StringComparison.OrdinalIgnoreCase)
                effectiveSql = effectiveSql.Replace("?", registrationValue)
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(effectiveSql, conn)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            If String.IsNullOrWhiteSpace(sourceTableName) Then
                Return ApplyDeletedFlagFilter(table, showDeletedOnly)
            End If

            Return ApplyDeletedFilterWithSourceHydration(table,
                                                         showDeletedOnly,
                                                         sourceTableName,
                                                         sourceKeyColumnName,
                                                         "PK",
                                                         sourceKeyColumnName,
                                                         "ID")
        End Function

        Public Shared Function CreateRole(registrationId As Integer, roleName As String, displayOrder As Integer) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Roles (RegistrationID, RoleName, IsActive, DisplayOrder, CreatedBy, CreatedOn) " &
                    "VALUES (@RegistrationID, @RoleName, 1, @DisplayOrder, @CreatedBy, GETDATE()); " &
                    "SELECT CAST(SCOPE_IDENTITY() as int)", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleName", If(String.IsNullOrWhiteSpace(roleName), CType(DBNull.Value, Object), CType(roleName.Trim(), Object)))
                    cmd.Parameters.AddWithValue("@DisplayOrder", displayOrder)
                    cmd.Parameters.AddWithValue("@CreatedBy", If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))

                    Dim result = cmd.ExecuteScalar()
                    InvalidateRoleMetadataCache()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        Public Shared Sub DeleteRole(roleId As Integer, Optional updatedBy As Integer = 0)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Roles", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Roles " &
                        "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", roleId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit("Roles_B",
                                   "FW_Roles",
                                   "Delete",
                                   "AfterSave",
                                   roleId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Soft deleted role record."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    InvalidateRoleMetadataCache()
                    Return
                End If

                Using trans = conn.BeginTransaction()
                    Try
                        ' Delete all FW_RoleFields for this role
                        Using cmd As New SqlCommand("DELETE FROM dbo.FW_RoleFields WHERE RoleID = @RoleID", conn, trans)
                            cmd.Parameters.AddWithValue("@RoleID", roleId)
                            cmd.ExecuteNonQuery()
                        End Using
                        
                        ' Delete all FW_RoleDetails for this role
                        Using cmd As New SqlCommand("DELETE FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID", conn, trans)
                            cmd.Parameters.AddWithValue("@RoleID", roleId)
                            cmd.ExecuteNonQuery()
                        End Using
                        
                        ' Delete the FW_Roles record
                        Using cmd As New SqlCommand("DELETE FROM dbo.FW_Roles WHERE ID = @ID", conn, trans)
                            cmd.Parameters.AddWithValue("@ID", roleId)
                            cmd.ExecuteNonQuery()
                        End Using
                        
                        trans.Commit()
                        LogUpdateAudit("Roles_B",
                                       "FW_Roles",
                                       "Delete",
                                       "AfterSave",
                                       roleId.ToString(CultureInfo.InvariantCulture),
                                       BuildSoftDeleteAuditSnapshotJson("Deleted role record."),
                                       True,
                                       Nothing,
                                       updatedBy)
                        InvalidateRoleMetadataCache()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Sub

        Public Shared Sub RestoreRole(roleId As Integer, Optional updatedBy As Integer = 0)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Roles", "DeletedFlag") Then
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_Roles " &
                        "SET IsActive = 1, DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", roleId)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using

                    LogUpdateAudit("Roles_B",
                                   "FW_Roles",
                                   "Restore",
                                   "AfterSave",
                                   roleId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Restored role record."),
                                   True,
                                   Nothing,
                                   updatedBy)
                    InvalidateRoleMetadataCache()
                    Return
                End If

                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Roles SET IsActive = 1, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", roleId)
                    cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                    cmd.ExecuteNonQuery()
                End Using
                LogUpdateAudit("Roles_B",
                               "FW_Roles",
                               "Restore",
                               "AfterSave",
                               roleId.ToString(CultureInfo.InvariantCulture),
                               BuildSoftDeleteAuditSnapshotJson("Restored role record."),
                               True,
                               Nothing,
                               updatedBy)
                InvalidateRoleMetadataCache()
            End Using
        End Sub

        Public Shared Function RoleNameExists(registrationId As Integer, roleName As String, excludeRoleId As Integer) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COUNT(1) FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID AND RoleName = @RoleName AND ID <> @ExcludeRoleId", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleName", roleName.Trim())
                    cmd.Parameters.AddWithValue("@ExcludeRoleId", excludeRoleId)
                    Return CInt(cmd.ExecuteScalar()) > 0
                End Using
            End Using
        End Function

        Public Shared Function CreateRoleWithPermissions(registrationId As Integer, roleName As String, displayOrder As Integer, 
            caCanChange As Boolean, canCreate As Boolean, canRead As Boolean, canUpdate As Boolean, canDelete As Boolean,
            canExport As Boolean, canImport As Boolean, canUseQBE As Boolean, canViewAllRecords As Boolean, canViewOnlyMyRecords As Boolean) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Roles (RegistrationID, RoleName, IsActive, DisplayOrder, CA_CanChange, Can_Create, Can_Read, Can_Update, Can_Delete, Can_Export, Can_Import, Can_UseQBE, Can_ViewAllRecords, Can_ViewOnlyMyRecords, CreatedBy, CreatedOn) " &
                    "VALUES (@RegistrationID, @RoleName, 1, @DisplayOrder, @CA_CanChange, @Can_Create, @Can_Read, @Can_Update, @Can_Delete, @Can_Export, @Can_Import, @Can_UseQBE, @Can_ViewAllRecords, @Can_ViewOnlyMyRecords, @CreatedBy, GETDATE()); " &
                    "SELECT CAST(SCOPE_IDENTITY() as int)", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleName", If(String.IsNullOrWhiteSpace(roleName), CType(DBNull.Value, Object), CType(roleName.Trim(), Object)))
                    cmd.Parameters.AddWithValue("@DisplayOrder", displayOrder)
                    cmd.Parameters.AddWithValue("@CA_CanChange", caCanChange)
                    cmd.Parameters.AddWithValue("@Can_Create", canCreate)
                    cmd.Parameters.AddWithValue("@Can_Read", canRead)
                    cmd.Parameters.AddWithValue("@Can_Update", canUpdate)
                    cmd.Parameters.AddWithValue("@Can_Delete", canDelete)
                    cmd.Parameters.AddWithValue("@Can_Export", canExport)
                    cmd.Parameters.AddWithValue("@Can_Import", canImport)
                    cmd.Parameters.AddWithValue("@Can_UseQBE", canUseQBE)
                    cmd.Parameters.AddWithValue("@Can_ViewAllRecords", canViewAllRecords)
                    cmd.Parameters.AddWithValue("@Can_ViewOnlyMyRecords", canViewOnlyMyRecords)
                    cmd.Parameters.AddWithValue("@CreatedBy", If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))

                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        Public Shared Function GetRolesByRegistration(registrationId As Integer) As DataTable
            Dim table As New DataTable("FW_Roles")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, RegistrationID, RoleName FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID ORDER BY RoleName", conn)
                    
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetRoleDetailsForRole(roleId As Integer, registrationId As Integer) As DataTable
            Dim table As New DataTable("FW_RoleDetails")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, RoleID, RegistrationID, SchemaID, DB_Table, Table_Alias, OverrideCaption, " &
                    "Can_Create, Can_Read, Can_Update, Can_Delete, Can_Export, Can_Import, Can_UseQBE, " &
                    "Can_ViewAllRecords, Can_ViewOnlyMyRecords, CA_CanChange, Expand_QBE, IsActive, MaxRecords, StartEmpty " &
                    "FROM dbo.FW_RoleDetails " &
                    "WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID " &
                    "ORDER BY DB_Table", conn)
                    
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetRoleTableAccessEntries(roleId As Integer, registrationId As Integer) As List(Of RoleTableAccessEntry)
            Dim entries As New List(Of RoleTableAccessEntry)()

            If roleId <= 0 OrElse registrationId <= 0 Then
                Return entries
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DB_Table, " &
                    "ISNULL(Can_Create, 0) AS Can_Create, " &
                    "ISNULL(Can_Read, 0) AS Can_Read, " &
                    "ISNULL(Can_Update, 0) AS Can_Update, " &
                    "ISNULL(Can_Delete, 0) AS Can_Delete, " &
                    "ISNULL(Can_Import, 0) AS Can_Import, " &
                    "ISNULL(Can_UseQBE, 0) AS Can_UseQBE, " &
                    "ISNULL(Can_ViewAllRecords, 0) AS Can_ViewAllRecords, " &
                    "ISNULL(Can_ViewOnlyMyRecords, 0) AS Can_ViewOnlyMyRecords, " &
                    "ISNULL(Expand_QBE, 0) AS Expand_QBE " &
                    "FROM dbo.FW_RoleDetails " &
                    "WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID AND ISNULL(IsActive, 1) = 1", conn)

                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim tableName = If(reader("DB_Table"), String.Empty).ToString().Trim()
                            If tableName = String.Empty Then
                                Continue While
                            End If

                            entries.Add(New RoleTableAccessEntry With {
                                .TableName = tableName,
                                .CanCreate = Convert.ToBoolean(reader("Can_Create")),
                                .CanReadOnly = Convert.ToBoolean(reader("Can_Read")),
                                .CanUpdate = Convert.ToBoolean(reader("Can_Update")),
                                .CanDelete = Convert.ToBoolean(reader("Can_Delete")),
                                .CanImport = Convert.ToBoolean(reader("Can_Import")),
                                .CanUseQbe = Convert.ToBoolean(reader("Can_UseQBE")),
                                .CanViewAllRecords = Convert.ToBoolean(reader("Can_ViewAllRecords")),
                                .CanViewOnlyMy = Convert.ToBoolean(reader("Can_ViewOnlyMyRecords")),
                                .CanExpandQbe = Convert.ToBoolean(reader("Expand_QBE"))
                            })
                        End While
                    End Using
                End Using
            End Using

            Return entries
        End Function

        Public Shared Function GetRoleDetailOverrideCaption(roleId As Integer, registrationId As Integer, dbTable As String) As String
            If roleId <= 0 OrElse registrationId <= 0 OrElse String.IsNullOrWhiteSpace(dbTable) Then
                Return String.Empty
            End If

            Dim cacheKey = BuildRoleDetailCacheKey(roleId, registrationId, dbTable)
            SyncLock metadataCacheLock
                Dim cachedValue As String = Nothing
                If roleOverrideCaptionCache.TryGetValue(cacheKey, cachedValue) Then
                    Return cachedValue
                End If
            End SyncLock

            Dim caption As String = String.Empty

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ISNULL(OverrideCaption, '') " &
                    "FROM dbo.FW_RoleDetails " &
                    "WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID AND DB_Table = @DBTable", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@DBTable", dbTable.Trim())

                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        caption = String.Empty
                    Else
                        caption = result.ToString().Trim()
                    End If
                End Using
            End Using

            SyncLock metadataCacheLock
                roleOverrideCaptionCache(cacheKey) = caption
            End SyncLock

            Return caption
        End Function

        Public Shared Function GetRoleDetailStartEmpty(roleId As Integer, registrationId As Integer, dbTable As String) As Boolean
            If roleId <= 0 OrElse registrationId <= 0 OrElse String.IsNullOrWhiteSpace(dbTable) Then
                Return False
            End If

            Dim cacheKey = BuildRoleDetailCacheKey(roleId, registrationId, dbTable)
            SyncLock metadataCacheLock
                Dim cachedValue As Boolean
                If roleStartEmptyCache.TryGetValue(cacheKey, cachedValue) Then
                    Return cachedValue
                End If
            End SyncLock

            Dim startEmpty As Boolean = False

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ISNULL(StartEmpty, 0) " &
                    "FROM dbo.FW_RoleDetails " &
                    "WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID AND DB_Table = @DBTable " &
                    "AND ISNULL(IsActive, 1) = 1", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@DBTable", dbTable.Trim())

                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        startEmpty = False
                    Else
                        startEmpty = Convert.ToBoolean(result)
                    End If
                End Using
            End Using

            SyncLock metadataCacheLock
                roleStartEmptyCache(cacheKey) = startEmpty
            End SyncLock

            Return startEmpty
        End Function

        Public Shared Function GetRoleFieldDisplayCaptions(roleId As Integer, registrationId As Integer, tableName As String) As Dictionary(Of String, String)
            Dim result As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If roleId <= 0 OrElse registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return result
            End If

            Dim cacheKey = BuildRoleDetailCacheKey(roleId, registrationId, tableName)
            SyncLock metadataCacheLock
                Dim cachedMap As Dictionary(Of String, String) = Nothing
                If roleFieldCaptionCache.TryGetValue(cacheKey, cachedMap) Then
                    Return CloneCaptionMap(cachedMap)
                End If
            End SyncLock

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT FieldName, " &
                    "ISNULL(LTRIM(RTRIM(OverrideCaption)), '') AS OverrideCaption, " &
                    "ISNULL(LTRIM(RTRIM(FriendlyFieldName)), '') AS FriendlyFieldName " &
                    "FROM dbo.FW_RoleFields " &
                    "WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID " &
                    "AND UPPER(LTRIM(RTRIM(TableName))) = UPPER(@TableName) " &
                    "AND ISNULL(IsActive, 1) = 1", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@TableName", tableName.Trim())

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim fieldName = SafeString(reader("FieldName"))
                            If fieldName = String.Empty Then
                                Continue While
                            End If

                            Dim overrideCaption = SafeString(reader("OverrideCaption"))
                            Dim friendlyName = SafeString(reader("FriendlyFieldName"))
                            Dim caption = If(overrideCaption <> String.Empty, overrideCaption, friendlyName)

                            If caption <> String.Empty Then
                                result(fieldName) = caption
                            End If
                        End While
                    End Using
                End Using
            End Using

            SyncLock metadataCacheLock
                roleFieldCaptionCache(cacheKey) = CloneCaptionMap(result)
            End SyncLock

            Return result
        End Function

        Public Shared Function AddRoleTablePermission(roleId As Integer, registrationId As Integer, 
                                                       roleSchemaId As Integer, dbTable As String, 
                                                       tableAlias As String, tableCaption As String) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim effectiveTableAlias = If(tableAlias, String.Empty).Trim()
                Dim effectiveTableCaption = If(tableCaption, String.Empty).Trim()
                If String.IsNullOrWhiteSpace(effectiveTableCaption) Then
                    effectiveTableCaption = effectiveTableAlias
                End If

                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_RoleDetails (RoleID, RegistrationID, SchemaID, DB_Table, Table_Alias, OverrideCaption, " &
                    "Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE, IsActive, StartEmpty, CreatedBy, CreatedOn) " &
                    "VALUES (@RoleID, @RegistrationID, @RoleSchemaID, @DBTable, @TableAlias, @TableCaption, 1, 0, 1, 1, 1, 1, 1, @CreatedBy, GETDATE()); " &
                    "SELECT CAST(SCOPE_IDENTITY() as int)", conn)
                    
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleSchemaID", roleSchemaId)
                    cmd.Parameters.AddWithValue("@DBTable", dbTable)
                    cmd.Parameters.AddWithValue("@TableAlias", effectiveTableAlias)
                    cmd.Parameters.AddWithValue("@TableCaption", effectiveTableCaption)
                    cmd.Parameters.AddWithValue("@CreatedBy", If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))
                    
                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        Public Shared Sub UpdateRoleDetails(detailId As Integer, tableAlias As String, tableCaption As String,
                                            canCreate As Boolean, canRead As Boolean, canUpdate As Boolean, 
                                            canDelete As Boolean, canViewAllRecords As Boolean,
                                            canViewOnlyMyRecords As Boolean, canUseQBE As Boolean,
                                            Optional propagateCaptionOverride As Boolean = False)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim effectiveTableAlias = If(tableAlias, String.Empty).Trim()
                Dim effectiveTableCaption = If(tableCaption, String.Empty).Trim()
                If String.IsNullOrWhiteSpace(effectiveTableCaption) Then
                    effectiveTableCaption = effectiveTableAlias
                End If

                Using trans = conn.BeginTransaction()
                    Try
                        Dim registrationId As Integer = 0
                        Dim schemaId As Integer = 0
                        Dim dbTable As String = String.Empty
                        Using identityCommand As New SqlCommand(
                            "SELECT RegistrationID, SchemaID, DB_Table FROM dbo.FW_RoleDetails WHERE ID = @ID", conn, trans)
                            identityCommand.Parameters.AddWithValue("@ID", detailId)
                            Using reader = identityCommand.ExecuteReader()
                                If Not reader.Read() Then
                                    Throw New InvalidOperationException("The selected role detail no longer exists.")
                                End If

                                registrationId = If(reader("RegistrationID") Is DBNull.Value, 0, Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture))
                                schemaId = If(reader("SchemaID") Is DBNull.Value, 0, Convert.ToInt32(reader("SchemaID"), CultureInfo.InvariantCulture))
                                dbTable = SafeString(reader("DB_Table"))
                            End Using
                        End Using

                        Using cmd As New SqlCommand(
                            "UPDATE dbo.FW_RoleDetails SET Table_Alias = @TableAlias, OverrideCaption = @TableCaption, " &
                            "Can_Create = @CanCreate, Can_Read = @CanRead, Can_Update = @CanUpdate, " &
                            "Can_Delete = @CanDelete, Can_ViewAllRecords = @CanViewAllRecords, " &
                            "Can_ViewOnlyMyRecords = @CanViewOnlyMyRecords, Can_UseQBE = @CanUseQBE, " &
                            "UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                            "WHERE ID = @ID", conn, trans)
                            cmd.Parameters.AddWithValue("@ID", detailId)
                            cmd.Parameters.AddWithValue("@TableAlias", effectiveTableAlias)
                            cmd.Parameters.AddWithValue("@TableCaption", effectiveTableCaption)
                            cmd.Parameters.AddWithValue("@CanCreate", canCreate)
                            cmd.Parameters.AddWithValue("@CanRead", canRead)
                            cmd.Parameters.AddWithValue("@CanUpdate", canUpdate)
                            cmd.Parameters.AddWithValue("@CanDelete", canDelete)
                            cmd.Parameters.AddWithValue("@CanViewAllRecords", canViewAllRecords)
                            cmd.Parameters.AddWithValue("@CanViewOnlyMyRecords", canViewOnlyMyRecords)
                            cmd.Parameters.AddWithValue("@CanUseQBE", canUseQBE)
                            cmd.Parameters.AddWithValue("@UpdatedBy", If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))
                            cmd.ExecuteNonQuery()
                        End Using

                        If propagateCaptionOverride Then
                            Using propagateCommand As New SqlCommand(
                                "UPDATE dbo.FW_RoleDetails SET OverrideCaption = @TableCaption, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND DB_Table = @DBTable", conn, trans)
                                propagateCommand.Parameters.AddWithValue("@TableCaption", effectiveTableCaption)
                                propagateCommand.Parameters.AddWithValue("@UpdatedBy", If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))
                                propagateCommand.Parameters.AddWithValue("@RegistrationID", registrationId)
                                propagateCommand.Parameters.AddWithValue("@SchemaID", schemaId)
                                propagateCommand.Parameters.AddWithValue("@DBTable", dbTable)
                                propagateCommand.ExecuteNonQuery()
                            End Using
                        End If

                        trans.Commit()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using

            InvalidateRoleMetadataCache()
        End Sub

        Public Shared Sub DeleteRoleDetails(detailId As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Using trans = conn.BeginTransaction()
                    Try
                        Dim roleId As Integer = 0
                        Dim registrationId As Integer = 0
                        Dim schemaId As Integer = 0
                        Dim tableName As String = String.Empty
                        Using identityCommand As New SqlCommand(
                            "SELECT RoleID, RegistrationID, SchemaID, DB_Table FROM dbo.FW_RoleDetails WHERE ID = @ID", conn, trans)
                            identityCommand.Parameters.AddWithValue("@ID", detailId)
                            Using reader = identityCommand.ExecuteReader()
                                If Not reader.Read() Then
                                    Return
                                End If

                                roleId = Convert.ToInt32(reader("RoleID"), CultureInfo.InvariantCulture)
                                registrationId = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture)
                                schemaId = Convert.ToInt32(reader("SchemaID"), CultureInfo.InvariantCulture)
                                tableName = SafeString(reader("DB_Table"))
                            End Using
                        End Using

                        ' Remove dependent fields by both the FK and the legacy identity columns.
                        Using cmd As New SqlCommand(
                            "DELETE FROM dbo.FW_RoleFields " &
                            "WHERE RoleDetailID = @ID " &
                            "OR (RoleID = @RoleID AND RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND TableName = @TableName)", conn, trans)
                            cmd.Parameters.AddWithValue("@ID", detailId)
                            cmd.Parameters.AddWithValue("@RoleID", roleId)
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                            cmd.Parameters.AddWithValue("@TableName", tableName)
                            cmd.ExecuteNonQuery()
                        End Using

                        Using cmd As New SqlCommand("DELETE FROM dbo.FW_RoleDetails WHERE ID = @ID", conn, trans)
                            cmd.Parameters.AddWithValue("@ID", detailId)
                            cmd.ExecuteNonQuery()
                        End Using

                        trans.Commit()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using

            InvalidateRoleMetadataCache()
        End Sub

        Public Shared Function GetRoleFieldsForRole(roleId As Integer) As DataTable
            Dim table As New DataTable("RoleFields")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, RoleDetailID, SchemaID, TableName, FieldName, FriendlyFieldName, CA_CanChange, " &
                    "ISNULL(Can_Create, 0) AS Can_Create, ISNULL(Can_Read, 0) AS Can_Read, " &
                    "ISNULL(Can_Update, 0) AS Can_Update, ISNULL(IsActive, 0) AS IsActive, " &
                    "ISNULL(IsRequired, 0) AS IsRequired, ISNULL(IsUnique, 0) AS IsUnique, " &
                    "ISNULL(Make_Invisible, 0) AS Make_Invisible, OrderBy, OverrideCaption " &
                    "FROM dbo.FW_RoleFields WHERE RoleID = @RoleID " &
                    "ORDER BY TableName, ISNULL(OrderBy, 9999), FieldName", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Sub InsertRoleFieldsForTable(registrationId As Integer, roleId As Integer, roleSchemaId As Integer, tableName As String, createdBy As Integer)
            Dim excludeFields = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "CreatedBy", "CreatedOn", "UpdatedBy", "UpdatedOn"
            }

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                ' Get the RoleDetailID for this role and schema
                Dim roleDetailId As Integer = 0
                Using rdCmd As New SqlCommand(
                    "SELECT ID FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND SchemaID = @SchemaID", conn)
                    rdCmd.Parameters.AddWithValue("@RoleID", roleId)
                    rdCmd.Parameters.AddWithValue("@SchemaID", roleSchemaId)
                    Dim result = rdCmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        roleDetailId = CInt(result)
                    End If
                End Using

                If roleDetailId <= 0 Then
                    Throw New Exception("RoleDetailID not found for RoleID=" & roleId & " SchemaID=" & roleSchemaId)
                End If

                ' Dynamically find primary key columns to exclude
                Using pkCmd As New SqlCommand(
                    "SELECT kcu.COLUMN_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " &
                    "JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu " &
                    "ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND tc.TABLE_NAME = kcu.TABLE_NAME " &
                    "WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_NAME = @TableName", conn)
                    pkCmd.Parameters.AddWithValue("@TableName", tableName)
                    Using reader = pkCmd.ExecuteReader()
                        While reader.Read()
                            excludeFields.Add(reader.GetString(0))
                        End While
                    End Using
                End Using

                Dim columns As New List(Of String)
                Using cmd As New SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " &
                    "WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION", conn)
                    cmd.Parameters.AddWithValue("@TableName", tableName)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            columns.Add(reader.GetString(0))
                        End While
                    End Using
                End Using

                For Each fieldName In columns
                    If excludeFields.Contains(fieldName) Then Continue For
                    Dim FileLink = tableName & "." & fieldName
                    Using cmd2 As New SqlCommand(
                        "IF NOT EXISTS (SELECT 1 FROM dbo.FW_RoleFields WHERE RoleID = @RoleID AND TableName = @TableName AND FieldName = @FieldName) " &
                        "INSERT INTO dbo.FW_RoleFields " &
                        "(RoleDetailID, RegistrationID, RoleID, SchemaID, TableName, FieldName, FileLink, FriendlyFieldName, Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                        "VALUES (@RoleDetailID, @RegistrationID, @RoleID, @SchemaID, @TableName, @FieldName, @FileLink, @FriendlyFieldName, 1, 1, 1, 0, @CreatedBy, GETDATE())", conn)
                        cmd2.Parameters.AddWithValue("@RoleDetailID", roleDetailId)
                        cmd2.Parameters.AddWithValue("@RegistrationID", registrationId)
                        cmd2.Parameters.AddWithValue("@RoleID", roleId)
                        cmd2.Parameters.AddWithValue("@SchemaID", roleSchemaId)
                        cmd2.Parameters.AddWithValue("@TableName", tableName)
                        cmd2.Parameters.AddWithValue("@FieldName", fieldName)
                        cmd2.Parameters.AddWithValue("@FileLink", FileLink)
                        cmd2.Parameters.AddWithValue("@FriendlyFieldName", FormatFieldName(fieldName))
                        cmd2.Parameters.AddWithValue("@CreatedBy", createdBy)
                        cmd2.ExecuteNonQuery()
                    End Using
                Next
            End Using
        End Sub

        Public Shared Sub InsertRoleFieldsWithTransaction(registrationId As Integer, roleId As Integer, roleSchemaId As Integer, tableName As String, createdBy As Integer, conn As SqlConnection, trans As SqlTransaction)
            Dim excludeFields = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "CreatedBy", "CreatedOn", "UpdatedBy", "UpdatedOn"
            }

            Dim roleDetailId As Integer = 0
            Using roleDetailCommand As New SqlCommand(
                "SELECT ID FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND RegistrationID = @RegistrationID AND SchemaID = @SchemaID", conn, trans)
                roleDetailCommand.Parameters.AddWithValue("@RoleID", roleId)
                roleDetailCommand.Parameters.AddWithValue("@RegistrationID", registrationId)
                roleDetailCommand.Parameters.AddWithValue("@SchemaID", roleSchemaId)
                Dim roleDetailResult = roleDetailCommand.ExecuteScalar()
                If roleDetailResult IsNot Nothing AndAlso Not IsDBNull(roleDetailResult) Then
                    roleDetailId = Convert.ToInt32(roleDetailResult, CultureInfo.InvariantCulture)
                End If
            End Using

            If roleDetailId <= 0 Then
                Throw New InvalidOperationException("RoleDetailID not found for the selected role table.")
            End If

            ' Dynamically find primary key columns to exclude
            Using pkCmd As New SqlCommand(
                "SELECT kcu.COLUMN_NAME FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " &
                "JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu " &
                "ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME AND tc.TABLE_NAME = kcu.TABLE_NAME " &
                "WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' AND tc.TABLE_NAME = @TableName", conn, trans)
                pkCmd.Parameters.AddWithValue("@TableName", tableName)
                Using reader = pkCmd.ExecuteReader()
                    While reader.Read()
                        excludeFields.Add(reader.GetString(0))
                    End While
                End Using
            End Using

            Dim columns As New List(Of String)
            Using cmd As New SqlCommand(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " &
                "WHERE TABLE_NAME = @TableName ORDER BY ORDINAL_POSITION", conn, trans)
                cmd.Parameters.AddWithValue("@TableName", tableName)
                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        columns.Add(reader.GetString(0))
                    End While
                End Using
            End Using

            For Each fieldName In columns
                If excludeFields.Contains(fieldName) Then Continue For
                Dim FileLink = tableName & "." & fieldName
                    Using cmd2 As New SqlCommand(
                        "IF NOT EXISTS (SELECT 1 FROM dbo.FW_RoleFields WHERE RoleID = @RoleID AND TableName = @TableName AND FieldName = @FieldName) " &
                        "INSERT INTO dbo.FW_RoleFields " &
                        "(RoleDetailID, RegistrationID, RoleID, SchemaID, TableName, FieldName, FileLink, FriendlyFieldName, OverrideCaption, Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                        "VALUES (@RoleDetailID, @RegistrationID, @RoleID, @SchemaID, @TableName, @FieldName, @FileLink, @FriendlyFieldName, " &
                        "(SELECT TOP 1 OverrideCaption FROM dbo.FW_RoleFields WHERE RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND TableName = @TableName AND FieldName = @FieldName AND RoleID <> @RoleID ORDER BY RoleID), " &
                        "1, 1, 1, 0, @CreatedBy, GETDATE())", conn, trans)
                    cmd2.Parameters.AddWithValue("@RoleDetailID", roleDetailId)
                    cmd2.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd2.Parameters.AddWithValue("@RoleID", roleId)
                    cmd2.Parameters.AddWithValue("@SchemaID", roleSchemaId)
                    cmd2.Parameters.AddWithValue("@TableName", tableName)
                    cmd2.Parameters.AddWithValue("@FieldName", fieldName)
                    cmd2.Parameters.AddWithValue("@FileLink", FileLink)
                    cmd2.Parameters.AddWithValue("@FriendlyFieldName", FormatFieldName(fieldName))
                    cmd2.Parameters.AddWithValue("@CreatedBy", createdBy)
                    cmd2.ExecuteNonQuery()
                End Using
            Next
        End Sub

        Public Shared Function AddRoleTableWithFields(roleId As Integer, registrationId As Integer, 
                                                      roleSchemaId As Integer, dbTable As String, 
                                                      tableAlias As String, tableCaption As String) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using trans = conn.BeginTransaction()
                    Try
                        ' Insert into FW_RoleDetails
                        Using cmd As New SqlCommand(
                            "INSERT INTO dbo.FW_RoleDetails (RoleID, RegistrationID, SchemaID, DB_Table, Table_Alias, OverrideCaption, " &
                            "Can_Create, Can_Read, Can_Update, Can_Delete, Can_UseQBE, IsActive, CreatedBy, CreatedOn) " &
                            "VALUES (@RoleID, @RegistrationID, @RoleSchemaID, @DBTable, @TableAlias, " &
                            "COALESCE((SELECT TOP 1 NULLIF(LTRIM(RTRIM(OverrideCaption)), '') FROM dbo.FW_RoleDetails " &
                            "WHERE RegistrationID = @RegistrationID AND SchemaID = @RoleSchemaID AND DB_Table = @DBTable AND RoleID <> @RoleID " &
                            "ORDER BY RoleID), @TableCaption), 1, 0, 1, 1, 1, 1, @CreatedBy, GETDATE()); " &
                            "SELECT CAST(SCOPE_IDENTITY() as int)", conn, trans)
                            
                            cmd.Parameters.AddWithValue("@RoleID", roleId)
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            cmd.Parameters.AddWithValue("@RoleSchemaID", roleSchemaId)
                            cmd.Parameters.AddWithValue("@DBTable", dbTable)
                            cmd.Parameters.AddWithValue("@TableAlias", tableAlias)
                            cmd.Parameters.AddWithValue("@TableCaption", tableCaption)
                            cmd.Parameters.AddWithValue("@CreatedBy", If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))
                            
                            Dim detailId = CInt(cmd.ExecuteScalar())
                            
                            ' Insert fields for this table within same transaction
                            InsertRoleFieldsWithTransaction(registrationId, roleId, roleSchemaId, dbTable, If(SessionState.IsActive, SessionState.Current.Value.UserID, 0), conn, trans)
                            
                            trans.Commit()
                            InvalidateRoleMetadataCache()
                            Return detailId
                        End Using
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Function

        Public Shared Sub UpdateRoleField(id As Integer, caCanChange As Object, canCreate As Boolean, canRead As Boolean,
                                          canUpdate As Boolean, isActive As Boolean, isRequired As Boolean, isUnique As Boolean,
                                          makeInvisible As Boolean, orderBy As Object, overrideCaption As String, friendlyFieldName As String, updatedBy As Integer,
                                          Optional propagateCaptionOverride As Boolean = False)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using trans = conn.BeginTransaction()
                    Try
                        Dim registrationId As Integer = 0
                        Dim schemaId As Integer = 0
                        Dim tableName As String = String.Empty
                        Dim fieldName As String = String.Empty
                        Using identityCommand As New SqlCommand(
                            "SELECT RegistrationID, SchemaID, TableName, FieldName FROM dbo.FW_RoleFields WHERE ID = @ID", conn, trans)
                            identityCommand.Parameters.AddWithValue("@ID", id)
                            Using reader = identityCommand.ExecuteReader()
                                If Not reader.Read() Then
                                    Return
                                End If

                                registrationId = If(reader("RegistrationID") Is DBNull.Value, 0, Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture))
                                schemaId = If(reader("SchemaID") Is DBNull.Value, 0, Convert.ToInt32(reader("SchemaID"), CultureInfo.InvariantCulture))
                                tableName = SafeString(reader("TableName"))
                                fieldName = SafeString(reader("FieldName"))
                            End Using
                        End Using

                        Using cmd As New SqlCommand(
                            "UPDATE dbo.FW_RoleFields SET " &
                            "CA_CanChange = @CA_CanChange, Can_Create = @Can_Create, Can_Read = @Can_Read, Can_Update = @Can_Update, " &
                            "IsActive = @IsActive, IsRequired = @IsRequired, IsUnique = @IsUnique, Make_Invisible = @Make_Invisible, " &
                            "OrderBy = @OrderBy, OverrideCaption = @OverrideCaption, FriendlyFieldName = @FriendlyFieldName, " &
                            "UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                            "WHERE ID = @ID", conn, trans)
                            AddRoleFieldUpdateParameters(cmd, id, caCanChange, canCreate, canRead, canUpdate, isActive, isRequired, isUnique, makeInvisible, orderBy, overrideCaption, friendlyFieldName, updatedBy)
                            cmd.ExecuteNonQuery()
                        End Using

                        If propagateCaptionOverride Then
                            Using propagateCommand As New SqlCommand(
                                "UPDATE dbo.FW_RoleFields SET OverrideCaption = @OverrideCaption, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND TableName = @TableName AND FieldName = @FieldName", conn, trans)
                                propagateCommand.Parameters.AddWithValue("@OverrideCaption", If(String.IsNullOrWhiteSpace(overrideCaption), CType(DBNull.Value, Object), overrideCaption.Trim()))
                                propagateCommand.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                                propagateCommand.Parameters.AddWithValue("@RegistrationID", registrationId)
                                propagateCommand.Parameters.AddWithValue("@SchemaID", schemaId)
                                propagateCommand.Parameters.AddWithValue("@TableName", tableName)
                                propagateCommand.Parameters.AddWithValue("@FieldName", fieldName)
                                propagateCommand.ExecuteNonQuery()
                            End Using
                        End If

                        trans.Commit()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
            InvalidateRoleMetadataCache()
        End Sub

        Private Shared Sub AddRoleFieldUpdateParameters(cmd As SqlCommand, id As Integer, caCanChange As Object,
                                                        canCreate As Boolean, canRead As Boolean, canUpdate As Boolean,
                                                        isActive As Boolean, isRequired As Boolean, isUnique As Boolean,
                                                        makeInvisible As Boolean, orderBy As Object, overrideCaption As String,
                                                        friendlyFieldName As String, updatedBy As Integer)
            cmd.Parameters.AddWithValue("@ID", id)

            If caCanChange Is Nothing OrElse IsDBNull(caCanChange) Then
                cmd.Parameters.Add("@CA_CanChange", SqlDbType.Bit).Value = DBNull.Value
            Else
                cmd.Parameters.AddWithValue("@CA_CanChange", CBool(caCanChange))
            End If

            cmd.Parameters.AddWithValue("@Can_Create", canCreate)
            cmd.Parameters.AddWithValue("@Can_Read", canRead)
            cmd.Parameters.AddWithValue("@Can_Update", canUpdate)
            cmd.Parameters.AddWithValue("@IsActive", isActive)
            cmd.Parameters.AddWithValue("@IsRequired", isRequired)
            cmd.Parameters.AddWithValue("@IsUnique", isUnique)
            cmd.Parameters.AddWithValue("@Make_Invisible", makeInvisible)

            Dim orderByInt As Integer = 0
            If orderBy Is Nothing OrElse IsDBNull(orderBy) OrElse Not Integer.TryParse(orderBy.ToString(), orderByInt) Then
                cmd.Parameters.Add("@OrderBy", SqlDbType.Int).Value = DBNull.Value
            Else
                cmd.Parameters.AddWithValue("@OrderBy", orderByInt)
            End If

            cmd.Parameters.AddWithValue("@OverrideCaption", If(String.IsNullOrWhiteSpace(overrideCaption), CType(DBNull.Value, Object), overrideCaption.Trim()))
            cmd.Parameters.AddWithValue("@FriendlyFieldName", If(String.IsNullOrWhiteSpace(friendlyFieldName), CType(DBNull.Value, Object), friendlyFieldName.Trim()))
            cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
        End Sub

        ' Helper: Format a single PascalCase segment (used in snake_case handling)
        ' E.g., "CompanyAdmin" -> "Company Admin", "UserID" -> "User ID"
        Private Shared Function FormatSinglePascalPart(part As String) As String
            If String.IsNullOrWhiteSpace(part) Then Return part
            
            Dim commonSuffixes = {"ID", "UID", "URL", "QBE", "FK", "PK"}
            Dim suffix = ""
            Dim nameToProcess = part
            
            ' Check if part ends with a common suffix
            For Each suf In commonSuffixes
                If part.EndsWith(suf) AndAlso part.Length > suf.Length Then
                    nameToProcess = part.Substring(0, part.Length - suf.Length)
                    suffix = suf
                    Exit For
                End If
            Next
            
            ' Split nameToProcess on capital letters
            Dim sb As New System.Text.StringBuilder()
            For i = 0 To nameToProcess.Length - 1
                Dim ch = nameToProcess(i)
                If i > 0 AndAlso Char.IsUpper(ch) Then
                    sb.Append(" ")
                End If
                sb.Append(ch)
            Next
            
            ' Capitalize first letter of each word, lowercase rest
            Dim words = sb.ToString().Split(" "c)
            Dim formatted As New System.Text.StringBuilder()
            For i = 0 To words.Length - 1
                If i > 0 Then formatted.Append(" ")
                If words(i).Length > 0 Then
                    formatted.Append(Char.ToUpper(words(i)(0)))
                    If words(i).Length > 1 Then formatted.Append(words(i).Substring(1).ToLower())
                End If
            Next
            
            ' Add suffix if found
            If suffix.Length > 0 Then
                formatted.Append(" ")
                formatted.Append(suffix)
            End If
            
            Return formatted.ToString()
        End Function

        Public Shared Function FormatFieldName(name As String) As String
            If String.IsNullOrWhiteSpace(name) Then Return name
            
            Dim debugOutput = $"FormatFieldName input: '{name}'"
            
            ' Handle snake_case first
            If name.Contains("_") Then
                Dim parts = name.Split("_"c)
                Dim snakeResult As New System.Text.StringBuilder()
                For i = 0 To parts.Length - 1
                    If i > 0 Then snakeResult.Append(" ")
                    If parts(i).Length > 0 Then
                        ' Each part might be PascalCase (like "CompanyAdmin" or "UserID"), so format it properly
                        Dim formattedPart = FormatSinglePascalPart(parts(i))
                        snakeResult.Append(formattedPart)
                    End If
                Next
                Dim finalSnakeResult = snakeResult.ToString()
                debugOutput &= $" -> (snake_case) '{finalSnakeResult}'"
                Try
                    IO.File.AppendAllText(IO.Path.Combine(IO.Path.GetTempPath(), "format_debug.log"), debugOutput & vbCrLf)
                Catch
                End Try
                Return finalSnakeResult
            End If
            
            ' For all uppercase: use vowel/consonant detection to find word boundaries
            If name = name.ToUpper() Then
                Dim sb As New System.Text.StringBuilder()
                For i = 0 To name.Length - 1
                    Dim ch = name(i)
                    
                    ' Add space before likely word starts (vowel after consonant)
                    If i > 0 AndAlso IsVowel(ch) AndAlso IsConsonant(name(i - 1)) Then
                        ' But not if previous char is already a vowel (to avoid "AE" -> "A E")
                        If Not IsVowel(name(i - 1)) Then
                            sb.Append(" ")
                        End If
                    End If
                    
                    sb.Append(ch)
                Next
                
                ' Capitalize first letter of each word, lowercase rest
                Dim words = sb.ToString().Split(" "c)
                Dim uppercaseFormatted As New System.Text.StringBuilder()
                For i = 0 To words.Length - 1
                    If i > 0 Then uppercaseFormatted.Append(" ")
                    If words(i).Length > 0 Then
                        uppercaseFormatted.Append(Char.ToUpper(words(i)(0)))
                        If words(i).Length > 1 Then uppercaseFormatted.Append(words(i).Substring(1).ToLower())
                    End If
                Next
                
                Dim finalUppercaseResult = uppercaseFormatted.ToString()
                debugOutput &= $" -> (uppercase) '{finalUppercaseResult}'"
                Try
                    IO.File.AppendAllText(IO.Path.Combine(IO.Path.GetTempPath(), "format_debug.log"), debugOutput & vbCrLf)
                Catch
                End Try
                Return finalUppercaseResult
            End If
            
            ' Handle PascalCase: insert spaces before capital letters, but keep common suffixes together
            ' Common suffixes to keep intact: ID, UID, URL, QBE, FK, PK
            Dim commonSuffixes = New String() {"ID", "UID", "URL", "QBE", "FK", "PK"}
            Dim nameToProcess = name
            Dim suffix = ""
            
            ' Check if name ends with a common suffix
            For Each suf In commonSuffixes
                If name.EndsWith(suf) AndAlso name.Length > suf.Length Then
                    nameToProcess = name.Substring(0, name.Length - suf.Length)
                    suffix = suf
                    debugOutput &= $" [found suffix: '{suf}']"
                    Exit For
                End If
            Next
            
            ' Format the main part (without suffix)
            Dim pbufr As New System.Text.StringBuilder()
            For i = 0 To nameToProcess.Length - 1
                Dim ch = nameToProcess(i)
                If Char.IsUpper(ch) AndAlso i > 0 Then
                    pbufr.Append(" ")
                End If
                pbufr.Append(ch)
            Next
            
            ' Add suffix if found
            If suffix.Length > 0 Then
                pbufr.Append(" ")
                pbufr.Append(suffix)
            End If
            
            Dim pascalResult = pbufr.ToString()
            debugOutput &= $" -> (pascalcase) '{pascalResult}'"
            Try
                IO.File.AppendAllText(IO.Path.Combine(IO.Path.GetTempPath(), "format_debug.log"), debugOutput & vbCrLf)
            Catch
            End Try
            Return pascalResult
        End Function

        Private Shared Function IsVowel(ch As Char) As Boolean
            Return "AEIOU".Contains(Char.ToUpper(ch))
        End Function

        Private Shared Function IsConsonant(ch As Char) As Boolean
            Return Char.IsLetter(ch) AndAlso Not IsVowel(ch)
        End Function

        Public Shared Function GetAvailableTablesForRoles() As DataTable
            Dim table As New DataTable("SchemaTables")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TABLE_NAME AS DB_Table, TABLE_NAME AS Table_Alias " &
                    "FROM INFORMATION_SCHEMA.TABLES " &
                    "WHERE TABLE_SCHEMA = 'dbo' AND (TABLE_NAME LIKE 'FW_%' OR TABLE_NAME LIKE 'AS_%') " &
                    "ORDER BY TABLE_NAME", conn)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Public Shared Function GetRolePermissionsForTable(roleId As Integer, tableName As String) As DataTable
            Dim table As New DataTable("RolePermissions")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT 'Create' AS Permission, ISNULL(IsCreate, 0) AS IsAllowed FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND TableName = @TableName " &
                    "UNION ALL " &
                    "SELECT 'Read' AS Permission, ISNULL(IsRead, 0) AS IsAllowed FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND TableName = @TableName " &
                    "UNION ALL " &
                    "SELECT 'Update' AS Permission, ISNULL(IsUpdate, 0) AS IsAllowed FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND TableName = @TableName " &
                    "UNION ALL " &
                    "SELECT 'Delete' AS Permission, ISNULL(IsDelete, 0) AS IsAllowed FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND TableName = @TableName", conn)
                    
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@TableName", tableName)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        Private Shared Sub InsertEnumeration(pageName As String, controlName As String, controlCaption As String, fileLink As String, jsAlias As String, userId As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Enumerations (PageName, ControlName, ControlCaption, FileLink, JsAlias, CreatedBy, CreatedOn) " &
                    "VALUES (@PageName, @ControlName, @ControlCaption, @FileLink, @JsAlias, @CreatedBy, GETDATE())", conn)
                    
                    cmd.Parameters.AddWithValue("@PageName", DbValueBounded(pageName, 50))
                    cmd.Parameters.AddWithValue("@ControlName", DbValueBounded(controlName, 100))
                    cmd.Parameters.AddWithValue("@ControlCaption", DbValueBounded(controlCaption, 50))
                    cmd.Parameters.AddWithValue("@FileLink", DbValueBounded(fileLink, 100))
                    cmd.Parameters.AddWithValue("@JsAlias", DbValueBounded(jsAlias, 20))
                    cmd.Parameters.AddWithValue("@CreatedBy", userId)
                    
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Private Shared Sub InsertEnumeration_U(pageName As String, controlName As String, controlCaption As String, fileLink As String, linkedControl As String, userId As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Enumerations_U (PageName, ControlName, ControlCaption, FileLink, LinkedControl, CreatedBy, CreatedOn) " &
                    "VALUES (@PageName, @ControlName, @ControlCaption, @FileLink, @LinkedControl, @CreatedBy, GETDATE())", conn)
                    
                    cmd.Parameters.AddWithValue("@PageName", DbValueBounded(pageName, 50))
                    cmd.Parameters.AddWithValue("@ControlName", DbValueBounded(controlName, 100))
                    cmd.Parameters.AddWithValue("@ControlCaption", DbValueBounded(controlCaption, 50))
                    cmd.Parameters.AddWithValue("@FileLink", DbValueBounded(fileLink, 100))
                    cmd.Parameters.AddWithValue("@LinkedControl", DbValueBounded(linkedControl, 50))
                    cmd.Parameters.AddWithValue("@CreatedBy", userId)
                    
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' Field-level control attributes for a page, scoped to the active session's registration
        ''' and role. FW_RoleFields rows are per role, so without the RoleID filter a field
        ''' configured for more than one role returns duplicate rows and an arbitrary role wins.
        ''' See sql\026_control_updates_role_scope.sql.
        ''' </summary>
        Public Shared Function GetControlUpdates(pageName As String) As DataTable
            Dim registrationId = If(SessionState.IsActive, SessionState.Current.Value.RegistrationID, 0)
            Dim roleId = If(SessionState.IsActive, SessionState.Current.Value.RoleID, 0)
            Dim table As New DataTable("ControlUpdates")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT * FROM dbo.vw_FW_ControlUpdates_U " &
                    "WHERE PageName = @PageName AND RegistrationID = @RegistrationID AND RoleID = @RoleID", conn)
                    cmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetTextColumnMaxLengths(tableName As String) As Dictionary(Of String, Integer)
            Dim results As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) Then
                Return results
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COLUMN_NAME, CHARACTER_MAXIMUM_LENGTH " &
                    "FROM INFORMATION_SCHEMA.COLUMNS " &
                    "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName " &
                    "AND DATA_TYPE IN ('varchar', 'nvarchar', 'char', 'nchar')", conn)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim columnName = reader("COLUMN_NAME").ToString().Trim()
                            Dim maxLength = 0
                            If Not IsDBNull(reader("CHARACTER_MAXIMUM_LENGTH")) Then
                                maxLength = Convert.ToInt32(reader("CHARACTER_MAXIMUM_LENGTH"), CultureInfo.InvariantCulture)
                            End If

                            If columnName <> String.Empty AndAlso maxLength > 0 Then
                                results(columnName) = maxLength
                            End If
                        End While
                    End Using
                End Using
            End Using

            Return results
        End Function

        Public Shared Function GetPageControlFieldMap(pageName As String, tableName As String) As Dictionary(Of String, String)
            Dim results As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(pageName) Then
                Return results
            End If

            Dim normalizedTable = NormalizeTableName(tableName)

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT ControlName, FileLink FROM dbo.FW_Enumerations_U WHERE PageName = @PageName", conn)
                    cmd.Parameters.AddWithValue("@PageName", pageName.Trim())

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim controlName = SafeString(reader("ControlName")).Trim()
                            Dim fileLink = SafeString(reader("FileLink")).Trim()
                            If controlName = String.Empty OrElse fileLink = String.Empty Then
                                Continue While
                            End If

                            Dim mappedColumn = ExtractColumnNameFromFileLink(fileLink, normalizedTable)
                            If mappedColumn = String.Empty Then
                                Continue While
                            End If

                            If Not results.ContainsKey(controlName) Then
                                results.Add(controlName, mappedColumn)
                            End If
                        End While
                    End Using
                End Using
            End Using

            Return results
        End Function

        Private Shared Function ExtractColumnNameFromFileLink(fileLink As String, tableName As String) As String
            If String.IsNullOrWhiteSpace(fileLink) Then
                Return String.Empty
            End If

            Dim trimmed = fileLink.Trim()
            Dim dotIndex = trimmed.LastIndexOf("."c)
            If dotIndex <= 0 OrElse dotIndex >= trimmed.Length - 1 Then
                Return String.Empty
            End If

            Dim tablePart = trimmed.Substring(0, dotIndex).Trim()
            Dim columnPart = trimmed.Substring(dotIndex + 1).Trim()
            If columnPart = String.Empty Then
                Return String.Empty
            End If

            If String.IsNullOrWhiteSpace(tableName) Then
                Return columnPart
            End If

            If tablePart.Equals(tableName, StringComparison.OrdinalIgnoreCase) OrElse
               tablePart.Equals("dbo." & tableName, StringComparison.OrdinalIgnoreCase) Then
                Return columnPart
            End If

            Return String.Empty
        End Function

        Private Shared Function NormalizeTableName(tableName As String) As String
            Dim normalized = If(tableName, String.Empty).Trim()
            If normalized = String.Empty Then
                Return String.Empty
            End If

            If normalized.StartsWith("dbo.", StringComparison.OrdinalIgnoreCase) Then
                normalized = normalized.Substring(4)
            End If

            Return normalized
        End Function

        Public Shared Sub ApplyControlUpdates(form As System.Windows.Forms.Form, pageName As String,
                                              Optional isNewRecord As Boolean = False)
            Try
                Dim updates = GetControlUpdates(pageName)
                If updates.Rows.Count = 0 Then Return

                Dim errors As New List(Of String)()

                For Each row As DataRow In updates.Rows
                    Dim controlName As String = String.Empty
                    Try
                        controlName = If(row("ControlName") Is DBNull.Value, String.Empty, row("ControlName").ToString().Trim())
                        Dim linkedControl = If(row("LinkedControl") Is DBNull.Value, String.Empty, row("LinkedControl").ToString().Trim())
                        Dim isRequired = If(row("IsRequired") Is DBNull.Value, False, CBool(row("IsRequired")))
                        Dim overrideCaption = If(row("OverrideCaption") Is DBNull.Value, String.Empty, row("OverrideCaption").ToString().Trim())

                        ' Field-level permissions from FW_RoleFields. Applied before required
                        ' styling so a hidden or unreadable field is never left interactive.
                        Dim fieldHidden = ApplyFieldPermissions(form, controlName, linkedControl, row, isNewRecord)

                        ' Apply required border and live validation events. A hidden field is
                        ' skipped: its border panel would be a stray visible control on a row that
                        ' otherwise has nothing left on it.
                        If isRequired AndAlso Not fieldHidden AndAlso Not String.IsNullOrWhiteSpace(controlName) Then
                            If ShouldSkipBrRequiredStyling(form, controlName, linkedControl) Then
                                Continue For
                            End If

                            Dim matches = form.Controls.Find(controlName, True)
                            If matches.Length > 0 Then
                                Dim ctrl = matches(0)
                                ctrl.Tag = "Required"

                                ' Hidden until the field is actually left empty. Defaulting to
                                ' visible put a stray control on the row, which also stopped a
                                ' hidden field's row from collapsing.
                                Dim borderPanel As New System.Windows.Forms.Panel() With {
                                    .Visible = False,
                                    .BackColor = SystemColors.Control,
                                    .Location = New System.Drawing.Point(ctrl.Left - 1, ctrl.Top - 1),
                                    .Size = New System.Drawing.Size(ctrl.Width + 2, ctrl.Height + 2),
                                    .Tag = "RequiredBorder_" & controlName
                                }
                                form.Controls.Add(borderPanel)
                                borderPanel.BringToFront()
                                ctrl.BringToFront()

                                ' Visibility is not managed here. FW_Base_U adopts this panel and
                                ' applies the shared rule - red only once the user has visited the
                                ' field and left it empty - so both kinds of required border on a
                                ' page behave identically.
                            End If
                        End If

                        ' Update label/linked control caption if override provided
                        If Not String.IsNullOrWhiteSpace(linkedControl) AndAlso Not String.IsNullOrWhiteSpace(overrideCaption) Then
                            Dim labelMatches = form.Controls.Find(linkedControl, True)
                            If labelMatches.Length > 0 Then
                                Dim labelCtrl = labelMatches(0)
                                labelCtrl.Text = EnsureRequiredMarker(overrideCaption)
                            End If
                        End If

                        ' Add asterisk to the associated label after override caption is applied
                        If isRequired AndAlso Not String.IsNullOrWhiteSpace(controlName) Then
                            Dim suffix As String = String.Empty
                            If controlName.StartsWith("TextBox_") Then : suffix = controlName.Substring(8)
                            ElseIf controlName.StartsWith("ComboBox_") Then : suffix = controlName.Substring(9)
                            ElseIf controlName.StartsWith("CheckBox_") Then : suffix = controlName.Substring(9)
                            ElseIf controlName.StartsWith("DateTimePicker_") Then : suffix = controlName.Substring(15)
                            ElseIf controlName.StartsWith("NumericUpDown_") Then : suffix = controlName.Substring(14)
                            ElseIf controlName.StartsWith("MaskedTextBox_") Then : suffix = controlName.Substring(14)
                            ElseIf controlName.StartsWith("RichTextBox_") Then : suffix = controlName.Substring(12)
                            End If

                            ' Try Label_X first, then fall back to LinkedControl, then the control itself
                            Dim asteriskTarget As String = String.Empty
                            If Not String.IsNullOrWhiteSpace(suffix) Then
                                Dim derivedLabel = "Label_" & suffix
                                If form.Controls.Find(derivedLabel, True).Length > 0 Then
                                    asteriskTarget = derivedLabel
                                End If
                            End If
                            If String.IsNullOrWhiteSpace(asteriskTarget) Then
                                asteriskTarget = If(Not String.IsNullOrWhiteSpace(linkedControl), linkedControl, controlName)
                            End If

                            If Not String.IsNullOrWhiteSpace(asteriskTarget) Then
                                Dim labelMatches = form.Controls.Find(asteriskTarget, True)
                                If labelMatches.Length > 0 Then
                                    labelMatches(0).Text = EnsureRequiredMarker(labelMatches(0).Text)
                                    labelMatches(0).BackColor = System.Drawing.Color.FromArgb(255, 255, 224)
                                End If
                            End If
                        End If

                    Catch ex As Exception
                        errors.Add($"'{controlName}': {ex.Message}")
                    End Try
                Next

                If errors.Count > 0 Then
                    System.Windows.Forms.MessageBox.Show(
                        String.Join(Environment.NewLine, errors),
                        "Control Update Warnings",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning)
                End If

            Catch ex As Exception
                ' Do not abend the page on outer failure
            End Try
        End Sub

        ''' <summary>
        ''' Validates every control flagged IsUnique in FW_RoleFields against the underlying table,
        ''' scoped to the active RegistrationID and excluding the record being edited.
        ''' Comparison is case-insensitive; blank values are skipped so several optional fields may
        ''' be left empty. Returns True when all checks pass.
        ''' </summary>
        Public Shared Function ValidateUniqueFields(form As System.Windows.Forms.Form,
                                                    pageName As String,
                                                    tableName As String,
                                                    isNewRecord As Boolean,
                                                    currentRecordKey As String,
                                                    ByRef errorMessage As String) As Boolean
            errorMessage = String.Empty
            Dim failures As New List(Of String)()

            Try
                Dim normalizedTable = NormalizeTableName(tableName)
                If String.IsNullOrWhiteSpace(normalizedTable) Then Return True

                Dim updates = GetControlUpdates(pageName)
                If updates.Rows.Count = 0 Then Return True

                Dim keyColumn = GetPrimaryKeyColumn(normalizedTable)
                Dim registrationId = If(SessionState.IsActive, SessionState.Current.Value.RegistrationID, 0)

                For Each row As DataRow In updates.Rows
                    If Not FlagOrDefault(row, "IsUnique", False) Then Continue For

                    Dim controlName = If(row("ControlName") Is DBNull.Value, String.Empty, row("ControlName").ToString().Trim())
                    If String.IsNullOrWhiteSpace(controlName) Then Continue For

                    Dim matches = form.Controls.Find(controlName, True)
                    If matches.Length = 0 Then Continue For
                    Dim ctrl = matches(0)

                    ' A masked field is not being changed, so it cannot introduce a duplicate.
                    If FieldPermissions.IsMasked(ctrl) Then Continue For

                    Dim value = If(ctrl.Text, String.Empty).Trim()
                    If value = String.Empty Then Continue For

                    Dim columnName = InferColumnNameFromControl(controlName)
                    If String.IsNullOrWhiteSpace(columnName) Then Continue For

                    ' Without a key column and a current key there is no way to exclude the record
                    ' from its own check, which would report every saved value as duplicated.
                    If Not isNewRecord AndAlso
                       (String.IsNullOrWhiteSpace(keyColumn) OrElse String.IsNullOrWhiteSpace(currentRecordKey)) Then
                        Continue For
                    End If

                    If CountMatchingValues(normalizedTable, columnName, value, registrationId,
                                           keyColumn, If(isNewRecord, String.Empty, currentRecordKey)) > 0 Then
                        Dim caption = DisplayNameFormatter.ToDisplayName(columnName)
                        failures.Add(caption & " must be unique. '" & value & "' is already in use.")
                    End If
                Next
            Catch ex As Exception
                ' A failed uniqueness lookup must not silently pass the save.
                failures.Add("Unique validation could not be completed: " & ex.Message)
            End Try

            If failures.Count = 0 Then Return True

            errorMessage = String.Join(Environment.NewLine, failures)
            Return False
        End Function

        Private Shared Function CountMatchingValues(tableName As String,
                                                    columnName As String,
                                                    value As String,
                                                    registrationId As Integer,
                                                    keyColumn As String,
                                                    excludeKey As String) As Integer
            Dim sql As New StringBuilder()
            sql.Append("SELECT COUNT(1) FROM dbo.[").Append(tableName).Append("] WHERE LOWER(LTRIM(RTRIM([")
            sql.Append(columnName).Append("]))) = @Value")

            Dim hasDeletedFlag = TableHasColumn(tableName, "DeletedFlag")
            If hasDeletedFlag Then sql.Append(" AND ISNULL(DeletedFlag, 0) = 0")

            Dim hasRegistration = TableHasColumn(tableName, "RegistrationID")
            If hasRegistration AndAlso registrationId > 0 Then sql.Append(" AND RegistrationID = @RegistrationID")

            If Not String.IsNullOrWhiteSpace(keyColumn) AndAlso Not String.IsNullOrWhiteSpace(excludeKey) Then
                sql.Append(" AND CONVERT(nvarchar(64), [").Append(keyColumn).Append("]) <> @ExcludeKey")
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(sql.ToString(), conn)
                    cmd.Parameters.AddWithValue("@Value", value.ToLowerInvariant())
                    If hasRegistration AndAlso registrationId > 0 Then
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    End If
                    If Not String.IsNullOrWhiteSpace(keyColumn) AndAlso Not String.IsNullOrWhiteSpace(excludeKey) Then
                        cmd.Parameters.AddWithValue("@ExcludeKey", excludeKey)
                    End If
                    Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        ''' <summary>
        ''' Who soft-deleted a record and when, or Nothing when it is not deleted, the table does
        ''' not support soft delete, or the row cannot be found.
        '''
        ''' A save that matches no row is reported as a concurrency conflict, but a soft-deleted
        ''' record is not a conflict - the row still exists, so an overwrite would succeed and quietly
        ''' write the user's edits onto a deleted record. Callers use this to tell the two apart.
        ''' </summary>
        Public Shared Function GetSoftDeleteInfo(tableName As String, recordId As Integer) As SoftDeleteInfo
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) OrElse recordId <= 0 Then Return Nothing

            Dim keyColumn = GetPrimaryKeyColumn(normalizedTable)
            If String.IsNullOrWhiteSpace(keyColumn) Then Return Nothing

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()

                    Dim columns = New List(Of String)()
                    Using schemaCmd As New SqlCommand(
                        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " &
                        "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName", conn)
                        schemaCmd.Parameters.AddWithValue("@TableName", normalizedTable)
                        Using reader = schemaCmd.ExecuteReader()
                            While reader.Read()
                                columns.Add(reader.GetString(0))
                            End While
                        End Using
                    End Using

                    If Not columns.Any(Function(name) String.Equals(name, "DeletedFlag", StringComparison.OrdinalIgnoreCase)) Then
                        Return Nothing
                    End If

                    Dim hasDeletedBy = columns.Any(Function(name) String.Equals(name, "DeletedBy", StringComparison.OrdinalIgnoreCase))
                    Dim hasDeletedOn = columns.Any(Function(name) String.Equals(name, "DeletedOn", StringComparison.OrdinalIgnoreCase))

                    Dim sql = "SELECT ISNULL(t.DeletedFlag, 0) AS DeletedFlag" &
                              If(hasDeletedOn, ", t.DeletedOn", ", CAST(NULL AS datetime) AS DeletedOn") &
                              If(hasDeletedBy, ", ISNULL(u.FirstLast, '') AS DeletedByName", ", '' AS DeletedByName") &
                              " FROM dbo." & QuoteGeneratedIdentifier(normalizedTable) & " t" &
                              If(hasDeletedBy, " LEFT JOIN dbo.FW_Users u ON u.UserID = t.DeletedBy", String.Empty) &
                              " WHERE t." & QuoteGeneratedIdentifier(keyColumn) & " = @RecordID"

                    Using cmd As New SqlCommand(sql, conn)
                        cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                        Using reader = cmd.ExecuteReader()
                            If Not reader.Read() Then Return Nothing
                            If Not Convert.ToBoolean(reader("DeletedFlag"), CultureInfo.InvariantCulture) Then Return Nothing

                            ' DeletedOn is written by SYSUTCDATETIME() and comes back with Kind
                            ' Unspecified, so it has to be marked UTC before converting - otherwise
                            ' the message shows a time hours away from the user's clock.
                            Dim deletedOn As Date? = Nothing
                            If Not reader("DeletedOn") Is DBNull.Value Then
                                deletedOn = Date.SpecifyKind(Convert.ToDateTime(reader("DeletedOn"), CultureInfo.InvariantCulture),
                                                             DateTimeKind.Utc).ToLocalTime()
                            End If

                            Return New SoftDeleteInfo With {
                                .DeletedByName = If(reader("DeletedByName") Is DBNull.Value, String.Empty, reader("DeletedByName").ToString().Trim()),
                                .DeletedOn = deletedOn
                            }
                        End Using
                    End Using
                End Using
            Catch
                Return Nothing
            End Try
        End Function

        Public Shared Function GetPrimaryKeyColumn(tableName As String) As String
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) Then Return String.Empty

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 kcu.COLUMN_NAME " &
                    "FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS tc " &
                    "INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE kcu " &
                    "  ON tc.CONSTRAINT_NAME = kcu.CONSTRAINT_NAME " &
                    "WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY' " &
                    "  AND tc.TABLE_SCHEMA = 'dbo' AND tc.TABLE_NAME = @TableName " &
                    "ORDER BY kcu.ORDINAL_POSITION", conn)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                    Dim result = cmd.ExecuteScalar()
                    Return If(result Is Nothing OrElse IsDBNull(result), String.Empty, result.ToString())
                End Using
            End Using
        End Function

        Private Shared Function InferColumnNameFromControl(controlName As String) As String
            Dim prefixes = New String() {"TextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_",
                                         "NumericUpDown_", "MaskedTextBox_", "RichTextBox_"}
            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return controlName.Substring(prefix.Length)
                End If
            Next
            Return String.Empty
        End Function

        ''' <summary>
        ''' Applies Make_Invisible, Can_Read, Can_Create and Can_Update from FW_RoleFields to a
        ''' single control and its Label_ partner. Column-level permissions on FW_RoleDetails
        ''' govern the CRUD buttons; these govern the individual field.
        ''' </summary>
        ''' <returns>
        ''' True when the field was hidden outright. The caller uses this to skip the required
        ''' border: a hidden field must not leave a border panel behind on its row.
        ''' </returns>
        Private Shared Function ApplyFieldPermissions(form As System.Windows.Forms.Form,
                                                      controlName As String,
                                                      linkedControl As String,
                                                      row As DataRow,
                                                      isNewRecord As Boolean) As Boolean
            If String.IsNullOrWhiteSpace(controlName) Then Return False

            Dim matches = form.Controls.Find(controlName, True)
            If matches.Length = 0 Then Return False
            Dim ctrl = matches(0)

            Dim label As System.Windows.Forms.Control = Nothing
            Dim labelName = ResolveLabelNameForControl(controlName, linkedControl)
            If Not String.IsNullOrWhiteSpace(labelName) Then
                Dim labelMatches = form.Controls.Find(labelName, True)
                If labelMatches.Length > 0 Then label = labelMatches(0)
            End If

            If FlagOrDefault(row, "Make_Invisible", False) Then
                FieldPermissions.HideField(ctrl, label)
                Return True
            End If

            ' Can_Read false means the value must not be disclosed. The mask is displayed and the
            ' real value is preserved by FieldPermissions so the save writes it back unchanged.
            If Not FlagOrDefault(row, "Can_Read", True) Then
                FieldPermissions.Mask(ctrl)
                Return False
            End If

            Dim entryAllowed = If(isNewRecord,
                                  FlagOrDefault(row, "Can_Create", True),
                                  FlagOrDefault(row, "Can_Update", True))
            If Not entryAllowed Then
                FieldPermissions.SetNoEntry(ctrl)
            End If

            Return False
        End Function

        ''' <summary>Reads a bit column that may be absent from the result set or null.</summary>
        Private Shared Function FlagOrDefault(row As DataRow, columnName As String, defaultValue As Boolean) As Boolean
            If row Is Nothing OrElse Not row.Table.Columns.Contains(columnName) Then Return defaultValue
            If row(columnName) Is DBNull.Value Then Return defaultValue
            Return Convert.ToBoolean(row(columnName))
        End Function

        ''' <summary>
        ''' Resolves the Label_ partner for a control, following the framework naming convention
        ''' and falling back to an explicit LinkedControl when one is configured.
        ''' </summary>
        Private Shared Function ResolveLabelNameForControl(controlName As String, linkedControl As String) As String
            Dim prefixes = New String() {"TextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_",
                                         "NumericUpDown_", "MaskedTextBox_", "RichTextBox_"}

            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return "Label_" & controlName.Substring(prefix.Length)
                End If
            Next

            Return If(linkedControl, String.Empty)
        End Function

        Private Shared Function EnsureRequiredMarker(caption As String) As String
            Dim result = If(caption, String.Empty).Trim()
            While result.EndsWith("*", StringComparison.Ordinal)
                result = result.Substring(0, result.Length - 1).TrimEnd()
            End While

            If String.IsNullOrWhiteSpace(result) Then
                Return "*"
            End If

            Return result & " *"
        End Function


        Private Shared Function ShouldSkipBrRequiredStyling(form As System.Windows.Forms.Form, controlName As String, linkedControl As String) As Boolean
            If form Is Nothing OrElse String.IsNullOrWhiteSpace(controlName) Then
                Return False
            End If

            Dim labelTargets As New List(Of String)()

            Dim suffix As String = String.Empty
            If controlName.StartsWith("TextBox_") Then : suffix = controlName.Substring(8)
            ElseIf controlName.StartsWith("ComboBox_") Then : suffix = controlName.Substring(9)
            ElseIf controlName.StartsWith("CheckBox_") Then : suffix = controlName.Substring(9)
            ElseIf controlName.StartsWith("DateTimePicker_") Then : suffix = controlName.Substring(15)
            ElseIf controlName.StartsWith("NumericUpDown_") Then : suffix = controlName.Substring(14)
            ElseIf controlName.StartsWith("MaskedTextBox_") Then : suffix = controlName.Substring(14)
            ElseIf controlName.StartsWith("RichTextBox_") Then : suffix = controlName.Substring(12)
            End If

            If Not String.IsNullOrWhiteSpace(suffix) Then
                labelTargets.Add("Label_" & suffix)
            End If

            If Not String.IsNullOrWhiteSpace(linkedControl) Then
                labelTargets.Add(linkedControl)
            End If

            labelTargets.Add(controlName)

            For Each targetName In labelTargets
                Dim matches = form.Controls.Find(targetName, True)
                If matches Is Nothing OrElse matches.Length = 0 Then
                    Continue For
                End If

                Dim labelCtrl = TryCast(matches(0), System.Windows.Forms.Control)
                If labelCtrl Is Nothing Then
                    Continue For
                End If

                If labelCtrl.BackColor.ToArgb() = System.Drawing.Color.FromArgb(221, 235, 247).ToArgb() Then
                    Return True
                End If
            Next

            Return False
        End Function

        ''' Validates required controls before save using Tag="Required" set at load time.
        ''' Returns True if valid, False if any required fields are empty.
        ''' </summary>
        ''' <summary>
        ''' Whether a combo is sitting on no real selection. Single owner of this test - the
        ''' "Make a Selection" placeholder row carries 0 for numeric lookups and an empty string
        ''' for text-keyed ones, and both count as empty. A value that is text rather than a
        ''' number is a genuine selection: it must not be treated as empty just because it does
        ''' not parse as an integer.
        ''' </summary>
        Public Shared Function IsEmptyComboSelection(combo As System.Windows.Forms.ComboBox) As Boolean
            If combo Is Nothing OrElse combo.SelectedIndex < 0 OrElse String.IsNullOrWhiteSpace(combo.Text) Then
                Return True
            End If

            If combo.SelectedValue Is Nothing OrElse IsDBNull(combo.SelectedValue) Then
                Return True
            End If

            Dim valueText = combo.SelectedValue.ToString().Trim()
            If valueText = String.Empty Then
                Return True
            End If

            Dim numericValue As Integer
            If Integer.TryParse(valueText, numericValue) Then
                Return numericValue <= 0
            End If

            Return False
        End Function

        ''' <param name="firstEmptyControl">
        ''' Receives the first control reported missing, in the same top-to-bottom, left-to-right
        ''' order the message lists them, so the caller can put the cursor there.
        ''' </param>
        Public Shared Function ValidateRequiredControls(form As System.Windows.Forms.Form,
                                                       ByRef errorMessage As String,
                                                       Optional ByRef firstEmptyControl As System.Windows.Forms.Control = Nothing) As Boolean
            errorMessage = String.Empty
            firstEmptyControl = Nothing
            Dim errors As New List(Of String)()
            Try
                Dim allControls As New List(Of System.Windows.Forms.Control)()
                CollectAllControls(form, allControls)

                ' Listed in tab order - the sequence the user actually moves through - so the
                ' message reads in the order the fields are reached and focus lands on the first
                ' one they will come to. A control that is not a tab stop has no meaningful
                ' TabIndex, so those fall to the end and are ordered by position instead.
                Dim requiredControls = allControls.Where(Function(c) c.Tag IsNot Nothing AndAlso c.Tag.ToString() = "Required")
                For Each ctrl In requiredControls.OrderBy(Function(c) If(c.TabStop, 0, 1)).
                                             ThenBy(Function(c) c.TabIndex).
                                             ThenBy(Function(c) GetAbsoluteLocation(c).Y).
                                             ThenBy(Function(c) GetAbsoluteLocation(c).X).
                                             ThenBy(Function(c) c.Name, StringComparer.OrdinalIgnoreCase)
                    Dim isEmpty As Boolean = False
                    Try
                        Dim ctrlType = ctrl.GetType()
                        Select Case ctrlType
                            Case GetType(System.Windows.Forms.ComboBox)
                                isEmpty = IsEmptyComboSelection(CType(ctrl, System.Windows.Forms.ComboBox))
                            Case GetType(System.Windows.Forms.TextBox)
                                isEmpty = String.IsNullOrWhiteSpace(ctrl.Text)
                            Case GetType(System.Windows.Forms.MaskedTextBox)
                                isEmpty = String.IsNullOrWhiteSpace(ctrl.Text)
                            Case GetType(System.Windows.Forms.RichTextBox)
                                isEmpty = String.IsNullOrWhiteSpace(ctrl.Text)
                            Case GetType(System.Windows.Forms.NumericUpDown)
                                Dim nud = CType(ctrl, System.Windows.Forms.NumericUpDown)
                                isEmpty = nud.Value = nud.Minimum
                            Case GetType(System.Windows.Forms.DateTimePicker)
                                Dim dtp = CType(ctrl, System.Windows.Forms.DateTimePicker)
                                isEmpty = dtp.Value = dtp.MinDate
                        End Select

                        If isEmpty Then
                            errors.Add(ResolveRequiredControlCaption(form, ctrl).ToUpperInvariant())
                            If firstEmptyControl Is Nothing Then firstEmptyControl = ctrl
                        End If
                    Catch
                        ' Skip control on error
                    End Try
                Next
            Catch
                ' Do not abend
            End Try
            If errors.Count > 0 Then
                errorMessage = "THE FOLLOWING ARE REQUIRED:" & Environment.NewLine & Environment.NewLine & String.Join(Environment.NewLine, errors)
                Return False
            End If
            Return True
        End Function

        Private Shared Function ResolveRequiredControlCaption(form As System.Windows.Forms.Form, ctrl As System.Windows.Forms.Control) As String
            If form Is Nothing OrElse ctrl Is Nothing OrElse String.IsNullOrWhiteSpace(ctrl.Name) Then
                Return String.Empty
            End If

            Dim labelTargets As New List(Of String)()

            Dim suffix As String = String.Empty
            If ctrl.Name.StartsWith("TextBox_") Then : suffix = ctrl.Name.Substring(8)
            ElseIf ctrl.Name.StartsWith("ComboBox_") Then : suffix = ctrl.Name.Substring(9)
            ElseIf ctrl.Name.StartsWith("CheckBox_") Then : suffix = ctrl.Name.Substring(9)
            ElseIf ctrl.Name.StartsWith("DateTimePicker_") Then : suffix = ctrl.Name.Substring(15)
            ElseIf ctrl.Name.StartsWith("NumericUpDown_") Then : suffix = ctrl.Name.Substring(14)
            ElseIf ctrl.Name.StartsWith("MaskedTextBox_") Then : suffix = ctrl.Name.Substring(14)
            ElseIf ctrl.Name.StartsWith("RichTextBox_") Then : suffix = ctrl.Name.Substring(12)
            End If

            If Not String.IsNullOrWhiteSpace(suffix) Then
                labelTargets.Add("Label_" & suffix)
            End If

            labelTargets.Add(ctrl.Name)

            For Each targetName In labelTargets
                Dim matches = form.Controls.Find(targetName, True)
                If matches Is Nothing OrElse matches.Length = 0 Then
                    Continue For
                End If

                Dim labelCtrl = TryCast(matches(0), System.Windows.Forms.Control)
                If labelCtrl Is Nothing OrElse String.IsNullOrWhiteSpace(labelCtrl.Text) Then
                    Continue For
                End If

                Dim caption = labelCtrl.Text.Trim()
                While caption.EndsWith("*", StringComparison.Ordinal)
                    caption = caption.Substring(0, caption.Length - 1).TrimEnd()
                End While

                If Not String.IsNullOrWhiteSpace(caption) Then
                    Return caption
                End If
            Next

            Return ctrl.Name.Replace("TextBox_", "").Replace("ComboBox_", "").Replace("MaskedTextBox_", "").Replace("RichTextBox_", "").Replace("NumericUpDown_", "").Replace("DateTimePicker_", "")
        End Function

        Private Shared Function GetAbsoluteLocation(ctrl As System.Windows.Forms.Control) As System.Drawing.Point
            Dim x As Integer = 0
            Dim y As Integer = 0
            Dim current As System.Windows.Forms.Control = ctrl

            While current IsNot Nothing
                x += current.Left
                y += current.Top
                current = current.Parent
            End While

            Return New System.Drawing.Point(x, y)
        End Function

        Friend Shared Sub CollectAllControls(container As System.Windows.Forms.Control, list As List(Of System.Windows.Forms.Control))
            For Each ctrl As System.Windows.Forms.Control In container.Controls
                list.Add(ctrl)
                If ctrl.Controls.Count > 0 Then
                    CollectAllControls(ctrl, list)
                End If
            Next
        End Sub

        Public Shared Function GetRoleDisplayOrder(roleId As Integer) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ISNULL(DisplayOrder, 0) FROM dbo.FW_Roles WHERE ID = @RoleID", conn)
                    
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso IsNumeric(result) Then
                        Return CInt(result)
                    End If
                    Return 0
                End Using
            End Using
        End Function

        Public Shared Function GetRoleIsActive(roleId As Integer) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ISNULL(IsActive, 0) FROM dbo.FW_Roles WHERE ID = @RoleID", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        Return CBool(result)
                    End If
                    Return False
                End Using
            End Using
        End Function

        Public Shared Function GetRoleTypAppAdmin(roleId As Integer) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ISNULL(Typ_AppAdmin, 0) FROM dbo.FW_Roles WHERE ID = @RoleID", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        Return CBool(result)
                    End If
                    Return False
                End Using
            End Using
        End Function

        ' Compatibility wrapper for existing callers.
        Public Shared Function GetRoleTypApplicationAdmin(roleId As Integer) As Boolean
            Return GetRoleTypAppAdmin(roleId)
        End Function

        Public Shared Function GetRoleTypCompanyAdmin(roleId As Integer) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ISNULL(Typ_CompanyAdmin, 0) FROM dbo.FW_Roles WHERE ID = @RoleID", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        Return CBool(result)
                    End If
                    Return False
                End Using
            End Using
        End Function

        ''' <summary>Returns the role ID where Typ_AppAdmin=1 for the given registration, or 0 if not found.</summary>
        Public Shared Function GetAppAdminRoleId(registrationId As Integer) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ID FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID AND Typ_AppAdmin = 1 AND IsActive = 1", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        ' Compatibility wrapper for existing callers.
        Public Shared Function GetApplicationAdminRoleId(registrationId As Integer) As Integer
            Return GetAppAdminRoleId(registrationId)
        End Function

        ''' <summary>Returns the role ID where Typ_CompanyAdmin=1 for the given registration, or 0 if not found.</summary>
        Public Shared Function GetCompanyAdminRoleId(registrationId As Integer) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ID FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID AND Typ_CompanyAdmin = 1 AND IsActive = 1", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        ''' <summary>Returns the user ID assigned to the Company Admin role for the registration, or 0 if not found.</summary>
        Public Shared Function GetCompanyAdminUserId(registrationId As Integer) As Integer
            If registrationId <= 0 Then
                Return 0
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 ur.UserID " &
                    "FROM dbo.FW_UserRoles ur " &
                    "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                    "INNER JOIN dbo.FW_Users u ON u.UserID = ur.UserID " &
                    "WHERE ur.RegistrationID = @RegistrationID " &
                    "  AND r.RegistrationID = @RegistrationID " &
                    "  AND ISNULL(r.Typ_CompanyAdmin, 0) = 1 " &
                    "  AND ISNULL(r.IsActive, 0) = 1 " &
                    "  AND ISNULL(ur.IsActive, 1) = 1 " &
                    "  AND ISNULL(u.IsActive, 1) = 1 " &
                    "ORDER BY ISNULL(ur.DisplayOrder, 0), ur.UserID", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        Public Shared Sub UpdateRoleIsActive(roleId As Integer, isActive As Boolean)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Roles SET IsActive = @IsActive WHERE ID = @ID", conn)
                    cmd.Parameters.AddWithValue("@ID", roleId)
                    cmd.Parameters.AddWithValue("@IsActive", isActive)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Sub UpdateRoleNameAndDisplayOrder(roleId As Integer, roleName As String, displayOrder As Integer, isActive As Boolean,
            Optional typAppAdmin As Boolean = False, Optional typCompanyAdmin As Boolean = False)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Roles SET RoleName = @RoleName, DisplayOrder = @DisplayOrder, IsActive = @IsActive, " &
                    "Typ_AppAdmin = @Typ_AppAdmin, Typ_CompanyAdmin = @Typ_CompanyAdmin WHERE ID = @RoleID", conn)
                    
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Dim nameValue As Object = If(String.IsNullOrWhiteSpace(roleName), CType(DBNull.Value, Object), CType(roleName.Trim(), Object))
                    cmd.Parameters.AddWithValue("@RoleName", nameValue)
                    cmd.Parameters.AddWithValue("@DisplayOrder", displayOrder)
                    cmd.Parameters.AddWithValue("@IsActive", isActive)
                    cmd.Parameters.AddWithValue("@Typ_AppAdmin", typAppAdmin)
                    cmd.Parameters.AddWithValue("@Typ_CompanyAdmin", typCompanyAdmin)
                    
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Function InsertRoleFieldsForTable(
            schemaId As Integer,
            tableName As String,
            registrationId As Integer,
            roleId As Integer,
            updatedBy As Integer) As Integer
            
            Dim insertedCount = 0
            
            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    
                    ' Get RoleDetailID from FW_RoleDetails for this (RoleID, SchemaID) pair
                    Dim roleDetailId As Integer = 0
                    Using cmd As New SqlCommand(
                        "SELECT ID FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND SchemaID = @SchemaID AND RegistrationID = @RegistrationID", conn)
                        cmd.Parameters.AddWithValue("@RoleID", roleId)
                        cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        Dim result = cmd.ExecuteScalar()
                        If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                            roleDetailId = CInt(result)
                        Else
                            Return 0  ' No RoleDetails entry, can't insert fields
                        End If
                    End Using
                    
                    ' Get all columns from sys.columns (with proper casing)
                    Dim excludeFields As New HashSet(Of String) From {"CREATEDBY", "CREATEDON", "UPDATEDBY", "UPDATEDON"}
                    
                    ' Get Primary Keys to exclude
                    Using cmd As New SqlCommand(
                        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE " &
                        "WHERE TABLE_NAME = @TableName AND CONSTRAINT_NAME LIKE 'PK%'", conn)
                        cmd.Parameters.AddWithValue("@TableName", tableName)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                excludeFields.Add(reader("COLUMN_NAME").ToString().ToUpper())
                            End While
                        End Using
                    End Using
                    
                    ' Get all schema columns
                    Dim schemaColumns As New List(Of String)()
                    Dim excludeFieldsUpper = excludeFields.Select(Function(f) f.ToUpper()).ToHashSet()
                    
                    Using cmd As New SqlCommand(
                        "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@TableName) ORDER BY column_id", conn)
                        cmd.Parameters.AddWithValue("@TableName", tableName)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim colName = reader("name").ToString()
                                Dim colNameUpper = colName.ToUpper()
                                If Not excludeFieldsUpper.Contains(colNameUpper) Then
                                    schemaColumns.Add(colName)  ' Keep proper casing
                                End If
                            End While
                        End Using
                    End Using
                    
                    ' Insert all schema fields for this role
                    For Each colName In schemaColumns
                        Dim friendlyName = FormatFieldName(colName)
                        Dim FileLink = tableName & "." & colName
                        
                        Using cmd As New SqlCommand(
                            "INSERT INTO dbo.FW_RoleFields " &
                            "(RegistrationID, RoleID, RoleDetailID, SchemaID, TableName, FieldName, FileLink, FriendlyFieldName, " &
                            "Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                            "VALUES (@RegistrationID, @RoleID, @RoleDetailID, @SchemaID, @TableName, @FieldName, @FileLink, @FriendlyFieldName, " &
                            "1, 1, 1, 0, @UpdatedBy, GETDATE())", conn)
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            cmd.Parameters.AddWithValue("@RoleID", roleId)
                            cmd.Parameters.AddWithValue("@RoleDetailID", roleDetailId)
                            cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                            cmd.Parameters.AddWithValue("@TableName", tableName)
                            cmd.Parameters.AddWithValue("@FieldName", colName)
                            cmd.Parameters.AddWithValue("@FileLink", FileLink)
                            cmd.Parameters.AddWithValue("@FriendlyFieldName", friendlyName)
                            cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                            insertedCount += cmd.ExecuteNonQuery()
                        End Using
                    Next
                    
                End Using
                
                Return insertedCount
            Catch ex As Exception
                Return 0
            End Try
        End Function

        Public Shared Function SyncRoleFieldsWithSchema(
            schemaId As Integer,
            tableName As String,
            registrationId As Integer,
            roleId As Integer,
            updatedBy As Integer,
            ByRef insertedCount As Integer,
            ByRef deletedCount As Integer) As Boolean
            
            Try
                insertedCount = 0
                deletedCount = 0
                
                Dim debugLog As New List(Of String)
                debugLog.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SyncRoleFieldsWithSchema started: SchemaId={schemaId}, Table={tableName}, RegId={registrationId}, RoleId={roleId}")
                
                ' Write initial log to confirm function was called
                Dim logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sync_debug.log")
                System.IO.File.WriteAllLines(logPath, debugLog)
                
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    debugLog.Add("[CONN] Connected to database")
                    
                    ' 1. Get all current FW_RoleFields for this SchemaID, RegistrationID, and RoleID
                    Dim currentFields As New HashSet(Of String)()
                    Using cmd As New SqlCommand(
                        "SELECT DISTINCT FieldName FROM dbo.FW_RoleFields WHERE SchemaID = @SchemaID AND RegistrationID = @RegistrationID AND RoleID = @RoleID", conn)
                        cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        cmd.Parameters.AddWithValue("@RoleID", roleId)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                currentFields.Add(reader("FieldName").ToString().ToUpper())
                            End While
                        End Using
                    End Using
                    debugLog.Add($"[QUERY1] Current fields in FW_RoleFields for this role: {currentFields.Count} ({String.Join(", ", currentFields.Take(5))}...)")
                    
                    ' 2. Get all columns from INFORMATION_SCHEMA for this table
                    Dim schemaColumns As New HashSet(Of String)()
                    Dim excludeFields As New HashSet(Of String) From {"CREATEDBY", "CREATEDON", "UPDATEDBY", "UPDATEDON"}
                    
                    ' Get Primary Keys to exclude
                    Using cmd As New SqlCommand(
                        "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.KEY_COLUMN_USAGE " &
                        "WHERE TABLE_NAME = @TableName AND CONSTRAINT_NAME LIKE 'PK%'", conn)
                        cmd.Parameters.AddWithValue("@TableName", tableName)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                excludeFields.Add(reader("COLUMN_NAME").ToString().ToUpper())
                            End While
                        End Using
                    End Using
                    debugLog.Add($"[QUERY2] Primary keys excluded: {String.Join(", ", excludeFields)}")
                    
                    ' Get all schema columns with proper casing from sys.columns
                    ' Build a dictionary of column names (uppercase key for comparison, actual casing as value)
                    Dim schemaColumnMap As New Dictionary(Of String, String)()
                    Dim excludeFieldsUpper = excludeFields.Select(Function(f) f.ToUpper()).ToHashSet()
                    
                    Using cmd As New SqlCommand(
                        "SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@TableName) ORDER BY column_id", conn)
                        cmd.Parameters.AddWithValue("@TableName", tableName)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim colName = reader("name").ToString()
                                Dim colNameUpper = colName.ToUpper()
                                If Not excludeFieldsUpper.Contains(colNameUpper) Then
                                    schemaColumnMap(colNameUpper) = colName  ' Store with proper casing
                                    schemaColumns.Add(colNameUpper)  ' Add uppercase for comparison
                                End If
                            End While
                        End Using
                    End Using
                    debugLog.Add($"[QUERY3] Schema columns in table: {schemaColumns.Count} ({String.Join(", ", schemaColumns.Take(5))}...)")
                    
                    ' 3. DELETE obsolete fields (in FW_RoleFields but NOT in schema) for this role
                    For Each fieldToDelete In currentFields
                        If Not schemaColumns.Contains(fieldToDelete) Then
                            Using cmd As New SqlCommand(
                                "DELETE FROM dbo.FW_RoleFields WHERE SchemaID = @SchemaID AND RegistrationID = @RegistrationID AND RoleID = @RoleID AND UPPER(FieldName) = @FieldName", conn)
                                cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                                cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                                cmd.Parameters.AddWithValue("@RoleID", roleId)
                                cmd.Parameters.AddWithValue("@FieldName", fieldToDelete)
                                deletedCount += cmd.ExecuteNonQuery()
                            End Using
                        End If
                    Next
                    debugLog.Add($"[DELETE] Deleted {deletedCount} obsolete fields")
                    
                    ' 4. INSERT new fields (in schema but NOT in FW_RoleFields) for this role
                    For Each fieldToInsert In schemaColumns
                        If Not currentFields.Contains(fieldToInsert) Then
                            ' Get the properly cased field name from the map
                            Dim properCasedField = schemaColumnMap(fieldToInsert)
                            debugLog.Add($"[INSERT] Will insert new field: {properCasedField}")
                            
                            Dim friendlyName = FormatFieldName(properCasedField)
                            Dim FileLink = tableName & "." & properCasedField
                            
                            ' Get RoleDetailsID from FW_RoleDetails for this (RoleID, SchemaID) pair
                            Dim roleDetailsId As Integer = 0
                            Using detailCmd As New SqlCommand(
                                "SELECT ID FROM dbo.FW_RoleDetails WHERE RoleID = @RoleID AND SchemaID = @SchemaID", conn)
                                detailCmd.Parameters.AddWithValue("@RoleID", roleId)
                                detailCmd.Parameters.AddWithValue("@SchemaID", schemaId)
                                Dim result = detailCmd.ExecuteScalar()
                                If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                                    roleDetailsId = CInt(result)
                                    debugLog.Add($"[INSERT] Found RoleDetailsID={roleDetailsId} for RoleID={roleId}, SchemaID={schemaId}")
                                Else
                                    debugLog.Add($"[INSERT] ERROR: No RoleDetailsID found for RoleID={roleId}, SchemaID={schemaId}")
                                End If
                            End Using
                            
                            Using cmd As New SqlCommand(
                                "INSERT INTO dbo.FW_RoleFields " &
                                "(RegistrationID, RoleID, RoleDetailID, SchemaID, TableName, FieldName, FileLink, FriendlyFieldName, " &
                                "Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                                "VALUES (@RegistrationID, @RoleID, @RoleDetailID, @SchemaID, @TableName, @FieldName, @FileLink, @FriendlyFieldName, " &
                                "1, 1, 1, 0, @UpdatedBy, GETDATE())", conn)
                                cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                                cmd.Parameters.AddWithValue("@RoleID", roleId)
                                cmd.Parameters.AddWithValue("@RoleDetailID", roleDetailsId)
                                cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                                cmd.Parameters.AddWithValue("@TableName", tableName)
                                cmd.Parameters.AddWithValue("@FieldName", properCasedField)
                                cmd.Parameters.AddWithValue("@FileLink", FileLink)
                                cmd.Parameters.AddWithValue("@FriendlyFieldName", friendlyName)
                                cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                                insertedCount += cmd.ExecuteNonQuery()
                            End Using
                        End If
                    Next
                    debugLog.Add($"[INSERT] Inserted {insertedCount} new fields")
                    debugLog.Add($"[SUCCESS] SyncRoleFieldsWithSchema completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                    
                    ' Write final debug log to both Temp and project folder
                    System.IO.File.WriteAllLines(logPath, debugLog)
                    Try
                        Dim projectLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync_debug.log")
                        System.IO.File.WriteAllLines(projectLogPath, debugLog)
                    Catch
                    End Try
                    
                End Using
                
                Return True
            Catch ex As Exception
                ' Write error log on failure
                Dim errorLog As New List(Of String)
                errorLog.Add($"[ERROR] SyncRoleFieldsWithSchema FAILED at {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
                errorLog.Add($"Message: {ex.Message}")
                errorLog.Add($"StackTrace: {ex.StackTrace}")
                Try
                    Dim logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sync_debug.log")
                    System.IO.File.WriteAllLines(logPath, errorLog)
                    Dim projectLogPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sync_debug.log")
                    System.IO.File.WriteAllLines(projectLogPath, errorLog)
                Catch
                End Try
                Return False
            End Try
        End Function

        Private Shared Function BuildDataViewFilterExpression(table As DataTable, filters As Dictionary(Of String, String)) As String
            Dim parts As New List(Of String)()

            For Each kvp In filters
                Dim rawKey = If(kvp.Key, String.Empty).Trim()
                Dim rawValue = If(kvp.Value, String.Empty).Trim()
                If rawValue = String.Empty Then Continue For

                Dim fieldName = rawKey
                Dim comparisonOperator As QbeComparisonOperator = QbeComparisonOperator.EqualsTo

                If rawKey.Contains("|") Then
                    Dim pieces = rawKey.Split("|"c)
                    fieldName = pieces(0)
                    If pieces.Length > 1 Then
                        [Enum].TryParse(pieces(1), True, comparisonOperator)
                    End If
                End If

                If Not table.Columns.Contains(fieldName) Then Continue For

                Dim col = table.Columns(fieldName)
                Dim expr = BuildSingleColumnFilterExpr(col, fieldName, comparisonOperator, rawValue)
                If Not String.IsNullOrEmpty(expr) Then parts.Add(expr)
            Next

            Return String.Join(" AND ", parts)
        End Function

        Private Shared Function BuildSingleColumnFilterExpr(col As DataColumn, fieldName As String, op As QbeComparisonOperator, value As String) As String
            Dim quotedField = "[" & fieldName.Replace("]", "]]" ) & "]"

            If col.DataType Is GetType(Boolean) Then
                Dim parsedBool As Boolean
                If Not TryParseBooleanFilter(value, parsedBool) Then Return String.Empty
                Return quotedField & " = " & parsedBool.ToString()
            End If

            If col.DataType Is GetType(Integer) OrElse col.DataType Is GetType(Int16) OrElse
               col.DataType Is GetType(Int64) OrElse col.DataType Is GetType(Single) OrElse
               col.DataType Is GetType(Double) OrElse col.DataType Is GetType(Decimal) Then
                Dim numOp As String
                Select Case op
                    Case QbeComparisonOperator.NotEquals         : numOp = "<>"
                    Case QbeComparisonOperator.GreaterThan       : numOp = ">"
                    Case QbeComparisonOperator.GreaterThanOrEqual : numOp = ">="
                    Case QbeComparisonOperator.LessThan          : numOp = "<"
                    Case QbeComparisonOperator.LessThanOrEqual   : numOp = "<="
                    Case Else                                    : numOp = "="
                End Select
                Return quotedField & " " & numOp & " " & value
            End If

            ' Text field
            Dim escaped = value.Replace("'", "''")
            Select Case op
                Case QbeComparisonOperator.NotEquals  : Return quotedField & " <> '"  & escaped & "'"
                Case QbeComparisonOperator.Contains   : Return quotedField & " LIKE '%" & escaped & "%'"
                Case QbeComparisonOperator.StartsWith : Return quotedField & " LIKE '"  & escaped & "%'"
                Case QbeComparisonOperator.EndsWith   : Return quotedField & " LIKE '%" & escaped & "'"
                Case Else                             : Return quotedField & " = '"    & escaped & "'"
            End Select
        End Function

        Public Shared Function GetSavedQbes(registrationId As Integer, userId As Integer, tableContext As String) As List(Of SavedQbeRecord)
            Dim results As New List(Of SavedQbeRecord)()
            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Dim sql As String =
                        "SELECT SavedQbeID, RegistrationID, UserID, QbeName, IsCompanyWide, TableContext, QbeData " &
                        "FROM dbo.FW_SavedQbe " &
                        "WHERE RegistrationID = @RegistrationID AND TableContext = @TableContext " &
                        "  AND (UserID = @UserID OR IsCompanyWide = 1) " &
                        "  AND ISNULL(DeletedFlag, 0) = 0 " &
                        "ORDER BY IsCompanyWide DESC, QbeName"
                    Using cmd As New SqlCommand(sql, conn)
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        cmd.Parameters.AddWithValue("@UserID", userId)
                        cmd.Parameters.AddWithValue("@TableContext", tableContext)
                        Using rdr = cmd.ExecuteReader()
                            While rdr.Read()
                                results.Add(New SavedQbeRecord With {
                                    .SavedQbeID = Convert.ToInt32(rdr("SavedQbeID")),
                                    .RegistrationID = Convert.ToInt32(rdr("RegistrationID")),
                                    .UserID = Convert.ToInt32(rdr("UserID")),
                                    .QbeName = rdr("QbeName").ToString(),
                                    .IsCompanyWide = Convert.ToBoolean(rdr("IsCompanyWide")),
                                    .TableContext = rdr("TableContext").ToString(),
                                    .QbeData = rdr("QbeData").ToString()
                                })
                            End While
                        End Using
                    End Using
                End Using
            Catch
            End Try
            Return results
        End Function

        Public Shared Function GetSchemaFromSelectSql(baseSelectSql As String, registrationId As Integer) As DataTable
            Dim schemaTable As New DataTable("SchemaOnly")
            If String.IsNullOrWhiteSpace(baseSelectSql) Then
                Return schemaTable
            End If

            Dim effectiveSql = baseSelectSql.Trim()
            If registrationId > 0 Then
                Dim registrationValue = registrationId.ToString(CultureInfo.InvariantCulture)
                effectiveSql = Regex.Replace(effectiveSql, "RegistrationID\s*=\s*(?:@RegistrationID|\?|\d+)", "RegistrationID = " & registrationValue, RegexOptions.IgnoreCase)
                effectiveSql = effectiveSql.Replace("@RegistrationID", registrationValue, StringComparison.OrdinalIgnoreCase)
                effectiveSql = effectiveSql.Replace("?", registrationValue)
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(effectiveSql, conn)
                        Using reader = cmd.ExecuteReader(CommandBehavior.SchemaOnly)
                            Dim providerSchema = reader.GetSchemaTable()
                            If providerSchema Is Nothing Then
                                Return schemaTable
                            End If

                            For Each row As DataRow In providerSchema.Rows
                                Dim colName = Convert.ToString(row("ColumnName"))
                                If String.IsNullOrWhiteSpace(colName) Then
                                    Continue For
                                End If

                                If schemaTable.Columns.Contains(colName) Then
                                    Continue For
                                End If

                                Dim dataType = TryCast(row("DataType"), Type)
                                If dataType Is Nothing Then
                                    dataType = GetType(String)
                                End If

                                schemaTable.Columns.Add(colName, dataType)
                            Next
                        End Using
                    End Using
                End Using
            Catch
                Return schemaTable
            End Try

            Return schemaTable
        End Function

        Public Shared Sub SaveQbe(record As SavedQbeRecord)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim existingId As Integer = 0
                Dim checkSql As String =
                    "SELECT SavedQbeID FROM dbo.FW_SavedQbe " &
                    "WHERE RegistrationID = @RegistrationID AND UserID = @UserID " &
                    "  AND QbeName = @QbeName AND TableContext = @TableContext " &
                    "  AND ISNULL(DeletedFlag, 0) = 0"
                Using cmd As New SqlCommand(checkSql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                    cmd.Parameters.AddWithValue("@UserID", record.UserID)
                    cmd.Parameters.AddWithValue("@QbeName", record.QbeName)
                    cmd.Parameters.AddWithValue("@TableContext", record.TableContext)
                    Dim result = cmd.ExecuteScalar()
                    If result IsNot Nothing AndAlso Not IsDBNull(result) Then
                        existingId = Convert.ToInt32(result)
                    End If
                End Using

                If existingId > 0 Then
                    Dim updateSql As String =
                        "UPDATE dbo.FW_SavedQbe SET IsCompanyWide = @IsCompanyWide, QbeData = @QbeData, UpdatedOn = GETDATE() " &
                        "WHERE SavedQbeID = @SavedQbeID"
                    Using cmd As New SqlCommand(updateSql, conn)
                        cmd.Parameters.AddWithValue("@IsCompanyWide", If(record.IsCompanyWide, 1, 0))
                        cmd.Parameters.AddWithValue("@QbeData", record.QbeData)
                        cmd.Parameters.AddWithValue("@SavedQbeID", existingId)
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Dim insertSql As String =
                        "INSERT INTO dbo.FW_SavedQbe (RegistrationID, UserID, QbeName, IsCompanyWide, TableContext, QbeData) " &
                        "VALUES (@RegistrationID, @UserID, @QbeName, @IsCompanyWide, @TableContext, @QbeData)"
                    Using cmd As New SqlCommand(insertSql, conn)
                        cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                        cmd.Parameters.AddWithValue("@UserID", record.UserID)
                        cmd.Parameters.AddWithValue("@QbeName", record.QbeName)
                        cmd.Parameters.AddWithValue("@IsCompanyWide", If(record.IsCompanyWide, 1, 0))
                        cmd.Parameters.AddWithValue("@TableContext", record.TableContext)
                        cmd.Parameters.AddWithValue("@QbeData", record.QbeData)
                        cmd.ExecuteNonQuery()
                    End Using
                End If
            End Using
        End Sub

        Public Shared Sub DeleteSavedQbe(savedQbeId As Integer, ownerUserId As Integer)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Dim lookupTable As String = String.Empty
                Dim lookupName As String = String.Empty
                Using lookupCmd As New SqlCommand(
                    "SELECT TOP 1 TableContext, QbeName FROM dbo.FW_SavedQbe WHERE SavedQbeID = @SavedQbeID AND UserID = @UserID", conn)
                    lookupCmd.Parameters.AddWithValue("@SavedQbeID", savedQbeId)
                    lookupCmd.Parameters.AddWithValue("@UserID", ownerUserId)
                    Using reader = lookupCmd.ExecuteReader()
                        If reader.Read() Then
                            lookupTable = SafeString(reader("TableContext"))
                            lookupName = SafeString(reader("QbeName"))
                        Else
                            Return
                        End If
                    End Using
                End Using

                Dim rowsAffected As Integer = 0
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_SavedQbe " &
                    "SET DeletedFlag = 1, DeletedBy = @DeletedBy, DeletedOn = SYSUTCDATETIME() " &
                    "WHERE SavedQbeID = @SavedQbeID AND UserID = @UserID AND ISNULL(DeletedFlag, 0) = 0", conn)
                    cmd.Parameters.AddWithValue("@SavedQbeID", savedQbeId)
                    cmd.Parameters.AddWithValue("@UserID", ownerUserId)
                    cmd.Parameters.AddWithValue("@DeletedBy", ownerUserId)
                    rowsAffected = cmd.ExecuteNonQuery()
                End Using

                If rowsAffected > 0 Then
                    LogUpdateAudit(lookupTable,
                                   "FW_SavedQbe",
                                   "Delete",
                                   "AfterSave",
                                   savedQbeId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson("Soft deleted saved QBE '" & lookupName & "'. Check the table."),
                                   True,
                                   Nothing,
                                   ownerUserId)
                End If
            End Using
        End Sub

        Public Shared Function GetLastUsedOrDefaultTableLayoutJson(registrationId As Integer,
                                                                    userId As Integer,
                                                                    pageName As String,
                                                                    tableName As String) As String
            Return GetPreferredTableLayout(registrationId,
                                           userId,
                                           pageName,
                                           tableName,
                                           False).Item1
        End Function

        Public Shared Function GetPreferredTableLayout(registrationId As Integer,
                                                       userId As Integer,
                                                       pageName As String,
                                                       tableName As String,
                                                       preferDefault As Boolean) As Tuple(Of String, String)
            If preferDefault Then
                Dim defaultJson = GetTableLayoutJson(registrationId, 0, pageName, tableName, "Default", "* Default")
                If Not String.IsNullOrWhiteSpace(defaultJson) Then
                    Return Tuple.Create(defaultJson, "Default")
                End If

                Dim lastUsedJson = GetTableLayoutJson(registrationId, userId, pageName, tableName, "LastUsed", "Last Used")
                Return Tuple.Create(lastUsedJson, "LastUsed")
            End If

            Dim preferredLastUsedJson = GetTableLayoutJson(registrationId, userId, pageName, tableName, "LastUsed", "Last Used")
            If Not String.IsNullOrWhiteSpace(preferredLastUsedJson) Then
                Return Tuple.Create(preferredLastUsedJson, "LastUsed")
            End If

            Dim preferredDefaultJson = GetTableLayoutJson(registrationId, 0, pageName, tableName, "Default", "* Default")
            Return Tuple.Create(preferredDefaultJson, "Default")
        End Function

        Public Shared Function LayoutJsonHasAnyMatchingKeys(layoutJson As String,
                                                            candidateKeys As IEnumerable(Of String)) As Boolean
            If String.IsNullOrWhiteSpace(layoutJson) OrElse candidateKeys Is Nothing Then
                Return False
            End If

            Dim parseJson = NormalizeLayoutJsonForParsing(layoutJson)
            If String.IsNullOrWhiteSpace(parseJson) Then
                Return False
            End If

            Dim keySet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each rawKey In candidateKeys
                Dim key = If(rawKey, String.Empty).Trim()
                If Not String.IsNullOrWhiteSpace(key) Then
                    keySet.Add(key)
                End If
            Next

            If keySet.Count = 0 Then
                Return False
            End If

            Try
                Using doc = JsonDocument.Parse(parseJson)
                    If doc.RootElement.ValueKind <> JsonValueKind.Array Then
                        Return False
                    End If

                    For Each item In doc.RootElement.EnumerateArray()
                        If item.ValueKind <> JsonValueKind.Object Then
                            Continue For
                        End If

                        Dim keyProp As JsonElement
                        If Not item.TryGetProperty("Key", keyProp) Then
                            Continue For
                        End If

                        Dim layoutKey = If(keyProp.GetString(), String.Empty).Trim()
                        If Not String.IsNullOrWhiteSpace(layoutKey) AndAlso keySet.Contains(layoutKey) Then
                            Return True
                        End If
                    Next
                End Using
            Catch
                Return False
            End Try

            Return False
        End Function

        Public Shared Function NormalizeLayoutJsonForParsing(layoutJson As String) As String
            If String.IsNullOrWhiteSpace(layoutJson) Then
                Return String.Empty
            End If

            Dim normalized = layoutJson.Trim()

            ' Repair legacy malformed values written with an extra quote before commas.
            normalized = Regex.Replace(normalized,
                                       ":(-?\d+|true|false)""(?=\s*,)",
                                       ":$1",
                                       RegexOptions.IgnoreCase)

            Return normalized
        End Function

        Public Shared Function GetTableLayoutJson(registrationId As Integer,
                                                  userId As Integer,
                                                  pageName As String,
                                                  tableName As String,
                                                  layoutType As String,
                                                  layoutName As String) As String
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) OrElse String.IsNullOrWhiteSpace(layoutType) Then
                Return String.Empty
            End If

            Dim normalizedPage = pageName.Trim()
            Dim normalizedTable = If(tableName, String.Empty).Trim()
            Dim normalizedType = layoutType.Trim()
            Dim normalizedName = If(layoutName, String.Empty).Trim()
            Dim deletedFilterClause As String = If(TableHasColumn("FW_TableLayouts", "DeletedFlag"), " AND ISNULL(DeletedFlag, 0) = 0", String.Empty)

            If normalizedType.Equals("LastUsed", StringComparison.OrdinalIgnoreCase) AndAlso String.IsNullOrWhiteSpace(normalizedName) Then
                normalizedName = "Last Used"
            ElseIf normalizedType.Equals("Default", StringComparison.OrdinalIgnoreCase) AndAlso String.IsNullOrWhiteSpace(normalizedName) Then
                normalizedName = "* Default"
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim sql As String
                If normalizedType.Equals("Default", StringComparison.OrdinalIgnoreCase) Then
                    sql =
                        "SELECT TOP 1 JsonState " &
                        "FROM dbo.FW_TableLayouts " &
                        "WHERE RegistrationID = @RegistrationID " &
                        "  AND PageName = @PageName " &
                        "  AND ISNULL(TableName, '') = @TableName " &
                        "  AND LayoutType = @LayoutType " &
                        "  AND (ISNULL(LayoutName, '') = @LayoutName OR ISNULL(LayoutName, '') = '') " &
                        "  AND IsActive = 1" & deletedFilterClause & " " &
                        "ORDER BY ISNULL(UpdatedOn, CreatedOn) DESC, ID DESC"
                ElseIf normalizedType.Equals("LastUsed", StringComparison.OrdinalIgnoreCase) Then
                    If userId <= 0 Then
                        Return String.Empty
                    End If

                    sql =
                        "SELECT TOP 1 JsonState " &
                        "FROM dbo.FW_TableLayouts " &
                        "WHERE RegistrationID = @RegistrationID " &
                        "  AND UserID = @UserID " &
                        "  AND PageName = @PageName " &
                        "  AND ISNULL(TableName, '') = @TableName " &
                        "  AND LayoutType = @LayoutType " &
                        "  AND (ISNULL(LayoutName, '') = @LayoutName OR ISNULL(LayoutName, '') = '') " &
                        "  AND IsActive = 1" & deletedFilterClause & " " &
                        "ORDER BY ISNULL(UpdatedOn, CreatedOn) DESC, ID DESC"
                Else
                    If String.IsNullOrWhiteSpace(normalizedName) Then
                        Return String.Empty
                    End If

                    If userId > 0 Then
                        sql =
                            "SELECT TOP 1 JsonState " &
                            "FROM dbo.FW_TableLayouts " &
                            "WHERE RegistrationID = @RegistrationID " &
                            "  AND UserID = @UserID " &
                            "  AND PageName = @PageName " &
                            "  AND ISNULL(TableName, '') = @TableName " &
                            "  AND LayoutType = @LayoutType " &
                            "  AND ISNULL(LayoutName, '') = @LayoutName " &
                            "  AND IsActive = 1" & deletedFilterClause & " " &
                            "ORDER BY ISNULL(UpdatedOn, CreatedOn) DESC, ID DESC"
                    Else
                        sql =
                            "SELECT TOP 1 JsonState " &
                            "FROM dbo.FW_TableLayouts " &
                            "WHERE RegistrationID = @RegistrationID " &
                            "  AND ISNULL(UserID, 0) = 0 " &
                            "  AND PageName = @PageName " &
                            "  AND ISNULL(TableName, '') = @TableName " &
                            "  AND LayoutType = @LayoutType " &
                            "  AND ISNULL(LayoutName, '') = @LayoutName " &
                            "  AND IsActive = 1" & deletedFilterClause & " " &
                            "ORDER BY ISNULL(UpdatedOn, CreatedOn) DESC, ID DESC"
                    End If
                End If

                Using cmd As New SqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", If(userId > 0, CType(userId, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@PageName", normalizedPage)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                    cmd.Parameters.AddWithValue("@LayoutType", normalizedType)
                    cmd.Parameters.AddWithValue("@LayoutName", normalizedName)

                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then
                        Return String.Empty
                    End If

                    Return result.ToString()
                End Using
            End Using
        End Function

        Public Shared Sub UpsertTableLayout(registrationId As Integer,
                                            userId As Integer,
                                            pageName As String,
                                            tableName As String,
                                            layoutType As String,
                                            layoutName As String,
                                            jsonState As String,
                                            updatedBy As Integer)
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) OrElse
               String.IsNullOrWhiteSpace(layoutType) OrElse String.IsNullOrWhiteSpace(jsonState) Then
                Return
            End If

            Dim normalizedPage = pageName.Trim()
            Dim normalizedTable = If(tableName, String.Empty).Trim()
            Dim normalizedType = layoutType.Trim()
            Dim normalizedName = If(layoutName, String.Empty).Trim()
            Dim deletedFilterFragment As String = If(TableHasColumn("FW_TableLayouts", "DeletedFlag"), " AND ISNULL(DeletedFlag, 0) = 0", String.Empty)

            If normalizedType.Equals("LastUsed", StringComparison.OrdinalIgnoreCase) AndAlso String.IsNullOrWhiteSpace(normalizedName) Then
                normalizedName = "Last Used"
            ElseIf normalizedType.Equals("Default", StringComparison.OrdinalIgnoreCase) AndAlso String.IsNullOrWhiteSpace(normalizedName) Then
                normalizedName = "* Default"
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim whereSql As String
                If normalizedType.Equals("Default", StringComparison.OrdinalIgnoreCase) Then
                    whereSql =
                        "RegistrationID = @RegistrationID AND PageName = @PageName AND ISNULL(TableName, '') = @TableName AND LayoutType = @LayoutType AND IsActive = 1" & deletedFilterFragment
                ElseIf normalizedType.Equals("LastUsed", StringComparison.OrdinalIgnoreCase) Then
                    If userId <= 0 Then
                        Return
                    End If

                    whereSql =
                        "RegistrationID = @RegistrationID AND UserID = @UserID AND PageName = @PageName AND ISNULL(TableName, '') = @TableName AND LayoutType = @LayoutType AND IsActive = 1" & deletedFilterFragment
                Else
                    If String.IsNullOrWhiteSpace(normalizedName) Then
                        Return
                    End If

                    If userId > 0 Then
                        whereSql =
                            "RegistrationID = @RegistrationID AND UserID = @UserID AND PageName = @PageName AND ISNULL(TableName, '') = @TableName AND LayoutType = @LayoutType AND ISNULL(LayoutName, '') = @LayoutName AND IsActive = 1" & deletedFilterFragment
                    Else
                        whereSql =
                            "RegistrationID = @RegistrationID AND ISNULL(UserID, 0) = 0 AND PageName = @PageName AND ISNULL(TableName, '') = @TableName AND LayoutType = @LayoutType AND ISNULL(LayoutName, '') = @LayoutName AND IsActive = 1" & deletedFilterFragment
                    End If
                End If

                Dim existingId As Integer = 0
                Dim existingSql = "SELECT TOP 1 ID FROM dbo.FW_TableLayouts WHERE " & whereSql & " ORDER BY ID DESC"
                Using cmd As New SqlCommand(existingSql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", If(userId > 0, CType(userId, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@PageName", normalizedPage)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                    cmd.Parameters.AddWithValue("@LayoutType", normalizedType)
                    cmd.Parameters.AddWithValue("@LayoutName", normalizedName)

                    Dim existingObj = cmd.ExecuteScalar()
                    If existingObj IsNot Nothing AndAlso Not IsDBNull(existingObj) Then
                        Integer.TryParse(existingObj.ToString(), existingId)
                    End If
                End Using

                If existingId > 0 Then
                    Dim updateSql =
                        "UPDATE dbo.FW_TableLayouts " &
                        "SET LayoutName = @LayoutName, JsonState = @JsonState, IsActive = 1, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                        "WHERE ID = @ID"
                    Using cmd As New SqlCommand(updateSql, conn)
                        cmd.Parameters.AddWithValue("@LayoutName", normalizedName)
                        cmd.Parameters.AddWithValue("@JsonState", jsonState)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@ID", existingId)
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Dim insertSql =
                        "INSERT INTO dbo.FW_TableLayouts " &
                        "(RegistrationID, UserID, PageName, TableName, LayoutType, LayoutName, JsonState, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                        "VALUES (@RegistrationID, @UserID, @PageName, @TableName, @LayoutType, @LayoutName, @JsonState, 1, @CreatedBy, GETDATE(), @UpdatedBy, GETDATE())"
                    Using cmd As New SqlCommand(insertSql, conn)
                        Dim userIdParamValue As Object = If(userId > 0 OrElse normalizedType.Equals("UserNamed", StringComparison.OrdinalIgnoreCase), CType(userId, Object), DBNull.Value)
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        cmd.Parameters.AddWithValue("@UserID", userIdParamValue)
                        cmd.Parameters.AddWithValue("@PageName", normalizedPage)
                        cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                        cmd.Parameters.AddWithValue("@LayoutType", normalizedType)
                        cmd.Parameters.AddWithValue("@LayoutName", normalizedName)
                        cmd.Parameters.AddWithValue("@JsonState", jsonState)
                        cmd.Parameters.AddWithValue("@CreatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using
                End If
            End Using
        End Sub

        Public Shared Sub DeleteTableLayout(registrationId As Integer,
                                            ownerUserId As Integer,
                                            pageName As String,
                                            tableName As String,
                                            layoutType As String,
                                            layoutName As String,
                                            updatedBy As Integer)
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) OrElse String.IsNullOrWhiteSpace(layoutType) Then
                Return
            End If

            Dim normalizedPage = pageName.Trim()
            Dim normalizedTable = If(tableName, String.Empty).Trim()
            Dim normalizedType = layoutType.Trim()
            Dim normalizedName = If(layoutName, String.Empty).Trim()

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim sql As String =
                    "UPDATE dbo.FW_TableLayouts " &
                        "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "  AND ISNULL(UserID, 0) = @OwnerUserID " &
                    "  AND PageName = @PageName " &
                    "  AND ISNULL(TableName, '') = @TableName " &
                    "  AND LayoutType = @LayoutType "

                If normalizedType.Equals("UserNamed", StringComparison.OrdinalIgnoreCase) Then
                    sql &= " AND ISNULL(LayoutName, '') = @LayoutName"
                End If

                sql &= " AND IsActive = 1"

                Using cmd As New SqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@OwnerUserID", ownerUserId)
                    cmd.Parameters.AddWithValue("@PageName", normalizedPage)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                    cmd.Parameters.AddWithValue("@LayoutType", normalizedType)
                    cmd.Parameters.AddWithValue("@LayoutName", normalizedName)
                    cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                    Dim rowsAffected = cmd.ExecuteNonQuery()

                    If rowsAffected > 0 Then
                        LogUpdateAudit(normalizedPage,
                                       "FW_TableLayouts",
                                       "Delete",
                                       "AfterSave",
                                       normalizedType & ":" & normalizedName,
                                       BuildSoftDeleteAuditSnapshotJson("Soft deleted table layout. Check the table."),
                                       True,
                                       registrationId,
                                       updatedBy)
                    End If
                End Using
            End Using
        End Sub

        Public Shared Function GetAvailableTableLayouts(registrationId As Integer,
                                                        userId As Integer,
                                                        pageName As String,
                                                        tableName As String) As DataTable
            Dim result As New DataTable("FW_TableLayouts_List")
            result.Columns.Add("LayoutType", GetType(String))
            result.Columns.Add("LayoutName", GetType(String))
            result.Columns.Add("OwnerUserID", GetType(Integer))
            result.Columns.Add("IsShared", GetType(Boolean))
            result.Columns.Add("DisplayName", GetType(String))

            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) Then
                Return result
            End If

            Dim normalizedPage = pageName.Trim()
            Dim normalizedTable = If(tableName, String.Empty).Trim()
            Dim deletedFilterClause As String = If(TableHasColumn("FW_TableLayouts", "DeletedFlag"), " AND ISNULL(DeletedFlag, 0) = 0", String.Empty)

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim sql =
                    "SELECT LayoutType, ISNULL(LayoutName, '') AS LayoutName, OwnerUserID, IsShared, DisplayName " &
                    "FROM (" &
                    "   SELECT TOP 1 'LastUsed' AS LayoutType, ISNULL(NULLIF(LTRIM(RTRIM(LayoutName)), ''), 'Last Used') AS LayoutName, @UserID AS OwnerUserID, CAST(0 AS bit) AS IsShared, ISNULL(NULLIF(LTRIM(RTRIM(LayoutName)), ''), 'Last Used') AS DisplayName, ISNULL(UpdatedOn, CreatedOn) AS SortDate " &
                    "   FROM dbo.FW_TableLayouts " &
                    "   WHERE RegistrationID = @RegistrationID " &
                    "     AND UserID = @UserID " &
                    "     AND PageName = @PageName " &
                    "     AND ISNULL(TableName, '') = @TableName " &
                    "     AND LayoutType = 'LastUsed' " &
                    "     AND IsActive = 1" & deletedFilterClause & " " &
                    "   ORDER BY ISNULL(UpdatedOn, CreatedOn) DESC, ID DESC" &
                    "   UNION ALL " &
                    "   SELECT TOP 1 'Default' AS LayoutType, ISNULL(NULLIF(LTRIM(RTRIM(LayoutName)), ''), '* Default') AS LayoutName, 0 AS OwnerUserID, CAST(1 AS bit) AS IsShared, ISNULL(NULLIF(LTRIM(RTRIM(LayoutName)), ''), '* Default') AS DisplayName, ISNULL(UpdatedOn, CreatedOn) AS SortDate " &
                    "   FROM dbo.FW_TableLayouts " &
                    "   WHERE RegistrationID = @RegistrationID " &
                    "     AND PageName = @PageName " &
                    "     AND ISNULL(TableName, '') = @TableName " &
                    "     AND LayoutType = 'Default' " &
                    "     AND IsActive = 1" & deletedFilterClause & " " &
                    "   ORDER BY ISNULL(UpdatedOn, CreatedOn) DESC, ID DESC" &
                    "   UNION ALL " &
                    "   SELECT 'UserNamed' AS LayoutType, ISNULL(LayoutName, '') AS LayoutName, @UserID AS OwnerUserID, CAST(0 AS bit) AS IsShared, ISNULL(LayoutName, '') AS DisplayName, ISNULL(UpdatedOn, CreatedOn) AS SortDate " &
                    "   FROM dbo.FW_TableLayouts " &
                    "   WHERE RegistrationID = @RegistrationID " &
                    "     AND UserID = @UserID " &
                    "     AND PageName = @PageName " &
                    "     AND ISNULL(TableName, '') = @TableName " &
                    "     AND LayoutType = 'UserNamed' " &
                    "     AND IsActive = 1" & deletedFilterClause & " " &
                    "   UNION ALL " &
                    "   SELECT 'UserNamed' AS LayoutType, ISNULL(LayoutName, '') AS LayoutName, 0 AS OwnerUserID, CAST(1 AS bit) AS IsShared, ISNULL(LayoutName, '') AS DisplayName, ISNULL(UpdatedOn, CreatedOn) AS SortDate " &
                    "   FROM dbo.FW_TableLayouts " &
                    "   WHERE RegistrationID = @RegistrationID " &
                    "     AND ISNULL(UserID, 0) = 0 " &
                    "     AND PageName = @PageName " &
                    "     AND ISNULL(TableName, '') = @TableName " &
                    "     AND LayoutType = 'UserNamed' " &
                    "     AND IsActive = 1" & deletedFilterClause & " " &
                    ") layouts " &
                    "ORDER BY CASE LayoutType WHEN 'LastUsed' THEN 1 WHEN 'Default' THEN 2 ELSE 3 END, IsShared DESC, DisplayName"

                Using cmd As New SqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", If(userId > 0, CType(userId, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@PageName", normalizedPage)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)

                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(result)
                    End Using
                End Using
            End Using

            Return result
        End Function

        Public Shared Function GetUserUiHintKeys(registrationId As Integer, userId As Integer) As List(Of String)
            Dim result As New List(Of String)()

            If registrationId <= 0 OrElse userId <= 0 Then
                Return result
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim sql =
                    "SELECT HintKey " &
                    "FROM dbo.FW_UserUiHints " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "  AND UserID = @UserID " &
                    "  AND Seen = 1"

                Using cmd As New SqlCommand(sql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            result.Add(If(reader("HintKey"), String.Empty).ToString())
                        End While
                    End Using
                End Using
            End Using

            Return result
        End Function

        Public Shared Sub MarkUserUiHintSeen(registrationId As Integer,
                                             userId As Integer,
                                             hintKey As String,
                                             updatedBy As Integer)
            If registrationId <= 0 OrElse userId <= 0 Then
                Return
            End If

            Dim normalizedHintKey = If(hintKey, String.Empty).Trim()
            If normalizedHintKey = String.Empty Then
                Return
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim existingId As Integer = 0
                Dim existingSql =
                    "SELECT TOP 1 ID " &
                    "FROM dbo.FW_UserUiHints " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "  AND UserID = @UserID " &
                    "  AND HintKey = @HintKey " &
                    "ORDER BY ID DESC"

                Using cmd As New SqlCommand(existingSql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@HintKey", normalizedHintKey)

                    Dim existingObj = cmd.ExecuteScalar()
                    If existingObj IsNot Nothing AndAlso Not IsDBNull(existingObj) Then
                        Integer.TryParse(existingObj.ToString(), existingId)
                    End If
                End Using

                If existingId > 0 Then
                    Dim updateSql =
                        "UPDATE dbo.FW_UserUiHints " &
                        "SET Seen = 1, SeenOn = GETDATE(), UpdatedOn = GETDATE(), UpdatedBy = @UpdatedBy " &
                        "WHERE ID = @ID"

                    Using cmd As New SqlCommand(updateSql, conn)
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@ID", existingId)
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Dim insertSql =
                        "INSERT INTO dbo.FW_UserUiHints " &
                        "(RegistrationID, UserID, HintKey, Seen, SeenOn, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                        "VALUES (@RegistrationID, @UserID, @HintKey, 1, GETDATE(), @CreatedBy, GETDATE(), @UpdatedBy, GETDATE())"

                    Using cmd As New SqlCommand(insertSql, conn)
                        cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        cmd.Parameters.AddWithValue("@UserID", userId)
                        cmd.Parameters.AddWithValue("@HintKey", normalizedHintKey)
                        cmd.Parameters.AddWithValue("@CreatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                        cmd.ExecuteNonQuery()
                    End Using
                End If
            End Using
        End Sub
    End Class
End Namespace
