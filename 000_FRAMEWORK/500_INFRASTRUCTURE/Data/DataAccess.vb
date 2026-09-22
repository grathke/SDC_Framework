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

Namespace SDC.Framework
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
            Public Property PageAlias As String
            Public Property RoleOverrideCaption As String

            Public Sub New()
                CrudCaptions = CrudButtonCaptions.DefaultCaptions()
                RoleAccess = New RoleTableAccessEntry()
                FieldCaptions = New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
                InvisibleFields = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                StartEmpty = False
                PageAlias = String.Empty
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
        Private Shared ReadOnly pageInitMetadataCache As New Dictionary(Of String, PageInitMetadata)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' Every page's caption, read once and held.
        '''
        ''' One query for the whole application rather than one per page or one per button. The
        ''' alternative was three round trips on a browse page load - table, SQL, then alias - all
        ''' fetching different columns of the same row, and one more per ribbon tile.
        '''
        ''' Held whole rather than per page, because a ribbon asks about every tile at once and a
        ''' page asks about one: filling it lazily would be one query per page anyway, which is the
        ''' thing being avoided.
        '''
        ''' A page generated while the application is running is absent until the caches are cleared
        ''' - a role change does it, and so does a restart. That is accepted: a generated page needs
        ''' a rebuild before it can be opened at all, so its caption was never going to be the thing
        ''' holding it up.
        ''' </summary>
        Private Shared ReadOnly pageAliasCache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' Which pages show the Hot Fields button, by page name.
        ''' </summary>
        ''' <remarks>
        ''' Filled by the same query and the same pass as the alias cache, so the flag costs no read
        ''' of its own. It is a second dictionary rather than a richer cache entry only because the
        ''' fold of DB_Table, Table_SQL and Background into this cache is still ahead of us; when
        ''' that happens this belongs in the same record.
        ''' </remarks>
        Private Shared ReadOnly pageHotFieldsCache As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' The rest of the FW_Pages row, cached beside the alias because it arrives in the same read.
        '''
        ''' Three methods used to fetch these one page at a time - the table name once per dashboard
        ''' icon, then the SQL and the colour on every page open - while EnsurePageAliasCache was
        ''' already reading every row of the same table in one query and throwing these columns away.
        ''' A measured five-page session spent 21 of its 144 round trips asking for columns it had
        ''' already been sent.
        '''
        ''' Unlike the schema cache this helps a page that has never been opened, because it is
        ''' loaded per application rather than per table.
        ''' </summary>
        Private Shared ReadOnly pageHotFieldListCache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly pageDbTableCache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly pageSqlCache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly pageBackgroundCache As New Dictionary(Of String, Integer?)(StringComparer.OrdinalIgnoreCase)
        Private Shared pageAliasCacheLoaded As Boolean

        ''' <summary>
        ''' Schema facts, held for the life of the process.
        '''
        ''' Whether a table has a column, whether it has a RowVersion, and what its primary key is
        ''' called cannot change between the application starting and stopping - nothing in the
        ''' application issues DDL. They were being asked every time anyway: one measured session of
        ''' logging in, opening a browse page, searching and opening it again made 87 database round
        ''' trips, and 27 of them were TableHasColumn. Opening the same page a second time cost the
        ''' same as the first.
        '''
        ''' Only a successful answer is stored. All three functions return a safe default when the
        ''' query throws, and caching that default would turn one dropped connection into a wrong
        ''' answer for the rest of the session.
        '''
        ''' The trade: a column added in SSMS while the application is running is not seen until it
        ''' restarts. That is the same bargain the alias cache already makes, and DDL against a live
        ''' application is not a thing this framework does.
        ''' </summary>
        ''' <summary>
        ''' The search lists a browse page offers, keyed table.column. A null entry is an answer
        ''' too - the column points at nothing, and asking the database again on every page open
        ''' would not change that.
        ''' </summary>
        Private Shared ReadOnly qbeChoiceCache As New Dictionary(Of String, DataTable)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Each table's computed columns and the columns they are built from.</summary>
        Private Shared ReadOnly computedSourceCache As New Dictionary(Of String, Dictionary(Of String, List(Of String)))(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' What a table's columns are, read once and then answered from memory.
        '''
        ''' Was one question per column - "does FW_Employees have DeletedFlag?" - which cached its
        ''' answer perfectly and still cost a round trip for every column nobody had asked about
        ''' yet. A traced page open on 2026-09-22 made nine of those, and separately asked three
        ''' more times for the same table's column list in three different dialects: the names, the
        ''' names in declared order, and the names with their maximum text length. Seven trips for
        ''' facts that all sit in the same row of INFORMATION_SCHEMA.COLUMNS.
        ''' </summary>
        Private Shared ReadOnly tableSchemaFactsCache As New Dictionary(Of String, TableSchemaFacts)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' The three things the framework asks about a table's columns, from one read.
        ''' </summary>
        Private NotInheritable Class TableSchemaFacts
            Public Property OrderedNames As List(Of String)
            Public Property NameSet As HashSet(Of String)
            Public Property TextMaxLengths As Dictionary(Of String, Integer)
        End Class

        ''' <summary>
        ''' A table's declared foreign keys. The query always returned the whole table; only the
        ''' caching was missing, and GetQbeValueChoices asks once per field - seven times for one
        ''' page in the same trace.
        ''' </summary>
        Private Shared ReadOnly columnRelationshipsCache As New Dictionary(Of String, Dictionary(Of String, ColumnRelationship))(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly rowVersionCache As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
        Private Shared ReadOnly primaryKeyCache As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' Drops the cached FW_Pages rows, so the next read reloads them.
        '''
        ''' For the writes that change a whole row - UpsertPageRecord and UpdatePageSql - rather than
        ''' patching each cached column at every write site and eventually missing one. Both are rare:
        ''' a page's first open, a generation, or the SQL fallback. One reload afterwards is cheaper
        ''' than a stale table name, which would send a page's reads at the wrong table entirely.
        '''
        ''' SavePageBackgroundColor and SavePageUsesHotFields patch their own entry instead, because
        ''' each changes one column and is called while somebody is watching the result.
        ''' </summary>
        Private Shared Sub InvalidatePageCache()
            SyncLock metadataCacheLock
                pageAliasCache.Clear()
                pageHotFieldsCache.Clear()
                pageHotFieldListCache.Clear()
                pageDbTableCache.Clear()
                pageSqlCache.Clear()
                pageBackgroundCache.Clear()
                pageAliasCacheLoaded = False
            End SyncLock
        End Sub

        Public Shared Sub InvalidateRoleMetadataCache()
            SyncLock metadataCacheLock
                crudCaptionCache.Clear()
                roleOverrideCaptionCache.Clear()
                roleStartEmptyCache.Clear()
                pageInitMetadataCache.Clear()
                pageAliasCache.Clear()
                pageHotFieldsCache.Clear()
                pageHotFieldListCache.Clear()
                pageDbTableCache.Clear()
                pageSqlCache.Clear()
                pageBackgroundCache.Clear()
                pageAliasCacheLoaded = False
            End SyncLock

            ' Schema facts go too. A role change is not a schema change, so this is not needed for
            ' correctness - it is the escape hatch. Adding a column in SSMS and switching role
            ' refreshes the answer without restarting the application, and a role change is rare
            ' enough that re-reading a handful of schema facts costs nothing worth counting.
            InvalidateSchemaCache()
        End Sub

        ''' <summary>
        ''' Forgets what the database schema looks like, so the next question goes back to the
        ''' server. For a column added or dropped while the application is running.
        ''' </summary>
        Public Shared Sub InvalidateSchemaCache()
            SyncLock metadataCacheLock
                tableSchemaFactsCache.Clear()
                columnRelationshipsCache.Clear()
                rowVersionCache.Clear()
                primaryKeyCache.Clear()
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
                        "FROM dbo.FW_Registration WHERE RegistrationID = @RegID", conn)
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
                                result.PageAlias = SafeString(reader("Table_Alias"))
                                result.RoleOverrideCaption = SafeString(reader("OverrideCaption"))
                            End If
                        End Using
                    End Using

                    ' Batch 3: Field captions
                    '
                    ' No IsActive filter, since 2026-09-09. FW_RoleFields.OverrideCaption is the
                    ' caption whenever it is present, whatever else is true of the row - what a
                    ' field is called is not a permission, and a role that cannot change a field
                    ' still has to read its name.
                    '
                    ' Filtering it produced exactly the confusion it was found by: the same field
                    ' captioned Gender for one role and Gender ID for another, because one row was
                    ' active and the other was not, with the same override written on both.
                    '
                    ' Make_Invisible is read here too and does not travel with the caption: that one
                    ' is a permission, and it keeps whatever the row says.
                    Using cmd As New SqlCommand(
                        "SELECT FieldName, " &
                        "ISNULL(LTRIM(RTRIM(OverrideCaption)), '') AS OverrideCaption, " &
                        "ISNULL(Make_Invisible, 0) AS Make_Invisible, " &
                        "ISNULL(IsActive, 1) AS IsActive " &
                        "FROM dbo.FW_RoleFields " &
                        "WHERE RoleID = @RoleID AND RegistrationID = @RegID " &
                        "AND UPPER(LTRIM(RTRIM(TableName))) = UPPER(@TableName)", conn)
                        cmd.Parameters.AddWithValue("@RoleID", roleId)
                        cmd.Parameters.AddWithValue("@RegID", registrationId)
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim fieldName = SafeString(reader("FieldName"))
                                If fieldName = String.Empty Then
                                    Continue While
                                End If

                                ' The override and nothing else. An override is an override: where
                                ' one is present it is the caption, and where there is none the
                                ' caller derives the name from the field itself through
                                ' DisplayNameFormatter, which is the better answer anyway.
                                '
                                ' FriendlyFieldName used to sit between those two and earned its
                                ' place on exactly one field in the whole database. It is seeded by
                                ' FormatFieldName, which has no acronym handling, so what it mostly
                                ' contributed was BTN_Create_Caption as "B T N Create Caption" and
                                ' CA_CanChange as "C A Can Change" - waiting on columns no page
                                ' happened to select. The column is still there and Roles still
                                ' edits it; nothing is named from it.
                                Dim caption = SafeString(reader("OverrideCaption"))

                                If caption <> String.Empty Then
                                    result.FieldCaptions(fieldName) = caption
                                End If

                                ' Only from a row that is switched on. Make_Invisible hides a field
                                ' from a role, and a permission on an inactive row is not in force -
                                ' which is the difference between it and the caption above, where
                                ' the row's state does not come into it.
                                If Convert.ToBoolean(reader("IsActive")) AndAlso Convert.ToBoolean(reader("Make_Invisible")) Then
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
                .PageAlias = source.PageAlias,
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
            Dim fullConnectionString = Environment.GetEnvironmentVariable("SDC_DB_CONNECTION")
            If Not String.IsNullOrWhiteSpace(fullConnectionString) Then
                Return fullConnectionString.Trim()
            End If

            Dim saved = GetUsableSavedSettings()
            If saved IsNot Nothing Then
                Return DatabaseConfigStore.BuildConnectionString(saved)
            End If

            Dim server = GetEnvironmentOrDefault("SDC_DB_SERVER", "BEELINK")
            Dim userId = GetEnvironmentOrDefault("SDC_DB_USER", "sa")
            Dim password = GetEnvironmentOrDefault("SDC_DB_PASSWORD", String.Empty)
            Dim database = GetEnvironmentOrDefault("SDC_DB_NAME", "WX_Framework")
            Dim encrypt = GetEnvironmentOrDefault("SDC_DB_ENCRYPT", "False")
            Dim trustServerCertificate = GetEnvironmentOrDefault("SDC_DB_TRUST_SERVER_CERT", "True")

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
            If Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDC_DB_CONNECTION")) Then
                Return String.Empty
            End If

            If String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDC_DB_PASSWORD")) Then
                ' Credentials saved through the configuration dialog count as configured, or the
                ' dialog would reappear on every launch and saving would achieve nothing.
                If GetUsableSavedSettings() IsNot Nothing Then
                    Return String.Empty
                End If

                Return "Database credentials are not configured." & Environment.NewLine &
                       Environment.NewLine &
                       "Enter them here, or set SDC_DB_CONNECTION to a full connection " &
                       "string. SDC_DB_SERVER, SDC_DB_USER, SDC_DB_NAME and " &
                       "SDC_DB_PASSWORD are the individual overrides." & Environment.NewLine &
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
            Return Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDC_DB_CONNECTION")) OrElse
                   Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDC_DB_PASSWORD"))
        End Function

        ''' <summary>
        ''' Saved credentials, but only when the environment has not already supplied a password.
        ''' Single owner of that precedence rule, so the startup check and the connection string
        ''' cannot disagree about whether the application is configured.
        ''' </summary>
        Private Shared Function GetUsableSavedSettings() As DatabaseConfigStore.DatabaseSettings
            If Not String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SDC_DB_PASSWORD")) Then
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

                Dim userNameLookup = NormalizeEmailForLookup(emailInput).Trim()
            If userNameLookup = String.Empty Then
                errorMessage = "User name is required."
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

                    Dim isActive As Boolean
                    Dim attemptRegistrationId As Integer = 0

                    ' A deleted account is not found at all - the view filters it - so it reports the
                    ' same "no user" as an address that never existed. Confirming that a deleted
                    ' account was once real tells an attacker something and tells an honest user
                    ' nothing they can act on.
                    If Not TryGetCurrentUserRecord(conn, userNameLookup, userId, dbEmail, firstName, lastName, storedPasswordHash, isActive, attemptRegistrationId) Then
                        errorMessage = "No user found for that user name."
                        RecordLoginAttempt(emailInput, "UnknownUser", 0, 0)
                        Return False
                    End If

                    ' Checked before the password, and answered plainly. Somebody whose access was
                    ' removed has a correct password and needs to know it is not the problem -
                    ' "invalid password" would send them to reset a password that works.
                    '
                    ' IsActive on FW_Users is the login privilege. Until 2026-09-04 nothing checked
                    ' it: removing somebody's access left them able to sign in, and LOGIN-03 recorded
                    ' that it should be refused while sitting untested.
                    If Not isActive Then
                        errorMessage = "This account is not active."
                        RecordLoginAttempt(emailInput, "Inactive", attemptRegistrationId, userId)
                        Return False
                    End If

                    If String.IsNullOrWhiteSpace(storedPasswordHash) Then
                        errorMessage = "Password is not configured for this user."
                        RecordLoginAttempt(emailInput, "NoPassword", attemptRegistrationId, userId)
                        Return False
                    End If

                    Dim hashInput = RemoveSpaces(enteredPassword)
                    Dim passwordMatches = ValidateComputedHashAgainstStored(hashInput, userId, storedPasswordHash)
                    If Not passwordMatches Then
                        errorMessage = "Invalid user name or password."
                        RecordLoginAttempt(emailInput, "WrongPassword", attemptRegistrationId, userId)
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
                ' Recorded locally when the server could not be reached, because the row below is
                ' written to the database and that write will usually fail on this branch. The
                ' journal will not.
                If OutageJournal.IsUnreachable(ex) Then OutageJournal.Note("signing in", ex)

                RecordLoginAttempt(emailInput, "DatabaseDown", 0, 0)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' The account behind a user name, for authentication.
        '''
        ''' IsActive comes back rather than being filtered, because login has to tell somebody their
        ''' access was removed instead of claiming they do not exist. Deleted users are filtered by
        ''' the view itself - a deleted account should not confirm it ever existed.
        '''
        ''' **User name only.** The login moved from email to UserName on 2026-09-14, as the first
        ''' step of separating a person from a login, and email is not a way in any more - an
        ''' account with no UserName cannot sign in, deliberately, rather than quietly falling back
        ''' to the identifier being retired.
        '''
        ''' UserName is read from FW_Users rather than the view, joined on the key the view already
        ''' returns. The view does not expose it, and a view is a protected contract - this needed
        ''' no change to it.
        ''' </summary>

        ''' <summary>
        ''' Signed-in accounts whose user name or email contains the text, for Switch User.
        '''
        ''' One box, so both are searched: the user name, the account's own email, and the email on
        ''' the employee record - accounts created from an employee carry no email of their own, the
        ''' address lives on FW_Employees. Contains rather than equals, because the person searching
        ''' often remembers part of a name.
        '''
        ''' Inactive accounts are returned too, flagged, so the dialog can say the person was found
        ''' and is inactive rather than that nobody matched - which read as though the account did
        ''' not exist. The dialog still refuses to switch to one: an account that could not sign in
        ''' for itself is not one to become. Inactive means either the employee or the login is off;
        ''' the two are kept in step, but a record changed outside the page can leave them apart.
        '''
        ''' Employees only. Contractors will have a login and no employee row, and their table will
        ''' need searching here too once it exists.
        '''
        ''' Through vw_FW_CurrentUser as login uses it, which leaves out deleted accounts. Capped,
        ''' because a single letter matches most of a registration.
        ''' </summary>
        ''' <summary>
        ''' Brings dbo.FW_SwitchUser into line with dbo.FW_UserPeople, which is the single place
        ''' that says who a login belongs to.
        '''
        ''' The procedure writes only rows that differ, so this is cheap to call before every read
        ''' and costs nothing at all when nothing has changed. FW_SwitchUser_B calls it from
        ''' PrepareBrowseSource.
        '''
        ''' Exceptions are left to the caller: the page swallows them and lists the last snapshot,
        ''' which is a decision about that page rather than about this method.
        ''' </summary>
        Public Shared Sub RefreshSwitchUserSnapshot()
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("dbo.usp_FW_RefreshSwitchUser", conn)
                    cmd.CommandType = CommandType.StoredProcedure
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        ''' <summary>
        ''' The account a row of the Switch User page stands for, read from the login rather than
        ''' from the snapshot.
        '''
        ''' Takes the snapshot row's own key - SwitchUserID, which is what the page aliases as PK -
        ''' and joins through to vw_FW_CurrentUser, which is what login itself uses. A person
        ''' deleted since the snapshot was taken therefore returns nothing, and the switch is
        ''' refused rather than started against an account that no longer exists.
        ''' </summary>
        Public Shared Function GetSwitchUserTarget(switchUserId As Integer) As UserContext
            If switchUserId <= 0 Then Return Nothing

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 v.UserId, " &
                    "       COALESCE(NULLIF(v.Email, ''), e.Email, '') AS Email, " &
                    "       ISNULL(v.FirstName, '') AS FirstName, ISNULL(v.LastName, '') AS LastName " &
                    "FROM dbo.FW_SwitchUser s " &
                    "INNER JOIN dbo.vw_FW_CurrentUser v ON v.UserId = s.UserId " &
                    "LEFT JOIN dbo.FW_Employees e ON e.UserId = s.UserId AND ISNULL(e.DeletedFlag, 0) = 0 " &
                    "WHERE s.SwitchUserID = @SwitchUserID", conn)
                    cmd.Parameters.AddWithValue("@SwitchUserID", switchUserId)

                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then Return Nothing

                        Return New UserContext With {
                            .UserId = Convert.ToInt32(reader("UserId"), CultureInfo.InvariantCulture),
                            .Email = SafeString(reader("Email")),
                            .FirstName = SafeString(reader("FirstName")),
                            .LastName = SafeString(reader("LastName"))
                        }
                    End Using
                End Using
            End Using
        End Function

        Public Shared Function FindUsersForSwitch(searchText As String) As List(Of (User As UserContext, UserName As String, Email As String, IsActive As Boolean))
            Dim found As New List(Of (User As UserContext, UserName As String, Email As String, IsActive As Boolean))()
            Dim text = If(searchText, String.Empty).Trim()
            If text = String.Empty Then Return found

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 50 v.UserId, ISNULL(u.UserName, '') AS UserName, " &
                    "       COALESCE(NULLIF(v.Email, ''), e.Email, '') AS Email, " &
                    "       ISNULL(v.FirstName, '') AS FirstName, ISNULL(v.LastName, '') AS LastName, " &
                    "       CASE WHEN ISNULL(v.IsActive, 0) = 1 AND ISNULL(e.IsActive, 1) = 1 THEN 1 ELSE 0 END AS IsActive " &
                    "FROM dbo.vw_FW_CurrentUser v " &
                    "INNER JOIN dbo.FW_Users u ON u.UserId = v.UserId " &
                    "LEFT JOIN dbo.FW_Employees e ON e.UserId = v.UserId AND ISNULL(e.DeletedFlag, 0) = 0 " &
                    "WHERE (u.UserName LIKE @Pattern OR v.Email LIKE @Pattern OR e.Email LIKE @Pattern) " &
                    "ORDER BY ISNULL(v.LastName, ''), ISNULL(v.FirstName, ''), u.UserName", conn)
                    cmd.Parameters.AddWithValue("@Pattern", "%" & text.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") & "%")

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim account As New UserContext With {
                                .UserId = Convert.ToInt32(reader("UserId"), CultureInfo.InvariantCulture),
                                .Email = SafeString(reader("Email")),
                                .FirstName = SafeString(reader("FirstName")),
                                .LastName = SafeString(reader("LastName"))
                            }
                            found.Add((account, SafeString(reader("UserName")), account.Email,
                                       Convert.ToInt32(reader("IsActive"), CultureInfo.InvariantCulture) = 1))
                        End While
                    End Using
                End Using
            End Using

            Return found
        End Function
        ''' <summary>
        ''' Records a failed sign-in, with the reason kept separate from the message on screen.
        '''
        ''' **Written immediately rather than queued.** Everything else recorded in the background
        ''' rides Telemetry's thirty-second flush, and this deliberately does not: a failed sign-in
        ''' is rare, so the round trip costs nothing worth saving, and it is the one record that
        ''' most needs to survive the process ending badly straight afterwards.
        '''
        ''' **It never throws and never blocks the login.** Somebody who cannot get in must not
        ''' also be told the audit failed - that is two problems where there was one. A write that
        ''' fails is lost on purpose.
        '''
        ''' **The reason must never reach the login screen.** "Unknown user" rather than "wrong
        ''' password" tells an attacker which names are real. It is recorded here, where only an
        ''' App Admin reads it, and the screen goes on saying the one thing it says today.
        '''
        ''' DatabaseDown is recorded on a best-effort basis and will usually fail, because the
        ''' reason it is being recorded is that the database could not be reached. It is written
        ''' for the partial case - a query that failed while the connection still works - and the
        ''' complete case is covered from outside the application. Nothing here should pretend
        ''' otherwise.
        ''' </summary>
        Private Shared Sub RecordLoginAttempt(attemptedUserName As String,
                                              reason As String,
                                              registrationId As Integer,
                                              userId As Integer)
            Try
                Using conn As New SqlConnection(BuildConnectionStringForDatabase(String.Empty))
                    conn.Open()

                    Using cmd As New SqlCommand(
                        "INSERT INTO dbo.FW_LoginAttempt " &
                        "  (AttemptedUserName, Reason, RegistrationID, UserID, MachineName, SessionKind, AppVersion) " &
                        "VALUES (@UserName, @Reason, @RegistrationID, @UserID, @MachineName, @SessionKind, @AppVersion)", conn)

                        ' Bounded rather than rejected. What was typed is what gets recorded, and
                        ' somebody who pastes a paragraph into the box should not cost a failed
                        ' write on top of a failed login.
                        cmd.Parameters.Add("@UserName", SqlDbType.VarChar, 100).Value =
                            DbValueBounded(If(attemptedUserName, String.Empty), 100)

                        cmd.Parameters.Add("@Reason", SqlDbType.VarChar, 20).Value = reason

                        cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value =
                            If(registrationId > 0, CType(registrationId, Object), DBNull.Value)

                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value =
                            If(userId > 0, CType(userId, Object), DBNull.Value)

                        cmd.Parameters.Add("@MachineName", SqlDbType.VarChar, 100).Value =
                            DbValueBounded(SafeMachineNameForAudit(), 100)

                        cmd.Parameters.Add("@SessionKind", SqlDbType.VarChar, 20).Value =
                            If(Program.InBrowserSession, "Thinfinity", "Desktop")

                        cmd.Parameters.Add("@AppVersion", SqlDbType.VarChar, 40).Value =
                            DbValueBounded(SafeAppVersionForAudit(), 40)

                        cmd.ExecuteNonQuery()
                    End Using
                End Using

            Catch
                ' Lost on purpose. See the summary: a failure to record a failure is not worth a
                ' second failure, and least of all in front of somebody who cannot sign in.
            End Try
        End Sub

        Private Shared Function SafeMachineNameForAudit() As String
            Try
                Return Environment.MachineName
            Catch
                Return String.Empty
            End Try
        End Function

        Private Shared Function SafeAppVersionForAudit() As String
            Try
                Return Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            Catch
                Return String.Empty
            End Try
        End Function

        Private Shared Function TryGetCurrentUserRecord(conn As SqlConnection, userNameLookup As String, ByRef userId As Integer, ByRef dbEmail As String, ByRef firstName As String, ByRef lastName As String, ByRef storedPasswordHash As String, ByRef isActive As Boolean, ByRef registrationId As Integer) As Boolean
            ' Every column is null-guarded, Email included. It was the one that was not, from when
            ' an email was how people signed in and could not be missing. An account created from
            ' an employee has no email at all - the person's address lives on FW_Employees - and
            ' login died on reader.GetString with "Data is Null", which names neither the column
            ' nor the account and reads like a broken password.
            Using cmd As New SqlCommand("SELECT TOP 1 v.UserId, ISNULL(v.Email, ''), ISNULL(v.FirstName, ''), ISNULL(v.LastName, ''), ISNULL(v.PasswordHash, ''), ISNULL(v.IsActive, 0), ISNULL(u.RegistrationID, 0) " &
                                        "FROM dbo.vw_FW_CurrentUser v " &
                                        "INNER JOIN dbo.FW_Users u ON u.UserId = v.UserId " &
                                        "WHERE LOWER(REPLACE(ISNULL(u.UserName, ''), ' ', '')) = @UserNameLookup", conn)
                cmd.Parameters.AddWithValue("@UserNameLookup", userNameLookup)

                Using reader = cmd.ExecuteReader()
                    If Not reader.Read() Then
                        Return False
                    End If

                    userId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)
                    dbEmail = reader.GetString(1)
                    firstName = reader.GetString(2)
                    lastName = reader.GetString(3)
                    storedPasswordHash = reader.GetString(4)
                    isActive = Convert.ToBoolean(reader.GetValue(5))

                    ' The tenant a failed sign-in belongs to, known before the password is checked.
                    ' FW_Users carries its own RegistrationID, so a wrong password against a real
                    ' account is attributable without a second query - see FW_LoginAttempt.
                    registrationId = Convert.ToInt32(reader.GetValue(6), CultureInfo.InvariantCulture)
                    Return True
                End Using
            End Using
        End Function

        ''' <summary>
        ''' A browse query rewritten so the criteria, the deleted state and the row cap are applied
        ''' by SQL Server, or the reason it was left alone.
        ''' </summary>
        Private Class WrappedBrowseQuery
            Public Property Sql As String = String.Empty
            Public Property Parameters As New List(Of SqlParameter)()
            Public Property DeclineReason As String = String.Empty

            Public ReadOnly Property Wrapped As Boolean
                Get
                    Return Sql <> String.Empty
                End Get
            End Property

            Public Shared Function Declined(reason As String) As WrappedBrowseQuery
                Return New WrappedBrowseQuery With {.DeclineReason = reason}
            End Function
        End Class

        ''' <summary>
        ''' The column types a browse result produces, keyed by the SQL that produces it.
        '''
        ''' Types are needed before the query runs, to build a typed parameter rather than send a
        ''' value as text and let the server parse it - which is where format and locale disagree,
        ''' and the disagreement returns wrong rows with no error.
        '''
        ''' Filled from every fill rather than asked for: a page opens with no criteria, and that
        ''' fill already carries the exact types for that SQL. By the time somebody types a
        ''' criterion the answer is here, so the common path costs no round trip at all. Only a
        ''' filtered search against SQL that has never been run pays a SchemaOnly call, once.
        ''' </summary>
        Private Shared ReadOnly browseResultTypes As New Dictionary(Of String, Dictionary(Of String, Type))(StringComparer.Ordinal)
        Private Shared ReadOnly browseResultTypesLock As New Object()

        ''' <summary>
        ''' Which pages declined to wrap, so the log records each reason once rather than once per
        ''' refresh.
        '''
        ''' A decline happens on every refresh of a page whose SQL cannot be wrapped, and a row per
        ''' refresh would put a write round trip on exactly the path this change exists to shorten -
        ''' while burying the fact in thousands of identical rows. Once per table and reason per run
        ''' is what makes the opt-out visible, which is all the log is for.
        ''' </summary>
        Private Shared ReadOnly browseDeclinesLogged As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Private Shared Sub RememberBrowseResultTypes(effectiveSql As String, table As DataTable)
            If String.IsNullOrWhiteSpace(effectiveSql) OrElse table Is Nothing OrElse table.Columns Is Nothing Then Return
            If table.Columns.Count = 0 Then Return

            Dim types As New Dictionary(Of String, Type)(StringComparer.OrdinalIgnoreCase)
            For Each column As DataColumn In table.Columns
                If column Is Nothing OrElse String.IsNullOrWhiteSpace(column.ColumnName) Then Continue For
                types(column.ColumnName) = column.DataType
            Next

            SyncLock browseResultTypesLock
                browseResultTypes(effectiveSql) = types
            End SyncLock
        End Sub

        Private Shared Function GetBrowseResultTypes(effectiveSql As String) As Dictionary(Of String, Type)
            If String.IsNullOrWhiteSpace(effectiveSql) Then Return Nothing

            SyncLock browseResultTypesLock
                Dim cached As Dictionary(Of String, Type) = Nothing
                If browseResultTypes.TryGetValue(effectiveSql, cached) Then Return cached
            End SyncLock

            ' Never run before, and a criterion is waiting. One SchemaOnly call, no rows.
            Dim schema = GetSchemaFromSelectSql(effectiveSql, 0)
            If schema Is Nothing OrElse schema.Columns Is Nothing OrElse schema.Columns.Count = 0 Then
                Return Nothing
            End If

            RememberBrowseResultTypes(effectiveSql, schema)

            SyncLock browseResultTypesLock
                Dim cached As Dictionary(Of String, Type) = Nothing
                If browseResultTypes.TryGetValue(effectiveSql, cached) Then Return cached
            End SyncLock

            Return Nothing
        End Function

        Private Shared Sub LogBrowseWrapDecline(sourceTableName As String, reason As String)
            Dim page = If(String.IsNullOrWhiteSpace(sourceTableName), "(unknown table)", sourceTableName.Trim())
            Dim key = page & "|" & reason

            SyncLock browseResultTypesLock
                If Not browseDeclinesLogged.Add(key) Then Return
            End SyncLock

            LogFallbackUsage("Browse_SqlPushdown_Declined", reason, page)
        End Sub

        ''' <summary>
        ''' The predicate that decides the deleted state in SQL, as three cases settled in
        ''' QBE_SQL_PUSHDOWN_SPEC.md section 10.
        '''
        ''' 1. The result selects DeletedFlag, so the answer is already in the derived table.
        ''' 2. It does not, but the base table has the column. The key is matched back to the table
        '''    with NOT EXISTS - a row whose key finds no match must survive as not-deleted, which
        '''    is what the in-memory hydration does today, and an inner join would silently drop it.
        '''    NOT EXISTS rather than the LEFT JOIN the spec describes: identical semantics, no risk
        '''    of a join multiplying rows, and the wrapper takes predicates and has no join slot.
        ''' 3. The base table has no DeletedFlag. No predicate, and Show Deleted is already
        '''    unavailable on those pages.
        '''
        ''' An empty string is a real answer, meaning case 3. Nothing is added, exactly as
        ''' ApplyBrowseDeletedFilterFallback returns its rows untouched.
        ''' </summary>
        Private Shared Function BuildBrowseDeletedPredicate(outputNames As List(Of String),
                                                           sourceTableName As String,
                                                           showDeletedOnly As Boolean) As String
            Dim wanted = If(showDeletedOnly, "1", "0")
            Dim alias_ = BrowseSqlWrapper.InnerAlias

            If outputNames IsNot Nothing AndAlso
               outputNames.Any(Function(n) String.Equals(n, "DeletedFlag", StringComparison.OrdinalIgnoreCase)) Then
                Return "ISNULL(" & alias_ & ".[DeletedFlag], 0) = " & wanted
            End If

            Dim normalized = NormalizeTableName(sourceTableName)
            If normalized = String.Empty OrElse Not IsSafeSqlIdentifier(normalized) Then Return String.Empty
            If Not TableHasColumn(normalized, "DeletedFlag") Then Return String.Empty

            Dim keyColumn = GetPrimaryKeyFieldName(normalized)
            If String.IsNullOrWhiteSpace(keyColumn) OrElse Not IsSafeSqlIdentifier(keyColumn) Then Return String.Empty

            ' PK first, then the table's own key name - the same resolution ResolveResultKeyColumn
            ' uses, because every browse query aliases its key that way.
            Dim resultKey As String = Nothing
            If outputNames IsNot Nothing Then
                resultKey = outputNames.FirstOrDefault(Function(n) String.Equals(n, "PK", StringComparison.OrdinalIgnoreCase))
                If resultKey Is Nothing Then
                    resultKey = outputNames.FirstOrDefault(Function(n) String.Equals(n, keyColumn, StringComparison.OrdinalIgnoreCase))
                End If
            End If

            ' No key in the result means no way to ask the table about it. The hydration gives up
            ' here too and returns the rows unfiltered, so this matches rather than inventing.
            If resultKey Is Nothing Then Return String.Empty

            Dim exists = "EXISTS (SELECT 1 FROM dbo.[" & normalized & "] AS d WHERE d.[" & keyColumn & "] = " &
                         alias_ & ".[" & resultKey & "] AND ISNULL(d.[DeletedFlag], 0) = 1)"

            Return If(showDeletedOnly, exists, "NOT " & exists)
        End Function

        ''' <summary>
        ''' The QBE criteria as SQL predicates over the wrapper's q.[name] columns, with every value
        ''' carried by a typed parameter.
        '''
        ''' **Anything it cannot build completely, it declines.** Not "drop that one criterion" -
        ''' a dropped criterion returns more rows than were asked for, which reads as everything
        ''' having matched. Declining runs the old path instead, which behaves exactly as it does
        ''' today right down to the message it shows for a date it cannot read.
        '''
        ''' The operator itself is not decided here. ResolveTextComparison and QbeDateBounds own
        ''' what an operator means, and they are the same two the in-memory path and the Users page
        ''' already call, so the three cannot drift apart on semantics.
        ''' </summary>
        Private Shared Function TryBuildBrowseSqlPredicates(filters As Dictionary(Of String, String),
                                                           outputNames As List(Of String),
                                                           types As Dictionary(Of String, Type),
                                                           predicates As List(Of String),
                                                           parameters As List(Of SqlParameter),
                                                           ByRef declineReason As String) As Boolean
            Dim alias_ = BrowseSqlWrapper.InnerAlias
            Dim nextParameter = 0

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

                ' The name is taken from the select list rather than from the key, so nothing a
                ' caller supplies reaches the SQL as an identifier. A criterion naming a column the
                ' result does not have is skipped, which is what the in-memory path does too.
                Dim column = outputNames.FirstOrDefault(Function(n) String.Equals(n, fieldName, StringComparison.OrdinalIgnoreCase))
                If column Is Nothing Then Continue For

                Dim columnType As Type = Nothing
                If types Is Nothing OrElse Not types.TryGetValue(column, columnType) OrElse columnType Is Nothing Then
                    declineReason = "the type of " & column & " is not known"
                    Return False
                End If

                Dim quoted = alias_ & ".[" & column.Replace("]", "]]") & "]"

                If columnType Is GetType(Boolean) Then
                    Dim parsedBool As Boolean
                    If Not TryParseBooleanFilter(rawValue, parsedBool) Then
                        declineReason = column & " could not be read as a yes or no"
                        Return False
                    End If

                    Dim name = "@qbe" & nextParameter.ToString(CultureInfo.InvariantCulture)
                    nextParameter += 1
                    predicates.Add(quoted & " " & GetSqlOperator(comparisonOperator, QbeFieldKind.BooleanField) & " " & name)
                    parameters.Add(New SqlParameter(name, SqlDbType.Bit) With {.Value = parsedBool})
                    Continue For
                End If

                If IsNumericBrowseType(columnType) Then
                    Dim name = "@qbe" & nextParameter.ToString(CultureInfo.InvariantCulture)
                    nextParameter += 1

                    Dim parameter As SqlParameter = Nothing
                    If columnType Is GetType(Int16) OrElse columnType Is GetType(Int32) OrElse columnType Is GetType(Int64) Then
                        Dim whole As Long
                        If Not Long.TryParse(rawValue, Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, whole) Then
                            declineReason = column & " could not be read as a whole number"
                            Return False
                        End If
                        parameter = New SqlParameter(name, SqlDbType.BigInt) With {.Value = whole}
                    Else
                        Dim fraction As Decimal
                        If Not Decimal.TryParse(rawValue, Globalization.NumberStyles.Number, CultureInfo.InvariantCulture, fraction) Then
                            declineReason = column & " could not be read as a number"
                            Return False
                        End If
                        parameter = New SqlParameter(name, SqlDbType.Decimal) With {.Value = fraction, .Precision = 29, .Scale = 9}
                    End If

                    predicates.Add(quoted & " " & GetSqlOperator(comparisonOperator, QbeFieldKind.NumericField) & " " & name)
                    parameters.Add(parameter)
                    Continue For
                End If

                If columnType Is GetType(Date) Then
                    Dim chosen As Date
                    If Not QbeDateBounds.TryParseFilterValue(rawValue, chosen) Then
                        ' Declined rather than answered. The old path says so in a message that
                        ' names the field and tells the user to pick from the calendar, and
                        ' running it is how that message still gets shown.
                        declineReason = column & " could not be read as a date"
                        Return False
                    End If

                    Dim limits = QbeDateBounds.Resolve(comparisonOperator, chosen)
                    Dim sides As New List(Of String)()

                    If limits.Lower.HasValue Then
                        Dim name = "@qbe" & nextParameter.ToString(CultureInfo.InvariantCulture)
                        nextParameter += 1
                        sides.Add(quoted & If(limits.Excluded, " < ", " >= ") & name)
                        parameters.Add(New SqlParameter(name, SqlDbType.DateTime2) With {.Value = limits.Lower.Value})
                    End If

                    If limits.Upper.HasValue Then
                        Dim name = "@qbe" & nextParameter.ToString(CultureInfo.InvariantCulture)
                        nextParameter += 1
                        sides.Add(quoted & If(limits.Excluded, " >= ", " < ") & name)
                        parameters.Add(New SqlParameter(name, SqlDbType.DateTime2) With {.Value = limits.Upper.Value})
                    End If

                    If sides.Count = 0 Then Continue For

                    predicates.Add("(" & String.Join(If(limits.Excluded, " OR ", " AND "), sides) & ")")
                    Continue For
                End If

                ' Text. A mid-string wildcard is not refused here as it is on the in-memory path -
                ' SQL Server is happy with Gl%nn, and that refusal exists only because a DataView
                ' filter throws on it.
                Dim comparison = ResolveTextComparison(rawValue, comparisonOperator)
                Dim textName = "@qbe" & nextParameter.ToString(CultureInfo.InvariantCulture)
                nextParameter += 1

                Select Case comparison.SqlOperator
                    Case "LIKE" : predicates.Add(quoted & " LIKE " & textName)
                    Case "NOT LIKE" : predicates.Add("NOT (" & quoted & " LIKE " & textName & ")")
                    Case "<>" : predicates.Add(quoted & " <> " & textName)
                    Case Else : predicates.Add(quoted & " = " & textName)
                End Select

                parameters.Add(New SqlParameter(textName, SqlDbType.NVarChar, -1) With {.Value = comparison.Pattern})
            Next

            Return True
        End Function

        Private Shared Function IsNumericBrowseType(candidate As Type) As Boolean
            Return candidate Is GetType(Int16) OrElse candidate Is GetType(Int32) OrElse
                   candidate Is GetType(Int64) OrElse candidate Is GetType(Single) OrElse
                   candidate Is GetType(Double) OrElse candidate Is GetType(Decimal)
        End Function

        ''' <summary>
        ''' Decides whether this browse query can be answered by SQL Server instead of by fetching
        ''' the table and filtering it here, and builds the statement if it can.
        '''
        ''' Every reason to decline is checked before anything runs, on the SQL that is about to be
        ''' executed rather than on what FW_Pages holds - a page's SQL can be overridden in code or
        ''' typed into the box at runtime, so FW_Pages is not what runs.
        ''' </summary>
        Private Shared Function TryBuildWrappedBrowseQuery(effectiveSql As String,
                                                          sourceTableName As String,
                                                          filters As Dictionary(Of String, String),
                                                          showDeletedOnly As Boolean,
                                                          maxRows As Integer,
                                                          registrationId As Integer,
                                                          hasExplicitRegistrationPredicate As Boolean) As WrappedBrowseQuery
            Dim outputNames As List(Of String) = Nothing
            If Not BrowseSqlWrapper.TryReadOutputNames(effectiveSql, outputNames) Then
                Return WrappedBrowseQuery.Declined("the select list could not be read")
            End If

            ' In-memory registration scoping removes rows after the fetch, and while rows are
            ' removed afterwards no TOP can be correct. Judged by the same condition the old path
            ' uses, with the select list standing in for the result's columns.
            If registrationId > 0 AndAlso Not hasExplicitRegistrationPredicate AndAlso
               IsRegistrationScopedTable(sourceTableName) AndAlso
               outputNames.Any(Function(n) String.Equals(n, "RegistrationID", StringComparison.OrdinalIgnoreCase)) Then
                Return WrappedBrowseQuery.Declined("the registration is scoped in memory, not in the SQL")
            End If

            Dim predicates As New List(Of String)()
            Dim parameters As New List(Of SqlParameter)()

            Dim deletedPredicate = BuildBrowseDeletedPredicate(outputNames, sourceTableName, showDeletedOnly)
            If deletedPredicate <> String.Empty Then predicates.Add(deletedPredicate)

            If filters IsNot Nothing AndAlso filters.Count > 0 Then
                Dim types = GetBrowseResultTypes(effectiveSql)
                If types Is Nothing Then
                    Return WrappedBrowseQuery.Declined("the result's column types could not be read")
                End If

                Dim reason As String = Nothing
                If Not TryBuildBrowseSqlPredicates(filters, outputNames, types, predicates, parameters, reason) Then
                    Return WrappedBrowseQuery.Declined(If(reason, "a criterion could not be turned into SQL"))
                End If
            End If

            ' One more than is shown. The caller knows the list is longer than the cap only because
            ' it received one more row than it asked for, and LimitBrowseRows turns that back into
            ' the cap and the message.
            Dim topRows = If(maxRows > 0, maxRows + 1, 0)

            Dim wrapped = BrowseSqlWrapper.TryWrap(effectiveSql, topRows, predicates, "PK")
            If Not wrapped.Wrapped Then
                Return WrappedBrowseQuery.Declined(wrapped.DeclineReason)
            End If

            Return New WrappedBrowseQuery With {.Sql = wrapped.Sql, .Parameters = parameters}
        End Function

        Public Shared Function GetBrowseRowsByRegistration(registrationId As Integer,
                                                         Optional filters As Dictionary(Of String, String) = Nothing,
                                                         Optional baseSelectSql As String = Nothing,
                                                         Optional showDeletedOnly As Boolean = False,
                                                         Optional additionalScopePredicate As String = Nothing,
                                                         Optional scopeUserId As Integer = 0,
                                                         Optional maxRows As Integer = 0,
                                                         Optional overrideExplicitRegistrationPredicate As Boolean = False,
                                                         Optional sourceTableName As String = Nothing) As DataTable
            Dim table As New DataTable("BrowseRows")

            Using conn As New SqlConnection(ConnectionString)
                ' Timed separately from the Fill, and carried back the same way, because the two
                ' answer different questions. A first open in a process pays for the login, the TLS
                ' handshake and the JIT of the whole data stack, and none of that is the query - but
                ' it lands inside the caller's fetch figure and reads exactly like a slow one.
                Dim openTimer = UsageCounters.StartTimer()
                conn.Open()
                Dim openMillis = UsageCounters.ElapsedMillis(openTimer)
                If openMillis.HasValue Then
                    table.ExtendedProperties("BrowseOpenMilliseconds") = openMillis.Value
                End If

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
                    ' A mention of RegistrationID is not proof the page is scoped: on FW_Registration
                    ' it is the primary key, aliased AS PK in the select list.
                    If registrationId > 0 AndAlso hasRegistrationReference AndAlso
                       IsRegistrationScopedTable(sourceTableName) Then
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

                    ' Can SQL Server answer this instead of us? Decided from the SQL about to run,
                    ' never from FW_Pages - the page's SQL can be overridden in code or typed into
                    ' the box at runtime, and what runs is what has to be understood.
                    '
                    ' When it can, the criteria, the deleted state and the row cap all go into the
                    ' statement, and the three in-memory steps below are skipped. When it cannot,
                    ' nothing is skipped and the old path runs exactly as it always has.
                    ' Timed, because it is not free and it is not the query. Deciding whether the
                    ' SQL can be wrapped means reading the result's column types, which is its own
                    ' trip to the server, and the answer is cached afterwards - so it is paid once
                    ' per page and lands inside the caller's first fetch figure looking like a slow
                    ' database.
                    Dim wrapTimer = UsageCounters.StartTimer()

                    Dim wrapped = TryBuildWrappedBrowseQuery(effectiveSql,
                                                             sourceTableName,
                                                             filters,
                                                             showDeletedOnly,
                                                             maxRows,
                                                             registrationId,
                                                             hasExplicitRegistrationPredicate)

                    Dim wrapMillis = UsageCounters.ElapsedMillis(wrapTimer)
                    If wrapMillis.HasValue Then
                        table.ExtendedProperties("BrowseWrapMilliseconds") = wrapMillis.Value
                    End If

                    If Not wrapped.Wrapped Then
                        LogBrowseWrapDecline(sourceTableName, wrapped.DeclineReason)
                    End If

                    Dim sqlToRun = If(wrapped.Wrapped, wrapped.Sql, effectiveSql)

                    Using cmd As New SqlCommand(sqlToRun, conn)
                        If scopeUserId > 0 AndAlso sqlToRun.Contains("@UserID", StringComparison.OrdinalIgnoreCase) Then
                            cmd.Parameters.AddWithValue("@UserID", scopeUserId)
                        End If
                        If sqlToRun.Contains("@RegistrationID", StringComparison.OrdinalIgnoreCase) Then
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                        End If
                        For Each parameter In wrapped.Parameters
                            cmd.Parameters.Add(parameter)
                        Next
                        Try
                            ' Timed, and the figure carried back on the table itself - the same way
                            ' BrowseRowsLimited already travels. This is the SQL alone: the caller
                            ' times the whole Find separately, and the two numbers diverge on a
                            ' custom-SQL page because the QBE filters are applied client-side after
                            ' this returns. Database time flat while perceived time grows with the
                            ' table is a completely different fault from "the query is slow", and
                            ' invisible with only one of them.
                            Dim queryTimer = UsageCounters.StartTimer()

                            Using da As New SqlDataAdapter(cmd)
                                da.Fill(table)
                            End Using

                            Dim queryMillis = UsageCounters.ElapsedMillis(queryTimer)
                            If queryMillis.HasValue Then
                                table.ExtendedProperties("BrowseQueryMilliseconds") = queryMillis.Value
                            End If
                        Catch ex As Exception
                            ' The query that runs is not the query that was stored - the registration
                            ' value is substituted into it, a scope predicate may have been
                            ' appended, and it may have been wrapped - so "invalid column name" on
                            ' its own leaves nothing to look at. The text that actually failed goes
                            ' into the message.
                            '
                            ' A wrapped query that throws is not retried unwrapped. A decline is a
                            ' decision made before running anything, on evidence; a catch-and-retry
                            ' would be a guess made afterwards, it would hide the wrap that needs
                            ' fixing, and it would catch a timeout or a dropped connection and
                            ' answer it by running the expensive query a second time.
                            Throw New InvalidOperationException(
                                ex.Message & Environment.NewLine & Environment.NewLine &
                                "SQL THAT FAILED:" & Environment.NewLine & sqlToRun, ex)
                        End Try
                    End Using

                    ' Nothing below this line runs on a wrapped query. Leaving the deleted step in
                    ' would re-run the hydration on the rows that came back - handing back the very
                    ' round trip this exists to remove - and in Show Deleted mode it would filter
                    ' twice and return nothing.
                    If wrapped.Wrapped Then
                        RememberBrowseResultTypes(effectiveSql, table)
                        Return LimitBrowseRows(table, maxRows)
                    End If

                    ' The types this result produces, kept so a later search on the same SQL can
                    ' build typed parameters without asking the server what shape its own columns
                    ' are. A page opens unfiltered far more often than it is searched, so this is
                    ' almost always already answered by the time it is needed.
                    RememberBrowseResultTypes(effectiveSql, table)

                    ' Same rule as the predicate above: a result carrying RegistrationID is not
                    ' scoped when that column is the table's own key.
                    If registrationId > 0 AndAlso Not hasExplicitRegistrationPredicate AndAlso
                       table.Columns.Contains("RegistrationID") AndAlso
                       IsRegistrationScopedTable(sourceTableName) Then
                        table = FilterBrowseRowsByRegistration(table, registrationId)
                    End If

                    table = ApplyBrowseDeletedFilterFallback(table, showDeletedOnly, sourceTableName)

                    If filters IsNot Nothing AndAlso filters.Count > 0 Then
                        Dim unsupportedFilter As String = Nothing
                        Dim filterExpr = BuildDataViewFilterExpression(table, filters, unsupportedFilter)

                        If Not String.IsNullOrWhiteSpace(unsupportedFilter) Then
                            Throw New InvalidOperationException(unsupportedFilter)
                        End If

                        If Not String.IsNullOrWhiteSpace(filterExpr) Then
                            Try
                                Dim view As New DataView(table)
                                view.RowFilter = filterExpr
                                ' ToTable builds a fresh DataTable and ExtendedProperties do not
                                ' travel with it, so the query timing measured at the fill was lost
                                ' on exactly the searches that had a QBE criterion.
                                Dim filtered = view.ToTable()
                                CarryBrowseProperties(table, filtered)

                                ' Limited here too. This path returned before LimitBrowseRows and
                                ' so ignored the row cap entirely.
                                Return LimitBrowseRows(filtered, maxRows)
                            Catch ex As Exception
                                ' Never fall back to the unfiltered table. A filter that was silently
                                ' dropped returns every row, which reads as "everything matched" - the
                                ' opposite of what happened, and invisible for as long as nobody counts.
                                Throw New InvalidOperationException(
                                    "The filter could not be applied on this page: " & ex.Message &
                                    Environment.NewLine & Environment.NewLine &
                                    "FILTER: " & filterExpr, ex)
                            End Try
                        End If
                    End If

                    Return LimitBrowseRows(table, maxRows)
                End If

                ' No SQL for this page, and there is no generic query that could stand in for one.
                '
                ' This used to fall back to a hardcoded SELECT against dbo.FW_Entity, which answered
                ' whichever page arrived here with another page's data - or, once FW_Entity was
                ' removed, with an error naming a table the caller has nothing to do with. Saying
                ' plainly that the page has no SQL is the only useful answer.
                '
                ' Reaching this is a configuration fault rather than a user error: FW_Pages is
                ' meant to gain a row with PK-safe fallback SQL the first time a page opens.
                Throw New InvalidOperationException(
                    "This page has no SQL configured." & Environment.NewLine & Environment.NewLine &
                    "A browse page reads its query from FW_Pages, and no row was found or created for " &
                    If(String.IsNullOrWhiteSpace(sourceTableName), "this page", sourceTableName) & "." & Environment.NewLine &
                    "Open the page's row in Role Tables and give it a SELECT that aliases its key AS PK.")
            End Using
        End Function

        ''' <summary>
        ''' Carries the notes a browse result travels with onto a table that replaced it.
        '''
        ''' DataView.ToTable builds a fresh DataTable and ExtendedProperties do not come with it, so
        ''' anything the data layer told the caller through them is silently dropped by a filter.
        ''' That cost the query timing on every search that had a QBE criterion - which is most of
        ''' them - and the health page showed a database figure from whichever unfiltered search
        ''' happened to record one.
        '''
        ''' Copied rather than merged: the new table is the same result, so it carries the same
        ''' notes. Nothing here interprets them, which is why it survives a new one being added.
        ''' </summary>
        Private Shared Sub CarryBrowseProperties(source As DataTable, target As DataTable)
            If source Is Nothing OrElse target Is Nothing Then Return
            If source.ExtendedProperties Is Nothing OrElse source.ExtendedProperties.Count = 0 Then Return

            For Each key In source.ExtendedProperties.Keys
                target.ExtendedProperties(key) = source.ExtendedProperties(key)
            Next
        End Sub

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

        ''' <summary>
        ''' Whether rows of this table belong to a registration. False when RegistrationID is the
        ''' table's own key - on FW_Registration the filter would mean "show me my own row".
        ''' </summary>
        Public Shared Function IsRegistrationScopedTable(tableName As String) As Boolean
            If String.IsNullOrWhiteSpace(tableName) Then Return True

            Dim normalized = NormalizeTableName(tableName)
            If normalized = String.Empty Then Return True

            Return Not String.Equals(GetPrimaryKeyFieldName(normalized), "RegistrationID", StringComparison.OrdinalIgnoreCase)
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

        Public Shared Function GetPageSqlByTable(registrationId As Integer, tableName As String) As String
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return String.Empty
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 Table_SQL FROM dbo." & PagesTable & " " &
                    "WHERE (RegistrationID = @RegistrationID OR RegistrationID IS NULL) AND DB_Table = @DBTable " &
                    "ORDER BY CASE WHEN RegistrationID = @RegistrationID THEN 0 ELSE 1 END, PageID DESC", conn)
                    
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

        Public Shared Function GetPageIdByTable(registrationId As Integer, tableName As String) As Integer?
            If registrationId <= 0 OrElse String.IsNullOrWhiteSpace(tableName) Then
                Return Nothing
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 PageID FROM dbo." & PagesTable & " " &
                    "WHERE (RegistrationID = @RegistrationID OR RegistrationID IS NULL) AND DB_Table = @DBTable " &
                    "ORDER BY CASE WHEN RegistrationID = @RegistrationID THEN 0 ELSE 1 END, PageID DESC", conn)

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

        Public Shared Function GetPageSqlByWindowOrPage(registrationId As Integer, windowOrPageName As String) As String
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return String.Empty
            End If

            EnsurePageAliasCache()

            SyncLock metadataCacheLock
                Dim cachedSql As String = Nothing
                If pageSqlCache.TryGetValue(windowOrPageName.Trim(), cachedSql) Then
                    Return If(cachedSql, String.Empty)
                End If
            End SyncLock

            ' Not in the cache means no such page, since the cache holds every row. Returning empty
            ' is what the query did for a page with no row, so the caller's fallback is unchanged.
            Return String.Empty
        End Function

        ''' <summary>
        ''' The stored background colour for a browse page, as an ARGB value, or Nothing when none
        ''' has been chosen. Read from the same row and on the same trip as the page's SQL.
        ''' </summary>
        Public Shared Function GetPageBackgroundColor(windowOrPageName As String) As Integer?
            If String.IsNullOrWhiteSpace(windowOrPageName) Then Return Nothing

            EnsurePageAliasCache()

            SyncLock metadataCacheLock
                Dim cachedBackground As Integer? = Nothing
                If pageBackgroundCache.TryGetValue(windowOrPageName.Trim(), cachedBackground) Then
                    Return cachedBackground
                End If
            End SyncLock

            Return Nothing
        End Function

        ''' <summary>
        ''' Stores a browse page's background colour as an ARGB value. Returns False when the page
        ''' has no FW_Pages row - there is nothing to attach the colour to, and inventing a row
        ''' here would create one without the SQL, alias and table name that give it meaning.
        ''' </summary>
        Public Shared Function SavePageBackgroundColor(windowOrPageName As String, argb As Integer, updatedBy As Integer) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then Return False

            Dim pageKey = windowOrPageName.Trim()

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                ' Creates the row when there is not one. This used to be a bare UPDATE, so a form
                ' with no FW_Pages row could apply a colour and not keep it, and said so in a
                ' dialog that told the user about a table they cannot reach. Every such page hit
                ' it - a dialog, a one-off screen, anything never generated - and the answer was
                ' always the same row, typed by hand.
                '
                ' Only the name and the colour are written. DB_Table and Table_SQL stay null
                ' because a colour says nothing about them, and a page that later wants a real row
                ' will fill them in through UpsertPageRecord as it always has.
                '
                ' One round trip either way, and it reports which happened so the cache can be
                ' patched on an update and dropped on an insert.
                Using cmd As New SqlCommand(
                    "DECLARE @Inserted bit = 0; " &
                    "IF NOT EXISTS (SELECT 1 FROM dbo." & PagesTable & " WHERE WindowOrPage = @WindowOrPage) " &
                    "BEGIN " &
                    "  INSERT INTO dbo." & PagesTable & " " &
                    "    (RegistrationID, WindowOrPage, Background, DeletedFlag, UseHotFields, CreatedBy, CreatedOn) " &
                    "  VALUES (NULL, @WindowOrPage, @Background, 0, 0, @ModifiedBy, GETDATE()); " &
                    "  SET @Inserted = 1; " &
                    "END " &
                    "ELSE " &
                    "  UPDATE dbo." & PagesTable & " " &
                    "  SET Background = @Background, ModifiedBy = @ModifiedBy, ModifiedOn = GETDATE() " &
                    "  WHERE WindowOrPage = @WindowOrPage; " &
                    "SELECT @Inserted", conn)

                    cmd.Parameters.AddWithValue("@Background", argb)
                    cmd.Parameters.AddWithValue("@ModifiedBy", updatedBy)
                    cmd.Parameters.AddWithValue("@WindowOrPage", pageKey)

                    Dim result = cmd.ExecuteScalar()
                    Dim inserted = result IsNot Nothing AndAlso
                                   Not IsDBNull(result) AndAlso
                                   Convert.ToBoolean(result, CultureInfo.InvariantCulture)

                    ' The colour is read from the cache, so a save that does not reach the cache is
                    ' a colour that appears not to have saved until the application restarts. A new
                    ' row is not in the cache at all, so that one is dropped and reloaded rather
                    ' than patched - patching it would answer a later question the cache has never
                    ' been told the rest of the answer to.
                    If inserted Then
                        InvalidatePageCache()
                    Else
                        SyncLock metadataCacheLock
                            If pageAliasCacheLoaded AndAlso pageBackgroundCache.ContainsKey(pageKey) Then
                                pageBackgroundCache(pageKey) = argb
                            End If
                        End SyncLock
                    End If

                    Return True
                End Using
            End Using
        End Function

        ''' <summary>
        ''' What a page is called: its Table_Alias, or an empty string when it has none.
        '''
        ''' The caption for a page and for every button that opens it, since 2026-09-06. Per page
        ''' rather than per table, which is the point - two browse pages can be different views
        ''' of one table and can be called different things.
        '''
        ''' Empty means "nothing chosen", and the caller falls back to the formatter. UpsertPageRecord
        ''' used to fill this with the table name for a page that had none, which made every page look
        ''' as though it had been named when nothing had; it now leaves it blank.
        '''
        ''' registrationId is accepted and not used. FW_Pages rows are shared - UpsertPageRecord
        ''' writes RegistrationID as NULL - so a page's caption is the same for every tenant. The
        ''' parameter is kept because honouring it later is a change to this query alone.
        ''' </summary>
        Public Shared Function GetPageAliasByWindowOrPage(registrationId As Integer, windowOrPageName As String) As String
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return String.Empty
            End If

            EnsurePageAliasCache()

            Dim aliasName As String = Nothing
            SyncLock metadataCacheLock
                If pageAliasCache.TryGetValue(windowOrPageName.Trim(), aliasName) Then
                    Return aliasName
                End If
            End SyncLock

            Return String.Empty
        End Function

        ''' <summary>
        ''' Fills the caption cache, once, with every page in one query.
        '''
        ''' The query runs outside the lock. Holding it across a round trip would make every page on
        ''' every thread wait for the database rather than for the dictionary, and the worst a race
        ''' costs here is the same harmless query twice.
        '''
        ''' Pages with no alias are stored as empty rather than left out, so a page that has not been
        ''' named is answered from memory instead of re-querying on every look.
        ''' </summary>
        ''' <summary>
        ''' Whether this page shows the Hot Fields button.
        ''' </summary>
        ''' <remarks>
        ''' Reads the cache the page's caption already loaded, so asking costs nothing. A page with
        ''' no FW_Pages row does not show the button - the feature is opt in, and a page nobody has
        ''' registered has not opted in.
        ''' </remarks>
        ''' <summary>
        ''' Records whether a page shows the Hot Fields button.
        ''' </summary>
        ''' <remarks>
        ''' Written when a page is generated, from the tick on the generation request. The cached
        ''' value is corrected in the same breath, so a page opened later in this session reflects
        ''' the change without the whole page cache being thrown away.
        ''' </remarks>
        Public Shared Function SetPageUsesHotFields(windowOrPageName As String, usesHotFields As Boolean) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return False
            End If

            Dim pageName = windowOrPageName.Trim()

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo." & PagesTable & " SET UseHotFields = @UseHotFields " &
                        "WHERE WindowOrPage = @WindowOrPage", conn)

                        cmd.Parameters.Add("@UseHotFields", SqlDbType.Bit).Value = usesHotFields
                        cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = pageName
                        cmd.ExecuteNonQuery()
                    End Using
                End Using
            Catch
                Return False
            End Try

            SyncLock metadataCacheLock
                If pageAliasCacheLoaded Then
                    pageHotFieldsCache(pageName) = usesHotFields
                End If
            End SyncLock

            Return True
        End Function

        Public Shared Function GetPageUsesHotFields(windowOrPageName As String) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return False
            End If

            EnsurePageAliasCache()

            SyncLock metadataCacheLock
                Dim usesHotFields As Boolean
                If pageHotFieldsCache.TryGetValue(windowOrPageName.Trim(), usesHotFields) Then
                    Return usesHotFields
                End If
            End SyncLock

            Return False
        End Function

        ''' <summary>
        ''' Which fields the Hot Fields panel shows for a page.
        '''
        ''' Empty means **nothing** is shown, not everything. An App Admin decides what this panel is
        ''' for, and until they have ticked something there is nothing to put in front of anybody.
        ''' They still see every field themselves, unticked, which is how they choose.
        '''
        ''' It read the other way round until 2026-09-09. Showing everything by default meant the
        ''' first untick froze every remaining field into an explicit list, so a column added to the
        ''' table afterwards would never appear and nothing would say why.
        '''
        ''' Comes from the row EnsurePageAliasCache already holds, so it costs no round trip.
        ''' </summary>
        Public Shared Function GetPageHotFields(windowOrPageName As String) As HashSet(Of String)
            Dim selected As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return selected
            End If

            EnsurePageAliasCache()

            Dim stored As String = Nothing
            SyncLock metadataCacheLock
                pageHotFieldListCache.TryGetValue(windowOrPageName.Trim(), stored)
            End SyncLock

            If String.IsNullOrWhiteSpace(stored) Then
                Return selected
            End If

            For Each fieldName In stored.Split({","c, ";"c}, StringSplitOptions.RemoveEmptyEntries)
                Dim trimmed = fieldName.Trim()
                If trimmed <> String.Empty Then
                    selected.Add(trimmed)
                End If
            Next

            Return selected
        End Function

        ''' <summary>
        ''' Stores which fields the Hot Fields panel shows. An empty set clears the column, which
        ''' returns the page to showing every field rather than showing none - an App Admin who
        ''' unticks everything gets the full list back instead of an empty panel they cannot use to
        ''' tick anything again.
        '''
        ''' Returns False when the page has no FW_Pages row. Patches the cache on success, because
        ''' the panel reads from it and a tick that only reached the database would appear to have
        ''' done nothing until the application restarted.
        ''' </summary>
        Public Shared Function SavePageHotFields(windowOrPageName As String,
                                                  fieldNames As IEnumerable(Of String),
                                                  updatedBy As Integer) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then Return False

            Dim ordered As New List(Of String)()
            If fieldNames IsNot Nothing Then
                For Each fieldName In fieldNames
                    Dim trimmed = If(fieldName, String.Empty).Trim()
                    If trimmed <> String.Empty AndAlso Not ordered.Contains(trimmed, StringComparer.OrdinalIgnoreCase) Then
                        ordered.Add(trimmed)
                    End If
                Next
            End If

            Dim stored = String.Join(",", ordered)

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo." & PagesTable & " " &
                        "SET HotFields = @HotFields, ModifiedBy = @ModifiedBy, ModifiedOn = GETDATE() " &
                        "WHERE WindowOrPage = @WindowOrPage", conn)

                        cmd.Parameters.Add("@HotFields", SqlDbType.VarChar, -1).Value =
                            If(stored = String.Empty, CObj(DBNull.Value), CObj(stored))
                        cmd.Parameters.AddWithValue("@ModifiedBy", updatedBy)
                        cmd.Parameters.AddWithValue("@WindowOrPage", windowOrPageName.Trim())

                        If cmd.ExecuteNonQuery() <= 0 Then
                            Return False
                        End If
                    End Using
                End Using
            Catch
                Return False
            End Try

            SyncLock metadataCacheLock
                If pageAliasCacheLoaded Then
                    pageHotFieldListCache(windowOrPageName.Trim()) = stored
                End If
            End SyncLock

            Return True
        End Function

        Private Shared Sub EnsurePageAliasCache()
            SyncLock metadataCacheLock
                If pageAliasCacheLoaded Then Return
            End SyncLock

            Dim loaded As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim loadedHotFields As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            Dim loadedDbTable As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim loadedHotFieldList As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim loadedSql As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim loadedBackground As New Dictionary(Of String, Integer?)(StringComparer.OrdinalIgnoreCase)

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    ' ORDER BY PageID matters. Nothing stops two rows sharing a WindowOrPage, and the
                    ' three methods this cache replaces each took the highest PageID - TOP 1 ORDER BY
                    ' PageID DESC. Reading ascending and letting a later row overwrite an earlier one
                    ' lands on the same row, so a duplicate resolves the same way it always has
                    ' rather than depending on the order the server happened to return.
                    Using cmd As New SqlCommand(
                        "SELECT WindowOrPage, ISNULL(LTRIM(RTRIM(Table_Alias)), '') AS Table_Alias, " &
                        "ISNULL(UseHotFields, 0) AS UseHotFields, " &
                        "DB_Table, Table_SQL, Background, HotFields " &
                        "FROM dbo." & PagesTable & " ORDER BY PageID", conn)

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim pageName = SafeString(reader("WindowOrPage"))
                                If pageName <> String.Empty Then
                                    loaded(pageName) = SafeString(reader("Table_Alias"))

                                    Dim hotFields = reader("UseHotFields")
                                    loadedHotFields(pageName) = hotFields IsNot Nothing AndAlso
                                                                hotFields IsNot DBNull.Value AndAlso
                                                                Convert.ToBoolean(hotFields, CultureInfo.InvariantCulture)

                                    loadedDbTable(pageName) = SafeString(reader("DB_Table"))
                                    loadedSql(pageName) = SafeString(reader("Table_SQL"))
                                    loadedHotFieldList(pageName) = SafeString(reader("HotFields"))

                                    Dim background = reader("Background")
                                    If background Is Nothing OrElse background Is DBNull.Value Then
                                        loadedBackground(pageName) = Nothing
                                    Else
                                        loadedBackground(pageName) = Convert.ToInt32(background, CultureInfo.InvariantCulture)
                                    End If
                                End If
                            End While
                        End Using
                    End Using
                End Using
            Catch
                ' A caption is not worth a page that will not open. Nothing is marked loaded, so the
                ' next look tries again, and until then every page falls back to the formatter.
                Return
            End Try

            SyncLock metadataCacheLock
                pageAliasCache.Clear()
                For Each pair In loaded
                    pageAliasCache(pair.Key) = pair.Value
                Next

                pageHotFieldsCache.Clear()
                For Each pair In loadedHotFields
                    pageHotFieldsCache(pair.Key) = pair.Value
                Next

                pageHotFieldListCache.Clear()
                For Each pair In loadedHotFieldList
                    pageHotFieldListCache(pair.Key) = pair.Value
                Next

                pageDbTableCache.Clear()
                For Each pair In loadedDbTable
                    pageDbTableCache(pair.Key) = pair.Value
                Next

                pageSqlCache.Clear()
                For Each pair In loadedSql
                    pageSqlCache(pair.Key) = pair.Value
                Next

                pageBackgroundCache.Clear()
                For Each pair In loadedBackground
                    pageBackgroundCache(pair.Key) = pair.Value
                Next

                pageAliasCacheLoaded = True
            End SyncLock
        End Sub

        Public Shared Function GetExposedPageChoices() As DataTable
            Dim choices As New DataTable("ExposedPages")
            choices.Columns.Add("ID", GetType(Integer))
            choices.Columns.Add("SchemaID", GetType(Integer))
            choices.Columns.Add("Table_Alias", GetType(String))
            choices.Columns.Add("DB_Table", GetType(String))
            choices.Columns.Add("WindowOrPage", GetType(String))

            ' Every page with a known table, not only those flagged ExposedToUser.
            '
            ' That flag gated this one list and nothing else. No code has ever written it, there is
            ' no screen that sets it, and the only write anywhere is a single INSERT in sql/053 - so
            ' every row sat at 0 and this list came back empty, which is how it was found. A gate
            ' with no key is not a permission, it is a page that does not work.
            '
            ' The list is what an App Admin picks a table from to explain somebody's access, so a
            ' page missing from it cannot be diagnosed at all. Listing them all is the useful
            ' answer, and a newly generated page now appears without anyone setting a flag.
            '
            ' The join to FW_RoleSchema stays: a table with no schema row has no field permissions
            ' to explain, so it would be an empty answer rather than a hidden one.
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT MIN(rt.PageID) AS ID, MIN(rs.ID) AS SchemaID, rt.Table_Alias, " &
                    "MIN(rt.DB_Table) AS DB_Table, MIN(rt.WindowOrPage) AS WindowOrPage " &
                    "FROM dbo." & PagesTable & " rt " &
                    "INNER JOIN dbo.FW_RoleSchema rs ON rs.DB_Table = rt.DB_Table AND ISNULL(rs.IsActive, 1) = 1 " &
                    "WHERE ISNULL(rt.DeletedFlag, 0) = 0 " &
                    "GROUP BY rt.Table_Alias ORDER BY rt.Table_Alias", conn)
                    cmd.CommandTimeout = 10
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(choices)
                    End Using
                End Using
            End Using

            Return choices
        End Function

        Public Shared Function GetPageDbTableByWindowOrPage(registrationId As Integer, windowOrPageName As String) As String
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return String.Empty
            End If

            EnsurePageAliasCache()

            SyncLock metadataCacheLock
                Dim cachedTable As String = Nothing
                If pageDbTableCache.TryGetValue(windowOrPageName.Trim(), cachedTable) Then
                    Return If(cachedTable, String.Empty)
                End If
            End SyncLock

            Return String.Empty
        End Function

        Public Shared Function CheckIfPageRecordExists(registrationId As Integer, windowOrPageName As String) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return False
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COUNT(1) FROM dbo." & PagesTable & " " &
                    "WHERE WindowOrPage = @WindowOrPage", conn)
                    
                    cmd.Parameters.AddWithValue("@WindowOrPage", windowOrPageName.Trim())
                    
                    Dim result = cmd.ExecuteScalar()
                    Return If(result IsNot Nothing AndAlso IsNumeric(result), CInt(result) > 0, False)
                End Using
            End Using
        End Function

        Public Shared Function GetPageMetadata(windowOrPageName As String) As DataRow
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return Nothing
            End If

            Dim table As New DataTable("FW_PagesMetadata")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 1 WindowOrPage, DB_Table, Table_Alias, Table_SQL " &
                    "FROM dbo." & PagesTable & " WHERE WindowOrPage = @WindowOrPage " &
                    "ORDER BY PageID DESC", conn)
                    cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = windowOrPageName.Trim()
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return If(table.Rows.Count = 0, Nothing, table.Rows(0))
        End Function

        Public Shared Function UpsertPageRecord(registrationId As Integer, windowOrPageName As String, dbTableName As String, tableAlias As String, tableSql As String, userId As Integer) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) OrElse String.IsNullOrWhiteSpace(dbTableName) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    
                    ' The caller's spelling of the table is only a request. The database has the
                    ' authoritative one, and the first statement below replaces @DBTable with it -
                    ' in the same batch, so it costs nothing. Two writers reach here: the page
                    ' generator, and Base_B when a page opens with no FW_Pages row, which passes the
                    ' page's own hard-coded field. Generated pages carried an upper-cased name for a
                    ' long time, which is why FW_Pages held FW_USERS and FW_Users for one table.
                    ' Normalising here means those rows correct themselves as the pages are opened,
                    ' without editing a single generated file.
                    '
                    ' ISNULL keeps the caller's value when sys.tables has no match, so a view - which
                    ' is not in sys.tables at all - is stored exactly as it was passed.
                    Dim storedTableName = dbTableName.Trim()

                    Using cmd As New SqlCommand(
                        "SELECT @DBTable = ISNULL((SELECT name FROM sys.tables WHERE name = @DBTable), @DBTable); " &
                        "IF EXISTS (SELECT 1 FROM dbo." & PagesTable & " WHERE WindowOrPage = @WindowOrPage) " &
                        "UPDATE dbo." & PagesTable & " SET RegistrationID = NULL, DB_Table = @DBTable, Table_Alias = @TableAlias, Table_SQL = @TableSQL WHERE WindowOrPage = @WindowOrPage " &
                        "ELSE INSERT INTO dbo." & PagesTable & " (RegistrationID, WindowOrPage, DB_Table, Table_Alias, Table_SQL, CreatedBy) VALUES (NULL, @WindowOrPage, @DBTable, @TableAlias, @TableSQL, @CreatedBy)", conn)
                        cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = windowOrPageName.Trim()

                        ' Sent back out so FW_TableAliases is keyed by the same spelling FW_Pages now
                        ' holds, rather than by whatever the caller happened to pass.
                        Dim dbTableParameter = cmd.Parameters.Add("@DBTable", SqlDbType.VarChar, 100)
                        dbTableParameter.Direction = ParameterDirection.InputOutput
                        dbTableParameter.Value = storedTableName

                        cmd.Parameters.Add("@TableAlias", SqlDbType.VarChar, 100).Value = If(String.IsNullOrWhiteSpace(tableAlias), dbTableName.Trim(), tableAlias.Trim())
                        cmd.Parameters.Add("@TableSQL", SqlDbType.VarChar, -1).Value = DbValue(tableSql)
                        cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = userId
                        cmd.ExecuteNonQuery()

                        If dbTableParameter.Value IsNot Nothing AndAlso Not DBNull.Value.Equals(dbTableParameter.Value) Then
                            Dim normalised = Convert.ToString(dbTableParameter.Value, CultureInfo.InvariantCulture)
                            If Not String.IsNullOrWhiteSpace(normalised) Then
                                storedTableName = normalised.Trim()
                            End If
                        End If
                    End Using
                    
                    ' Also save the table alias to FW_TableAliases for global reuse
                    SaveTableAlias(storedTableName, If(String.IsNullOrWhiteSpace(tableAlias), storedTableName, tableAlias.Trim()))

                    ' This row is cached - alias, table name and SQL all come from it - so the cache
                    ' has just been made wrong by the write above.
                    InvalidatePageCache()

                    Return True
                End Using
            Catch ex As Exception
                LogFallbackUsage("SQL_Fallback_UpsertException",
                                 "Failed to persist FW_Pages fallback for " & windowOrPageName & ": " & ex.Message,
                                 windowOrPageName,
                                 registrationId)
                Return False
            End Try
        End Function

        Public Shared Function UpdatePageSql(windowOrPageName As String, tableSql As String) As Boolean
            If String.IsNullOrWhiteSpace(windowOrPageName) Then
                Return False
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "UPDATE dbo." & PagesTable & " SET Table_SQL = @TableSQL WHERE WindowOrPage = @WindowOrPage", conn)
                        cmd.Parameters.Add("@WindowOrPage", SqlDbType.VarChar, 100).Value = windowOrPageName.Trim()
                        cmd.Parameters.Add("@TableSQL", SqlDbType.VarChar, -1).Value = DbValue(tableSql)
                        Dim updated = cmd.ExecuteNonQuery() > 0
                        If updated Then
                            InvalidatePageCache()
                        End If
                        Return updated
                    End Using
                End Using
            Catch ex As Exception
                LogFallbackUsage("SQL_Fallback_UpdateException",
                                 "Failed to update FW_Pages SQL for " & windowOrPageName & ": " & ex.Message,
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

        ''' <summary>
        ''' Every table a page can be generated against, spelled as the database spells it.
        ''' </summary>
        ''' <remarks>
        ''' This used to return UPPER(name). Upper case reads better in a picker, but the name the
        ''' caller selected was also the name written into the generated page, and from there into
        ''' every audit row that page wrote - which is why FW_AuditTrail holds both FW_Users and
        ''' FW_USERS for one table. The upper-casing belongs to whatever displays the list, so it
        ''' now happens there. Ordering is unchanged.
        ''' </remarks>
        Public Shared Function GetDatabaseTables() As List(Of String)
            Dim tables As New List(Of String)()

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    ' Every table in dbo. It listed FW_, AS_ and CRM_ until 2026-09-18, which was a
                    ' list that had to grow for each new application - and CTY_ tables would have
                    ' been invisible here until somebody remembered to add them. A table nobody
                    ' should pick is switched off by its FW_RoleSchema row, which is one list
                    ' instead of two that can disagree.
                    '
                    ' Switched off means not offered here either: a table nobody can be granted
                    ' permission on is one whose generated pages nobody could open. Only where a
                    ' row exists and says so - a table the sweep has not seen yet has no row at
                    ' all, and that is a new table, not a refused one.
                    Using cmd As New SqlCommand(
                        "SELECT t.name FROM sys.tables t " &
                        "WHERE t.schema_id = SCHEMA_ID('dbo') " &
                        "  AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleSchema s " &
                        "                  WHERE s.DB_Table = t.name AND ISNULL(s.IsActive, 1) = 0) " &
                        "ORDER BY UPPER(t.name)", conn)
                        
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

        ''' <summary>
        ''' Turns a table name into its display alias - FW_RoleDetails becomes "Role Details".
        ''' </summary>
        ''' <param name="tableName">
        ''' Must carry the database's own casing. The word breaks are found by looking for a
        ''' lower-to-upper transition, so an upper-cased name yields "ROLEDETAILS" rather than
        ''' "Role Details". This used to be guaranteed by asking sys.tables for the real casing on
        ''' every call, which cost one round trip per table - 26 in a single measured session.
        ''' Every caller already holds the correctly-cased name, so the contract moved here.
        ''' </param>
        Public Shared Function FormatTableNameAsAlias(tableName As String) As String
            If String.IsNullOrWhiteSpace(tableName) Then
                Return tableName
            End If

            Dim name = tableName.Trim()
            
            ' Strip FW_Perm_, FW_ or AS_ prefix (case-insensitive in VB.NET).
            '
            ' FW_Perm_ first, because the longer prefix has to win: three characters off
            ' FW_Perm_Dashboard leaves "Perm Dashboard", which is what an administrator saw in the
            ' Roles table list until 2026-09-10. The prefix marks a table that exists only to carry
            ' a permission - see CLAUDE.md - and it is for developers, not for the person ticking
            ' the box.
            '
            ' This matters more than a one-off correction to the row would: the schema sync
            ' rewrites Table_Alias from this function every time it runs, so an alias fixed by hand
            ' is undone on the next sync.
            If name.ToUpper().StartsWith("FW_PERM_") Then
                name = name.Substring(8)
            ElseIf name.ToUpper().StartsWith("FW_") Then
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

        ''' <summary>
        ''' Every selectable table, with the upper-cased name shown to the user and the display
        ''' alias derived from it.
        ''' </summary>
        ''' <remarks>
        ''' One round trip. The query returns the name twice - once as the database spells it, for
        ''' FormatTableNameAsAlias, and once upper-cased, for the list the user reads. Deriving the
        ''' alias used to re-query sys.tables per table to recover the casing this now simply keeps.
        ''' </remarks>
        Public Shared Function GetDatabaseTablesWithAliases() As List(Of TableData)
            Dim tables As New List(Of TableData)()

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT name, UPPER(name) AS DisplayName FROM sys.tables " &
                        "WHERE name LIKE 'FW_%' OR name LIKE 'AS_%' OR name LIKE 'CRM_%' " &
                        "ORDER BY UPPER(name)", conn)

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim actualName = reader.GetString(0)
                                Dim displayName = reader.GetString(1)
                                tables.Add(New TableData(displayName, FormatTableNameAsAlias(actualName)))
                            End While
                        End Using
                    End Using
                End Using
            Catch
                ' Return what was read; an empty list leaves the caller's picker empty rather than
                ' failing, which is how GetDatabaseTables has always behaved.
            End Try

            Return tables
        End Function

        Public Shared Function TableHasColumn(tableName As String, columnName As String) As Boolean
            If String.IsNullOrWhiteSpace(tableName) OrElse String.IsNullOrWhiteSpace(columnName) Then
                Return False
            End If

            Dim facts = GetTableSchemaFacts(tableName)
            If facts Is Nothing Then
                Return False
            End If

            Return facts.NameSet.Contains(columnName.Trim())
        End Function

        ''' <summary>
        ''' A table's columns - names, declared order and text lengths - from one read, cached for
        ''' the process and cleared by InvalidateSchemaCache.
        '''
        ''' Filtered to the dbo schema. The per-column question it replaces was not, but every
        ''' table in this database is dbo, no table name appears in two schemas, and every statement
        ''' the framework writes is dbo-qualified - so this narrows a question that had no other
        ''' answer to give. A database that put framework tables in another schema would need this
        ''' revisited, and would have larger problems first.
        ''' </summary>
        ''' <returns>
        ''' Nothing when the question could not be asked at all. That is not the same as a table
        ''' with no such column, and the difference matters: a failure must not be remembered as an
        ''' answer, or a transient fault would tell the application for the rest of the session that
        ''' a table has no DeletedFlag and quietly turn soft delete off.
        ''' </returns>
        Private Shared Function GetTableSchemaFacts(tableName As String) As TableSchemaFacts
            Dim key = NormalizeTableName(tableName)
            If key = String.Empty Then
                Return Nothing
            End If

            Dim cached As TableSchemaFacts = Nothing
            SyncLock metadataCacheLock
                If tableSchemaFactsCache.TryGetValue(key, cached) Then
                    Return cached
                End If
            End SyncLock

            Try
                Dim orderedNames As New List(Of String)()
                Dim nameSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                Dim textLengths As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)

                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH " &
                        "FROM INFORMATION_SCHEMA.COLUMNS " &
                        "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName " &
                        "ORDER BY ORDINAL_POSITION", conn)

                        cmd.Parameters.AddWithValue("@TableName", key)

                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim name = SafeString(reader("COLUMN_NAME"))
                                If name = String.Empty Then Continue While

                                orderedNames.Add(name)
                                nameSet.Add(name)

                                Dim dataType = SafeString(reader("DATA_TYPE")).ToLowerInvariant()
                                If dataType = "varchar" OrElse dataType = "nvarchar" OrElse
                                   dataType = "char" OrElse dataType = "nchar" Then

                                    If Not IsDBNull(reader("CHARACTER_MAXIMUM_LENGTH")) Then
                                        Dim maxLength = Convert.ToInt32(reader("CHARACTER_MAXIMUM_LENGTH"), CultureInfo.InvariantCulture)
                                        If maxLength > 0 Then textLengths(name) = maxLength
                                    End If
                                End If
                            End While
                        End Using
                    End Using
                End Using

                Dim facts As New TableSchemaFacts With {
                    .OrderedNames = orderedNames,
                    .NameSet = nameSet,
                    .TextMaxLengths = textLengths
                }

                SyncLock metadataCacheLock
                    tableSchemaFactsCache(key) = facts
                End SyncLock

                Return facts
            Catch
                Return Nothing
            End Try
        End Function

        Public Shared Function TableHasRowVersion(tableName As String) As Boolean
            If String.IsNullOrWhiteSpace(tableName) Then
                Return False
            End If

            Dim cached As Boolean
            SyncLock metadataCacheLock
                If rowVersionCache.TryGetValue(tableName.Trim(), cached) Then
                    Return cached
                End If
            End SyncLock

            Try
                Dim answer As Boolean
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT COUNT(1) FROM sys.columns AS c " &
                        "INNER JOIN sys.tables AS t ON t.object_id = c.object_id " &
                        "INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id " &
                        "WHERE s.name = N'dbo' AND t.name = @TableName AND c.system_type_id = 189", conn)
                        cmd.Parameters.AddWithValue("@TableName", tableName.Trim())
                        answer = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0
                    End Using
                End Using

                ' Success only. A page whose concurrency protection is reported absent because of a
                ' dropped connection must ask again, not be told so for the rest of the session.
                SyncLock metadataCacheLock
                    rowVersionCache(tableName.Trim()) = answer
                End SyncLock

                Return answer
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
            ' The one write a preview is allowed to make, decided deliberately.
            '
            ' Every other gated path either changes a record or is a convenience nobody asked for.
            ' This is page configuration, it is pressed on purpose, and the moment somebody wants to
            ' arrange a tab order is while looking at the layout it belongs to - which is what a
            ' preview is. Blocking it meant a Save that appeared to do nothing.
            '
            ' A row saved for a field that is in the request but not yet generated is kept rather
            ' than being a problem: ApplySavedTabOrder matches saved rows to controls by name and
            ' ignores the ones it cannot find, so the order is simply waiting for the field to
            ' exist.

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

        Public Shared Function GetPageGenerationById(generatedPageId As Integer) As DataRow
            Dim table As New DataTable("PageGeneration")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT * FROM dbo." & GeneratedPagesTable & " WHERE GeneratedPageID = @GeneratedPageID", conn)
                    cmd.Parameters.Add("@GeneratedPageID", SqlDbType.Int).Value = generatedPageId
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
                    "SELECT TOP 1 GeneratedPageID FROM dbo." & GeneratedPagesTable & " " &
                    "WHERE RequestName = @RequestName AND BrowsePageName = @BrowsePageName AND MaintenancePageName = @MaintenancePageName " &
                    "ORDER BY GeneratedPageID DESC", conn)
                    cmd.Parameters.Add("@RequestName", SqlDbType.VarChar, -1).Value = If(requestName, String.Empty).Trim()
                    cmd.Parameters.Add("@BrowsePageName", SqlDbType.VarChar, -1).Value = If(browsePageName, String.Empty).Trim()
                    cmd.Parameters.Add("@MaintenancePageName", SqlDbType.VarChar, -1).Value = If(maintenancePageName, String.Empty).Trim()
                    Dim result = cmd.ExecuteScalar()
                    If result Is Nothing OrElse IsDBNull(result) Then Return 0
                    Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        Public Shared Function SavePageGenerationMaintenanceBaseline(generatedPageId As Integer,
                                                                       source As String,
                                                                       sourceHash As String) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo." & GeneratedPagesTable & " SET GeneratedMaintenanceSource = @Source, GeneratedMaintenanceHash = @Hash, UpdatedOn = GETDATE() WHERE GeneratedPageID = @GeneratedPageID", conn)
                    cmd.Parameters.Add("@Source", SqlDbType.VarChar, -1).Value = If(source, String.Empty)
                    cmd.Parameters.Add("@Hash", SqlDbType.VarChar, 64).Value = If(sourceHash, String.Empty)
                    cmd.Parameters.Add("@GeneratedPageID", SqlDbType.Int).Value = generatedPageId
                    Return cmd.ExecuteNonQuery() = 1
                End Using
            End Using
        End Function

        ''' <summary>
        ''' Records the hash of a generated browse page, so a hand-edited _B page is protected the
        ''' same way a _U page is. There is no source column to match the maintenance baseline: the
        ''' browse side only needs to answer whether the file still matches what was generated.
        ''' </summary>
        Public Shared Function SavePageGenerationBrowseBaseline(generatedPageId As Integer,
                                                                sourceHash As String) As Boolean
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo." & GeneratedPagesTable & " SET GeneratedBrowseHash = @Hash, UpdatedOn = GETDATE() WHERE GeneratedPageID = @GeneratedPageID", conn)
                    cmd.Parameters.Add("@Hash", SqlDbType.VarChar, 64).Value = If(sourceHash, String.Empty)
                    cmd.Parameters.Add("@GeneratedPageID", SqlDbType.Int).Value = generatedPageId
                    Return cmd.ExecuteNonQuery() = 1
                End Using
            End Using
        End Function

        Public Shared Function GetPageGenerationSchema() As DataTable
            Dim table As New DataTable("PageGenerationSchema")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 0 * FROM dbo." & GeneratedPagesTable, conn)
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

        ''' <summary>
        ''' The columns SQL Server fills for itself, which no write may ever name.
        '''
        ''' Nothing else in the framework reads is_computed, so a generated page had no way to know
        ''' and offered FW_Users.FirstLast as an ordinary text box. SQL Server refuses any UPDATE or
        ''' INSERT naming a computed column, so the record could not be saved at all - and the page
        ''' had invited the value in the first place.
        ''' </summary>
        Public Shared Function GetComputedColumnNames(tableName As String) As HashSet(Of String)
            Dim computed As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(tableName) Then Return computed

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT c.name " &
                    "FROM sys.columns AS c " &
                    "INNER JOIN sys.tables AS t ON t.object_id = c.object_id " &
                    "INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id " &
                    "WHERE s.name = N'dbo' AND t.name = @TableName AND c.is_computed = 1", conn)
                    cmd.Parameters.Add("@TableName", SqlDbType.VarChar, 128).Value = tableName.Trim()
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            computed.Add(reader.GetString(0))
                        End While
                    End Using
                End Using
            End Using

            Return computed
        End Function

        ''' <summary>
        ''' Every registered table, whether it is enabled, and what already depends on it.
        '''
        ''' Enabled is FW_RoleSchema.IsActive, reported as it is stored rather than inverted, so the
        ''' screen and the column say the same thing.
        '''
        ''' The two counts are what disabling costs. Roles is how many roles hold the table: their
        ''' permissions keep working, because permission resolution reads FW_RoleDetails without
        ''' consulting IsActive - what is lost is the ability to change them, since Roles_U's left
        ''' grid and the access diagnostic both drop an inactive table. Pages is how many FW_Pages
        ''' rows name it: those pages still open, but they can no longer be regenerated, because
        ''' page generation will not offer the table.
        ''' </summary>
        Public Shared Function GetTableEnablement() As DataTable
            Dim table As New DataTable("TableEnablement")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT s.DB_Table, " &
                    "       ISNULL(NULLIF(LTRIM(RTRIM(s.Table_Alias)), ''), s.DB_Table) AS Table_Alias, " &
                    "       CASE WHEN ISNULL(s.IsActive, 1) = 0 THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS IsEnabled, " &
                    "       (SELECT COUNT(DISTINCT rd.RoleID) FROM dbo.FW_RoleDetails rd " &
                    "         WHERE rd.DB_Table = s.DB_Table AND ISNULL(rd.DeletedFlag, 0) = 0) AS RoleCount, " &
                    "       (SELECT COUNT(*) FROM dbo." & PagesTable & " p " &
                    "         WHERE p.DB_Table = s.DB_Table AND ISNULL(p.DeletedFlag, 0) = 0) AS PageCount " &
                    "FROM dbo.FW_RoleSchema s " &
                    "WHERE ISNULL(s.DeletedFlag, 0) = 0 " &
                    "ORDER BY ISNULL(NULLIF(LTRIM(RTRIM(s.Table_Alias)), ''), s.DB_Table), s.DB_Table", conn)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function

        ''' <summary>
        ''' Enables and disables tables, all of them or none of them.
        '''
        ''' One transaction because this is a permission change: half-applied, some tables would be
        ''' granted and others not, with nothing on screen saying which half took.
        ''' </summary>
        Public Shared Function SetTableEnablement(enablement As Dictionary(Of String, Boolean)) As Integer
            If enablement Is Nothing OrElse enablement.Count = 0 Then Return 0

            ' At the write, not only on the buttons. Three places open this checklist and each hides
            ' its button from everybody else, which is three chances to forget and no protection at
            ' all for a caller written later.
            If Not SessionState.IsApplicationAdmin Then
                Throw New InvalidOperationException("ONLY AN APPLICATION ADMINISTRATOR MAY ENABLE OR DISABLE A TABLE.")
            End If

            Dim changed = 0

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using trans = conn.BeginTransaction()
                    Try
                        For Each pair In enablement
                            If String.IsNullOrWhiteSpace(pair.Key) Then Continue For

                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_RoleSchema SET IsActive = @IsActive " &
                                "WHERE DB_Table = @DBTable AND ISNULL(IsActive, 1) <> @IsActive", conn, trans)
                                cmd.Parameters.Add("@IsActive", SqlDbType.Bit).Value = If(pair.Value, 1, 0)
                                cmd.Parameters.Add("@DBTable", SqlDbType.VarChar, 128).Value = pair.Key.Trim()
                                changed += cmd.ExecuteNonQuery()
                            End Using
                        Next

                        trans.Commit()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using

            ' A table's availability has changed, and the role metadata caches hold the old answer.
            If changed > 0 Then InvalidateRoleMetadataCache()

            Return changed
        End Function

        ''' <summary>
        ''' The columns each computed column is built from, as ComputedColumn -> its sources.
        '''
        ''' Read from the expression SQL Server stores, not guessed: FirstLast is
        ''' `isnull([FirstName],'') + ... + isnull([LastName],'')`, so the bracketed names are the
        ''' answer. Only names that are really columns of the same table survive, which discards
        ''' the bracketed function and type names an expression can also contain.
        '''
        ''' What it is for: a computed column cannot be typed into, so a maintenance page offering
        ''' FirstLast alone shows a name nobody can change. Knowing its sources lets the generator
        ''' offer them beside it.
        '''
        ''' One query for the table, held for the life of the process like the other schema facts.
        ''' </summary>
        Public Shared Function GetComputedColumnSources(tableName As String) As Dictionary(Of String, List(Of String))
            Dim sources As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)

            Dim normalizedTable = NormalizeTableName(tableName)
            If normalizedTable = String.Empty Then Return sources

            SyncLock metadataCacheLock
                Dim cached As Dictionary(Of String, List(Of String)) = Nothing
                If computedSourceCache.TryGetValue(normalizedTable, cached) Then
                    For Each pair In cached
                        sources(pair.Key) = New List(Of String)(pair.Value)
                    Next
                    Return sources
                End If
            End SyncLock

            Try
                Dim columns = New HashSet(Of String)(GetTableColumnList(normalizedTable), StringComparer.OrdinalIgnoreCase)

                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT c.name, c.definition " &
                        "FROM sys.computed_columns AS c " &
                        "INNER JOIN sys.tables AS t ON t.object_id = c.object_id " &
                        "INNER JOIN sys.schemas AS s ON s.schema_id = t.schema_id " &
                        "WHERE s.name = N'dbo' AND t.name = @TableName", conn)
                        cmd.Parameters.Add("@TableName", SqlDbType.VarChar, 128).Value = normalizedTable
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                Dim computedName = SafeString(reader.GetValue(0))
                                Dim definition = SafeString(reader.GetValue(1))
                                If computedName = String.Empty OrElse definition = String.Empty Then Continue While

                                Dim named As New List(Of String)()
                                For Each match As Match In Regex.Matches(definition, "\[([^\]]+)\]")
                                    Dim candidate = match.Groups(1).Value
                                    If Not columns.Contains(candidate) Then Continue For
                                    If String.Equals(candidate, computedName, StringComparison.OrdinalIgnoreCase) Then Continue For
                                    If named.Contains(candidate, StringComparer.OrdinalIgnoreCase) Then Continue For
                                    named.Add(candidate)
                                Next

                                If named.Count > 0 Then sources(computedName) = named
                            End While
                        End Using
                    End Using
                End Using

                SyncLock metadataCacheLock
                    Dim keep As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
                    For Each pair In sources
                        keep(pair.Key) = New List(Of String)(pair.Value)
                    Next
                    computedSourceCache(normalizedTable) = keep
                End SyncLock
            Catch
                ' No sources is the behaviour that existed before this did.
            End Try

            Return sources
        End Function

        ''' <summary>
        ''' Where a dashboard's icons have been dragged to, as ActionKey -> (column, row).
        '''
        ''' Only icons that have been moved appear. Anything missing is still where its source put
        ''' it, which is what makes a newly generated icon land at the generator's cell rather than
        ''' at the origin.
        ''' </summary>
        Public Shared Function GetDashboardIconPositions(dashboardName As String) As Dictionary(Of String, Point)
            Dim positions As New Dictionary(Of String, Point)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(dashboardName) Then Return positions
            ' Missing-schema guard, phrased through the existing helper: the column exists only if the
            ' table does, so a database without 054 applied simply has no saved arrangement.
            If Not TableHasColumn("FW_DashboardLayouts", "ActionKey") Then Return positions

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ActionKey, GridRow, GridColumn FROM dbo.FW_DashboardLayouts " &
                    "WHERE DashboardName = @DashboardName AND ISNULL(DeletedFlag, 0) = 0 " &
                    "AND GridRow IS NOT NULL AND GridColumn IS NOT NULL", conn)
                    cmd.Parameters.AddWithValue("@DashboardName", dashboardName.Trim())
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            positions(Convert.ToString(reader("ActionKey"))) =
                                New Point(Convert.ToInt32(reader("GridColumn"), CultureInfo.InvariantCulture),
                                          Convert.ToInt32(reader("GridRow"), CultureInfo.InvariantCulture))
                        End While
                    End Using
                End Using
            End Using

            Return positions
        End Function

        ''' <summary>
        ''' Records where icons now sit, as ActionKey -> (column, row).
        '''
        ''' Takes a set rather than one icon because a drop onto an occupied cell moves two of them,
        ''' and half a swap is worse than no swap: both rows commit or neither does.
        ''' </summary>
        Public Shared Function SaveDashboardIconPositions(
                                                          dashboardName As String,
                                                          positions As IEnumerable(Of KeyValuePair(Of String, Point)),
                                                          userId As Integer) As Boolean
            Return WriteIconPositions(dashboardName, positions, userId)
        End Function

        ''' <summary>
        ''' The same write, for the main menu's ribbon, where the row is a one-row grid: GridRow is
        ''' 1 and GridColumn is the tile's rank along the row.
        '''
        ''' A separate entry point purely because the authorization differs. Rearranging the ribbon
        ''' is an App Admin's to do and nobody else's, while dragging a dashboard icon has always
        ''' been open to any user - so the test belongs on this door rather than on the shared write
        ''' behind it, which would silently take a working dashboard behaviour away.
        '''
        ''' Guarded here and not only where the drag is wired: a handler that is not attached is not
        ''' authorization. Returns False when refused, so a caller cannot mistake a denial for a
        ''' save that changed nothing.
        ''' </summary>
        Public Shared Function SaveRibbonTileOrder(
                                                   surfaceName As String,
                                                   ranks As IEnumerable(Of KeyValuePair(Of String, Integer)),
                                                   userId As Integer) As Boolean
            If Not SessionState.IsApplicationAdmin Then Return False
            If ranks Is Nothing Then Return False

            Return WriteIconPositions(surfaceName,
                                      ranks.Select(Function(pair) New KeyValuePair(Of String, Point)(
                                          pair.Key, New Point(pair.Value, 1))),
                                      userId)
        End Function

        Private Shared Function WriteIconPositions(
                                                   dashboardName As String,
                                                   positions As IEnumerable(Of KeyValuePair(Of String, Point)),
                                                   userId As Integer) As Boolean
            If String.IsNullOrWhiteSpace(dashboardName) OrElse positions Is Nothing Then Return False
            If Not TableHasColumn("FW_DashboardLayouts", "ActionKey") Then Return False

            Dim moved = positions.Where(Function(pair) Not String.IsNullOrWhiteSpace(pair.Key)).ToList()
            If moved.Count = 0 Then Return False

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using transaction = conn.BeginTransaction()
                    Try
                        For Each pair In moved
                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_DashboardLayouts " &
                                "SET GridRow = @GridRow, GridColumn = @GridColumn, DeletedFlag = 0, UpdatedBy = @UserID, UpdatedOn = GETDATE() " &
                                "WHERE DashboardName = @DashboardName AND ActionKey = @ActionKey; " &
                                "IF @@ROWCOUNT = 0 " &
                                "INSERT INTO dbo.FW_DashboardLayouts (DashboardName, ActionKey, GridRow, GridColumn, CreatedBy, CreatedOn) " &
                                "VALUES (@DashboardName, @ActionKey, @GridRow, @GridColumn, @UserID, GETDATE());", conn, transaction)
                                cmd.Parameters.AddWithValue("@DashboardName", dashboardName.Trim())
                                cmd.Parameters.AddWithValue("@ActionKey", pair.Key.Trim())
                                cmd.Parameters.AddWithValue("@GridRow", pair.Value.Y)
                                cmd.Parameters.AddWithValue("@GridColumn", pair.Value.X)
                                cmd.Parameters.AddWithValue("@UserID", userId)
                                cmd.ExecuteNonQuery()
                            End Using
                        Next

                        transaction.Commit()
                        Return True
                    Catch
                        transaction.Rollback()
                        Return False
                    End Try
                End Using
            End Using
        End Function

        ''' <summary>
        ''' The icon pictures an App Admin has chosen, as ActionKey -> stored icon choice.
        '''
        ''' Only icons that have been changed appear. Anything missing keeps the picture its
        ''' dashboard was written with, which is what lets a newly added icon show the one its
        ''' source names rather than nothing at all.
        ''' </summary>
        Public Shared Function GetDashboardIconOverrides(dashboardName As String) As Dictionary(Of String, String)
            Dim chosenIcons As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If String.IsNullOrWhiteSpace(dashboardName) Then Return chosenIcons
            ' Missing-schema guard: without 057 there is no column, and so no chosen picture.
            If Not TableHasColumn("FW_DashboardLayouts", "IconFileName") Then Return chosenIcons

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ActionKey, IconFileName FROM dbo.FW_DashboardLayouts " &
                    "WHERE DashboardName = @DashboardName AND ISNULL(DeletedFlag, 0) = 0 " &
                    "AND IconFileName IS NOT NULL AND LTRIM(RTRIM(IconFileName)) <> ''", conn)
                    cmd.Parameters.AddWithValue("@DashboardName", dashboardName.Trim())
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            chosenIcons(Convert.ToString(reader("ActionKey"))) = Convert.ToString(reader("IconFileName"))
                        End While
                    End Using
                End Using
            End Using

            Return chosenIcons
        End Function

        ''' <summary>
        ''' Records the picture chosen for one icon, or clears it when iconFileName is empty.
        '''
        ''' The App Admin test is made here, not only where the menu is offered. A hidden menu item
        ''' is not authorization - it is the absence of an invitation - and this is the boundary the
        ''' write actually crosses. Returns False when refused, so a caller cannot mistake a denial
        ''' for a save that simply changed nothing.
        '''
        ''' Upserts against UX_FW_DashboardLayouts_Icon, so an icon that has never been dragged gets
        ''' a row on its first re-picturing. That row's position is NULL rather than 0, 0: a corner
        ''' cell is a real answer, and writing one would move the icon there on the next launch, so
        ''' choosing a picture would quietly rearrange the dashboard. An existing row keeps the
        ''' position it was dropped at.
        ''' </summary>
        Public Shared Function SaveDashboardIconOverride(dashboardName As String,
                                                         actionKey As String,
                                                         iconFileName As String,
                                                         userId As Integer) As Boolean
            If Not SessionState.IsApplicationAdmin Then Return False
            If String.IsNullOrWhiteSpace(dashboardName) OrElse String.IsNullOrWhiteSpace(actionKey) Then Return False
            If Not TableHasColumn("FW_DashboardLayouts", "IconFileName") Then Return False

            Dim storedValue As Object = If(String.IsNullOrWhiteSpace(iconFileName), DBNull.Value, CObj(iconFileName.Trim()))

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_DashboardLayouts " &
                    "SET IconFileName = @IconFileName, DeletedFlag = 0, UpdatedBy = @UserID, UpdatedOn = GETDATE() " &
                    "WHERE DashboardName = @DashboardName AND ActionKey = @ActionKey; " &
                    "IF @@ROWCOUNT = 0 " &
                    "INSERT INTO dbo.FW_DashboardLayouts (DashboardName, ActionKey, GridRow, GridColumn, IconFileName, CreatedBy, CreatedOn) " &
                    "VALUES (@DashboardName, @ActionKey, NULL, NULL, @IconFileName, @UserID, GETDATE());", conn)
                    cmd.Parameters.AddWithValue("@DashboardName", dashboardName.Trim())
                    cmd.Parameters.AddWithValue("@ActionKey", actionKey.Trim())
                    cmd.Parameters.AddWithValue("@IconFileName", storedValue)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using

            Return True
        End Function

        ''' <summary>
        ''' Every column of one record, for the Hot Fields panel.
        ''' </summary>
        ''' <remarks>
        ''' Called only while the panel is open. Closed - which is most pages most of the time - the
        ''' feature costs nothing at all; open, it costs this one query per row the user clicks,
        ''' which is what they opened the panel to see. See BASE_BHF_SPEC.md section 6 for why this
        ''' is preferred over widening every browse page's SELECT to carry columns it never shows.
        '''
        ''' The table and key column are bracket quoted rather than parameterised, because neither
        ''' can be a parameter in T-SQL. Both come from the schema - the page's own table name and
        ''' GetPrimaryKeyFieldName - rather than from anything a user typed, and the closing bracket
        ''' is doubled so a name containing one cannot end the quoting early.
        ''' </remarks>
        Public Shared Function GetRecordFields(tableName As String, keyValue As Object) As DataTable
            Dim record As New DataTable("HotFields")

            If String.IsNullOrWhiteSpace(tableName) OrElse keyValue Is Nothing OrElse keyValue Is DBNull.Value Then
                Return record
            End If

            Dim keyColumn = GetPrimaryKeyFieldName(tableName)
            If String.IsNullOrWhiteSpace(keyColumn) Then
                Return record
            End If

            Dim quotedTable = "[" & tableName.Trim().Replace("]", "]]") & "]"
            Dim quotedKey = "[" & keyColumn.Trim().Replace("]", "]]") & "]"

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 * FROM dbo." & quotedTable & " WHERE " & quotedKey & " = @RecordKey", conn)

                        cmd.Parameters.AddWithValue("@RecordKey", keyValue)
                        Using adapter As New SqlDataAdapter(cmd)
                            adapter.Fill(record)
                        End Using
                    End Using
                End Using
            Catch
                ' A panel that cannot read a record shows nothing. It must never be the reason the
                ' page behind it stops working.
                record.Clear()
            End Try

            Return record
        End Function

        Public Shared Function GetPrimaryKeyFieldName(tableName As String) As String
            If String.IsNullOrWhiteSpace(tableName) Then Return String.Empty

            Dim cached As String = Nothing
            SyncLock metadataCacheLock
                If primaryKeyCache.TryGetValue(tableName.Trim(), cached) Then
                    Return cached
                End If
            End SyncLock

            Dim keyName As String
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
                    keyName = If(result Is Nothing OrElse IsDBNull(result), String.Empty, result.ToString())
                End Using
            End Using

            ' This one throws rather than returning a default, so reaching here is already proof the
            ' answer came from the server. An empty string is a real answer - a table with no
            ' declared primary key - and is worth caching so it is not asked for again.
            SyncLock metadataCacheLock
                primaryKeyCache(tableName.Trim()) = keyName
            End SyncLock

            Return keyName
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
        ''' <summary>
        ''' The rows a lookup combo offers.
        '''
        ''' Filtered by what the table actually has, because a lookup table is any table: FW_Gender
        ''' is per registration, a table of country codes would not be. Asking TableHasColumn rather
        ''' than assuming is what lets both work.
        '''
        '''   - RegistrationID, when present: the session's registration, plus rows with none. A
        '''     NULL RegistrationID means the row is shared by every registration, the same
        '''     convention FW_Pages uses. Without this filter FW_Gender offered Male and Female
        '''     twice - once for each registration - which is what prompted this.
        '''   - DeletedFlag, when present: soft-deleted rows are not offered.
        '''
        ''' IsActive is deliberately not filtered. An existing record can point at a row that has
        ''' since been deactivated, and hiding it would leave that record's combo blank - losing the
        ''' value on the next save rather than merely styling it. Keeping inactive rows selectable
        ''' is the lesser fault, and one the page can override if it needs to.
        ''' </summary>
        ''' <summary>
        ''' How many registrations a lookup table actually has rows for: -1 when it has no
        ''' RegistrationID column at all, otherwise the count of distinct values in it.
        '''
        ''' The column existing is not the same as the column being used. A table can carry a
        ''' RegistrationID that every row leaves null, and scoping a list by a column nobody
        ''' populates would empty it. This lets the choice be offered already answered, from what
        ''' the data says rather than what the schema allows.
        ''' </summary>
        Public Shared Function CountLookupRegistrations(tableName As String) As Integer
            Dim normalizedTable = NormalizeTableName(tableName)
            If normalizedTable = String.Empty Then Return -1
            If Not TableHasColumn(normalizedTable, "RegistrationID") Then Return -1

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT COUNT(DISTINCT [RegistrationID]) FROM dbo." & QuoteGeneratedIdentifier(normalizedTable) &
                        " WHERE [RegistrationID] IS NOT NULL", conn)
                        Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                    End Using
                End Using
            Catch
                Return 0
            End Try
        End Function

        ''' <param name="keepValue">
        ''' The row the record being edited already points at, which is kept in the list even when
        ''' it is no longer active.
        '''
        ''' Without it, filtering on IsActive quietly loses data: a record whose manager has since
        ''' left would find no matching row, ConfigureLookupCombo would fall back to "Make a
        ''' Selection", and the next save would write a null over a manager nobody meant to clear.
        ''' So the rule is what you would say out loud - you cannot choose someone inactive, but
        ''' you can still see who was chosen. Pass 0 for a new record, which has chosen nobody.
        ''' </param>
        ''' <summary>
        ''' The rows of FW_Format_Date or FW_Format_Time, for the combos that choose one.
        '''
        ''' Not GetLookupTable, for two reasons. These come back in DisplayOrder, which is how
        ''' usual each format is - sorted alphabetically by description the ISO one lands in the
        ''' middle of the list for no reason a reader could see. And both the pattern and its
        ''' description are needed, because the label is built from them rather than stored: what
        ''' somebody picks from is then what they will actually see, and no stored sample can
        ''' drift from the pattern beside it.
        ''' </summary>
        ''' <summary>A registration's time zone, or 0 when it has none.</summary>
        Public Shared Function GetRegistrationTimeZoneId(registrationId As Integer) As Integer
            If registrationId <= 0 Then Return 0

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(TimeZoneID, 0) FROM dbo.FW_Registration WHERE RegistrationID = @ID", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = registrationId
                        Dim result = cmd.ExecuteScalar()
                        If result Is Nothing OrElse Convert.IsDBNull(result) Then Return 0
                        Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                    End Using
                End Using
            Catch
                Return 0
            End Try
        End Function



        ''' <summary>
        ''' One login's page zooms, in one round trip.
        '''
        ''' Keyed on the user rather than the employee: not everybody who signs in is an employee,
        ''' and a contractor with a login and no employee row would otherwise have nowhere to store
        ''' anything.
        ''' </summary>
        Public Shared Function GetPageZooms(userId As Integer) As Dictionary(Of String, Single)
            Dim zooms As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
            If userId <= 0 Then Return zooms

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT PageName, ZoomFactor FROM dbo.FW_PageZooms " &
                    "WHERE UserID = @UserID AND ISNULL(DeletedFlag, 0) = 0", conn)
                    cmd.Parameters.AddWithValue("@UserID", userId)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            zooms(SafeString(reader("PageName"))) =
                                Convert.ToSingle(reader("ZoomFactor"), CultureInfo.InvariantCulture)
                        End While
                    End Using
                End Using
            End Using

            Return zooms
        End Function

        ''' <summary>
        ''' Writes one page zoom. The unique index on employee and page is what lets this upsert
        ''' without reading first.
        ''' </summary>
        Public Shared Sub SavePageZoom(userId As Integer, pageName As String, factor As Single,
                                       registrationId As Integer)
            If userId <= 0 OrElse String.IsNullOrWhiteSpace(pageName) Then Return

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_PageZooms " &
                    "   SET ZoomFactor = @Factor, DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, " &
                    "       UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                    " WHERE UserID = @UserID AND PageName = @PageName; " &
                    "IF @@ROWCOUNT = 0 " &
                    "  INSERT INTO dbo.FW_PageZooms (RegistrationID, UserID, PageName, ZoomFactor, CreatedBy, CreatedOn) " &
                    "  VALUES (@RegistrationID, @UserID, @PageName, @Factor, @UpdatedBy, GETDATE());", conn)

                    cmd.Parameters.AddWithValue("@Factor", CDec(Math.Round(factor, 2)))
                    ' Two different answers from one id. UserID is whose zoom this is and stays
                    ' the person whose screen it belongs to; UpdatedBy is who changed it, which
                    ' while an administrator is viewing as somebody else is the administrator.
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@PageName", pageName.Trim())
                    cmd.Parameters.AddWithValue("@UpdatedBy", SessionState.ActingUserID)
                    cmd.Parameters.AddWithValue("@RegistrationID",
                                                If(registrationId > 0, CType(registrationId, Object), DBNull.Value))
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub
        ''' <summary>
        ''' The licence terms, with the offset that defines them.
        '''
        ''' Not GetLookupTable: that returns the value and the caption and nothing else, and a term
        ''' without its OffsetDays cannot set a date or be checked against one. Ordered by
        ''' DisplayOrder so the list climbs, with Custom last.
        ''' </summary>
        Public Shared Function GetLicenseTerms() As DataTable
            Dim table As New DataTable("FW_LicenseTerms")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT LicenseTermID, TermName, OffsetDays FROM dbo.FW_LicenseTerms " &
                    "WHERE ISNULL(DeletedFlag, 0) = 0 AND ISNULL(IsActive, 1) = 1 " &
                    "ORDER BY DisplayOrder, LicenseTermID", conn)
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(table)
                    End Using
                End Using
            End Using

            Return table
        End Function
        ''' <summary>Every time zone's IANA id, keyed by TimeZoneID. One read, for the whole list.</summary>
        Public Shared Function GetTimeZoneIanaIds() As Dictionary(Of Integer, String)
            Dim result As New Dictionary(Of Integer, String)()
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT TimeZoneID, ISNULL(TimeZoneName, '') FROM dbo.FW_TimeZones", conn)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            result(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)) = reader.GetString(1)
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Shared Function GetFormatOptions(tableName As String, keyColumn As String) As DataTable
            Dim table As New DataTable(tableName)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT " & QuoteGeneratedIdentifier(keyColumn) & " AS FormatID, FormatPattern, Description " &
                    "FROM dbo." & QuoteGeneratedIdentifier(tableName) & " " &
                    "WHERE ISNULL(IsActive, 1) = 1 " &
                    "ORDER BY DisplayOrder, Description", conn)
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using
            Return table
        End Function

        Public Shared Function GetLookupTable(tableName As String,
                                              valueColumn As String,
                                              displayColumn As String,
                                              Optional filterByRegistration As Boolean = True,
                                              Optional keepValue As Integer = 0,
                                              Optional allTimeZones As Boolean = False) As DataTable
            Dim result As New DataTable()
            Dim normalizedTable = NormalizeTableName(tableName)

            Dim filters As New List(Of String)()
            Dim scopeToRegistration = filterByRegistration AndAlso TableHasColumn(normalizedTable, "RegistrationID")
            If scopeToRegistration Then
                filters.Add("([RegistrationID] = @RegistrationID OR [RegistrationID] IS NULL)")
            End If
            If TableHasColumn(normalizedTable, "DeletedFlag") Then
                filters.Add("ISNULL([DeletedFlag], 0) = 0")
            End If

            ' Rows 1 to 9 are the US zones anyone picks from; the rest of FW_TimeZones is the full
            ' IANA list, several hundred rows deep. Here rather than at each call site, so a
            ' generated page gets the same list without the generator knowing anything about it.
            ' The whole list, US zones first. Sorted here rather than by the caller, so the one
            ' place that knows this table is odd is the one place that orders it.
            If allTimeZones AndAlso String.Equals(normalizedTable, "FW_TimeZones", StringComparison.OrdinalIgnoreCase) Then
                Dim everyZone As New DataTable()
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TimeZoneID, DisplayName FROM dbo.FW_TimeZones " &
                        "ORDER BY CASE WHEN TimeZoneID BETWEEN 1 AND 9 THEN 0 ELSE 1 END, DisplayName", conn)
                        Using da As New SqlDataAdapter(cmd)
                            da.Fill(everyZone)
                        End Using
                    End Using
                End Using
                Return everyZone
            End If

            If String.Equals(normalizedTable, "FW_TimeZones", StringComparison.OrdinalIgnoreCase) Then
                filters.Add("[TimeZoneID] BETWEEN 1 AND 9")
            End If

            ' Deleted rows are gone from the list outright; inactive ones survive only as the
            ' answer already given. A deleted row is a mistake or a removal, and nothing should
            ' still be pointing at it - an inactive one is a real past choice.
            Dim honourIsActive = TableHasColumn(normalizedTable, "IsActive")
            If honourIsActive Then
                filters.Add("(ISNULL([IsActive], 1) = 1 OR " & QuoteGeneratedIdentifier(valueColumn) & " = @KeepValue)")
            End If

            Dim whereClause = If(filters.Count = 0, String.Empty, " WHERE " & String.Join(" AND ", filters))

            ' An inactive row that survived says so, or it reads as an ordinary choice that other
            ' people are mysteriously unable to make.
            Dim displaySelect = QuoteGeneratedIdentifier(displayColumn) & " AS " & QuoteGeneratedIdentifier(displayColumn)
            If honourIsActive Then
                displaySelect = "CASE WHEN ISNULL([IsActive], 1) = 1 THEN CAST(" & QuoteGeneratedIdentifier(displayColumn) & " AS nvarchar(4000)) " &
                                "ELSE CAST(" & QuoteGeneratedIdentifier(displayColumn) & " AS nvarchar(4000)) + N' (inactive)' END AS " &
                                QuoteGeneratedIdentifier(displayColumn)
            End If

            ' A lookup that carries DisplayOrder is saying the rows have an intended sequence, and
            ' alphabetical is not it: the licence terms read 1 Year, 10 Days, 2 Years, 30 Days
            ' sorted by name. The display column still breaks ties, so a table that has the column
            ' but never set it is ordered exactly as before.
            Dim orderClause = " ORDER BY " & QuoteGeneratedIdentifier(displayColumn)
            If TableHasColumn(normalizedTable, "DisplayOrder") Then
                orderClause = " ORDER BY [DisplayOrder], " & QuoteGeneratedIdentifier(displayColumn)
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand("SELECT " & QuoteGeneratedIdentifier(valueColumn) & " AS " & QuoteGeneratedIdentifier(valueColumn) &
                                            ", " & displaySelect &
                                            " FROM dbo." & QuoteGeneratedIdentifier(normalizedTable) &
                                            whereClause &
                                            orderClause, conn)
                    If honourIsActive Then
                        cmd.Parameters.Add("@KeepValue", SqlDbType.Int).Value = keepValue
                    End If
                    If scopeToRegistration Then
                        ' The registration being worked in, not the one signed in under. It comes
                        ' from the browse page's registration combo, which every _B page has, and
                        ' falls back to the session where nothing has set it.
                        '
                        ' It was the session's until 2026-09-22, which meant an App Admin working
                        ' in another company saw that company's rows in the grid and their own
                        ' company's names in every drop-down. The role selector on the employee
                        ' page had already been fixed this way; every other lookup had not.
                        cmd.Parameters.AddWithValue("@RegistrationID", SessionState.WorkingRegistrationID())
                    End If

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
                    Using tx = conn.BeginTransaction()
                        Using cmd As New SqlCommand(
                            "UPDATE dbo." & QuoteGeneratedIdentifier(normalizedTable) &
                            " SET " & String.Join(", ", assignments) &
                            " WHERE " & QuoteGeneratedIdentifier(primaryKey) & " = @RecordID", conn, tx)
                            cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                            cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = If(userId > 0, CType(userId, Object), DBNull.Value)

                            If cmd.ExecuteNonQuery() <> 1 Then
                                tx.Rollback()
                                Return "The record was not found. It may already have been deleted."
                            End If
                        End Using

                        ' Deleting an employee takes their sign-in with it. Both rows or neither:
                        ' a person removed from the company who can still log in is the failure
                        ' worth preventing, and doing it here rather than on the page means no
                        ' page can forget.
                        '
                        ' This is the simple reading, chosen on 2026-09-14 over deriving access
                        ' from the employee record. It is the wrong answer the day a second
                        ' application shares these logins - one application's delete would lock
                        ' somebody out of another - and it is meant to be revisited then.
                        If IsEmployeeLoginTable(normalizedTable) Then
                            SetEmployeeLoginDeleted(conn, tx, recordId, userId, True)
                        End If

                        tx.Commit()
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

        ''' <summary>
        ''' Clears the soft delete so the record returns to the normal view. The other half of
        ''' SoftDeleteGeneratedPageRecord, undoing exactly what that one sets.
        '''
        ''' Uniqueness needs no check here. A deleted row keeps its address, and
        ''' GetEmailUnavailableMessage refuses to hand that address to anybody else while the row
        ''' exists - so the duplicate a restore could otherwise create is refused at the point it
        ''' would have been created, not at this one.
        '''
        ''' A table with some other unique constraint could still refuse the UPDATE. That surfaces
        ''' as the SQL Server message rather than being anticipated here, because the framework
        ''' cannot know which constraint a generated page's table carries.
        ''' </summary>
        Public Shared Function RestoreGeneratedPageRecord(tableName As String,
                                                          primaryKey As String,
                                                          recordId As Integer,
                                                          userId As Integer,
                                                          pageName As String) As String
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) OrElse String.IsNullOrWhiteSpace(primaryKey) OrElse recordId <= 0 Then
                Return "The record could not be identified."
            End If

            If Not TableHasColumn(normalizedTable, "DeletedFlag") Then
                Return "This table does not support restore. It has no DeletedFlag column, so nothing was ever soft-deleted."
            End If

            Try
                Dim assignments As New List(Of String)() From {"[DeletedFlag] = 0"}
                If TableHasColumn(normalizedTable, "DeletedBy") Then assignments.Add("[DeletedBy] = NULL")
                If TableHasColumn(normalizedTable, "DeletedOn") Then assignments.Add("[DeletedOn] = NULL")
                If TableHasColumn(normalizedTable, "IsActive") Then assignments.Add("[IsActive] = 1")
                If TableHasColumn(normalizedTable, "UpdatedBy") Then assignments.Add("[UpdatedBy] = @UserID")
                If TableHasColumn(normalizedTable, "UpdatedOn") Then assignments.Add("[UpdatedOn] = GETDATE()")

                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using tx = conn.BeginTransaction()
                        Using cmd As New SqlCommand(
                            "UPDATE dbo." & QuoteGeneratedIdentifier(normalizedTable) &
                            " SET " & String.Join(", ", assignments) &
                            " WHERE " & QuoteGeneratedIdentifier(primaryKey) & " = @RecordID", conn, tx)
                            cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                            cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = If(userId > 0, CType(userId, Object), DBNull.Value)

                            If cmd.ExecuteNonQuery() <> 1 Then
                                tx.Rollback()
                                Return "The record was not found. It may already have been restored."
                            End If
                        End Using

                        ' Exactly what the delete undid. A restored employee who still could not
                        ' sign in would look restored and not be.
                        If IsEmployeeLoginTable(normalizedTable) Then
                            SetEmployeeLoginDeleted(conn, tx, recordId, userId, False)
                        End If

                        tx.Commit()
                    End Using
                End Using

                LogUpdateAudit(If(String.IsNullOrWhiteSpace(pageName), "FW_Base_B", pageName),
                               normalizedTable,
                               "Restore",
                               "AfterSave",
                               recordId.ToString(CultureInfo.InvariantCulture),
                               String.Empty,
                               True)

                Return String.Empty
            Catch ex As Exception
                Return ex.Message
            End Try
        End Function

        ''' <summary>
        ''' The table's columns, for a generated page, with the database's own defaults on them.
        '''
        ''' A new record is made from this with NewRow, and a DataTable filled from a query knows
        ''' the columns but not their defaults - so every defaulted column started null. A check box
        ''' shows null as unticked and saves it as False, which is how FW_Employees.IsActive,
        ''' defaulting to 1 in the database, came out 0 for an employee created through the page:
        ''' User 1, on 2026-09-15. Now that an inactive employee also switches off their login,
        ''' the same slip would leave a new employee unable to sign in.
        '''
        ''' Constant defaults only - a number, a bit, a quoted string. A function such as getdate()
        ''' is left to the database, where it belongs: evaluated when the page opens it would stamp
        ''' the record with the wrong moment.
        '''
        ''' One round trip still: the columns and their defaults come back as two result sets.
        ''' </summary>
        Public Shared Function GetGeneratedPageSchema(tableName As String) As DataTable
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT TOP 0 * FROM dbo." & QuoteGeneratedIdentifier(tableName) & "; " &
                    "SELECT c.name, d.definition FROM sys.columns c " &
                    "JOIN sys.default_constraints d ON d.object_id = c.default_object_id " &
                    "WHERE c.object_id = OBJECT_ID(@QualifiedTable);", conn)
                    cmd.Parameters.AddWithValue("@QualifiedTable", "dbo." & tableName)

                    ' Fill, not DataTable.Load. Load applies the column metadata too - an identity
                    ' column comes back read-only and auto-numbering - and this schema has only ever
                    ' carried names and types. The one thing that should change is the defaults.
                    Dim results As New DataSet()
                    Using adapter As New SqlDataAdapter(cmd)
                        adapter.Fill(results)
                    End Using

                    Dim table = results.Tables(0)
                    If results.Tables.Count > 1 Then
                        For Each defaultRow As DataRow In results.Tables(1).Rows
                            ApplyColumnDefault(table, Convert.ToString(defaultRow(0)), Convert.ToString(defaultRow(1)))
                        Next
                    End If

                    ' Handed back on its own, as it always was, rather than still inside a DataSet a
                    ' caller might try to add it to another of.
                    results.Tables.Remove(table)
                    table.TableName = tableName
                    Return table
                End Using
            End Using
        End Function

        ''' <summary>
        ''' Puts a constant database default on a DataTable column, and ignores anything else.
        '''
        ''' SQL Server stores defaults wrapped in parentheses - ((1)), ('abc'), (N'abc') - so they
        ''' are unwrapped first. What is left is used only if it is a literal the column's type can
        ''' hold; a function call, or a value that will not convert, leaves the column as it was.
        ''' </summary>
        Private Shared Sub ApplyColumnDefault(table As DataTable, columnName As String, definition As String)
            If table Is Nothing OrElse String.IsNullOrWhiteSpace(columnName) OrElse Not table.Columns.Contains(columnName) Then Return

            Dim text = If(definition, String.Empty).Trim()
            While text.Length >= 2 AndAlso text.StartsWith("(") AndAlso text.EndsWith(")")
                text = text.Substring(1, text.Length - 2).Trim()
            End While

            If text.StartsWith("N'", StringComparison.Ordinal) Then text = text.Substring(1)

            Dim column = table.Columns(columnName)
            Try
                If text.StartsWith("'") AndAlso text.EndsWith("'") AndAlso text.Length >= 2 Then
                    If column.DataType IsNot GetType(String) Then Return
                    column.DefaultValue = text.Substring(1, text.Length - 2).Replace("''", "'")
                    Return
                End If

                ' Anything with a bracket or a letter left in it is an expression, not a value.
                If text.Contains("(") OrElse text.Any(Function(ch) Char.IsLetter(ch)) Then Return

                If column.DataType Is GetType(Boolean) Then
                    column.DefaultValue = text <> "0"
                Else
                    column.DefaultValue = Convert.ChangeType(text, column.DataType, CultureInfo.InvariantCulture)
                End If
            Catch
                ' Not a value this column can hold. The database will still apply it on insert.
            End Try
        End Sub

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
            ' The one write that must never be refused quietly: a page that thinks it saved
            ' would close, and the edit would be gone with no record of it anywhere.
            ReadOnlyPreview.Refuse("The record")

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

            ' A password typed on any page is taken out of the ordinary column write and put
            ' through WritePasswordHash instead, which is the only code that knows the contract:
            ' hash it keyed on the UserId, store the hash, and leave the sentinel in the column
            ' the value was typed into. The raw string is never written anywhere.
            '
            ' Held here rather than in each page because that is where it failed. The rule lived in
            ' Users_AppAdmin_U alone, so a generated page editing FW_Users wrote "1234" into
            ' [Password] as though it were any other text column and left PasswordHash null - a
            ' plaintext password stored, and an account that could not log in. A rule enforced in one
            ' form is not enforced.
            Dim pendingPassword As String = Nothing
            Dim pendingRoleIds As List(Of Integer) = Nothing

            ' An employee's typed password goes the same way a user's does: out of the value set
            ' before anything is written, and the column it came from never holds it. The chosen
            ' role leaves with it, for a different reason - it is not a column at all.
            If IsEmployeeLoginTable(tableName) Then
                pendingPassword = TakeGeneratedPasswordValue(values)
                pendingRoleIds = TakeGeneratedRoleValues(values)
            End If

            If IsUserPasswordTable(tableName) Then
                pendingPassword = TakeGeneratedPasswordValue(values)

                ' Still normalised, so the same address is stored the same way every time. What
                ' has gone is the uniqueness check that used to follow it: an email was how people
                ' signed in, so two accounts sharing one left login picking by row order. Login
                ' moved to UserName on 2026-09-14, and an email is now just a way to reach
                ' somebody - two people can share one, and across registrations they routinely do.
                NormalizeGeneratedEmailValue(values)
            End If

            ' Computed columns are the database's to fill, and SQL Server refuses outright any write
            ' that names one. Dropped here, alongside the key and RowVersion, for the same reason the
            ' password is hashed here rather than on the page: a rule that lives in one form is not a
            ' rule. A page offering FW_Users.FirstLast as an ordinary text box could not save at all,
            ' and every page the generator produced would have carried the same fault.
            '
            ' Dropping the value is correct rather than merely safe - the database recomputes the
            ' column from the fields that were written, so nothing the user meant is lost.
            Dim computedColumns = GetComputedColumnNames(tableName)
            Dim writableValues = values.Where(Function(pair) schema.Columns.Contains(pair.Key) AndAlso
                                                       Not String.Equals(pair.Key, primaryKey, StringComparison.OrdinalIgnoreCase) AndAlso
                                                       Not String.Equals(pair.Key, "RowVersion", StringComparison.OrdinalIgnoreCase) AndAlso
                                                       Not computedColumns.Contains(pair.Key) AndAlso
                                                       Not IsProtectedPasswordColumn(tableName, pair.Key)).ToList()
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                ' One transaction over the whole save, which it did not used to be. The row was
                ' committed and the password hashed afterwards on a second connection, so a
                ' failure between them left an account that existed and could not be signed into -
                ' the code said as much, logging Password_HashFailedAfterSave. An employee makes
                ' that worse still: it writes two tables, and half of that is a person with no
                ' login or a login with no person.
                Using tx = conn.BeginTransaction()
                  Try
                    If recordId <= 0 Then
                    If schema.Columns.Contains("RegistrationID") AndAlso
                       Not String.Equals(tableName.Trim(), "FW_Registration", StringComparison.OrdinalIgnoreCase) AndAlso
                       SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso SessionState.Current.Value.RegistrationID > 0 Then
                        Dim registrationValue = writableValues.FirstOrDefault(Function(pair) String.Equals(pair.Key, "RegistrationID", StringComparison.OrdinalIgnoreCase)).Value
                        Dim explicitRegistrationId As Integer
                        If registrationValue Is Nothing OrElse Not Integer.TryParse(Convert.ToString(registrationValue, CultureInfo.InvariantCulture), explicitRegistrationId) OrElse explicitRegistrationId <= 0 Then
                            writableValues.RemoveAll(Function(pair) String.Equals(pair.Key, "RegistrationID", StringComparison.OrdinalIgnoreCase))
                            ' The registration being looked at, not the one the user signs in
                            ' under - an App Admin adding a record while a browse page shows
                            ' Saraland means it to be Saraland's.
                            writableValues.Add(New KeyValuePair(Of String, Object)("RegistrationID", SessionState.WorkingRegistrationID()))
                        End If
                    End If
                    ' The login first. FW_Employees.UserId is NOT NULL, so the employee row
                    ' cannot be written until the account it points at exists - and doing it in
                    ' this order means there is no window where either half stands alone, and no
                    ' second update to write the key back.
                    If IsEmployeeLoginTable(tableName) Then
                        Dim employeeUserName = GetGeneratedValueText(values, "UserName")
                        Dim employeeRegistration = 0
                        Dim registrationForLogin = writableValues.FirstOrDefault(Function(pair) String.Equals(pair.Key, "RegistrationId", StringComparison.OrdinalIgnoreCase)).Value
                        If registrationForLogin Is Nothing OrElse Not Integer.TryParse(Convert.ToString(registrationForLogin, CultureInfo.InvariantCulture), employeeRegistration) Then
                            employeeRegistration = If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.RegistrationID, 0)
                        End If

                        ' The name is carried onto the login as well as the employee. Nothing reads
                        ' it there except login itself, but a FW_Users row with no name is
                        ' unreadable to anyone looking at the table.
                        Dim createdUserId = CreateLoginForEmployee(conn, tx,
                                                                   employeeUserName,
                                                                   GetGeneratedValueText(values, "FirstName"),
                                                                   GetGeneratedValueText(values, "LastName"),
                                                                   employeeRegistration,
                                                                   userId)

                        ' The hash is keyed on the UserId that has just been issued. It could not
                        ' have been computed any earlier.
                        If Not String.IsNullOrWhiteSpace(pendingPassword) Then
                            If Not WritePasswordHash(conn, tx, createdUserId, pendingPassword, userId) Then
                                Throw New InvalidOperationException("The password could not be stored for the new sign-in.")
                            End If
                        End If

                        writableValues.RemoveAll(Function(pair) String.Equals(pair.Key, "UserId", StringComparison.OrdinalIgnoreCase))
                        writableValues.Add(New KeyValuePair(Of String, Object)("UserId", createdUserId))

                        ' The mask, never the typed value. IsProtectedPasswordColumn kept the real
                        ' one out of the value set; this puts the placeholder in deliberately.
                        If schema.Columns.Contains("Password") Then
                            writableValues.RemoveAll(Function(pair) String.Equals(pair.Key, "Password", StringComparison.OrdinalIgnoreCase))
                            writableValues.Add(New KeyValuePair(Of String, Object)("Password", StoredPasswordMask))
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
                    Using cmd As New SqlCommand("INSERT INTO dbo." & QuoteGeneratedIdentifier(tableName) & " (" & String.Join(", ", columns) & ") VALUES (" & String.Join(", ", parameters) & ")", conn, tx)
                        AddGeneratedParameters(cmd, writableValues, schema)
                        If schema.Columns.Contains("CreatedBy") Then cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = userId
                        cmd.CommandText &= "; SELECT CAST(SCOPE_IDENTITY() AS INT);"
                        Dim insertedId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)

                        ' The registration's default role, if it has named one. After the insert
                        ' because roles are keyed to the employee, and the employee did not exist
                        ' until this statement - which is also why this cannot live with the
                        ' login creation above.
                        If IsEmployeeLoginTable(tableName) Then
                            SetEmployeeRoles(conn, tx, insertedId, pendingRoleIds, userId)

                            ' The login was created active; an employee saved inactive switches it off.
                            SyncLoginActive(conn, tx, EmployeeLoginId(conn, tx, insertedId), values, userId)
                        End If

                        ' After the insert, never before: the hash is keyed on the UserId, and it
                        ' cannot be computed until the row exists and identity has issued one.
                        ' FW_Users only - an employee's account was hashed above, before its row.
                        If IsUserPasswordTable(tableName) AndAlso Not String.IsNullOrWhiteSpace(pendingPassword) Then
                            If Not WritePasswordHash(conn, tx, insertedId, pendingPassword, userId) Then
                                Throw New InvalidOperationException("The password could not be stored for the new sign-in.")
                            End If
                        End If

                        tx.Commit()
                        Return insertedId
                    End Using
                End If

                    ' An edit reaches the login too: a new password is re-hashed against the
                    ' UserId the employee already points at, and a changed user name is written
                    ' to both rows or neither. An employee whose name moved on one row only is an
                    ' employee who cannot sign in.
                    If IsEmployeeLoginTable(tableName) Then
                        Dim existingUserId = EmployeeLoginId(conn, tx, recordId)
                        If existingUserId > 0 Then
                            SyncLoginUserName(conn, tx, existingUserId, GetGeneratedValueText(values, "UserName"), userId)
                            SyncLoginActive(conn, tx, existingUserId, values, userId)

                            ' TakeGeneratedPasswordValue returns nothing for the mask, so an
                            ' untouched box re-hashes nothing. Only a genuinely new password gets
                            ' this far.
                            If Not String.IsNullOrWhiteSpace(pendingPassword) Then
                                If Not WritePasswordHash(conn, tx, existingUserId, pendingPassword, userId) Then
                                    Throw New InvalidOperationException("The password could not be updated for this sign-in.")
                                End If
                            End If
                        End If

                        If schema.Columns.Contains("Password") Then
                            writableValues.RemoveAll(Function(pair) String.Equals(pair.Key, "Password", StringComparison.OrdinalIgnoreCase))
                            writableValues.Add(New KeyValuePair(Of String, Object)("Password", StoredPasswordMask))
                        End If
                    End If

                Dim assignments = writableValues.Select(Function(pair, index) QuoteGeneratedIdentifier(pair.Key) & " = @Value" & index.ToString(CultureInfo.InvariantCulture)).ToList()
                If schema.Columns.Contains("UpdatedBy") Then assignments.Add("[UpdatedBy] = @UpdatedBy")
                If schema.Columns.Contains("UpdatedOn") Then assignments.Add("[UpdatedOn] = GETDATE()")
                Dim whereClause = QuoteGeneratedIdentifier(primaryKey) & " = @RecordID"
                If schema.Columns.Contains("RowVersion") Then whereClause &= " AND [RowVersion] = @RowVersion"
                Using cmd As New SqlCommand("UPDATE dbo." & QuoteGeneratedIdentifier(tableName) & " SET " & String.Join(", ", assignments) & " WHERE " & whereClause, conn, tx)
                    AddGeneratedParameters(cmd, writableValues, schema)
                    cmd.Parameters.Add("@RecordID", SqlDbType.Int).Value = recordId
                    If schema.Columns.Contains("UpdatedBy") Then cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = userId
                    If schema.Columns.Contains("RowVersion") Then cmd.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = If(originalRowVersion, New Byte() {})
                    If cmd.ExecuteNonQuery() <> 1 Then
                        ' No row matched: either the RowVersion moved on or the record is gone.
                        ' Distinguishing the two is what lets the page offer an overwrite for one
                        ' and refuse it for the other. Rolled back first, so a login edit made
                        ' above does not survive a row that was never written.
                        tx.Rollback()
                        outcome = If(GeneratedPageRecordExists(conn, tableName, primaryKey, recordId),
                                     SaveResult.RecordChanged,
                                     SaveResult.RecordDeleted)
                        Return 0
                    End If

                    ' After the row is known to have been written, and inside the same
                    ' transaction. Editing an employee reconciles their roles to whatever the
                    ' picker holds - added, removed, or untouched when the page has no picker
                    ' and sends nothing.
                    If IsEmployeeLoginTable(tableName) Then
                        SetEmployeeRoles(conn, tx, recordId, pendingRoleIds, userId)
                    End If

                    If IsUserPasswordTable(tableName) AndAlso Not String.IsNullOrWhiteSpace(pendingPassword) Then
                        If Not WritePasswordHash(conn, tx, recordId, pendingPassword, userId) Then
                            Throw New InvalidOperationException("The password could not be updated for this sign-in.")
                        End If
                    End If

                    tx.Commit()
                    Return recordId
                End Using
                  Catch
                    ' Nothing half-written reaches the database. The message is left to the
                    ' caller, which shows it: a user name already taken is the one a person can
                    ' act on, and swallowing it would report a save that did not happen.
                    Try
                        tx.Rollback()
                    Catch telemetryEx As Exception
                        Telemetry.Error(telemetryEx, "DataAccess.TrySaveGeneratedPageRecord")
                    End Try
                    Throw
                  End Try
                End Using
            End Using
        End Function

        ''' <summary>
        ''' The table whose password columns carry the hash contract. Named in one place so the
        ''' three checks below cannot disagree about which table they are protecting.
        ''' </summary>
        Private Shared Function IsUserPasswordTable(tableName As String) As Boolean
            Return String.Equals(If(tableName, String.Empty).Trim(), "FW_Users", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' The table whose rows own a login without being one.
        '''
        ''' An employee is a person; FW_Users is how that person signs in. Saving an employee
        ''' therefore has to reach two tables, and FW_Employees.UserId is NOT NULL - so the login
        ''' is created first and the employee carries its key, rather than the other way round.
        ''' </summary>
        Private Shared Function IsEmployeeLoginTable(tableName As String) As Boolean
            Return String.Equals(If(tableName, String.Empty).Trim(), "FW_Employees", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Columns a page may never write directly.
        '''
        ''' Password because it must be hashed on the way in; PasswordHash because it is derived and
        ''' nothing outside ComputePasswordHashForUser is entitled to produce one. A page offering
        ''' either as an ordinary text box is not wrong to - Password is meant to be editable - it
        ''' simply must not reach the column as typed.
        ''' </summary>
        Private Shared Function IsProtectedPasswordColumn(tableName As String, columnName As String) As Boolean
            If Not IsUserPasswordTable(tableName) AndAlso Not IsEmployeeLoginTable(tableName) Then
                Return False
            End If

            Dim name = If(columnName, String.Empty).Trim()
            Return String.Equals(name, "Password", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(name, "PasswordHash", StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Rewrites an Email in a generated page's value set to its stored form, in place.
        ''' </summary>
        Private Shared Sub NormalizeGeneratedEmailValue(values As Dictionary(Of String, Object))
            If values Is Nothing Then
                Return
            End If

            For Each key In values.Keys.Where(Function(k) String.Equals(k, "Email", StringComparison.OrdinalIgnoreCase)).ToList()
                Dim raw = values(key)
                If raw Is Nothing OrElse IsDBNull(raw) Then
                    Continue For
                End If

                Dim normalized = NormalizeEmailForStorage(raw.ToString())
                If normalized <> String.Empty Then
                    values(key) = normalized
                End If
            Next
        End Sub

        ''' <summary>
        ''' A value from a generated page's dictionary as trimmed text, or empty when absent or null.
        ''' </summary>
        Private Shared Function GetGeneratedValueText(values As Dictionary(Of String, Object), columnName As String) As String
            If values Is Nothing Then
                Return String.Empty
            End If

            For Each key In values.Keys.Where(Function(k) String.Equals(k, columnName, StringComparison.OrdinalIgnoreCase))
                Dim raw = values(key)
                If raw Is Nothing OrElse IsDBNull(raw) Then
                    Return String.Empty
                End If

                Return raw.ToString().Trim()
            Next

            Return String.Empty
        End Function

        ''' <summary>
        ''' Lifts a typed password out of the value set, or returns Nothing when there is nothing to
        ''' do.
        '''
        ''' The sentinel means the field was displayed and left alone, which is the ordinary case on
        ''' every edit - rehashing then would replace a good password with the same one and churn
        ''' UpdatedOn for no reason. An empty box means the same.
        ''' </summary>
        ''' <summary>
        ''' The key a generated employee page uses to carry the role chosen on screen.
        '''
        ''' Not a column on FW_Employees - a role lives in FW_EmployeeRoles - and it travels in
        ''' the value set instead, taken out before anything is written. Only schema columns
        ''' survive the filter, and it would be dropped there anyway; taking it out deliberately
        ''' is how the save gets to read it.
        ''' </summary>
        Public Const AssignRoleValueKey As String = "AssignRoleID"

        ''' <summary>
        ''' The roles chosen on screen, or Nothing when the page did not ask.
        '''
        ''' Nothing and an empty list mean different things, which is why this returns a
        ''' reference rather than a count. Nothing is "this page has no role picker, leave the
        ''' roles alone"; empty is "the picker was there and everything was moved off it", which
        ''' the save must act on by removing what is there.
        ''' </summary>
        Private Shared Function TakeGeneratedRoleValues(values As Dictionary(Of String, Object)) As List(Of Integer)
            If values Is Nothing Then Return Nothing

            For Each key In values.Keys.Where(Function(k) String.Equals(k, AssignRoleValueKey, StringComparison.OrdinalIgnoreCase)).ToList()
                Dim raw = If(values(key) Is Nothing OrElse IsDBNull(values(key)), String.Empty, values(key).ToString())
                values.Remove(key)

                Dim chosen As New List(Of Integer)()
                For Each part In raw.Split(","c)
                    Dim roleId As Integer
                    If Integer.TryParse(part.Trim(), roleId) AndAlso roleId > 0 AndAlso Not chosen.Contains(roleId) Then
                        chosen.Add(roleId)
                    End If
                Next

                Return chosen
            Next

            Return Nothing
        End Function

        Private Shared Function TakeGeneratedPasswordValue(values As Dictionary(Of String, Object)) As String
            If values Is Nothing Then
                Return Nothing
            End If

            For Each key In values.Keys.Where(Function(k) String.Equals(k, "Password", StringComparison.OrdinalIgnoreCase)).ToList()
                Dim raw = If(values(key) Is Nothing OrElse IsDBNull(values(key)), String.Empty, values(key).ToString()).Trim()
                If raw = String.Empty OrElse String.Equals(raw, StoredPasswordMask, StringComparison.Ordinal) Then
                    Return Nothing
                End If

                Return raw
            Next

            Return Nothing
        End Function

        ''' <summary>
        ''' Writes a password hash on a connection and transaction the caller owns.
        '''
        ''' The point of taking them as arguments is that the hash then commits or rolls back with
        ''' the row it belongs to. What this replaced opened its own connection, so the row was
        ''' committed before the password was and a failure between them left an account that
        ''' existed and could not be signed into.
        '''
        ''' The raw value is never written. FW_Users still has a Password column and still gets
        ''' the mask, but the typed string exists only in memory on the way to the hash.
        ''' </summary>
        Private Shared Function WritePasswordHash(conn As SqlConnection,
                                                  tx As SqlTransaction,
                                                  targetUserId As Integer,
                                                  rawPassword As String,
                                                  updatedBy As Integer) As Boolean
            If targetUserId <= 0 Then Return False

            Dim rawValue = If(rawPassword, String.Empty).Trim()
            If rawValue = String.Empty Then Return False

            Dim passwordHash = ComputePasswordHashForUser(rawValue, targetUserId)
            If String.IsNullOrWhiteSpace(passwordHash) Then Return False

            Using cmd As New SqlCommand(
                "UPDATE dbo.FW_Users " &
                "SET PasswordHash = @PasswordHash, [Password] = @PasswordMask, " &
                "    UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                "WHERE UserID = @UserID", conn, tx)
                cmd.Parameters.Add("@PasswordHash", SqlDbType.NVarChar, 255).Value = passwordHash
                cmd.Parameters.Add("@PasswordMask", SqlDbType.VarChar, 50).Value = StoredPasswordMask
                cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = updatedBy
                cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = targetUserId
                Return cmd.ExecuteNonQuery() = 1
            End Using
        End Function

        ''' <summary>
        ''' Creates the login an employee signs in with, and returns its UserId.
        '''
        ''' First, not last. FW_Employees.UserId is NOT NULL, so the employee row cannot exist
        ''' until this one does - which also means there is no window where an employee has no
        ''' account, and no second update to write the key back.
        '''
        ''' No password is set here. The hash is keyed on the UserId this returns, so it cannot be
        ''' computed until the row exists; the caller does it next, in the same transaction.
        ''' </summary>
        Private Shared Function CreateLoginForEmployee(conn As SqlConnection,
                                                       tx As SqlTransaction,
                                                       userName As String,
                                                       firstName As String,
                                                       lastName As String,
                                                       registrationId As Integer,
                                                       createdBy As Integer) As Integer
            Dim name = If(userName, String.Empty).Trim()
            If name = String.Empty Then
                Throw New InvalidOperationException("A user name is required: it is what the employee signs in with.")
            End If

            ' Refused here rather than left to the unique index, so the message names the problem
            ' instead of quoting a constraint. Deleted accounts count - they keep their name, and
            ' restoring one must not collide with something created since.
            Using check As New SqlCommand(
                "SELECT COUNT(*) FROM dbo.FW_Users WHERE LOWER(LTRIM(RTRIM(UserName))) = @UserName", conn, tx)
                check.Parameters.Add("@UserName", SqlDbType.VarChar, 50).Value = name.ToLowerInvariant()
                If Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0 Then
                    Throw New InvalidOperationException("That user name is already in use: " & name)
                End If
            End Using

            Using cmd As New SqlCommand(
                "INSERT INTO dbo.FW_Users (RegistrationID, UserName, FirstName, LastName, IsActive, CreatedBy, CreatedOn) " &
                "VALUES (@RegistrationID, @UserName, @FirstName, @LastName, 1, @CreatedBy, GETDATE()); " &
                "SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx)
                cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value =
                    If(registrationId > 0, CType(registrationId, Object), DBNull.Value)
                cmd.Parameters.Add("@UserName", SqlDbType.VarChar, 50).Value = name
                cmd.Parameters.Add("@FirstName", SqlDbType.VarChar, 100).Value =
                    If(String.IsNullOrWhiteSpace(firstName), CType(DBNull.Value, Object), firstName.Trim())
                cmd.Parameters.Add("@LastName", SqlDbType.VarChar, 100).Value =
                    If(String.IsNullOrWhiteSpace(lastName), CType(DBNull.Value, Object), lastName.Trim())
                cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = createdBy
                Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using
        End Function

        ''' <summary>
        ''' Keeps the login's active flag in step with the employee's.
        '''
        ''' The employee is the authority. An employee and a user are the same person here, and a
        ''' login left active behind an inactive employee is somebody who has been let go and can
        ''' still sign in - which is what the data showed on 2026-09-16, User 1 inactive as an
        ''' employee and active as a login. Both directions, so reactivating an employee lets them
        ''' back in rather than leaving a login nobody thought to switch on.
        '''
        ''' Only when the page actually carried IsActive. A page that does not show the flag says
        ''' nothing about it, and must not be read as having switched it off.
        ''' </summary>
        Private Shared Sub SyncLoginActive(conn As SqlConnection,
                                           tx As SqlTransaction,
                                           loginId As Integer,
                                           values As Dictionary(Of String, Object),
                                           updatedBy As Integer)
            If loginId <= 0 OrElse values Is Nothing Then Return

            Dim key = values.Keys.FirstOrDefault(Function(k) String.Equals(k, "IsActive", StringComparison.OrdinalIgnoreCase))
            If key Is Nothing Then Return

            Dim raw = values(key)
            If raw Is Nothing OrElse IsDBNull(raw) Then Return

            Dim active As Boolean
            If TypeOf raw Is Boolean Then
                active = CBool(raw)
            Else
                Dim text = raw.ToString().Trim()
                active = text = "1" OrElse String.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
            End If

            Using cmd As New SqlCommand(
                "UPDATE dbo.FW_Users SET IsActive = @IsActive, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                "WHERE UserID = @UserID AND ISNULL(IsActive, 0) <> @IsActive", conn, tx)
                cmd.Parameters.Add("@IsActive", SqlDbType.Bit).Value = active
                cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = updatedBy
                cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = loginId
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>
        ''' Keeps the login's user name in step when an employee's is edited.
        '''
        ''' Two spellings of one fact, so they are written together or not at all. An employee
        ''' whose user name was changed on one row only is an employee who cannot sign in.
        ''' </summary>
        Private Shared Sub SyncLoginUserName(conn As SqlConnection,
                                             tx As SqlTransaction,
                                             targetUserId As Integer,
                                             userName As String,
                                             updatedBy As Integer)
            Dim name = If(userName, String.Empty).Trim()
            If targetUserId <= 0 OrElse name = String.Empty Then Return

            ' Refused by name rather than left to the unique index, which would surface as a
            ' constraint violation naming neither the field nor who has it. Editing is where this
            ' matters most: creating a duplicate is caught at the point of asking, but taking a
            ' name off somebody else happens to an account that already works.
            '
            ' Deleted accounts count, as they do on insert - a soft-deleted row keeps its name,
            ' and giving it away makes that row impossible to restore.
            Using check As New SqlCommand(
                "SELECT COUNT(*) FROM dbo.FW_Users " &
                "WHERE LOWER(LTRIM(RTRIM(ISNULL(UserName, '')))) = @UserName AND UserID <> @UserID", conn, tx)
                check.Parameters.Add("@UserName", SqlDbType.VarChar, 50).Value = name.ToLowerInvariant()
                check.Parameters.Add("@UserID", SqlDbType.Int).Value = targetUserId
                If Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0 Then
                    Throw New InvalidOperationException("That user name is already in use: " & name)
                End If
            End Using

            Using cmd As New SqlCommand(
                "UPDATE dbo.FW_Users SET UserName = @UserName, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                "WHERE UserID = @UserID AND ISNULL(UserName, '') <> @UserName", conn, tx)
                cmd.Parameters.Add("@UserName", SqlDbType.VarChar, 50).Value = name
                cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = updatedBy
                cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = targetUserId
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>
        ''' Soft-deletes or restores the login belonging to an employee, in the caller's
        ''' transaction.
        '''
        ''' One method for both directions, taking the state as an argument, because the two have
        ''' to be exact opposites - written separately they drift, and a restore that leaves one
        ''' field set is an account nobody can explain.
        ''' </summary>
        Private Shared Sub SetEmployeeLoginDeleted(conn As SqlConnection,
                                                   tx As SqlTransaction,
                                                   employeeId As Integer,
                                                   actingUserId As Integer,
                                                   deleted As Boolean)
            Dim loginId = EmployeeLoginId(conn, tx, employeeId)
            If loginId <= 0 Then Return

            Dim sql = If(deleted,
                         "UPDATE dbo.FW_Users SET DeletedFlag = 1, DeletedBy = @UserID, DeletedOn = SYSUTCDATETIME(), " &
                         "    IsActive = 0, UpdatedBy = @UserID, UpdatedOn = GETDATE() WHERE UserID = @LoginID",
                         "UPDATE dbo.FW_Users SET DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, " &
                         "    IsActive = 1, UpdatedBy = @UserID, UpdatedOn = GETDATE() WHERE UserID = @LoginID")

            Using cmd As New SqlCommand(sql, conn, tx)
                cmd.Parameters.Add("@LoginID", SqlDbType.Int).Value = loginId
                cmd.Parameters.Add("@UserID", SqlDbType.Int).Value =
                    If(actingUserId > 0, CType(actingUserId, Object), DBNull.Value)
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        ''' <summary>
        ''' Gives a new employee the role chosen on the create screen.
        '''
        ''' Chosen, not configured. A stored default on the registration was built first and
        ''' taken out the same day: whoever is typing in a new employee knows what that employee
        ''' does, and asking them there answers the question at the one moment somebody is
        ''' looking at it. A setting elsewhere guesses, and is applied without being read.
        '''
        ''' Without a role, creating an employee produced a person, a login and no way in - the
        ''' account authenticated correctly and was turned away for having none. The create
        ''' screen looked like one step and was two.
        '''
        ''' The role must belong to this employee's own registration, be live, and never be an
        ''' Application Admin role. The screen offers none of those, and a screen is not where
        ''' this is enforced: a value can arrive from a stale form, and the rule that must not be
        ''' bypassed is the one handing out the ability to change every other role.
        ''' </summary>
        Private Shared Sub SetEmployeeRoles(conn As SqlConnection,
                                            tx As SqlTransaction,
                                            employeeId As Integer,
                                            chosenRoleIds As List(Of Integer),
                                            actingUserId As Integer)
            If employeeId <= 0 OrElse chosenRoleIds Is Nothing Then Return

            ' Nothing and empty mean different things here, and this is where the difference
            ' earns its keep. Nothing is a page with no role picker - My Profile, or any page
            ' that does not offer roles - and its roles are left exactly as they are. Empty is a
            ' page that offered the question and came back with no answer, which is refused.
            '
            ' Refused at the boundary as well as on the page. EmployeeRolesSelector.Validate
            ' already stops it, but a rule that lives only in a form is not a rule: a second
            ' employee page, or a companion whose OnValidating is deleted while the grids stay,
            ' would strip somebody's last role and the save would report success.
            '
            ' What this cannot enforce is that every employee has a role. A page with no picker
            ' can create one without, and refusing that would block pages with no way to ask.
            If chosenRoleIds.Count = 0 Then
                Throw New InvalidOperationException(
                    "An employee must have at least one role." & Environment.NewLine &
                    "Without one they can sign in and reach nothing.")
            End If

            ' Whatever is no longer on the right-hand side, soft-deleted rather than removed.
            ' A role somebody held is a fact about what they could do, and the audit trail reads
            ' against rows that still exist.
            Using remove As New SqlCommand(
                "UPDATE dbo.FW_EmployeeRoles " &
                "SET DeletedFlag = 1, DeletedBy = @ActingUserID, DeletedOn = SYSUTCDATETIME(), " &
                "    IsActive = 0, UpdatedBy = @ActingUserID, UpdatedOn = GETDATE() " &
                "WHERE EmployeeID = @EmployeeID AND ISNULL(DeletedFlag, 0) = 0 " &
                "  AND RoleID NOT IN (SELECT value FROM STRING_SPLIT(@Keep, ','))", conn, tx)
                remove.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId
                remove.Parameters.Add("@ActingUserID", SqlDbType.Int).Value =
                    If(actingUserId > 0, CType(actingUserId, Object), DBNull.Value)
                ' -1 stands in for "keep nothing" - STRING_SPLIT of an empty string yields one
                ' empty row, which NOT IN then compares against and matches nothing.
                remove.Parameters.Add("@Keep", SqlDbType.VarChar, -1).Value =
                    If(chosenRoleIds.Count = 0, "-1", String.Join(",", chosenRoleIds))
                remove.ExecuteNonQuery()
            End Using

            For Each roleId In chosenRoleIds
                ' Each one checked against this employee's own registration, and never an
                ' Application Admin role. The screen offers neither, and a screen is not where
                ' this is enforced: a value can arrive from a stale form, and the rule that must
                ' not be bypassed is the one handing out the ability to change every other role.
                Using add As New SqlCommand(
                    "UPDATE dbo.FW_EmployeeRoles " &
                    "SET DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, IsActive = 1, " &
                    "    UpdatedBy = @ActingUserID, UpdatedOn = GETDATE() " &
                    "WHERE EmployeeID = @EmployeeID AND RoleID = @RoleID; " &
                    "IF @@ROWCOUNT = 0 " &
                    "INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, DeletedFlag, CreatedBy, CreatedOn) " &
                    "SELECT e.RegistrationId, e.EmployeeID, r.ID, ISNULL(r.DisplayOrder, 1), 1, 0, @ActingUserID, GETDATE() " &
                    "FROM dbo.FW_Employees e " &
                    "INNER JOIN dbo.FW_Roles r ON r.ID = @RoleID " &
                    "                         AND r.RegistrationID = e.RegistrationId " &
                    "                         AND ISNULL(r.IsActive, 1) = 1 " &
                    "                         AND ISNULL(r.DeletedFlag, 0) = 0 " &
                    "                         AND ISNULL(r.Typ_AppAdmin, 0) = 0 " &
                    "WHERE e.EmployeeID = @EmployeeID", conn, tx)
                    add.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId
                    add.Parameters.Add("@RoleID", SqlDbType.Int).Value = roleId
                    add.Parameters.Add("@ActingUserID", SqlDbType.Int).Value =
                        If(actingUserId > 0, CType(actingUserId, Object), DBNull.Value)
                    add.ExecuteNonQuery()
                End Using
            Next
        End Sub

        ''' <summary>
        ''' Every role an employee holds, ranked, with its name.
        '''
        ''' Names come back with the ids because a role somebody holds might no longer be
        ''' offered - deactivated, or Application Admin - and the picker still has to show it.
        ''' Looking it up from the offered list would leave it blank, or drop it, and a role
        ''' that cannot be seen cannot be taken away.
        ''' </summary>
        Public Shared Function GetEmployeeRoleIds(employeeId As Integer) As List(Of (RoleId As Integer, RoleName As String, DisplayOrder As Integer))
            Dim held As New List(Of (RoleId As Integer, RoleName As String, DisplayOrder As Integer))()
            If employeeId <= 0 Then Return held

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT er.RoleID, ISNULL(r.RoleName, '') AS RoleName, ISNULL(r.DisplayOrder, 0) AS DisplayOrder " &
                        "FROM dbo.FW_EmployeeRoles er " &
                        "INNER JOIN dbo.FW_Roles r ON r.ID = er.RoleID " &
                        "WHERE er.EmployeeID = @ID AND ISNULL(er.IsActive, 1) = 1 AND ISNULL(er.DeletedFlag, 0) = 0 " &
                        "ORDER BY CASE WHEN ISNULL(r.DisplayOrder, 0) = 0 THEN 1 ELSE 0 END, ISNULL(r.DisplayOrder, 255), r.RoleName", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = employeeId
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                held.Add((Convert.ToInt32(reader("RoleID"), CultureInfo.InvariantCulture),
                                          Convert.ToString(reader("RoleName")),
                                          Convert.ToInt32(reader("DisplayOrder"), CultureInfo.InvariantCulture)))
                            End While
                        End Using
                    End Using
                End Using
            Catch telemetryEx As Exception
                Telemetry.Error(telemetryEx, "DataAccess.GetEmployeeRoleIds")
            End Try

            Return held
        End Function

        ''' <summary>
        ''' The role an employee already holds, highest ranked first, or zero.
        '''
        ''' Only ever shown, never acted on: the create screen's combo uses it to display what an
        ''' existing person has, disabled. Somebody holding several roles is represented by their
        ''' most senior, which is a simplification the page owns up to by refusing to edit there
        ''' at all - Roles shows the whole set.
        ''' </summary>
        Public Shared Function GetEmployeeRoleId(employeeId As Integer) As Integer
            If employeeId <= 0 Then Return 0

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 er.RoleID FROM dbo.FW_EmployeeRoles er " &
                        "INNER JOIN dbo.FW_Roles r ON r.ID = er.RoleID " &
                        "WHERE er.EmployeeID = @ID AND ISNULL(er.IsActive, 1) = 1 AND ISNULL(er.DeletedFlag, 0) = 0 " &
                        "ORDER BY CASE WHEN ISNULL(r.DisplayOrder, 0) = 0 THEN 1 ELSE 0 END, ISNULL(r.DisplayOrder, 255)", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = employeeId
                        Dim result = cmd.ExecuteScalar()
                        If result Is Nothing OrElse Convert.IsDBNull(result) Then Return 0
                        Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                    End Using
                End Using
            Catch
                Return 0
            End Try
        End Function

        ''' <summary>
        ''' The employee behind a sign-in, or zero.
        '''
        ''' The other direction from EmployeeLoginId, and the one My Profile needs: somebody
        ''' knows who they are signed in as and wants their own record. Deleted employees are
        ''' excluded - a deleted record is not a profile to edit.
        ''' </summary>
        ''' <summary>
        ''' This person's IANA time zone, or empty for the registration's.
        '''
        ''' Null on the employee is the normal case and means "use the registration's" - it is not
        ''' a missing value to default.
        ''' </summary>
        Public Shared Function GetEmployeeTimeZoneName(userId As Integer) As String
            If userId <= 0 Then Return String.Empty

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(z.TimeZoneName, '') FROM dbo.FW_Employees e " &
                        "JOIN dbo.FW_TimeZones z ON z.TimeZoneID = e.TimeZoneID " &
                        "WHERE e.UserId = @UserID AND ISNULL(e.DeletedFlag, 0) = 0 ORDER BY e.EmployeeID", conn)
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId
                        Dim result = cmd.ExecuteScalar()
                        If result Is Nothing OrElse Convert.IsDBNull(result) Then Return String.Empty
                        Return Convert.ToString(result, CultureInfo.InvariantCulture)
                    End Using
                End Using
            Catch
                Return String.Empty
            End Try
        End Function

        Public Shared Function GetEmployeeIdForUser(userId As Integer) As Integer
            If userId <= 0 Then Return 0

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 EmployeeID FROM dbo.FW_Employees " &
                        "WHERE UserId = @UserID AND ISNULL(DeletedFlag, 0) = 0 ORDER BY EmployeeID", conn)
                        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId
                        Dim result = cmd.ExecuteScalar()
                        If result Is Nothing OrElse Convert.IsDBNull(result) Then Return 0
                        Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                    End Using
                End Using
            Catch
                Return 0
            End Try
        End Function

        ''' <summary>The UserId an existing employee row already points at.</summary>
        Private Shared Function EmployeeLoginId(conn As SqlConnection, tx As SqlTransaction, employeeId As Integer) As Integer
            Using cmd As New SqlCommand("SELECT UserId FROM dbo.FW_Employees WHERE EmployeeID = @ID", conn, tx)
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = employeeId
                Dim result = cmd.ExecuteScalar()
                If result Is Nothing OrElse Convert.IsDBNull(result) Then Return 0
                Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
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
                parameter.Value = GeneratedParameterValue(values(index).Value, column)
            Next
        End Sub

        ''' <summary>
        ''' What a control's value becomes at the parameter.
        '''
        ''' Every non-lookup field on a generated page arrives here as the text of a text box, so an
        ''' empty date box arrives as "" against a datetime parameter and ADO.NET refuses it:
        ''' "Failed to convert parameter value from a String to a DateTime." A blank int, decimal or
        ''' bit fails the same way, with the same message naming a different type - and no message
        ''' names the column, so the user is told a save failed and nothing about which field.
        '''
        ''' An empty box means no value, which is DBNull. Decided here rather than on the page for
        ''' two reasons: every generated page has the same boxes and would otherwise each need the
        ''' same rule, and the page cannot see the column type, which is the thing that decides it.
        '''
        ''' A text column keeps its empty string. "" and NULL are different values there, and a page
        ''' that has always stored one must not quietly start storing the other.
        '''
        ''' A NOT NULL column still refuses the null, which is the right outcome: the field should
        ''' have been marked Admin Required, and the database saying the column cannot be null is
        ''' more use than a conversion error that names no column at all.
        ''' </summary>
        Private Shared Function GeneratedParameterValue(value As Object, column As DataColumn) As Object
            If value Is Nothing OrElse Convert.IsDBNull(value) Then Return DBNull.Value

            Dim text = TryCast(value, String)
            If text Is Nothing Then Return value
            If column.DataType Is GetType(String) OrElse column.DataType Is GetType(Char) Then Return text

            Return If(text.Trim() = String.Empty, CObj(DBNull.Value), CObj(text))
        End Function

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
                                                   generatedPageId As Integer,
                                                   values As Dictionary(Of String, Object),
                                                   originalRowVersion As Byte()) As Boolean
            ' Every column the page sends. Three were missing until 2026-09-14 -
            ' CreateAsFrameworkPages, TableAlias and HotFields - so the page built a parameter for
            ' each, the command carried it, and no column ever received it. Nothing failed: the
            ' save reported success and the setting was simply the value it had before.
            '
            ' CreateAsFrameworkPages was the one that showed: ticking it renamed the pages to FW_,
            ' the request saved without it, and generation then wrote the unprefixed names the
            ' stored row still asked for. That flag became Owner on 2026-09-18 - one answer for
            ' the prefix and the folder - and is in this list for the same reason it was added.
            Dim writableColumns = New String() {
                "RequestName", "PageBaseName", "BrowsePageName", "MaintenancePageName", "UnderlyingTableName",
                "UseRegistrationID", "BrowseFields", "MaintenanceFields", "BrowseSql", "LookupFields", "AdminRequiredFields",
                "MenuCaller", "IconFileName", "GenerateBrowsePage", "GenerateMaintenancePage", "UseQbeOnly",
                "UseHotFields", "Owner", "TableAlias", "HotFields", "Column2Fields"
            }

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                If isNewRecord Then
                    Dim columnNames = String.Join(", ", writableColumns)
                    Dim parameterNames = String.Join(", ", writableColumns.Select(Function(column) "@" & column))
                    Using cmd As New SqlCommand("INSERT INTO dbo." & GeneratedPagesTable & " (" & columnNames & ", CreatedBy) VALUES (" & parameterNames & ", @CreatedBy)", conn)
                        AddPageGenerationParameters(cmd, values)
                        cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = SessionState.ActingUserID
                        cmd.ExecuteNonQuery()
                    End Using
                    Return True
                End If

                Dim assignments = String.Join(", ", writableColumns.Select(Function(column) column & " = @" & column))
                Using cmd As New SqlCommand("UPDATE dbo." & GeneratedPagesTable & " SET " & assignments & ", UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() WHERE GeneratedPageID = @GeneratedPageID AND RowVersion = @RowVersion", conn)
                    AddPageGenerationParameters(cmd, values)
                    cmd.Parameters.Add("@UpdatedBy", SqlDbType.Int).Value = SessionState.ActingUserID
                    cmd.Parameters.Add("@GeneratedPageID", SqlDbType.Int).Value = generatedPageId
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
                                   String.Equals(pair.Key, "UseQbeOnly", StringComparison.OrdinalIgnoreCase) OrElse
                                   String.Equals(pair.Key, "UseHotFields", StringComparison.OrdinalIgnoreCase) OrElse
                                   False,
                                   command.Parameters.Add("@" & pair.Key, SqlDbType.Bit),
                                   command.Parameters.Add("@" & pair.Key, SqlDbType.VarChar, -1))
                parameter.Value = If(pair.Value Is Nothing, DBNull.Value, pair.Value)
            Next
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
                        "LEFT JOIN dbo.FW_Registration r ON r.RegistrationID = cu.RegistrationID " &
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
                        "FROM dbo.FW_EmployeeRoles ur " &
                        "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                        "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                        "WHERE emp.UserId = @UserID " &
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

        ''' <summary>
        ''' The form an email is stored in: lower case, trimmed, no spaces.
        '''
        ''' The same shape login compares by, so what is stored is already what a lookup asks for.
        ''' That is the point of normalising on the way in rather than on every read - the column
        ''' becomes canonical, so a plain unique index on Email means exactly what the application
        ''' means, and LOWER(REPLACE(...)) at read time stops being the only thing standing between
        ''' two rows login cannot tell apart.
        '''
        ''' Public because it is a write-side rule that pages and the data layer both need, unlike
        ''' the read-side normaliser below.
        ''' </summary>
        Public Shared Function NormalizeEmailForStorage(email As String) As String
            Return NormalizeEmailForLookup(email)
        End Function

        Private Shared Function NormalizeEmailForLookup(email As String) As String
            If email Is Nothing Then
                Return String.Empty
            End If

            Return email.Replace(" ", String.Empty).Trim().ToLowerInvariant()
        End Function

        ''' <summary>
        ''' Whether another user already holds this email, in any registration.
        '''
        ''' Deliberately not scoped by registration, unlike every other uniqueness rule here. An
        ''' email is how somebody signs in, and login does not know a registration yet - it finds the
        ''' account first and takes the registration from it. Two accounts sharing an email means
        ''' TryGetCurrentUserRecord's SELECT TOP 1 picks one of them by row order, so which account
        ''' you get - which password, which roles - is undefined. That is the failure this prevents,
        ''' and it does not respect registration boundaries.
        '''
        ''' Compared the way login compares, through NormalizeEmailForLookup and the same
        ''' LOWER(REPLACE(...)) in SQL. A check that normalised differently would let through a pair
        ''' that login then treats as the same address, which is the whole problem back again.
        '''
        ''' Soft-deleted rows are excluded: a deleted user must not hold an address hostage.
        ''' excludeUserId leaves the record being edited out, so saving a user without changing
        ''' their email does not report them as a duplicate of themselves.
        ''' </summary>
        Public Enum EmailAvailability
            Free
            HeldByActiveUser
            HeldByDeletedUser
            CouldNotBeChecked
        End Enum

        ''' <summary>
        ''' Who, if anyone, already holds this email - counting soft-deleted users, and saying so
        ''' separately.
        '''
        ''' A deleted row still holds its address. Letting the address be reused while that row
        ''' exists creates a duplicate the moment somebody restores it, and a restore clears
        ''' DeletedFlag without checking anything - so the conflict would be created by an operation
        ''' this check never sees. Refusing up front means restore is always safe and needs no
        ''' second rule. The rule outlives any one restore path: the FW_Users one was deleted with
        ''' its pages on 2026-09-09, and the next one will be no more careful.
        '''
        ''' Told apart rather than merged, because the two need different actions from whoever hit
        ''' them: an active holder means pick another address, a deleted one means restore that user,
        ''' change their address, or remove them for good. A single "already in use" would leave an
        ''' administrator hunting a user they cannot see.
        ''' </summary>
        Public Shared Function CheckEmailAvailability(email As String, excludeUserId As Integer, ByRef holderName As String) As EmailAvailability
            holderName = String.Empty

            Dim normalized = NormalizeEmailForLookup(email)
            If normalized = String.Empty Then
                Return EmailAvailability.Free
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(FirstLast, ''), ISNULL(DeletedFlag, 0) FROM dbo.FW_Users " &
                        "WHERE LOWER(REPLACE(Email, ' ', '')) = @Email " &
                        "AND (@ExcludeUserID = 0 OR UserId <> @ExcludeUserID) " &
                        "ORDER BY ISNULL(DeletedFlag, 0), UserId", conn)
                        cmd.Parameters.AddWithValue("@Email", normalized)
                        cmd.Parameters.AddWithValue("@ExcludeUserID", excludeUserId)

                        Using reader = cmd.ExecuteReader()
                            If Not reader.Read() Then
                                Return EmailAvailability.Free
                            End If

                            holderName = reader.GetString(0).Trim()
                            ' Ordered so an active holder is reported ahead of a deleted one: an
                            ' address held by both is a live conflict first.
                            Return If(Convert.ToBoolean(reader.GetValue(1)),
                                      EmailAvailability.HeldByDeletedUser,
                                      EmailAvailability.HeldByActiveUser)
                        End Using
                    End Using
                End Using
            Catch
                ' A check that cannot run must not report "free" - that would wave a duplicate
                ' through on a transient fault. Refusing the save is the safe direction to fail.
                Return EmailAvailability.CouldNotBeChecked
            End Try
        End Function

        ''' <summary>
        ''' How to refer to whoever holds an email: by name when the name says something, and by
        ''' <paramref name="fallback"/> when it does not.
        '''
        ''' A name built from the address itself says nothing. FirstLast is FirstName + LastName, and
        ''' a user entered as first name "1@1.com", last name "1@1.com" produces "1@1.com 1@1.com" -
        ''' so the message read "The email 1@1.com already belongs to 1@1.com 1@1.com", stating the
        ''' address three times and identifying nobody.
        '''
        ''' Compared through the email normaliser so spacing and case do not decide it.
        ''' </summary>
        Private Shared Function DescribeEmailHolder(holderName As String, email As String, fallback As String) As String
            Dim name = If(holderName, String.Empty).Trim()
            If name = String.Empty Then
                Return fallback
            End If

            Dim normalizedEmail = NormalizeEmailForLookup(email)
            If normalizedEmail <> String.Empty AndAlso
               NormalizeEmailForLookup(name).Contains(normalizedEmail) Then
                Return fallback
            End If

            Return name
        End Function

        ''' <summary>
        ''' The message for an email that cannot be used, or empty when it can. One wording, used by
        ''' both the page validation and the write boundary, so the two cannot describe the same
        ''' refusal differently.
        ''' </summary>
        Public Shared Function GetEmailUnavailableMessage(email As String, excludeUserId As Integer) As String
            Dim holderName As String = Nothing

            Select Case CheckEmailAvailability(email, excludeUserId, holderName)
                Case EmailAvailability.Free
                    Return String.Empty

                Case EmailAvailability.HeldByActiveUser
                    Return "The email " & email.Trim() & " already belongs to " &
                           DescribeEmailHolder(holderName, email, "another user") & "." & Environment.NewLine &
                           "An email is how a user signs in, it must be unique."

                Case EmailAvailability.HeldByDeletedUser
                    Return "The email " & email.Trim() & " belongs to " &
                           DescribeEmailHolder(holderName, email, "a user") & ", whose record is deleted." & Environment.NewLine &
                           "Restore that user, change their email, or remove the record permanently - " &
                           "reusing it now would create two accounts with one email as soon as theirs is restored."

                Case Else
                    Return "The email " & email.Trim() & " could not be checked for duplicates, so the record was not saved."
            End Select
        End Function

        Private Shared Function ValidateComputedHashAgainstStored(passwordWithoutSpaces As String, userId As Integer, storedHash As String) As Boolean
            If String.IsNullOrWhiteSpace(storedHash) Then
                Return False
            End If

            Dim candidates As New List(Of String)()
            candidates.Add(ComputeHmacHashAsUnicodeString(passwordWithoutSpaces, Encoding.Unicode.GetBytes(userId.ToString(CultureInfo.InvariantCulture)), Encoding.Unicode))
            candidates.Add(ComputeHmacHashAsUnicodeString(passwordWithoutSpaces, Encoding.UTF8.GetBytes(userId.ToString(CultureInfo.InvariantCulture)), Encoding.UTF8))

            Dim idBytes = BitConverter.GetBytes(userId)
            candidates.Add(ComputeHmacHashAsUnicodeString(passwordWithoutSpaces, idBytes, Encoding.Unicode))
            candidates.Add(ComputeHmacHashAsUnicodeString(passwordWithoutSpaces, idBytes, Encoding.UTF8))

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

        ''' <summary>
        ''' The table holding page-generation requests, and its key.
        '''
        ''' Named once. Before 2026-09-03 the table name was a string literal in nine places across
        ''' three files, which is the arrangement in which a rename lands in eight of them - and the
        ''' rename from FW_PageGeneration_B_U to FW_GeneratedPages was precisely the occasion for
        ''' finding that out.
        '''
        ''' Bare, without a schema, because it is used both in SQL - where "dbo." is prepended - and
        ''' as the table name a page reports to the framework, where a schema prefix would not match
        ''' the FW_Pages row.
        ''' </summary>
        Public Const GeneratedPagesTable As String = "FW_GeneratedPages"
        Public Const GeneratedPagesKey As String = "GeneratedPageID"

        ''' <summary>
        ''' The page registry: one row per page per registration, holding the table a page reads,
        ''' the SELECT it runs, its caption, display order and colour. Base_B reads it on every page
        ''' load to find out what to query.
        '''
        ''' Called FW_Pages until 2026-09-03, which was wrong in a way that cost real time -
        ''' the table has no RoleID column and never had one. Role permissions are FW_RoleDetails
        ''' and FW_RoleFields. The old name was confusing enough that the codebase used "RoleTable"
        ''' for both ideas at once: RoleTableAccessEntry and GetRoleTableAccessEntries read
        ''' FW_RoleDetails and have nothing to do with this table, which is why the rename had to be
        ''' done by hand rather than by search and replace.
        ''' </summary>
        Public Const PagesTable As String = "FW_Pages"
        Public Const PagesKey As String = "PageID"

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

        Private Shared Function ComputeHmacHashAsUnicodeString(passwordWithoutSpaces As String, keyBytes As Byte(), messageEncoding As Encoding) As String
            Return Encoding.Unicode.GetString(ComputeHmacHashBytes(passwordWithoutSpaces, keyBytes, messageEncoding))
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

        ''' <summary>
        ''' How a text filter compares, and the value to compare against.
        '''
        ''' The two travel together because they have to agree and they are used at separate call
        ''' sites - the operator while the SQL is being assembled, the value later while parameters
        ''' are bound. Deciding them in two places is how "LIKE" ends up paired with a value nobody
        ''' wrapped, and the search silently matches nothing.
        ''' </summary>
        Private Structure TextComparison
            Public ReadOnly SqlOperator As String
            Public ReadOnly Pattern As String

            Public Sub New(sqlOperator As String, pattern As String)
                Me.SqlOperator = sqlOperator
                Me.Pattern = pattern
            End Sub

            Public ReadOnly Property IsPatternMatch As Boolean
                Get
                    Return SqlOperator = "LIKE" OrElse SqlOperator = "NOT LIKE"
                End Get
            End Property
        End Structure

        ''' <summary>
        ''' Decides how a text value is compared, honouring a wildcard the user typed.
        '''
        ''' A value containing % is a LIKE pattern and is used exactly as typed, whatever the
        ''' operator says. The operator does not then add wildcards of its own: Contains on
        ''' "%Rathk%" would otherwise ask for "%%Rathk%%", which still matches but is no longer the
        ''' pattern that was typed.
        '''
        ''' Nothing is lost by this. Before it, % was a literal character under Equals, so
        ''' "= Glenn%" looked for a name ending in a percent sign and always returned nothing, and
        ''' "&lt;&gt; Glenn%" excluded nothing and returned everything. Only queries that could not
        ''' work change meaning.
        '''
        ''' Underscore is deliberately not a switch. It is a LIKE wildcard once a pattern is in
        ''' play, but on its own it stays literal, so "= FW_Entity" remains an exact match rather
        ''' than quietly also finding FW.Entity. One switch, and full SQL semantics past it.
        '''
        ''' With no wildcard the behaviour is what it always was, so every existing search and every
        ''' saved QBE is unaffected.
        ''' </summary>
        Private Shared Function ResolveTextComparison(value As String,
                                                      comparisonOperator As QbeComparisonOperator) As TextComparison
            If Not String.IsNullOrEmpty(value) AndAlso value.Contains("%"c) Then
                If comparisonOperator = QbeComparisonOperator.NotEquals Then
                    Return New TextComparison("NOT LIKE", value)
                End If

                Return New TextComparison("LIKE", value)
            End If

            Select Case comparisonOperator
                Case QbeComparisonOperator.NotEquals
                    Return New TextComparison("<>", value)
                Case QbeComparisonOperator.Contains
                    Return New TextComparison("LIKE", "%" & value & "%")
                Case QbeComparisonOperator.StartsWith
                    Return New TextComparison("LIKE", value & "%")
                Case QbeComparisonOperator.EndsWith
                    Return New TextComparison("LIKE", "%" & value)
                Case Else
                    Return New TextComparison("=", value)
            End Select
        End Function

        ''' <summary>
        ''' Whether a pattern carries a wildcard anywhere but its two ends.
        '''
        ''' Only the client-side DataView path asks. SQL Server is happy with "Gl%nn"; a DataTable
        ''' filter expression allows a wildcard at the start or the end and nowhere else, and throws
        ''' rather than failing to match. Asking first is what turns that into a message.
        ''' </summary>
        Private Shared Function HasInnerWildcard(pattern As String) As Boolean
            If String.IsNullOrEmpty(pattern) Then
                Return False
            End If

            For i = 1 To pattern.Length - 2
                If pattern(i) = "%"c Then
                    Return True
                End If
            Next

            Return False
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
                    ' Text has one owner, and it is not this function. ResolveTextComparison decides
                    ' the operator and the value together because they have to agree; answering half
                    ' the question here is what let a typed wildcard be dropped on the floor.
                    Throw New InvalidOperationException(
                        "Text filters are resolved by ResolveTextComparison, which returns the operator and the value together.")
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


        ''' <returns>
        ''' How many rows were written. A control only produces a row when it is data-bound, so a
        ''' page can enumerate to nothing and the caller needs to be able to say so.
        ''' </returns>


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




        ''' <summary>
        ''' Users of a registration, as a lookup source: UserID AS ID, FirstLast.
        '''
        ''' <paramref name="excludeUserId"/> leaves one user out. The manager lookup passes the user
        ''' being edited, because nobody reports to themselves and a foreign key cannot say so - the
        ''' key is satisfied by a row pointing at itself.
        '''
        ''' Soft-deleted users are excluded. They were not, until 2026-09-03: the function had been
        ''' written and never called, so nothing had ever exposed the omission. A deleted user must
        ''' not be offerable as somebody's manager.
        ''' </summary>
        Public Shared Function GetUsersByRegistration(registrationId As Integer,
                                                      Optional excludeUserId As Integer = 0) As DataTable
            Dim table As New DataTable("FW_Users")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserID AS ID, FirstLast FROM dbo.FW_Users " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "AND ISNULL(DeletedFlag, 0) = 0 " &
                    "AND (@ExcludeUserID = 0 OR UserID <> @ExcludeUserID) " &
                    "ORDER BY FirstLast", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@ExcludeUserID", excludeUserId)

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
                    "FROM dbo.FW_EmployeeRoles ur " &
                    "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                    "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                    "WHERE emp.UserId = @UserID AND ur.RegistrationID = @RegistrationID " &
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
                    "CASE WHEN EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles ur " &
                    "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                    "WHERE emp.UserId = @UserID AND ur.RoleID = r.ID " &
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
                    "FROM dbo." & PagesTable & " WHERE RegistrationID = @RegistrationID OR RegistrationID IS NULL " &
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
                    "CASE WHEN NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles ur0 " &
                    "INNER JOIN dbo.FW_Employees emp0 ON emp0.EmployeeID = ur0.EmployeeID " &
                    "WHERE emp0.UserId = @UserID " &
                    "AND ur0.RegistrationID = @RegistrationID AND ISNULL(ur0.IsActive, 1) = 1) " &
                    "THEN 'MISSING ROLE' ELSE 'ROLE HAS NO READ PERMISSION' END AS DiagnosticReason " &
                    "FROM dbo." & PagesTable & " rt " &
                    "WHERE (rt.RegistrationID = @RegistrationID OR rt.RegistrationID IS NULL) " &
                    "AND NOT EXISTS (" &
                    "SELECT 1 FROM dbo.FW_EmployeeRoles ur " &
                    "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                    "INNER JOIN dbo.FW_RoleDetails rd ON rd.RoleID = ur.RoleID " &
                    "AND rd.RegistrationID = @RegistrationID AND rd.DB_Table = rt.DB_Table " &
                    "WHERE emp.UserId = @UserID AND ur.RegistrationID = @RegistrationID " &
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

        ''' <summary>
        ''' Every role that reaches one table for one user, and what each of them grants.
        '''
        ''' Restored on 2026-09-09 with FW_UserAccessDiagnostic_B, which is its only caller. Both
        ''' were removed six days after the procedure behind them broke on a renamed table - the
        ''' page was unreachable from any menu, so nothing was exercising it and nothing complained.
        ''' Unreachable turned out to be the wrong test: the page could set an individual permission,
        ''' which the page kept in its place could not.
        ''' </summary>
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
                    "SELECT GenderID, ISNULL(GenderDescription, '') AS GenderDescription " &
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
                Using cmd As New SqlCommand("SELECT RegistrationTypeID AS ID, RegTypeName AS RegistrationType FROM dbo.FW_RegistrationType ORDER BY RegTypeName", conn)
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
                    "SELECT TOP 1 RegistrationID, RegName, Smarty_AuthID, Smarty_AuthToken, Smarty_EmbeddedKey, ISNULL(Smarty_UseEmbeddedKey, 0) AS Smarty_UseEmbeddedKey, RegistrationTypeID, FormatDateID, FormatTimeID, r.TimeZoneID, Address1, Address2, City, State, Zip, MainFax, MainPhone, MainEMail, WebLandingPage, " &
                    "ISNULL(AllowMultipleRoles, 0) AS AllowMultipleRoles, " &
                    "ISNULL(AllowPasswordChangeAtLogin, 0) AS AllowPasswordChangeAtLogin, " &
                    "ISNULL(AllowUpdateMyProfile, 0) AS AllowUpdateMyProfile, " &
                    "ISNULL(AllowUpdateMyProfileEmail, 0) AS AllowUpdateMyProfileEmail, " &
                    "ISNULL(LTRIM(RTRIM(HomeGraphic)), '') AS HomeGraphic, LicenseExpiration_Date, LicenseStart_Date, LicenseTermID, " &
                    "ISNULL(TwoFactorAuthentication, 0) AS TwoFactorAuthentication, " &
                    "ISNULL(HDUserSupport, 0) AS HDUserSupport, " &
                    "ISNULL(HDApplicationSupport, 0) AS HDApplicationSupport, " &
                    "MessageRetrievalFrequency, " &
                    "ISNULL(IsActive, 1) AS IsActive, r.RowVersion, ISNULL(z.TimeZoneName, '') AS TimeZoneName " &
                    "FROM dbo.FW_Registration r LEFT JOIN dbo.FW_TimeZones z ON z.TimeZoneID = r.TimeZoneID WHERE r.RegistrationID = @ID", conn)

                    cmd.Parameters.AddWithValue("@ID", registrationId)

                    Using reader = cmd.ExecuteReader()
                        If Not reader.Read() Then
                            Return Nothing
                        End If

                        Return New RegistrationRecord With {
                            .ID = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture),
                            .RegName = SafeString(reader("RegName")),
                            .Smarty_AuthID = SafeString(reader("Smarty_AuthID")),
                            .Smarty_AuthToken = SafeString(reader("Smarty_AuthToken")),
                            .Smarty_EmbeddedKey = SafeString(reader("Smarty_EmbeddedKey")),
                            .Smarty_UseEmbeddedKey = Convert.ToBoolean(reader("Smarty_UseEmbeddedKey"), CultureInfo.InvariantCulture),
                            .RegistrationTypeID = If(IsDBNull(reader("RegistrationTypeID")), 0, Convert.ToInt32(reader("RegistrationTypeID"), CultureInfo.InvariantCulture)),
                            .FormatDateID = If(IsDBNull(reader("FormatDateID")), 0, Convert.ToInt32(reader("FormatDateID"), CultureInfo.InvariantCulture)),
                            .FormatTimeID = If(IsDBNull(reader("FormatTimeID")), 0, Convert.ToInt32(reader("FormatTimeID"), CultureInfo.InvariantCulture)),
                            .TimeZoneID = If(IsDBNull(reader("TimeZoneID")), 0, Convert.ToInt32(reader("TimeZoneID"), CultureInfo.InvariantCulture)),
                            .TimeZoneName = SafeString(reader("TimeZoneName")),
                            .Address1 = SafeString(reader("Address1")),
                            .Address2 = SafeString(reader("Address2")),
                            .City = SafeString(reader("City")),
                            .State = SafeString(reader("State")),
                            .Zip = SafeString(reader("Zip")),
                            .MainFax = SafeString(reader("MainFax")),
                            .MainPhone = SafeString(reader("MainPhone")),
                            .MainEMail = SafeString(reader("MainEMail")),
                            .WebLandingPage = SafeString(reader("WebLandingPage")),
                            .AllowMultipleRoles = Convert.ToBoolean(reader("AllowMultipleRoles"), CultureInfo.InvariantCulture),
                            .AllowPasswordChangeAtLogin = Convert.ToBoolean(reader("AllowPasswordChangeAtLogin"), CultureInfo.InvariantCulture),
                            .AllowUpdateMyProfile = Convert.ToBoolean(reader("AllowUpdateMyProfile"), CultureInfo.InvariantCulture),
                            .AllowUpdateMyProfileEmail = Convert.ToBoolean(reader("AllowUpdateMyProfileEmail"), CultureInfo.InvariantCulture),
                            .HomeGraphic = SafeString(reader("HomeGraphic")),
                            .LicenseExpiration = If(IsDBNull(reader("LicenseExpiration_Date")), CType(Nothing, Date?), CType(Convert.ToDateTime(reader("LicenseExpiration_Date"), CultureInfo.InvariantCulture), Date?)),
                            .LicenseStart = If(IsDBNull(reader("LicenseStart_Date")), CType(Nothing, Date?), CType(Convert.ToDateTime(reader("LicenseStart_Date"), CultureInfo.InvariantCulture), Date?)),
                            .LicenseTermID = If(IsDBNull(reader("LicenseTermID")), 0, Convert.ToInt32(reader("LicenseTermID"), CultureInfo.InvariantCulture)),
                            .TwoFactorAuthentication = Convert.ToBoolean(reader("TwoFactorAuthentication"), CultureInfo.InvariantCulture),
                            .HDUserSupport = Convert.ToInt32(reader("HDUserSupport"), CultureInfo.InvariantCulture),
                            .HDApplicationSupport = Convert.ToInt32(reader("HDApplicationSupport"), CultureInfo.InvariantCulture),
                            .MessageRetrievalFrequency = If(IsDBNull(reader("MessageRetrievalFrequency")), CType(Nothing, Integer?), CType(Convert.ToInt32(reader("MessageRetrievalFrequency"), CultureInfo.InvariantCulture), Integer?)),
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
                Using cmd As New SqlCommand("SELECT TOP 1 ISNULL(HDUserSupport, 0), ISNULL(HDApplicationSupport, 0) FROM dbo.FW_Registration WHERE RegistrationID = @ID", conn)
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

        ''' <summary>
        ''' Creates a registration, and with it the roles and the first administrator that make it
        ''' reachable.
        '''
        ''' One transaction. A registration that exists with no roles and nobody in it cannot be
        ''' signed into and has to be finished by hand in SQL, which is how Saraland was built.
        ''' </summary>
        Public Shared Function CreateRegistration(record As RegistrationRecord,
                                                  currentUserId As Integer,
                                                  Optional administrator As RegistrationAdminRequest = Nothing) As Integer
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using tx = conn.BeginTransaction()
                    Try
                        Dim newId = InsertRegistrationRow(conn, tx, record, currentUserId)
                        If newId > 0 AndAlso administrator IsNot Nothing AndAlso administrator.IsComplete Then
                            SeedRolesFromTemplate(conn, tx, newId, currentUserId)
                            CreateRegistrationAdministrator(conn, tx, newId, administrator, currentUserId)
                        End If

                        tx.Commit()
                        Return newId
                    Catch
                        tx.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Function

        ''' <summary>
        ''' The roles a new registration starts with, copied from FW_RoleTemplate. Does nothing
        ''' when the registration already has roles, so it cannot double them.
        ''' </summary>
        Private Shared Sub SeedRolesFromTemplate(conn As SqlConnection, tx As SqlTransaction,
                                                 registrationId As Integer, currentUserId As Integer)
            If OBJECT_IDMissing(conn, tx, "dbo.FW_RoleTemplate") Then Return

            Using check As New SqlCommand("SELECT COUNT(*) FROM dbo.FW_Roles WHERE RegistrationID = @ID", conn, tx)
                check.Parameters.Add("@ID", SqlDbType.Int).Value = registrationId
                If Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) > 0 Then Return
            End Using

            Using cmd As New SqlCommand(
                "INSERT INTO dbo.FW_Roles (RegistrationID, RoleName, CA_CanChange, " &
                "Can_Create, Can_Read, Can_Update, Can_Delete, Can_Export, Can_Import, " &
                "Can_UseQBE, Can_ViewAllRecords, Can_ViewOnlyMyRecords, DisplayOrder, IsActive, " &
                "Typ_AppAdmin, Typ_CompanyAdmin, Typ_RW, Typ_RO, Typ_User, Typ_OnlyMyRecords, " &
                "CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                "SELECT @ID, t.RoleName, t.CA_CanChange, " &
                "t.Can_Create, t.Can_Read, t.Can_Update, t.Can_Delete, t.Can_Export, t.Can_Import, " &
                "t.Can_UseQBE, t.Can_ViewAllRecords, t.Can_ViewOnlyMyRecords, t.DisplayOrder, 1, " &
                "t.Typ_AppAdmin, t.Typ_CompanyAdmin, t.Typ_RW, t.Typ_RO, t.Typ_User, t.Typ_OnlyMyRecords, " &
                "@By, GETUTCDATE(), @By, GETUTCDATE() " &
                "FROM dbo.FW_RoleTemplate t ORDER BY t.ID", conn, tx)
                cmd.Parameters.Add("@ID", SqlDbType.Int).Value = registrationId
                cmd.Parameters.Add("@By", SqlDbType.Int).Value = currentUserId
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Private Shared Function OBJECT_IDMissing(conn As SqlConnection, tx As SqlTransaction, name As String) As Boolean
            Using cmd As New SqlCommand("SELECT CASE WHEN OBJECT_ID(@N, 'U') IS NULL THEN 1 ELSE 0 END", conn, tx)
                cmd.Parameters.Add("@N", SqlDbType.VarChar, 200).Value = name
                Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) = 1
            End Using
        End Function

        ''' <summary>
        ''' The registration's first person: a login, its password, an employee, and the Company
        ''' Admin role.
        '''
        ''' The role is found by its Typ_CompanyAdmin flag rather than by name - a registration may
        ''' well call it City Admin, and the flag is what the application actually tests.
        ''' </summary>
        Private Shared Sub CreateRegistrationAdministrator(conn As SqlConnection, tx As SqlTransaction,
                                                           registrationId As Integer,
                                                           administrator As RegistrationAdminRequest,
                                                           currentUserId As Integer)
            Dim newUserId = CreateLoginForEmployee(conn, tx,
                                                   administrator.UserName,
                                                   administrator.FirstName,
                                                   administrator.LastName,
                                                   registrationId,
                                                   currentUserId)
            If newUserId <= 0 Then Throw New InvalidOperationException("The sign-in for the new registration could not be created.")

            If Not WritePasswordHash(conn, tx, newUserId, administrator.TemporaryPassword, currentUserId) Then
                Throw New InvalidOperationException("The temporary password could not be stored.")
            End If

            Dim employeeId As Integer
            Using cmd As New SqlCommand(
                "INSERT INTO dbo.FW_Employees (RegistrationID, UserId, FirstName, LastName, UserName, " &
                "IsActive, DeletedFlag, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                "VALUES (@Reg, @User, @First, @Last, @Name, 1, 0, @By, GETUTCDATE(), @By, GETUTCDATE()); " &
                "SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx)
                cmd.Parameters.Add("@Reg", SqlDbType.Int).Value = registrationId
                cmd.Parameters.Add("@User", SqlDbType.Int).Value = newUserId
                cmd.Parameters.Add("@First", SqlDbType.VarChar, 100).Value = administrator.FirstName.Trim()
                cmd.Parameters.Add("@Last", SqlDbType.VarChar, 100).Value = administrator.LastName.Trim()
                cmd.Parameters.Add("@Name", SqlDbType.VarChar, 50).Value = administrator.UserName.Trim()
                cmd.Parameters.Add("@By", SqlDbType.Int).Value = currentUserId
                employeeId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using

            Using cmd As New SqlCommand(
                "INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, " &
                "IsActive, DeletedFlag, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                "SELECT TOP 1 @Reg, @Emp, r.ID, r.DisplayOrder, 1, 0, @By, GETUTCDATE(), @By, GETUTCDATE() " &
                "FROM dbo.FW_Roles r WHERE r.RegistrationID = @Reg AND ISNULL(r.Typ_CompanyAdmin, 0) = 1 " &
                "ORDER BY r.DisplayOrder, r.ID", conn, tx)
                cmd.Parameters.Add("@Reg", SqlDbType.Int).Value = registrationId
                cmd.Parameters.Add("@Emp", SqlDbType.Int).Value = employeeId
                cmd.Parameters.Add("@By", SqlDbType.Int).Value = currentUserId
                If cmd.ExecuteNonQuery() = 0 Then
                    Throw New InvalidOperationException("No Company Admin role exists for the new registration, so nobody could be put in charge of it.")
                End If
            End Using
        End Sub

        Private Shared Function InsertRegistrationRow(conn As SqlConnection, tx As SqlTransaction,
                                                      record As RegistrationRecord, currentUserId As Integer) As Integer
            Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_Registration " &
                    "(RegName, RegistrationTypeID, FormatDateID, FormatTimeID, TimeZoneID, Address1, Address2, City, State, Zip, MainFax, MainPhone, MainEMail, WebLandingPage, Smarty_AuthID, Smarty_AuthToken, Smarty_EmbeddedKey, Smarty_UseEmbeddedKey, AllowMultipleRoles, AllowPasswordChangeAtLogin, AllowUpdateMyProfile, AllowUpdateMyProfileEmail, HomeGraphic, LicenseExpiration_Date, LicenseStart_Date, LicenseTermID, TwoFactorAuthentication, MessageRetrievalFrequency, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn) " &
                    "VALUES " &
                    "(@RegName, @RegistrationTypeID, @FormatDateID, @FormatTimeID, @TimeZoneID, @Address1, @Address2, @City, @State, @Zip, @MainFax, @MainPhone, @MainEMail, @WebLandingPage, @Smarty_AuthID, @Smarty_AuthToken, @Smarty_EmbeddedKey, @Smarty_UseEmbeddedKey, @AllowMultipleRoles, @AllowPasswordChangeAtLogin, @AllowUpdateMyProfile, @AllowUpdateMyProfileEmail, @HomeGraphic, @LicenseExpiration_Date, @LicenseStart_Date, @LicenseTermID, @TwoFactorAuthentication, @MessageRetrievalFrequency, @IsActive, @CurrentUserId, GETDATE(), @CurrentUserId, GETDATE()); " &
                    "SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx)

                    cmd.Parameters.AddWithValue("@RegName", DbValue(record.RegName))
                    cmd.Parameters.AddWithValue("@RegistrationTypeID", If(record.RegistrationTypeID > 0, CType(record.RegistrationTypeID, Object), DBNull.Value))
                    ' Zero means nothing was chosen, which the foreign key can only accept as NULL,
                    ' and DisplayFormats reads a missing choice as the framework default. The page
                    ' always offers a real row, so in practice this only fires for a registration
                    ' created before the combos existed.
                    cmd.Parameters.AddWithValue("@FormatDateID", If(record.FormatDateID > 0, CType(record.FormatDateID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@FormatTimeID", If(record.FormatTimeID > 0, CType(record.FormatTimeID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@TimeZoneID", If(record.TimeZoneID > 0, CType(record.TimeZoneID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@Address1", DbValue(record.Address1))
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
                    cmd.Parameters.AddWithValue("@AllowMultipleRoles", record.AllowMultipleRoles)
                    cmd.Parameters.AddWithValue("@AllowPasswordChangeAtLogin", record.AllowPasswordChangeAtLogin)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfile", record.AllowUpdateMyProfile)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfileEmail", record.AllowUpdateMyProfileEmail)
                    cmd.Parameters.AddWithValue("@HomeGraphic", If(String.IsNullOrWhiteSpace(record.HomeGraphic), CType(DBNull.Value, Object), record.HomeGraphic.Trim()))
                    cmd.Parameters.AddWithValue("@LicenseExpiration_Date", If(record.LicenseExpiration.HasValue, CType(record.LicenseExpiration.Value.Date, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@LicenseStart_Date", If(record.LicenseStart.HasValue, CType(record.LicenseStart.Value.Date, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@LicenseTermID", If(record.LicenseTermID > 0, CType(record.LicenseTermID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@TwoFactorAuthentication", record.TwoFactorAuthentication)
                    ' Nothing chosen stays NULL rather than becoming a zero the timer would have to
                    ' treat as "never check".
                    cmd.Parameters.AddWithValue("@MessageRetrievalFrequency", If(record.MessageRetrievalFrequency.HasValue, CType(record.MessageRetrievalFrequency.Value, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@CurrentUserId", currentUserId)

                Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using
        End Function

        Public Shared Function UpdateRegistration(record As RegistrationRecord, currentUserId As Integer) As SaveResult
            If Not TableHasRowVersion("FW_Registration") OrElse record.RowVersion Is Nothing Then
                Return SaveResult.ConcurrencyUnavailable
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "UPDATE dbo.FW_Registration SET " &
                    "RegName = @RegName, " &
                    "RegistrationTypeID = @RegistrationTypeID, " &
                    "FormatDateID = @FormatDateID, " &
                    "FormatTimeID = @FormatTimeID, " &
                    "TimeZoneID = @TimeZoneID, " &
                    "Address1 = @Address1, " &
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
                    "AllowMultipleRoles = @AllowMultipleRoles, " &
                    "AllowPasswordChangeAtLogin = @AllowPasswordChangeAtLogin, " &
                    "AllowUpdateMyProfile = @AllowUpdateMyProfile, " &
                    "AllowUpdateMyProfileEmail = @AllowUpdateMyProfileEmail, " &
                    "HomeGraphic = @HomeGraphic, " &
                    "LicenseExpiration_Date = @LicenseExpiration_Date, " &
                    "LicenseStart_Date = @LicenseStart_Date, " &
                    "LicenseTermID = @LicenseTermID, " &
                    "TwoFactorAuthentication = @TwoFactorAuthentication, " &
                    "MessageRetrievalFrequency = @MessageRetrievalFrequency, " &
                    "IsActive = @IsActive, " &
                    "UpdatedBy = @CurrentUserId, " &
                    "UpdatedOn = GETDATE() " &
                    "WHERE RegistrationID = @ID AND RowVersion = @OriginalRowVersion", conn)

                    cmd.Parameters.AddWithValue("@ID", record.ID)
                    cmd.Parameters.AddWithValue("@RegName", DbValue(record.RegName))
                    cmd.Parameters.AddWithValue("@RegistrationTypeID", If(record.RegistrationTypeID > 0, CType(record.RegistrationTypeID, Object), DBNull.Value))
                    ' Zero means nothing was chosen, which the foreign key can only accept as NULL,
                    ' and DisplayFormats reads a missing choice as the framework default. The page
                    ' always offers a real row, so in practice this only fires for a registration
                    ' created before the combos existed.
                    cmd.Parameters.AddWithValue("@FormatDateID", If(record.FormatDateID > 0, CType(record.FormatDateID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@FormatTimeID", If(record.FormatTimeID > 0, CType(record.FormatTimeID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@TimeZoneID", If(record.TimeZoneID > 0, CType(record.TimeZoneID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@Address1", DbValue(record.Address1))
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
                    cmd.Parameters.AddWithValue("@AllowMultipleRoles", record.AllowMultipleRoles)
                    cmd.Parameters.AddWithValue("@AllowPasswordChangeAtLogin", record.AllowPasswordChangeAtLogin)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfile", record.AllowUpdateMyProfile)
                    cmd.Parameters.AddWithValue("@AllowUpdateMyProfileEmail", record.AllowUpdateMyProfileEmail)
                    cmd.Parameters.AddWithValue("@HomeGraphic", If(String.IsNullOrWhiteSpace(record.HomeGraphic), CType(DBNull.Value, Object), record.HomeGraphic.Trim()))
                    cmd.Parameters.AddWithValue("@LicenseExpiration_Date", If(record.LicenseExpiration.HasValue, CType(record.LicenseExpiration.Value.Date, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@LicenseStart_Date", If(record.LicenseStart.HasValue, CType(record.LicenseStart.Value.Date, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@LicenseTermID", If(record.LicenseTermID > 0, CType(record.LicenseTermID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@TwoFactorAuthentication", record.TwoFactorAuthentication)
                    ' Nothing chosen stays NULL rather than becoming a zero the timer would have to
                    ' treat as "never check".
                    cmd.Parameters.AddWithValue("@MessageRetrievalFrequency", If(record.MessageRetrievalFrequency.HasValue, CType(record.MessageRetrievalFrequency.Value, Object), DBNull.Value))
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
                    "SELECT LicenseExpiration_Date AS LicenseValue FROM dbo.FW_Registration WHERE RegistrationID = @ID", conn)
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
                    "SELECT r.RegistrationID, r.RegName, ISNULL(rt.RegTypeName, '') AS RegistrationType " &
                    "FROM dbo.FW_Registration r " &
                    "LEFT JOIN dbo.FW_RegistrationType rt ON rt.RegistrationTypeID = r.RegistrationTypeID " &
                    "ORDER BY r.RegName", conn)
                    
                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            Return table
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
                    ' The acting user, not the session's. While an administrator is viewing as
                    ' somebody else the session belongs to that person, and a row saying they did
                    ' something the administrator did is a trail that cannot be trusted about
                    ' anybody. Most callers pass no actor and fall through to here - every page
                    ' save, and the generic delete and restore - so this is what makes it honest.
                    Dim acting = SessionState.ActingUserID
                    If acting > 0 Then
                        resolvedUserId = acting
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

        ''' <summary>
        ''' The snapshot an audit row stores, with a note added when it was written while an
        ''' administrator was viewing as somebody else.
        '''
        ''' Two facts, both needed to read the row alone: who was being viewed, and which
        ''' registration they belong to. The row's UserID is the administrator - a user of another
        ''' registration entirely - so a reader in the viewed company cannot resolve that id
        ''' against their own list of people, and without the note has nothing to go on.
        '''
        ''' Prefixed rather than merged into the JSON. The snapshot is a page's own record of what
        ''' changed, in whatever shape that page uses, and parsing it to add a field would make
        ''' this depend on every page's format.
        ''' </summary>
        Private Shared Function SwitchedUserAuditNote(snapshotJson As String) As String
            If Not SwitchedUser.IsActive OrElse SwitchedUser.Original Is Nothing Then
                Return snapshotJson
            End If

            Dim session = SessionState.Current
            Dim viewedName = If(session.HasValue, session.Value.FirstLast, String.Empty)
            Dim viewedId = If(session.HasValue, session.Value.UserID, 0)
            Dim viewedRegistration = If(session.HasValue, session.Value.RegistrationName, String.Empty)

            Dim note = "[VIEWED AS " & If(String.IsNullOrWhiteSpace(viewedName), "USER", viewedName.ToUpperInvariant()) &
                       " (" & viewedId.ToString(CultureInfo.InvariantCulture) & ")" &
                       If(String.IsNullOrWhiteSpace(viewedRegistration), String.Empty, " OF " & viewedRegistration.ToUpperInvariant()) &
                       " BY " & SwitchedUser.Original.DisplayName.ToUpperInvariant() &
                       " (" & SwitchedUser.Original.UserId.ToString(CultureInfo.InvariantCulture) & ")]"

            If String.IsNullOrWhiteSpace(snapshotJson) Then
                Return note
            End If

            Return note & Environment.NewLine & snapshotJson
        End Function

        Public Shared Sub LogUpdateAudit(pageName As String,
                                         tableName As String,
                                         operationType As String,
                                         phase As String,
                                         Optional recordKey As String = "",
                                         Optional snapshotJson As String = "",
                                         Optional saveSucceeded As Boolean? = Nothing,
                                         Optional registrationId As Integer? = Nothing,
                                         Optional userId As Integer? = Nothing)
            ' Refused, not recorded. An audit row for a save that never happened is worse
            ' than no row at all - it asserts a change nobody made.
            If ReadOnlyPreview.ShouldSkip() Then Return

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
                    ' The acting user, not the session's. While an administrator is viewing as
                    ' somebody else the session belongs to that person, and a row saying they did
                    ' something the administrator did is a trail that cannot be trusted about
                    ' anybody. Most callers pass no actor and fall through to here - every page
                    ' save, and the generic delete and restore - so this is what makes it honest.
                    Dim acting = SessionState.ActingUserID
                    If acting > 0 Then
                        resolvedUserId = acting
                    End If
                End If
            End If

            ' A row written while an administrator is viewing as somebody else says so, in itself.
            ' The trail already holds a Start row naming who they became, but a row that needs a
            ' different row to be understood is a row somebody will read alone and misread - and
            ' the id on it belongs to a user of another registration, whose name a reader scoped
            ' to their own company cannot resolve at all.
            Dim auditSnapshot = SwitchedUserAuditNote(snapshotJson)

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
                        cmd.Parameters.AddWithValue("@SnapshotJson", DbValue(auditSnapshot))
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
                    "LEFT JOIN dbo.FW_Registration r ON r.RegistrationID = a.RegistrationID " &
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
                            "FROM dbo.FW_Registration r LEFT JOIN dbo.FW_TimeZones z ON z.TimeZoneID = r.TimeZoneID WHERE r.RegistrationID = @ID", conn)
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

        ''' <summary>
        ''' The registration's date and time patterns, in one round trip.
        '''
        ''' Joined rather than fetched as two lookups, and read once at login rather than when a
        ''' date is drawn. Login already makes six separate registration queries; this is not
        ''' going to be the seventh and eighth.
        '''
        ''' Empty means the registration has not chosen. DisplayFormats supplies the default -
        ''' deciding it here as well would be two answers to one question.
        ''' </summary>
        Public Shared Sub GetRegistrationDisplayFormats(registrationId As Integer,
                                                        ByRef datePattern As String,
                                                        ByRef timePattern As String)
            datePattern = String.Empty
            timePattern = String.Empty
            If registrationId <= 0 Then Return

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(d.FormatPattern, '') AS DatePattern, " &
                        "             ISNULL(t.FormatPattern, '') AS TimePattern " &
                        "FROM dbo.FW_Registration r " &
                        "LEFT JOIN dbo.FW_Format_Date d ON d.FormatDateID = r.FormatDateID " &
                        "LEFT JOIN dbo.FW_Format_Time t ON t.FormatTimeID = r.FormatTimeID " &
                        "WHERE r.RegistrationID = @ID", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = registrationId

                        Using reader = cmd.ExecuteReader()
                            If reader.Read() Then
                                datePattern = SafeString(reader("DatePattern"))
                                timePattern = SafeString(reader("TimePattern"))
                            End If
                        End Using
                    End Using
                End Using
            Catch
                ' A registration read before sql/093 has been applied has no such tables, and a
                ' login that fails because nobody has chosen a date format would be a poor trade.
                datePattern = String.Empty
                timePattern = String.Empty
            End Try
        End Sub

        ''' <summary>
        ''' Both row caps in one round trip.
        '''
        ''' Two settings, one query. GetMaxRecordsNoQBE already read this row at sign-in, and
        ''' asking a second time for the column beside the one it fetched would be a round trip
        ''' bought for nothing - a session of logging in and opening a few pages was measured at
        ''' 110 of them, which is how the caches came to exist.
        '''
        ''' The defaults are the fallbacks, not the values: 10 without criteria and 200 with them,
        ''' applied when the column is null, zero or unreadable. A registration that has never been
        ''' asked has not decided anything.
        ''' </summary>
        Public Shared Sub GetRecordCaps(registrationId As Integer,
                                        ByRef withoutCriteria As Integer,
                                        ByRef withCriteria As Integer)
            withoutCriteria = 10
            withCriteria = 200

            If registrationId <= 0 Then Return

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(MaxRecordsNoQBE, 10) AS NoQbe, " &
                        "             ISNULL(MaxRecordsWithQBE, 200) AS WithQbe " &
                        "FROM dbo.FW_Registration WHERE RegistrationID = @ID", conn)

                        cmd.Parameters.AddWithValue("@ID", registrationId)

                        Using reader = cmd.ExecuteReader()
                            If Not reader.Read() Then Return

                            Dim noQbe = Convert.ToInt32(reader("NoQbe"))
                            Dim withQbe = Convert.ToInt32(reader("WithQbe"))

                            If noQbe > 0 Then withoutCriteria = noQbe
                            If withQbe > 0 Then withCriteria = withQbe
                        End Using
                    End Using
                End Using

            Catch ex As Exception
                ' The defaults above stand. A browse page with no cap is far worse than one with a
                ' cap somebody did not choose.
                Telemetry.Error(ex, "DataAccess.GetRecordCaps", Telemetry.FaultOrigin.Swallowed)
            End Try
        End Sub
        Public Shared Function GetMaxRecordsNoQBE(registrationId As Integer) As Integer
            If registrationId <= 0 Then
                Return 10
            End If

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 ISNULL(MaxRecordsNoQBE, 10) AS MaxRecords " &
                        "FROM dbo.FW_Registration r LEFT JOIN dbo.FW_TimeZones z ON z.TimeZoneID = r.TimeZoneID WHERE r.RegistrationID = @ID", conn)
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
                        Dim unsupportedFilter As String = Nothing
                        Dim filterExpr = BuildDataViewFilterExpression(table, filters, unsupportedFilter)

                        If Not String.IsNullOrWhiteSpace(unsupportedFilter) Then
                            Throw New InvalidOperationException(unsupportedFilter)
                        End If

                        If Not String.IsNullOrWhiteSpace(filterExpr) Then
                            Try
                                Dim view As New DataView(table)
                                view.RowFilter = filterExpr
                                Dim filteredUsers = view.ToTable()
                                CarryBrowseProperties(table, filteredUsers)
                                Return filteredUsers
                            Catch ex As Exception
                                ' Never fall back to the unfiltered table. A filter that was silently
                                ' dropped returns every row, which reads as "everything matched" - the
                                ' opposite of what happened, and invisible for as long as nobody counts.
                                Throw New InvalidOperationException(
                                    "The filter could not be applied on this page: " & ex.Message &
                                    Environment.NewLine & Environment.NewLine &
                                    "FILTER: " & filterExpr, ex)
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
                            sql.Append(" AND ").Append(fieldName).Append(" ").Append(ResolveTextComparison(value, comparisonOperator).SqlOperator).Append(" @").Append(fieldName)
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
                                cmd.Parameters.AddWithValue("@" & fieldName, ResolveTextComparison(value, comparisonOperator).Pattern)
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

        ''' <summary>
        ''' Hydrates DeletedFlag for a browse result that does not select it, from the page's own
        ''' table.
        '''
        ''' The table has to be passed in. This named FW_Entity unconditionally, for every browse
        ''' page rather than only the entity ones, so a page's keys were looked up in a table it has
        ''' nothing to do with. FW_Entity 8 was soft-deleted and FW_Users 8 was not, and that was
        ''' enough to drop Alan Smith from both user access pages - the two rows shared a number and
        ''' nothing else.
        '''
        ''' No table means no answer: returning the rows unfiltered is right, because guessing a
        ''' table is exactly what caused the defect.
        '''
        ''' The DeletedFlag test is repeated here rather than left to the hydration, so a result
        ''' that already carries the column costs no primary-key lookup.
        ''' </summary>
        Private Shared Function ApplyBrowseDeletedFilterFallback(source As DataTable,
                                                                 showDeletedOnly As Boolean,
                                                                 sourceTableName As String) As DataTable
            If source Is Nothing Then Return source
            If source.Columns IsNot Nothing AndAlso source.Columns.Contains("DeletedFlag") Then
                Return ApplyDeletedFlagFilter(source, showDeletedOnly)
            End If

            Dim normalized = NormalizeTableName(sourceTableName)
            If normalized = String.Empty Then Return source

            Dim keyColumn = GetPrimaryKeyFieldName(normalized)
            If String.IsNullOrWhiteSpace(keyColumn) Then Return source

            ' PK first, because every browse query aliases its key that way. The table's own key
            ' name is the fallback, for a query that selects it plainly.
            Return ApplyDeletedFilterWithSourceHydration(source,
                                                         showDeletedOnly,
                                                         normalized,
                                                         keyColumn,
                                                         "PK",
                                                         keyColumn)
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

            Dim onPage As New HashSet(Of Integer)(ids)

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Using cmd As New SqlCommand()
                    cmd.Connection = conn

                    ' Asked the other way round on 2026-09-20, and it is not a tidy-up.
                    '
                    ' This used to send one parameter per row on the page - "WHERE key IN (@ID0,
                    ' @ID1, ... )" - which SQL Server refuses past 2,100 of them. A browse page
                    ' showing ten thousand rows therefore failed outright with "the incoming
                    ' request has too many parameters", and it failed on open rather than on a
                    ' search, so the page could not be reached at all. Found with 10,000 test
                    ' employees; it would have found a real customer the same way.
                    '
                    ' Asking which rows of the table are deleted needs no parameters and no
                    ' chunking, and the answer is intersected in memory. It is also fewer round
                    ' trips than batching would have been - one, always, rather than one per two
                    ' thousand rows.
                    '
                    ' The trade: this reads every deleted key of the table rather than only the
                    ' ones on the page. Deleted rows are the small set in every table here, and an
                    ' integer key costs four bytes - a table would need millions of deleted rows
                    ' before that mattered, and one with millions of deleted rows has a different
                    ' problem.
                    cmd.CommandText =
                        "SELECT " & sourceKeyColumnName & " FROM dbo." & sourceTableName &
                        " WHERE ISNULL(DeletedFlag, 0) = 1"

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            If reader.IsDBNull(0) Then Continue While

                            Dim deletedId = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)

                            ' Only the ones actually on the page. The caller asks "which of these
                            ' are deleted", and answering with more than it asked about would make
                            ' the set wrong for any caller that measures it.
                            If onPage.Contains(deletedId) Then result.Add(deletedId)
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
                    "SELECT UserID, RegistrationID, FirstName, LastName, FirstLast, LastFirst, Email, Phone, Address1, Address2, City, State, Zip, IsActive, SuperAdmin, AssignedManagerID, RowVersion " &
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
                                .Address1 = If(reader("Address1") Is DBNull.Value, String.Empty, reader("Address1").ToString()),
                                .Address2 = If(reader("Address2") Is DBNull.Value, String.Empty, reader("Address2").ToString()),
                                .City = If(reader("City") Is DBNull.Value, String.Empty, reader("City").ToString()),
                                .State = If(reader("State") Is DBNull.Value, String.Empty, reader("State").ToString()),
                                .Zip = If(reader("Zip") Is DBNull.Value, String.Empty, reader("Zip").ToString()),
                                .IsActive = If(reader("IsActive") Is DBNull.Value, False, CBool(reader("IsActive"))),
                                .SuperAdmin = If(reader("SuperAdmin") Is DBNull.Value, False, CBool(reader("SuperAdmin"))),
                                .AssignedManagerID = If(reader("AssignedManagerID") Is DBNull.Value, 0, CInt(reader("AssignedManagerID"))),
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
                    "WHERE r.RegistrationID = @RegistrationID AND r.IsActive = 1 " &
                    "  AND ISNULL(r.DeletedFlag, 0) = 0 "

                If userId > 0 Then
                    ' A soft deleted assignment must not keep a role out of the available list -
                    ' the user no longer holds it, so it is available again.
                    sql &= "AND NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles ur " &
                           "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                           "WHERE emp.UserId = @UserID AND ur.RoleID = r.ID " &
                           "AND ISNULL(ur.DeletedFlag, 0) = 0) "
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
                    "FROM dbo.FW_EmployeeRoles ur " &
                    "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                    "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                    "WHERE emp.UserId = @UserID " &
                    "  AND ur.RegistrationID = @RegistrationID " &
                    "  AND ISNULL(ur.IsActive, 1) = 1 " &
                    "  AND ISNULL(ur.DeletedFlag, 0) = 0 " &
                    "  AND ISNULL(r.IsActive, 1) = 1 " &
                    "  AND ISNULL(r.DeletedFlag, 0) = 0 " &
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
            Dim table As New DataTable("vw_FW_EmployeeRoles")
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT UserRoleID, RegistrationID, EmployeeID, UserId, RoleID, RoleName, DisplayOrder, IsActive, CreatedBy, CreatedOn, UpdatedBy, UpdatedOn " &
                    "FROM dbo.vw_FW_EmployeeRoles WHERE UserId = @UserID " &
                    "ORDER BY (SELECT ISNULL(r.DisplayOrder, 255) FROM dbo.FW_Roles r WHERE r.ID = vw_FW_EmployeeRoles.RoleID), RoleName", conn)
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
                    "IF NOT EXISTS (SELECT 1 FROM dbo.FW_EmployeeRoles er " &
                    "  INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = er.EmployeeID " &
                    "  WHERE emp.UserId = @UserID AND er.RoleID = @RoleID) " &
                    "INSERT INTO dbo.FW_EmployeeRoles (RegistrationID, EmployeeID, RoleID, DisplayOrder, IsActive, CreatedBy, CreatedOn) " &
                    "SELECT @RegistrationID, emp.EmployeeID, @RoleID, @DisplayOrder, 1, @CreatedBy, GETDATE() " &
                    "  FROM dbo.FW_Employees emp WHERE emp.UserId = @UserID", conn)
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
                    "DELETE er FROM dbo.FW_EmployeeRoles er " &
                    "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = er.EmployeeID " &
                    "WHERE emp.UserId = @UserID AND er.RoleID = @RoleID", conn)
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
                            "SELECT @MaxUsers = MaxUsers FROM dbo.FW_Registration WITH (UPDLOCK, HOLDLOCK) WHERE RegistrationID = @RegistrationID; " &
                            "IF ISNULL(@MaxUsers, 0) > 0 AND " &
                            "(SELECT COUNT(*) FROM dbo.FW_Users WHERE RegistrationID = @RegistrationID AND ISNULL(DeletedFlag, 0) = 0) >= @MaxUsers " &
                            "THROW 52300, 'USER LIMIT REACHED. NO ADDITIONAL USERS CAN BE CREATED FOR THIS REGISTRATION.', 1;", conn, trans)
                            limitCommand.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                            limitCommand.ExecuteNonQuery()
                        End Using

                        Using cmd As New SqlCommand(
                            "INSERT INTO dbo.FW_Users (RegistrationID, FirstName, LastName, Email, Phone, Address1, Address2, City, State, Zip, IsActive, SuperAdmin, AssignedManagerID, CreatedBy, CreatedOn) " &
                            "VALUES (@RegistrationID, @FirstName, @LastName, @Email, @Phone, @Address1, @Address2, @City, @State, @Zip, @IsActive, @SuperAdmin, @AssignedManagerID, @CreatedBy, GETDATE()); " &
                            "SELECT CAST(SCOPE_IDENTITY() as int)", conn, trans)
                            cmd.Parameters.AddWithValue("@RegistrationID", record.RegistrationID)
                            cmd.Parameters.AddWithValue("@FirstName", CType(If(String.IsNullOrWhiteSpace(record.FirstName), DBNull.Value, CObj(record.FirstName.Trim())), Object))
                            cmd.Parameters.AddWithValue("@LastName", CType(If(String.IsNullOrWhiteSpace(record.LastName), DBNull.Value, CObj(record.LastName.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Email", CType(If(String.IsNullOrWhiteSpace(record.Email), DBNull.Value, CObj(NormalizeEmailForStorage(record.Email))), Object))
                            cmd.Parameters.AddWithValue("@Phone", CType(If(String.IsNullOrWhiteSpace(record.Phone), DBNull.Value, CObj(record.Phone.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Address1", CType(If(String.IsNullOrWhiteSpace(record.Address1), DBNull.Value, CObj(record.Address1.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Address2", CType(If(String.IsNullOrWhiteSpace(record.Address2), DBNull.Value, CObj(record.Address2.Trim())), Object))
                            cmd.Parameters.AddWithValue("@City", CType(If(String.IsNullOrWhiteSpace(record.City), DBNull.Value, CObj(record.City.Trim())), Object))
                            cmd.Parameters.AddWithValue("@State", CType(If(String.IsNullOrWhiteSpace(record.State), DBNull.Value, CObj(record.State.Trim())), Object))
                            cmd.Parameters.AddWithValue("@Zip", CType(If(String.IsNullOrWhiteSpace(record.Zip), DBNull.Value, CObj(record.Zip.Trim())), Object))
                            cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                            cmd.Parameters.AddWithValue("@SuperAdmin", record.SuperAdmin)
                            cmd.Parameters.AddWithValue("@AssignedManagerID", If(record.AssignedManagerID > 0, CType(record.AssignedManagerID, Object), DBNull.Value))
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
                    "Address1 = @Address1, Address2 = @Address2, City = @City, State = @State, Zip = @Zip, " &
                    "IsActive = @IsActive, SuperAdmin = @SuperAdmin, AssignedManagerID = @AssignedManagerID, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                    "WHERE UserID = @UserID AND RowVersion = @OriginalRowVersion", conn)
                    cmd.Parameters.AddWithValue("@UserID", record.UserID)
                    cmd.Parameters.AddWithValue("@FirstName", CType(If(String.IsNullOrWhiteSpace(record.FirstName), DBNull.Value, CObj(record.FirstName.Trim())), Object))
                    cmd.Parameters.AddWithValue("@LastName", CType(If(String.IsNullOrWhiteSpace(record.LastName), DBNull.Value, CObj(record.LastName.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Email", CType(If(String.IsNullOrWhiteSpace(record.Email), DBNull.Value, CObj(NormalizeEmailForStorage(record.Email))), Object))
                    cmd.Parameters.AddWithValue("@Phone", CType(If(String.IsNullOrWhiteSpace(record.Phone), DBNull.Value, CObj(record.Phone.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Address1", CType(If(String.IsNullOrWhiteSpace(record.Address1), DBNull.Value, CObj(record.Address1.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Address2", CType(If(String.IsNullOrWhiteSpace(record.Address2), DBNull.Value, CObj(record.Address2.Trim())), Object))
                    cmd.Parameters.AddWithValue("@City", CType(If(String.IsNullOrWhiteSpace(record.City), DBNull.Value, CObj(record.City.Trim())), Object))
                    cmd.Parameters.AddWithValue("@State", CType(If(String.IsNullOrWhiteSpace(record.State), DBNull.Value, CObj(record.State.Trim())), Object))
                    cmd.Parameters.AddWithValue("@Zip", CType(If(String.IsNullOrWhiteSpace(record.Zip), DBNull.Value, CObj(record.Zip.Trim())), Object))
                    cmd.Parameters.AddWithValue("@IsActive", record.IsActive)
                    cmd.Parameters.AddWithValue("@SuperAdmin", record.SuperAdmin)
                    cmd.Parameters.AddWithValue("@AssignedManagerID", If(record.AssignedManagerID > 0, CType(record.AssignedManagerID, Object), DBNull.Value))
                    cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                    cmd.Parameters.Add("@OriginalRowVersion", SqlDbType.Timestamp).Value = record.RowVersion
                    If cmd.ExecuteNonQuery() = 0 Then
                        Return SaveResult.RecordChanged
                    End If
                End Using
            End Using
            Return SaveResult.Succeeded
        End Function

        ''' <summary>
        ''' Refreshes the table list Roles_U offers, and the caption shown against each table.
        '''
        ''' Adds what is new and re-derives every alias, but never removes: this runs on a page
        ''' load, and a page load is not the place to discover that a dropped table has taken every
        ''' role's permission for it with it. The sweep behind Update Schema does that half, where
        ''' it is asked for and reported.
        ''' </summary>
        Public Shared Sub SyncRoleSchemaWithDatabase()
            Dim syncUserId = SessionState.ActingUserID

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                Dim added As Integer = 0
                Dim removed As Integer = 0
                SyncRoleSchemaTables(conn, syncUserId, added, removed, False)

                ' The alias is derived from the table name, so a formatting change reaches every
                ' row rather than only the ones added since.
                Dim tableNames As New List(Of String)()
                Using tableCmd As New SqlCommand(
                    "SELECT name FROM sys.tables WHERE schema_id = SCHEMA_ID('dbo') " &
                    "AND (name LIKE 'FW[_]%' OR name LIKE 'AS[_]%') ORDER BY name", conn)
                    Using reader = tableCmd.ExecuteReader()
                        While reader.Read()
                            tableNames.Add(reader.GetString(0))
                        End While
                    End Using
                End Using

                For Each tableName In tableNames
                    Using cmd As New SqlCommand(
                        "UPDATE dbo.FW_RoleSchema SET Table_Alias = @TableAlias WHERE DB_Table = @DBTable", conn)
                        cmd.Parameters.AddWithValue("@DBTable", tableName)
                        cmd.Parameters.AddWithValue("@TableAlias", FormatTableNameAsAlias(tableName))
                        cmd.ExecuteNonQuery()
                    End Using
                Next
            End Using
        End Sub

        ''' <summary>
        ''' The most rows a zip search will return before it refuses and asks for a narrower one.
        ''' An empty search matches 41,725 distinct places; a state on its own matches thousands.
        ''' </summary>
        Public Const ZipCodeSearchLimit As Integer = 500

        ''' <summary>
        ''' Places matching any combination of city, state and zip - the manual alternative to
        ''' Smarty, behind the Zip Coder button.
        '''
        ''' **A city matches its aliases as well as its name.** FW_ZipCodes carries one row per
        ''' alias, so "NYC" and "Manhattan" are rows of their own against New York zips. That is
        ''' not noise to be filtered out: searching "NYC" finds 111 rows and **not one of them is
        ''' PrimaryRecord = 'P'**, so collapsing the table to its primary rows - which looks like
        ''' the obvious way to deal with 80,305 rows over 41,696 zips - would return nothing at all
        ''' for every alias anybody is likely to type.
        '''
        ''' DISTINCT does the collapsing instead, and only where it is honest: those 111 NYC rows
        ''' are 111 different Manhattan zips, not one zip repeated.
        '''
        ''' **A blank box drops its condition** rather than matching blank. Matching is exact and
        ''' case-insensitive, which is the behaviour the equivalent query in the other application
        ''' has always had - "New" finds nothing, "New York" and "NYC" both find Manhattan.
        '''
        ''' The WHERE is assembled from whichever boxes are filled; the values stay parameters.
        ''' </summary>
        Public Shared Function SearchZipCodes(city As String,
                                              state As String,
                                              zipCode As String,
                                              ByRef limitReached As Boolean) As DataTable
            Dim table As New DataTable("ZipCodeSearch")
            limitReached = False

            Dim conditions As New List(Of String)()
            Dim cityText = If(city, String.Empty).Trim()
            Dim stateText = If(state, String.Empty).Trim()
            Dim zipText = If(zipCode, String.Empty).Trim()

            If cityText <> String.Empty Then
                conditions.Add("(UPPER(City) = UPPER(@City) OR UPPER(CityAliasName) = UPPER(@City))")
            End If
            If stateText <> String.Empty Then
                conditions.Add("UPPER(State) = UPPER(@State)")
            End If
            If zipText <> String.Empty Then
                conditions.Add("ZipCode = @ZipCode")
            End If

            Dim whereClause = If(conditions.Count = 0, String.Empty, " WHERE " & String.Join(" AND ", conditions))

            ' One more than the limit, so a full page can be told from an overflowing one without
            ' a second COUNT query.
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT DISTINCT TOP " & (ZipCodeSearchLimit + 1).ToString(CultureInfo.InvariantCulture) & " " &
                    "ZipCode, ISNULL(NULLIF(LTRIM(RTRIM(CityMixedCase)), ''), City) AS City, State " &
                    "FROM dbo.FW_ZipCodes" & whereClause & " " &
                    "ORDER BY State, City, ZipCode", conn)

                    If cityText <> String.Empty Then cmd.Parameters.Add("@City", SqlDbType.NVarChar, 70).Value = cityText
                    If stateText <> String.Empty Then cmd.Parameters.Add("@State", SqlDbType.NVarChar, 4).Value = stateText
                    If zipText <> String.Empty Then cmd.Parameters.Add("@ZipCode", SqlDbType.NVarChar, 10).Value = zipText
                    cmd.CommandTimeout = 30

                    Using da As New SqlDataAdapter(cmd)
                        da.Fill(table)
                    End Using
                End Using
            End Using

            If table.Rows.Count > ZipCodeSearchLimit Then
                limitReached = True
                While table.Rows.Count > ZipCodeSearchLimit
                    table.Rows.RemoveAt(table.Rows.Count - 1)
                End While
            End If

            Return table
        End Function

        Public Shared Function GetRoleSchemaTable() As DataTable
            Dim table As New DataTable("FW_RoleSchema")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                ' Ordered by the alias, which is the column Roles_U actually shows. It was ordered
                ' by DB_Table, so the list sorted on a value nobody could see: FW_Perm_Dashboard
                ' displays as "Dashboard" and sorted under P, and every FW_ table sorted as though
                ' the prefix were part of its name.
                '
                ' Falls back to DB_Table where the alias is blank, so a row with no alias still
                ' lands somewhere predictable rather than at the top.
                Using cmd As New SqlCommand(
                    "SELECT ID, DB_Table, Table_Alias FROM dbo.FW_RoleSchema WHERE IsActive = 1 " &
                    "ORDER BY ISNULL(NULLIF(LTRIM(RTRIM(Table_Alias)), ''), DB_Table), DB_Table", conn)
                    
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
                    cmd.Parameters.AddWithValue("@CreatedBy", SessionState.ActingUserID)

                    Dim result = cmd.ExecuteScalar()
                    InvalidateRoleMetadataCache()
                    Return If(result IsNot Nothing AndAlso Not IsDBNull(result), CInt(result), 0)
                End Using
            End Using
        End Function

        ''' <summary>
        ''' What still depends on this role. Call before deleting: a role a registration relies on
        ''' for its company admin cannot be deleted at all, and a role users hold needs their
        ''' explicit agreement because the delete takes their assignment with it.
        ''' </summary>
        Public Shared Function GetRoleUsage(roleId As Integer) As RoleUsage
            Dim usage As New RoleUsage()
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ISNULL(r.RoleName, '') AS RoleName, " &
                    "(SELECT COUNT(*) FROM dbo.FW_EmployeeRoles ur WHERE ur.RoleID = @RoleID AND ISNULL(ur.IsActive, 1) = 1 AND ISNULL(ur.DeletedFlag, 0) = 0) AS UserCount, " &
                    "(SELECT COUNT(*) FROM dbo.FW_Registration g WHERE g.CompanyAdminRoleID = @RoleID) AS RegistrationCount " &
                    "FROM dbo.FW_Roles r WHERE r.ID = @RoleID", conn)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    Using reader = cmd.ExecuteReader()
                        If reader.Read() Then
                            usage.RoleName = reader("RoleName").ToString()
                            usage.UserCount = Convert.ToInt32(reader("UserCount"))
                            usage.RegistrationCount = Convert.ToInt32(reader("RegistrationCount"))
                        End If
                    End Using
                End Using
            End Using
            Return usage
        End Function

        Public Shared Sub DeleteRole(roleId As Integer, Optional updatedBy As Integer = 0)
            Using conn As New SqlConnection(ConnectionString)
                conn.Open()

                If TableHasColumn("FW_Roles", "DeletedFlag") Then
                    ' A role is not one row. Its table permissions, its field permissions and the
                    ' assignments that give it to users all go with it, in one transaction: a
                    ' partial delete would leave users holding a role that no longer exists, which
                    ' is how RoleID 16 came to have 133 permission rows and no role.
                    Dim detailsDeleted = 0
                    Dim fieldsDeleted = 0
                    Dim assignmentsDeleted = 0

                    Using trans = conn.BeginTransaction()
                        Try
                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_Roles " &
                                "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE ID = @ID", conn, trans)
                                cmd.Parameters.AddWithValue("@ID", roleId)
                                cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                cmd.ExecuteNonQuery()
                            End Using

                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_RoleDetails " &
                                "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE RoleID = @RoleID AND ISNULL(DeletedFlag, 0) = 0", conn, trans)
                                cmd.Parameters.AddWithValue("@RoleID", roleId)
                                cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                detailsDeleted = cmd.ExecuteNonQuery()
                            End Using

                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_RoleFields " &
                                "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE RoleID = @RoleID AND ISNULL(DeletedFlag, 0) = 0", conn, trans)
                                cmd.Parameters.AddWithValue("@RoleID", roleId)
                                cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                fieldsDeleted = cmd.ExecuteNonQuery()
                            End Using

                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_EmployeeRoles " &
                                "SET IsActive = 0, DeletedFlag = 1, DeletedBy = @UpdatedBy, DeletedOn = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE RoleID = @RoleID AND ISNULL(DeletedFlag, 0) = 0", conn, trans)
                                cmd.Parameters.AddWithValue("@RoleID", roleId)
                                cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                assignmentsDeleted = cmd.ExecuteNonQuery()
                            End Using

                            trans.Commit()
                        Catch
                            trans.Rollback()
                            Throw
                        End Try
                    End Using

                    LogUpdateAudit("Roles_B",
                                   "FW_Roles",
                                   "Delete",
                                   "AfterSave",
                                   roleId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson(
                                       "Soft deleted role record with " &
                                       detailsDeleted.ToString(CultureInfo.InvariantCulture) & " table permissions, " &
                                       fieldsDeleted.ToString(CultureInfo.InvariantCulture) & " field permissions and " &
                                       assignmentsDeleted.ToString(CultureInfo.InvariantCulture) & " user assignments."),
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
                    ' The restore un-cascades what the delete cascaded, in one transaction.
                    '
                    ' DeleteRole soft-deletes four tables - FW_Roles, FW_RoleDetails, FW_RoleFields
                    ' and FW_EmployeeRoles - and until 2026-09-21 this restored only the first. A
                    ' delete and restore round trip therefore lost every table permission, every
                    ' field permission and every member of the role, while the role reappeared in
                    ' the list looking perfectly normal. Found on Role 4: 3 detail rows, 38 field
                    ' rows and 3 members all left deleted behind a role that read as restored.
                    '
                    ' Matched on the role's own DeletedOn, not on RoleID alone. A blind reset would
                    ' also revive children deleted individually and deliberately before the role
                    ' went - a field permission somebody removed last week - and resurrecting a
                    ' decision is worse than leaving a row deleted. DeleteRole stamps every
                    ' cascaded child with the same SYSUTCDATETIME(), so that timestamp identifies
                    ' exactly what this delete took and nothing else.
                    '
                    ' Read before the parent is cleared, because clearing it sets DeletedOn to NULL
                    ' and the children could then match nothing.
                    Dim deletedOn As Object = Nothing
                    Using cmd As New SqlCommand("SELECT DeletedOn FROM dbo.FW_Roles WHERE ID = @ID", conn)
                        cmd.Parameters.AddWithValue("@ID", roleId)
                        deletedOn = cmd.ExecuteScalar()
                    End Using

                    Using trans = conn.BeginTransaction()
                        Try
                            Using cmd As New SqlCommand(
                                "UPDATE dbo.FW_Roles " &
                                "SET IsActive = 1, DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE ID = @ID", conn, trans)
                                cmd.Parameters.AddWithValue("@ID", roleId)
                                cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                cmd.ExecuteNonQuery()
                            End Using

                            ' No timestamp to match on means the role was deleted before this
                            ' cascade existed, or by hand. The parent is restored and the children
                            ' are left alone rather than guessed at.
                            If deletedOn IsNot Nothing AndAlso Not IsDBNull(deletedOn) Then
                                For Each childTable In {"FW_RoleDetails", "FW_RoleFields", "FW_EmployeeRoles"}
                                    If Not TableHasColumn(childTable, "DeletedFlag") Then Continue For

                                    Using cmd As New SqlCommand(
                                        "UPDATE dbo." & childTable & " " &
                                        "SET IsActive = 1, DeletedFlag = 0, DeletedBy = NULL, DeletedOn = NULL, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                        "WHERE RoleID = @RoleID AND ISNULL(DeletedFlag, 0) = 1 AND DeletedOn = @DeletedOn", conn, trans)
                                        cmd.Parameters.AddWithValue("@RoleID", roleId)
                                        cmd.Parameters.AddWithValue("@DeletedOn", deletedOn)
                                        cmd.Parameters.AddWithValue("@UpdatedBy", If(updatedBy > 0, CType(updatedBy, Object), DBNull.Value))
                                        cmd.ExecuteNonQuery()
                                    End Using
                                Next
                            End If

                            trans.Commit()
                        Catch
                            trans.Rollback()
                            Throw
                        End Try
                    End Using

                    LogUpdateAudit("Roles_B",
                                   "FW_Roles",
                                   "Restore",
                                   "AfterSave",
                                   roleId.ToString(CultureInfo.InvariantCulture),
                                   BuildSoftDeleteAuditSnapshotJson(
                                       "Restored role record, with its table permissions, field permissions and members."),
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
                    cmd.Parameters.AddWithValue("@CreatedBy", SessionState.ActingUserID)

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

        ''' <summary>
        ''' A registration's roles, for choosing one rather than for managing them.
        '''
        ''' Its own name rather than a flag on GetRolesByRegistration, which already exists twice
        ''' with different signatures - a third overload made the one-argument call ambiguous and
        ''' broke Roles_U. The two that are there are worth consolidating; that is a change for
        ''' its own day, not a side effect of this one.
        '''
        ''' Deleted and inactive roles are left out. An administration page has to show a role in
        ''' order to restore it, but offering a deleted role to somebody being given one would
        ''' hand out access through a role nobody can see.
        '''
        ''' Application Admin is left out as well. It is the role that can change every other
        ''' role, and nothing about adding an employee should be able to grant it - least of all
        ''' a list where it sits one line above the ordinary answers and is reached by a
        ''' mis-click. Granting it stays a deliberate act in Roles.
        ''' </summary>
        Public Shared Function GetSelectableRolesByRegistration(registrationId As Integer) As DataTable
            Dim table As New DataTable("FW_Roles")

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID, RegistrationID, RoleName, ISNULL(DisplayOrder, 0) AS DisplayOrder " &
                    "FROM dbo.FW_Roles " &
                    "WHERE RegistrationID = @RegistrationID " &
                    "  AND ISNULL(IsActive, 1) = 1 AND ISNULL(DeletedFlag, 0) = 0 " &
                    "  AND ISNULL(Typ_AppAdmin, 0) = 0 " &
                    "ORDER BY CASE WHEN ISNULL(DisplayOrder, 0) = 0 THEN 1 ELSE 0 END, " &
                    "         ISNULL(DisplayOrder, 255), RoleName", conn)
                    cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value = registrationId
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
                    cmd.Parameters.AddWithValue("@CreatedBy", SessionState.ActingUserID)
                    
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
                            cmd.Parameters.AddWithValue("@UpdatedBy", SessionState.ActingUserID)
                            cmd.ExecuteNonQuery()
                        End Using

                        If propagateCaptionOverride Then
                            Using propagateCommand As New SqlCommand(
                                "UPDATE dbo.FW_RoleDetails SET OverrideCaption = @TableCaption, UpdatedBy = @UpdatedBy, UpdatedOn = GETDATE() " &
                                "WHERE RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND DB_Table = @DBTable", conn, trans)
                                propagateCommand.Parameters.AddWithValue("@TableCaption", effectiveTableCaption)
                                propagateCommand.Parameters.AddWithValue("@UpdatedBy", SessionState.ActingUserID)
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
                    "FROM dbo.FW_RoleFields WHERE RoleID = @RoleID AND ISNULL(DeletedFlag, 0) = 0 " &
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
                        "(RoleDetailID, RegistrationID, RoleID, SchemaID, TableName, FieldName, FileLink, FriendlyFieldName, OverrideCaption, Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                        "VALUES (@RoleDetailID, @RegistrationID, @RoleID, @SchemaID, @TableName, @FieldName, @FileLink, @FriendlyFieldName, " &
                        "(SELECT TOP 1 OverrideCaption FROM dbo.FW_RoleFields WHERE RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND TableName = @TableName AND FieldName = @FieldName AND OverrideCaption IS NOT NULL ORDER BY UpdatedOn DESC, ID DESC), " &
                        "1, 1, 1, 0, @CreatedBy, GETDATE())", conn)
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

        ''' <summary>
        ''' What a table is granted when a role first gains it.
        ''' </summary>
        Public Enum RoleTableGrant
            ''' <summary>
            ''' The long-standing defaults: Create, Update, Delete and QBE on, and **Read off**.
            ''' That combination is odd and is not being changed here - it is what every existing
            ''' row was created with, and a table added today should match the ones added
            ''' yesterday.
            ''' </summary>
            FullAccess = 0

            ''' <summary>
            ''' A table whose only purpose is to be permitted or not: Read and nothing else. The
            ''' full defaults are actively wrong for one - they grant everything except the single
            ''' permission that decides whether the feature appears, so an administrator who had
            ''' just enabled a table would find nothing had happened.
            ''' </summary>
            ReadOnlyGate = 1

            ''' <summary>
            ''' A table that is only ever browsed: Read and QBE, which is how the page is searched,
            ''' and nothing that writes. FW_SwitchUser is the first.
            ''' </summary>
            BrowseOnly = 2
        End Enum

        ''' <summary>
        ''' Adds a table to a role, with its field rows. What it is granted is <paramref name="grant"/>.
        ''' </summary>
        Public Shared Function AddRoleTableWithFields(roleId As Integer, registrationId As Integer,
                                                      roleSchemaId As Integer, dbTable As String,
                                                      tableAlias As String, tableCaption As String,
                                                      Optional grant As RoleTableGrant = RoleTableGrant.FullAccess) As Integer
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
                            "ORDER BY RoleID), @TableCaption), @CanCreate, @CanRead, @CanUpdate, @CanDelete, @CanUseQbe, 1, @CreatedBy, GETDATE()); " &
                            "SELECT CAST(SCOPE_IDENTITY() as int)", conn, trans)

                            Dim writes = (grant = RoleTableGrant.FullAccess)
                            Dim reads = (grant <> RoleTableGrant.FullAccess)
                            cmd.Parameters.AddWithValue("@CanCreate", If(writes, 1, 0))
                            cmd.Parameters.AddWithValue("@CanRead", If(reads, 1, 0))
                            cmd.Parameters.AddWithValue("@CanUpdate", If(writes, 1, 0))
                            cmd.Parameters.AddWithValue("@CanDelete", If(writes, 1, 0))
                            ' QBE is how a browse-only page is searched at all, so it comes with
                            ' Read there. A gate table has nothing to search.
                            cmd.Parameters.AddWithValue("@CanUseQbe", If(grant = RoleTableGrant.ReadOnlyGate, 0, 1))
                            
                            cmd.Parameters.AddWithValue("@RoleID", roleId)
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            cmd.Parameters.AddWithValue("@RoleSchemaID", roleSchemaId)
                            cmd.Parameters.AddWithValue("@DBTable", dbTable)
                            cmd.Parameters.AddWithValue("@TableAlias", tableAlias)
                            cmd.Parameters.AddWithValue("@TableCaption", tableCaption)
                            cmd.Parameters.AddWithValue("@CreatedBy", SessionState.ActingUserID)
                            
                            Dim detailId = CInt(cmd.ExecuteScalar())
                            
                            ' Insert fields for this table within same transaction
                            InsertRoleFieldsWithTransaction(registrationId, roleId, roleSchemaId, dbTable, SessionState.ActingUserID, conn, trans)
                            
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
            Return pascalResult
        End Function

        Private Shared Function IsVowel(ch As Char) As Boolean
            Return "AEIOU".Contains(Char.ToUpper(ch))
        End Function

        Private Shared Function IsConsonant(ch As Char) As Boolean
            Return Char.IsLetter(ch) AndAlso Not IsVowel(ch)
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



        ''' <summary>
        ''' Field-level control attributes for a page, scoped to the active session's registration
        ''' and role. FW_RoleFields rows are per role, so without the RoleID filter a field
        ''' configured for more than one role returns duplicate rows and an arbitrary role wins.
        ''' See sql\026_control_updates_role_scope.sql.
        ''' </summary>
        ''' <summary>
        ''' One control on a page, and the table column its name maps it to.
        ''' </summary>
        Private NotInheritable Class ControlFieldLink
            Public Property ControlName As String
            Public Property LabelName As String
        End Class

        ''' <summary>
        ''' Field attributes for a page's controls, derived from the live form.
        '''
        ''' The control name carries the mapping: TextBox_Address1, on a page whose table is
        ''' FW_Users, is FW_Users.Address1. That is the convention the framework already applies
        ''' when it creates a field, so the page in front of the user *is* the mapping, and a
        ''' control added today is permission-aware today.
        '''
        ''' This replaced a join through FW_Enumerations_U - a snapshot of the page written by the
        ''' Enum button. Any control added after that snapshot was invisible to permissions with no
        ''' error anywhere: Users_AppAdmin_U had five such fields, and FW_HD_Issues_U,
        ''' FW_Registration_U and PageGeneration_U had never been enumerated at all, so field
        ''' permissions had never once applied to them. The table, the view and the Enum button are
        ''' all still in place; nothing reads them on this path.
        ''' </summary>
        Public Shared Function GetControlUpdates(form As System.Windows.Forms.Form,
                                                 pageName As String,
                                                 tableName As String) As DataTable
            Dim table As New DataTable("ControlUpdates")
            table.Columns.Add("PageName", GetType(String))
            table.Columns.Add("ControlName", GetType(String))
            table.Columns.Add("LinkedControl", GetType(String))
            table.Columns.Add("OverrideCaption", GetType(String))
            For Each flagColumn In {"CA_CanChange", "Can_Create", "Can_Read", "Can_Update",
                                    "IsRequired", "IsUnique", "Make_Invisible"}
                table.Columns.Add(flagColumn, GetType(Boolean))
            Next
            table.Columns.Add("OrderBy", GetType(Object))

            If form Is Nothing Then Return table

            Dim normalizedTable = NormalizeTableName(tableName)
            If normalizedTable = String.Empty Then Return table

            ' A control named for something that is not a column of the table maps to nothing. That
            ' is a page defect to report, not a row to apply.
            Dim columns = GetTableColumnNames(normalizedTable)
            If columns.Count = 0 Then Return table

            Dim derived As New Dictionary(Of String, ControlFieldLink)(StringComparer.OrdinalIgnoreCase)
            CollectBoundControls(form, form, normalizedTable, columns, derived)
            If derived.Count = 0 Then Return table

            Dim registrationId = If(SessionState.IsActive, SessionState.Current.Value.RegistrationID, 0)
            Dim roleId = If(SessionState.IsActive, SessionState.Current.Value.RoleID, 0)

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT FileLink, OverrideCaption, CA_CanChange, Can_Create, Can_Read, Can_Update, " &
                    "IsRequired, IsUnique, Make_Invisible, OrderBy " &
                    "FROM dbo.FW_RoleFields " &
                    "WHERE RegistrationID = @RegistrationID AND RoleID = @RoleID " &
                    "AND IsActive = 1 AND ISNULL(DeletedFlag, 0) = 0 AND TableName = @TableName", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@RoleID", roleId)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim link As ControlFieldLink = Nothing

                            ' Matched here rather than in SQL: a page declares its own table name and
                            ' the casing need not agree with the stored rows - EntityX_U says
                            ' FW_ENTITY where the rows say FW_Entity. Relying on database collation
                            ' for that would work until the day it did not.
                            If Not derived.TryGetValue(SafeString(reader("FileLink")), link) Then
                                Continue While
                            End If

                            Dim row = table.NewRow()
                            row("PageName") = pageName
                            row("ControlName") = link.ControlName
                            row("LinkedControl") = link.LabelName
                            row("OverrideCaption") = reader("OverrideCaption")
                            row("CA_CanChange") = reader("CA_CanChange")
                            row("Can_Create") = reader("Can_Create")
                            row("Can_Read") = reader("Can_Read")
                            row("Can_Update") = reader("Can_Update")
                            row("IsRequired") = reader("IsRequired")
                            row("IsUnique") = reader("IsUnique")
                            row("Make_Invisible") = reader("Make_Invisible")
                            row("OrderBy") = reader("OrderBy")
                            table.Rows.Add(row)
                        End While
                    End Using
                End Using
            End Using

            Return table
        End Function

        ''' <summary>
        ''' Walks the form for controls whose name maps them to a column of the page's table, keyed
        ''' by FileLink. A label is paired by the same convention, so an override caption lands on
        ''' the caption belonging to the field rather than on whatever was nearest.
        ''' </summary>
        Private Shared Sub CollectBoundControls(root As System.Windows.Forms.Control,
                                                container As System.Windows.Forms.Control,
                                                tableName As String,
                                                columns As HashSet(Of String),
                                                results As Dictionary(Of String, ControlFieldLink))
            If container Is Nothing OrElse container.Controls Is Nothing Then Return

            For Each ctrl As System.Windows.Forms.Control In container.Controls
                If ctrl Is Nothing Then Continue For

                Dim fieldName = BoundFieldNameFromControlName(ctrl.Name)
                If fieldName <> String.Empty AndAlso columns.Contains(fieldName) Then
                    Dim fileLink = tableName & "." & fieldName
                    If Not results.ContainsKey(fileLink) Then
                        Dim labelName = "Label_" & fieldName
                        If root.Controls.Find(labelName, True).Length = 0 Then
                            labelName = String.Empty
                        End If

                        results(fileLink) = New ControlFieldLink With {
                            .ControlName = ctrl.Name,
                            .LabelName = labelName
                        }
                    End If
                End If

                CollectBoundControls(root, ctrl, tableName, columns, results)
            Next
        End Sub

        ''' <summary>
        ''' The column a control name maps to, or empty for a control that is not field-shaped.
        ''' The prefixes match the ones ApplyControlUpdates already understands.
        ''' </summary>
        Private Shared Function BoundFieldNameFromControlName(controlName As String) As String
            Dim name = If(controlName, String.Empty).Trim()
            If name = String.Empty Then Return String.Empty

            For Each prefix In {"TextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_",
                                "NumericUpDown_", "MaskedTextBox_", "RichTextBox_"}
                If name.StartsWith(prefix, StringComparison.Ordinal) Then
                    Return name.Substring(prefix.Length)
                End If
            Next

            Return String.Empty
        End Function

        ''' <summary>
        ''' Field-shaped controls on a page that map to no column of its table.
        '''
        ''' With the mapping derived rather than stored, a control named for a column that does not
        ''' exist is the one remaining way a field can fail silently: it simply never receives a
        ''' permission, exactly as Address1 did for six weeks. Reporting it is the whole reason the
        ''' derivation is safe to rely on.
        '''
        ''' A page may legitimately carry an input that is not a column of its own table -
        ''' FW_HD_Issues_U's new-response box writes to the conversation table - so those are
        ''' declared by the page and passed in here rather than guessed at.
        ''' </summary>
        Public Shared Function FindUnmappedFieldControls(form As System.Windows.Forms.Form,
                                                         tableName As String,
                                                         declaredUnbound As HashSet(Of String)) As List(Of String)
            Dim unmapped As New List(Of String)()
            If form Is Nothing Then Return unmapped

            Dim normalizedTable = NormalizeTableName(tableName)
            If normalizedTable = String.Empty Then Return unmapped

            ' No table means no verdict. A page whose table is missing has a bigger problem, and
            ' reporting every field on it as unmapped would bury that.
            Dim columns = GetTableColumnNames(normalizedTable)
            If columns.Count = 0 Then Return unmapped

            CollectUnmappedControls(form, columns, declaredUnbound, unmapped)
            unmapped.Sort(StringComparer.OrdinalIgnoreCase)
            Return unmapped
        End Function

        Private Shared Sub CollectUnmappedControls(container As System.Windows.Forms.Control,
                                                   columns As HashSet(Of String),
                                                   declaredUnbound As HashSet(Of String),
                                                   unmapped As List(Of String))
            If container Is Nothing OrElse container.Controls Is Nothing Then Return

            For Each ctrl As System.Windows.Forms.Control In container.Controls
                If ctrl Is Nothing Then Continue For

                Dim fieldName = BoundFieldNameFromControlName(ctrl.Name)
                If fieldName <> String.Empty AndAlso
                   Not columns.Contains(fieldName) AndAlso
                   (declaredUnbound Is Nothing OrElse Not declaredUnbound.Contains(ctrl.Name)) AndAlso
                   Not unmapped.Contains(ctrl.Name) Then
                    unmapped.Add(ctrl.Name)
                End If

                CollectUnmappedControls(ctrl, columns, declaredUnbound, unmapped)
            Next
        End Sub

        ''' <summary>The column a control name maps to, for reporting. Empty if not field-shaped.</summary>
        Public Shared Function ColumnNameFromControlName(controlName As String) As String
            Return BoundFieldNameFromControlName(controlName)
        End Function

        ''' <summary>
        ''' What a column points at: the lookup table and the key it references.
        ''' </summary>
        Public NotInheritable Class ColumnRelationship
            Public Property LookupTable As String
            Public Property KeyColumn As String
        End Class

        ''' <summary>
        ''' The declared relationships of a table, keyed by the column that holds the reference.
        '''
        ''' Read from the foreign keys rather than guessed from names. A name can only be guessed
        ''' when it happens to match - GenderID to FW_Gender does, AssignedManagerID to FW_Users
        ''' never could - so a declared relationship is the only thing that finds the second kind.
        '''
        ''' Single-column keys only. A composite foreign key does not describe a lookup a combo box
        ''' can offer, so including it would propose something unbuildable.
        ''' </summary>
        Public Shared Function GetColumnRelationships(tableName As String) As Dictionary(Of String, ColumnRelationship)
            Dim relationships As New Dictionary(Of String, ColumnRelationship)(StringComparer.OrdinalIgnoreCase)
            Dim normalized = NormalizeTableName(tableName)
            If normalized = String.Empty Then Return relationships

            ' Copied out of the cache rather than handed over. Callers have always been given a
            ' dictionary of their own, and one of them could reasonably add to it; a cached instance
            ' passed out by reference would let that edit every later caller's answer.
            Dim cachedRelationships As Dictionary(Of String, ColumnRelationship) = Nothing
            SyncLock metadataCacheLock
                columnRelationshipsCache.TryGetValue(normalized, cachedRelationships)
            End SyncLock

            If cachedRelationships IsNot Nothing Then
                For Each pair In cachedRelationships
                    relationships(pair.Key) = New ColumnRelationship With {
                        .LookupTable = pair.Value.LookupTable,
                        .KeyColumn = pair.Value.KeyColumn
                    }
                Next

                Return relationships
            End If

            Dim readWithoutError As Boolean = False

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT pc.name AS FromColumn, rt.name AS LookupTable, rc.name AS KeyColumn " &
                        "FROM sys.foreign_keys fk " &
                        "JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id " &
                        "JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id " &
                        "JOIN sys.tables rt ON rt.object_id = fkc.referenced_object_id " &
                        "JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id " &
                        "WHERE fk.parent_object_id = OBJECT_ID('dbo.' + @TableName) " &
                        "AND (SELECT COUNT(*) FROM sys.foreign_key_columns x WHERE x.constraint_object_id = fk.object_id) = 1", conn)
                        cmd.Parameters.AddWithValue("@TableName", normalized)
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                relationships(SafeString(reader("FromColumn"))) = New ColumnRelationship With {
                                    .LookupTable = SafeString(reader("LookupTable")),
                                    .KeyColumn = SafeString(reader("KeyColumn"))
                                }
                            End While
                        End Using
                    End Using
                End Using

                readWithoutError = True
            Catch
                ' No relationships found means every field is offered plainly, which is how the
                ' generator behaved before this existed.
            End Try

            ' Only a completed read is remembered. A table that genuinely declares no foreign keys
            ' caches an empty answer and stops being asked; a query that failed is asked again.
            If readWithoutError Then
                Dim toCache As New Dictionary(Of String, ColumnRelationship)(StringComparer.OrdinalIgnoreCase)
                For Each pair In relationships
                    toCache(pair.Key) = New ColumnRelationship With {
                        .LookupTable = pair.Value.LookupTable,
                        .KeyColumn = pair.Value.KeyColumn
                    }
                Next

                SyncLock metadataCacheLock
                    columnRelationshipsCache(normalized) = toCache
                End SyncLock
            End If

            Return relationships
        End Function

        ''' <summary>
        ''' What a search field can be searched for, when the column points at a lookup table.
        '''
        ''' Two columns: Value, which is what the grid actually holds and therefore what a filter
        ''' has to match, and Display, which is what the user reads. A browse page filters in
        ''' memory against its own rows, so offering the label without the key would produce a
        ''' search that matches nothing.
        '''
        ''' Nothing is returned unless the column has a **declared** single-column foreign key and
        ''' the table it points at has a column worth reading. Guessing from names finds GenderID
        ''' to FW_Gender and never finds AssignedManagerID to FW_Users, which is the case that
        ''' matters.
        '''
        ''' Held for the life of the process, like the other schema facts: a lookup list changes
        ''' when somebody adds a row to it, and a browse page's search list is not where that has
        ''' to be seen first.
        ''' </summary>
        Public Shared Function GetQbeValueChoices(tableName As String, columnName As String) As DataTable
            Dim normalizedTable = NormalizeTableName(tableName)
            If normalizedTable = String.Empty OrElse String.IsNullOrWhiteSpace(columnName) Then Return Nothing

            ' The registration belongs in the key because it decides the answer. GetLookupTable
            ' scopes a lookup to the registration being worked in wherever that table carries one -
            ' FW_Gender, FW_Employees, FW_Roles and FW_Users all do - so a key of table and column
            ' alone served the first registration's list to the second for the rest of the session.
            Dim cacheKey = SessionState.WorkingRegistrationID().ToString(CultureInfo.InvariantCulture) &
                           "|" & normalizedTable & "." & columnName.Trim()
            SyncLock metadataCacheLock
                Dim cached As DataTable = Nothing
                If qbeChoiceCache.TryGetValue(cacheKey, cached) Then Return cached
            End SyncLock

            Dim choices As DataTable = Nothing

            Try
                Dim relationship As ColumnRelationship = Nothing
                If GetColumnRelationships(normalizedTable).TryGetValue(columnName.Trim(), relationship) AndAlso
                   relationship IsNot Nothing AndAlso
                   Not String.IsNullOrWhiteSpace(relationship.LookupTable) Then

                    Dim displayColumn = SuggestDisplayColumn(relationship.LookupTable)
                    If Not String.IsNullOrWhiteSpace(displayColumn) Then
                        Dim rows = GetLookupTable(relationship.LookupTable, relationship.KeyColumn, displayColumn)
                        If rows IsNot Nothing AndAlso rows.Rows.Count > 0 Then
                            choices = New DataTable("QbeChoices")
                            choices.Columns.Add("Value", GetType(String))
                            choices.Columns.Add("Display", GetType(String))

                            For Each row As DataRow In rows.Rows
                                Dim value = Convert.ToString(row(relationship.KeyColumn), CultureInfo.InvariantCulture)
                                Dim display = Convert.ToString(row(displayColumn), CultureInfo.InvariantCulture)
                                If String.IsNullOrWhiteSpace(value) Then Continue For

                                ' The key is shown beside the label because the grid shows the key:
                                ' somebody looking at a column of numbers should be able to see
                                ' which number they are choosing.
                                choices.Rows.Add(value, If(String.IsNullOrWhiteSpace(display), value, display & "  (" & value & ")"))
                            Next
                        End If
                    End If
                End If
            Catch
                ' A field with no list is a field that is typed into, which is how every field
                ' behaved before this existed.
                choices = Nothing
            End Try

            SyncLock metadataCacheLock
                qbeChoiceCache(cacheKey) = choices
            End SyncLock

            Return choices
        End Function

        ''' <summary>
        ''' The column of a lookup table most likely to be its label.
        '''
        ''' A relationship gives the key and never says which column is the name, so this is a
        ''' suggestion the user overrides. Measured against this schema it is right for FW_Gender
        ''' (GenderDescription), FW_Roles (RoleName) and FW_HD_IssueCategories (CategoryName), and
        ''' wrong for FW_Users, where the label is FirstLast - a business decision no rule derives.
        ''' </summary>
        Public Shared Function SuggestDisplayColumn(tableName As String) As String
            Dim columns = GetTableColumnList(tableName)
            If columns.Count = 0 Then Return String.Empty

            Dim skip As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
                "CreatedBy", "CreatedOn", "UpdatedBy", "UpdatedOn", "ModifiedBy", "ModifiedOn",
                "DeletedBy", "DeletedOn", "DeletedFlag", "RowVersion", "IsActive", "RegistrationID",
                "Password", "PasswordHash", "TOTPKey"
            }

            Dim textColumns = GetTextColumnMaxLengths(tableName)
            Dim candidates = columns.Where(Function(c) textColumns.ContainsKey(c) AndAlso Not skip.Contains(c)).ToList()
            If candidates.Count = 0 Then Return String.Empty

            ' A column named for the table beats column order: GenderDescription over whatever
            ' happens to be declared first.
            Dim stem = If(normalizeStem(tableName), String.Empty)
            Dim named = candidates.FirstOrDefault(Function(c) stem <> String.Empty AndAlso
                                                              c.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            If Not String.IsNullOrWhiteSpace(named) Then Return named

            Dim descriptive = candidates.FirstOrDefault(Function(c) c.EndsWith("Name", StringComparison.OrdinalIgnoreCase) OrElse
                                                                     c.EndsWith("Description", StringComparison.OrdinalIgnoreCase))
            If Not String.IsNullOrWhiteSpace(descriptive) Then Return descriptive

            Return candidates(0)
        End Function

        Private Shared Function normalizeStem(tableName As String) As String
            Dim name = NormalizeTableName(tableName)
            If name.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then name = name.Substring(3)
            Return name
        End Function

        ''' <summary>
        ''' A table's columns in their declared order, for offering a choice of them. Ordered by
        ''' column_id rather than alphabetically because that is the order someone reading the table
        ''' would expect, and it puts the key first.
        ''' </summary>
        Public Shared Function GetTableColumnList(tableName As String) As List(Of String)
            Dim facts = GetTableSchemaFacts(tableName)
            If facts Is Nothing Then
                ' An empty list means the picker offers nothing rather than the page failing.
                Return New List(Of String)()
            End If

            ' A copy, because the caller is handed a list it may reasonably sort or add to, and the
            ' cached one is shared with everything that asks after it.
            Return New List(Of String)(facts.OrderedNames)
        End Function

        ''' <summary>Column names of a table, for deciding which controls are field-shaped.</summary>
        Private Shared Function GetTableColumnNames(tableName As String) As HashSet(Of String)
            Dim facts = GetTableSchemaFacts(tableName)
            If facts Is Nothing Then
                Return New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            End If

            Return New HashSet(Of String)(facts.NameSet, StringComparer.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' The date-ish columns of a table, and which kind each is: "date", "datetime" or "time".
        '''
        ''' Read from the schema rather than guessed from the column name, so a column called
        ''' Updated is a date because it is declared one, and a column called BirthDateText is not.
        '''
        ''' The three kinds are what the page does with them: a date gets a calendar, a datetime
        ''' gets a calendar and a time, and a time gets an up-down with no calendar at all.
        ''' </summary>
        ''' <summary>
        ''' The columns of a table that accept NULL.
        '''
        ''' A date field uses this to decide whether to show its check box - the built-in way to
        ''' say "no date". Asked of the schema rather than inferred from whether the field is
        ''' Admin Required: those are different questions, and a column can be nullable in the
        ''' database while a page insists on it, or the reverse.
        ''' </summary>
        Public Shared Function GetNullableColumnNames(tableName As String) As HashSet(Of String)
            Dim results As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) Then
                Return results
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " &
                    "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName AND IS_NULLABLE = 'YES'", conn)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim columnName = reader("COLUMN_NAME").ToString().Trim()
                            If columnName <> String.Empty Then results.Add(columnName)
                        End While
                    End Using
                End Using
            End Using

            Return results
        End Function

        ''' <summary>
        ''' The bit columns of a table.
        '''
        ''' Read from the schema rather than guessed from a name beginning with Is or Allow. A
        ''' page asking somebody to type True into a box is asking for Ture, and the value that
        ''' reaches the database is then whatever the parser made of it.
        ''' </summary>
        Public Shared Function GetBitColumnNames(tableName As String) As HashSet(Of String)
            Dim results As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) Then Return results

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " &
                    "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName AND DATA_TYPE = 'bit'", conn)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim columnName = reader("COLUMN_NAME").ToString().Trim()
                            If columnName <> String.Empty Then results.Add(columnName)
                        End While
                    End Using
                End Using
            End Using

            Return results
        End Function

        Public Shared Function GetDateColumnKinds(tableName As String) As Dictionary(Of String, String)
            Dim results As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim normalizedTable = NormalizeTableName(tableName)
            If String.IsNullOrWhiteSpace(normalizedTable) Then
                Return results
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT COLUMN_NAME, DATA_TYPE " &
                    "FROM INFORMATION_SCHEMA.COLUMNS " &
                    "WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName " &
                    "AND DATA_TYPE IN ('date', 'datetime', 'datetime2', 'smalldatetime', 'time')", conn)
                    cmd.Parameters.AddWithValue("@TableName", normalizedTable)

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim columnName = reader("COLUMN_NAME").ToString().Trim()
                            Dim dataType = reader("DATA_TYPE").ToString().Trim().ToLowerInvariant()
                            If columnName = String.Empty Then Continue While

                            Select Case dataType
                                Case "date" : results(columnName) = "date"
                                Case "time" : results(columnName) = "time"
                                Case Else : results(columnName) = "datetime"
                            End Select
                        End While
                    End Using
                End Using
            End Using

            Return results
        End Function

        Public Shared Function GetTextColumnMaxLengths(tableName As String) As Dictionary(Of String, Integer)
            Dim facts = GetTableSchemaFacts(tableName)
            If facts Is Nothing Then
                Return New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            End If

            Return New Dictionary(Of String, Integer)(facts.TextMaxLengths, StringComparer.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Control name to column name for a page, derived from the live form.
        '''
        ''' Derived for the same reason GetControlUpdates is: this map decides which MaxLength a
        ''' text box gets, and read from the enumeration it was silently empty for any page whose
        ''' controls postdated the last Enum run - so an over-long value reached the database and
        ''' failed there instead of being trimmed at the control.
        ''' </summary>
        Public Shared Function GetPageControlFieldMap(form As System.Windows.Forms.Form,
                                                      pageName As String,
                                                      tableName As String) As Dictionary(Of String, String)
            Dim results As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            If form Is Nothing OrElse String.IsNullOrWhiteSpace(pageName) Then
                Return results
            End If

            Dim normalizedTable = NormalizeTableName(tableName)
            If normalizedTable = String.Empty Then
                Return results
            End If

            Dim columns = GetTableColumnNames(normalizedTable)
            If columns.Count = 0 Then
                Return results
            End If

            Dim derived As New Dictionary(Of String, ControlFieldLink)(StringComparer.OrdinalIgnoreCase)
            CollectBoundControls(form, form, normalizedTable, columns, derived)

            For Each pair In derived
                Dim mappedColumn = ExtractColumnNameFromFileLink(pair.Key, normalizedTable)
                If mappedColumn = String.Empty Then
                    Continue For
                End If

                If Not results.ContainsKey(pair.Value.ControlName) Then
                    results.Add(pair.Value.ControlName, mappedColumn)
                End If
            Next

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
                                              tableName As String,
                                              Optional isNewRecord As Boolean = False)
            Try
                Dim updates = GetControlUpdates(form, pageName, tableName)
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

                        ' App Admin required is declared on the page and painted blue. It owns the
                        ' required styling and overrides the permission styling below - and nothing
                        ' else. The override caption still applies, as does everything else on the
                        ' row.
                        Dim appAdminOwnsRequired = ShouldSkipBrRequiredStyling(form, controlName, linkedControl)

                        ' Apply required border and live validation events. A hidden field is
                        ' skipped: its border panel would be a stray visible control on a row that
                        ' otherwise has nothing left on it.
                        If isRequired AndAlso Not fieldHidden AndAlso Not appAdminOwnsRequired AndAlso Not String.IsNullOrWhiteSpace(controlName) Then

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

                        ' The override caption is text and nothing more - it is stored without an
                        ' asterisk and set here without one. The required marker and the label
                        ' colour are display decisions, made below by whichever path owns required
                        ' for this field.
                        '
                        ' The one exception is a field the page declared required: that block is
                        ' skipped for it, and replacing the label text here would drop the marker
                        ' the page put there, so it is restored.
                        If Not String.IsNullOrWhiteSpace(linkedControl) AndAlso Not String.IsNullOrWhiteSpace(overrideCaption) Then
                            Dim labelMatches = form.Controls.Find(linkedControl, True)
                            If labelMatches.Length > 0 Then
                                Dim labelCtrl = labelMatches(0)
                                labelCtrl.Text = If(appAdminOwnsRequired,
                                                    EnsureRequiredMarker(overrideCaption),
                                                    overrideCaption.Trim())
                            End If
                        End If

                        ' Add asterisk to the associated label after override caption is applied.
                        ' Skipped when App Admin owns the field: the page already marked it, and
                        ' repainting the label yellow here would take the precedence away.
                        If isRequired AndAlso Not appAdminOwnsRequired AndAlso Not String.IsNullOrWhiteSpace(controlName) Then
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
                                    labelMatches(0).BackColor = FW_Base_U.PermissionRequiredBackColor
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
        ''' <summary>
        ''' The message to show when a page is about to save a user name somebody else holds,
        ''' or empty when it is not.
        '''
        ''' This is the invariant that replaced the unique email: a user name is how somebody
        ''' signs in, and login matches it with SELECT TOP 1, so two rows holding one name means
        ''' login picks by row order. Not a rule an administrator should be able to switch off,
        ''' which is why it is here rather than in the FW_RoleFields loop.
        '''
        ''' Checked against FW_Users, whichever table the page edits. An employee's user name is
        ''' written through to their login, so a name free on FW_Employees and taken on FW_Users
        ''' is still a name this person cannot have.
        '''
        ''' Deleted accounts count. A soft-deleted row keeps its name, and handing that name to
        ''' somebody else makes the original impossible to restore.
        ''' </summary>
        Private Shared Function GetUserNameDuplicateMessage(form As System.Windows.Forms.Form,
                                                            tableName As String,
                                                            currentRecordKey As String) As String
            Dim normalizedTable = NormalizeTableName(tableName)
            If form Is Nothing Then Return String.Empty
            If Not IsUserPasswordTable(normalizedTable) AndAlso Not IsEmployeeLoginTable(normalizedTable) Then
                Return String.Empty
            End If

            Dim matches = form.Controls.Find("TextBox_UserName", True)
            If matches.Length = 0 Then Return String.Empty

            Dim typedName = If(matches(0).Text, String.Empty).Trim()
            If typedName = String.Empty Then Return String.Empty

            ' Which login this page must not compare against - its own. An employee page knows its
            ' employee, so the login is looked up from it; a user page is the login already. On a
            ' new record nothing is excluded, which is right: it is not one of them yet.
            Dim currentKey As Integer
            Integer.TryParse(If(currentRecordKey, String.Empty).Trim(), currentKey)

            Dim excludeUserId = 0
            If currentKey > 0 Then
                excludeUserId = If(IsEmployeeLoginTable(normalizedTable), LoginIdForEmployee(currentKey), currentKey)
            End If

            Dim holder = UserNameHolder(typedName, excludeUserId)
            If holder = String.Empty Then Return String.Empty

            Return "The user name " & typedName & " already belongs to " & holder & "." & Environment.NewLine &
                   "A user name is how somebody signs in. It has to be theirs alone."
        End Function

        ''' <summary>Who holds a user name, other than the account being edited, or empty.</summary>
        Private Shared Function UserNameHolder(userName As String, excludeUserId As Integer) As String
            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand(
                        "SELECT TOP 1 CASE WHEN ISNULL(DeletedFlag, 0) = 1 THEN 'a deleted account' " &
                        "            ELSE 'another account' END " &
                        "FROM dbo.FW_Users " &
                        "WHERE LOWER(LTRIM(RTRIM(ISNULL(UserName, '')))) = @UserName AND UserID <> @ExcludeUserID", conn)
                        cmd.Parameters.Add("@UserName", SqlDbType.VarChar, 50).Value = userName.Trim().ToLowerInvariant()
                        cmd.Parameters.Add("@ExcludeUserID", SqlDbType.Int).Value = excludeUserId
                        Dim result = cmd.ExecuteScalar()
                        Return If(result Is Nothing OrElse Convert.IsDBNull(result), String.Empty, Convert.ToString(result))
                    End Using
                End Using
            Catch
                ' Unreachable means unknown, and refusing a save on a check that could not run
                ' would block work for a reason nobody can see. The unique index still stands.
                Return String.Empty
            End Try
        End Function

        ''' <summary>The login an employee points at, outside any transaction.</summary>
        Private Shared Function LoginIdForEmployee(employeeId As Integer) As Integer
            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Using cmd As New SqlCommand("SELECT UserId FROM dbo.FW_Employees WHERE EmployeeID = @ID", conn)
                        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = employeeId
                        Dim result = cmd.ExecuteScalar()
                        If result Is Nothing OrElse Convert.IsDBNull(result) Then Return 0
                        Return Convert.ToInt32(result, CultureInfo.InvariantCulture)
                    End Using
                End Using
            Catch
                Return 0
            End Try
        End Function

        Public Shared Function ValidateUniqueFields(form As System.Windows.Forms.Form,
                                                    pageName As String,
                                                    tableName As String,
                                                    isNewRecord As Boolean,
                                                    currentRecordKey As String,
                                                    ByRef errorMessage As String) As Boolean
            errorMessage = String.Empty
            Dim failures As New List(Of String)()

            ' A duplicate email used to be refused here as a framework invariant, because an email
            ' was how somebody signed in. It no longer is - login matches on UserName - so two
            ' people sharing an address is ordinary rather than broken, and across registrations
            ' it always was. The rule an administrator can still switch on per field, through
            ' FW_RoleFields.IsUnique, is handled by the loop below like any other.
            '
            ' UserName is the invariant now, and it is enforced where it can be kept: in the
            ' transaction that writes it, and by a unique index behind that.
            Dim duplicateUserNameMessage = GetUserNameDuplicateMessage(form, tableName, currentRecordKey)
            If Not String.IsNullOrWhiteSpace(duplicateUserNameMessage) Then
                errorMessage = duplicateUserNameMessage
                Return False
            End If

            Try
                Dim normalizedTable = NormalizeTableName(tableName)
                If String.IsNullOrWhiteSpace(normalizedTable) Then Return True

                Dim updates = GetControlUpdates(form, pageName, normalizedTable)
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

                If labelCtrl.BackColor.ToArgb() = FW_Base_U.AppAdminRequiredBackColor.ToArgb() Then
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
        ''' The placeholder every selection combo starts on. Selecting it is selecting nothing.
        Public Const EmptyComboPlaceholder As String = "Make a Selection"

        Public Shared Function IsEmptyComboSelection(combo As System.Windows.Forms.ComboBox) As Boolean
            If combo Is Nothing OrElse combo.SelectedIndex < 0 OrElse String.IsNullOrWhiteSpace(combo.Text) Then
                Return True
            End If

            ' A combo filled with plain items has no SelectedValue, so the bound-combo rules below
            ' would call every selection empty. Judge it on what is selected instead: a real
            ' selection that is not the placeholder is a value.
            If combo.DataSource Is Nothing Then
                Return String.Equals(combo.Text.Trim(), EmptyComboPlaceholder, StringComparison.OrdinalIgnoreCase)
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
                                ' A picker never reaches MinDate, so that alone tests nothing. It
                                ' says "no date" one of two ways: by unticking its own check box,
                                ' or - where a required field cannot offer a tick that contradicts
                                ' it - by showing nothing at all.
                                If dtp.ShowCheckBox Then
                                    isEmpty = Not dtp.Checked
                                Else
                                    isEmpty = dtp.Value = dtp.MinDate OrElse
                                              (dtp.Format = System.Windows.Forms.DateTimePickerFormat.Custom AndAlso
                                               String.IsNullOrWhiteSpace(dtp.CustomFormat))
                                End If
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

        ''' <summary>The ids of the roles flagged as Application Admin for one registration.</summary>
        ''' <remarks>
        ''' Keyed on Typ_AppAdmin rather than on the role's name. A role can be renamed, and a name
        ''' match would then quietly stop hiding it - which is the failure that matters here, since
        ''' it fails open.
        '''
        ''' Errors are not swallowed. A visibility rule that cannot be evaluated must not quietly
        ''' return an empty set, because an empty set means nothing is hidden.
        ''' </remarks>
        Public Shared Function GetAppAdminRoleIds(registrationId As Integer) As HashSet(Of Integer)
            Dim roleIds As New HashSet(Of Integer)()
            If registrationId <= 0 Then
                Return roleIds
            End If

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT ID FROM dbo.FW_Roles " &
                    "WHERE RegistrationID = @RegistrationID AND ISNULL(Typ_AppAdmin, 0) = 1", conn)

                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            If Not reader.IsDBNull(0) Then
                                roleIds.Add(reader.GetInt32(0))
                            End If
                        End While
                    End Using
                End Using
            End Using

            Return roleIds
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
                    "SELECT TOP 1 emp.UserId " &
                    "FROM dbo.FW_EmployeeRoles ur " &
                    "INNER JOIN dbo.FW_Employees emp ON emp.EmployeeID = ur.EmployeeID " &
                    "INNER JOIN dbo.FW_Roles r ON r.ID = ur.RoleID " &
                    "INNER JOIN dbo.FW_Users u ON u.UserID = emp.UserId " &
                    "WHERE ur.RegistrationID = @RegistrationID " &
                    "  AND r.RegistrationID = @RegistrationID " &
                    "  AND ISNULL(r.Typ_CompanyAdmin, 0) = 1 " &
                    "  AND ISNULL(r.IsActive, 0) = 1 " &
                    "  AND ISNULL(ur.IsActive, 1) = 1 " &
                    "  AND ISNULL(u.IsActive, 1) = 1 " &
                    "ORDER BY ISNULL(ur.DisplayOrder, 0), ur.EmployeeID", conn)
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
                            "(RegistrationID, RoleID, RoleDetailID, SchemaID, TableName, FieldName, FileLink, FriendlyFieldName, OverrideCaption, " &
                            "Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                            "VALUES (@RegistrationID, @RoleID, @RoleDetailID, @SchemaID, @TableName, @FieldName, @FileLink, @FriendlyFieldName, " &
                            "(SELECT TOP 1 OverrideCaption FROM dbo.FW_RoleFields WHERE RegistrationID = @RegistrationID AND SchemaID = @SchemaID AND TableName = @TableName AND FieldName = @FieldName AND OverrideCaption IS NOT NULL ORDER BY UpdatedOn DESC, ID DESC), " &
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

        ''' <summary>
        ''' Brings one role and table back in line with the database.
        '''
        ''' The single-pair entry point, for the Add button in Roles_U. It opens a connection and
        ''' hands straight to the core, which is the only place the rules live.
        ''' </summary>
        Public Shared Function SyncRoleFieldsWithSchema(
            schemaId As Integer,
            tableName As String,
            registrationId As Integer,
            roleId As Integer,
            updatedBy As Integer,
            ByRef insertedCount As Integer,
            ByRef deletedCount As Integer,
            ByRef repairedCount As Integer,
            Optional writeDebugLog As Boolean = True) As Boolean

            insertedCount = 0
            deletedCount = 0
            repairedCount = 0

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    SyncRoleFieldsCore(conn, updatedBy, schemaId, roleId,
                                       insertedCount, deletedCount, repairedCount)
                End Using

                Return True
            Catch ex As Exception
                If writeDebugLog Then
                    Try
                        System.IO.File.AppendAllText(
                            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sync_debug.log"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") &
                            " SyncRoleFieldsWithSchema failed for schema " & schemaId.ToString(CultureInfo.InvariantCulture) &
                            ", role " & roleId.ToString(CultureInfo.InvariantCulture) & ": " & ex.Message & Environment.NewLine)
                    Catch telemetryEx As Exception
                        Telemetry.Error(telemetryEx, "DataAccess.SyncRoleFieldsWithSchema")
                    End Try
                End If

                Return False
            End Try
        End Function

        ''' <summary>
        ''' The rules, in four statements and one pass.
        '''
        ''' Scope is optional: zero for both means every role and every table, which is what the
        ''' Update Schema tile and the startup drift watch ask for. This was written per role until
        ''' 2026-09-16, opening its own connection and running a statement per field - forty-one
        ''' connections and several hundred round trips to establish that nothing had changed. The
        ''' work is identical; the difference is whether the server is asked once or once per field.
        '''
        ''' The order matters. Obsolete rows go first, so the repair does not bother with rows that
        ''' are about to disappear. The revive runs before the insert, so a column that comes back
        ''' brings its old configuration rather than arriving as a fresh inactive row. Only then is
        ''' what is genuinely missing added.
        '''
        ''' FriendlyFieldName is the one thing SQL cannot produce - it comes from
        ''' DisplayNameFormatter - so missing rows are read back, named here, and written in
        ''' batches rather than one statement each.
        ''' </summary>
        Private Shared Sub SyncRoleFieldsCore(conn As SqlConnection,
                                              updatedBy As Integer,
                                              scopeSchemaId As Integer,
                                              scopeRoleId As Integer,
                                              ByRef insertedCount As Integer,
                                              ByRef deletedCount As Integer,
                                              ByRef repairedCount As Integer)

            Dim scoped = scopeSchemaId > 0 AndAlso scopeRoleId > 0
            Dim fieldScope = If(scoped, " AND rf.SchemaID = @ScopeSchemaID AND rf.RoleID = @ScopeRoleID ", String.Empty)

            ' 1. Rows whose column is gone. Physically, including any already flagged deleted: the
            ' row governs a column that no longer exists, so there is nothing left for it to say.
            deletedCount = ExecuteScoped(conn,
                "DELETE rf FROM dbo.FW_RoleFields rf " &
                "JOIN dbo.FW_RoleSchema s ON s.ID = rf.SchemaID " &
                "WHERE OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "  AND COL_LENGTH('dbo.' + s.DB_Table, rf.FieldName) IS NULL " & fieldScope,
                scopeSchemaId, scopeRoleId)

            ' 2. TableName and FileLink, the two columns that decide whether a row ever reaches a
            ' control. FW_RoleSchema.DB_Table is the authority for both.
            repairedCount = ExecuteScoped(conn,
                "UPDATE rf SET rf.TableName = s.DB_Table, " &
                "              rf.FileLink = s.DB_Table + '.' + rf.FieldName, " &
                "              rf.UpdatedBy = @UpdatedBy, rf.UpdatedOn = GETDATE() " &
                "FROM dbo.FW_RoleFields rf " &
                "JOIN dbo.FW_RoleSchema s ON s.ID = rf.SchemaID " &
                "WHERE OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "  AND (ISNULL(rf.TableName, '') <> s.DB_Table " &
                "       OR ISNULL(rf.FileLink, '') <> s.DB_Table + '.' + rf.FieldName) " & fieldScope,
                scopeSchemaId, scopeRoleId, updatedBy)

            ' 3. A soft-deleted row whose column exists again comes back with its settings. Only a
            ' role soft-delete can leave one, now that step 1 removes obsolete rows outright.
            insertedCount = ExecuteScoped(conn,
                "UPDATE rf SET rf.DeletedFlag = 0, rf.DeletedBy = NULL, rf.DeletedOn = NULL, " &
                "              rf.TableName = s.DB_Table, " &
                "              rf.FileLink = s.DB_Table + '.' + rf.FieldName, " &
                "              rf.UpdatedBy = @UpdatedBy, rf.UpdatedOn = GETDATE() " &
                "FROM dbo.FW_RoleFields rf " &
                "JOIN dbo.FW_RoleSchema s ON s.ID = rf.SchemaID " &
                "WHERE ISNULL(rf.DeletedFlag, 0) = 1 " &
                "  AND OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "  AND COL_LENGTH('dbo.' + s.DB_Table, rf.FieldName) IS NOT NULL " & fieldScope,
                scopeSchemaId, scopeRoleId, updatedBy)

            ' 4. What is missing, read in one pass and written in batches.
            insertedCount += InsertMissingRoleFields(conn, ReadMissingRoleFields(conn, scopeSchemaId, scopeRoleId), updatedBy)
        End Sub

        ''' <summary>
        ''' Every column a role should have a permission row for and does not.
        '''
        ''' The audit stamps and the primary key are left out, as they always have been: nobody is
        ''' asked whether a role may read CreatedOn, and a key is not a field anybody grants. The
        ''' same exclusions appear in HasSchemaDrifted, which must agree with this or the drift it
        ''' reports would survive a sweep and be found again on the next startup.
        ''' </summary>
        Private Shared Function ReadMissingRoleFields(conn As SqlConnection,
                                                      scopeSchemaId As Integer,
                                                      scopeRoleId As Integer) As List(Of MissingRoleField)
            Dim missing As New List(Of MissingRoleField)()
            Dim scoped = scopeSchemaId > 0 AndAlso scopeRoleId > 0
            Dim detailScope = If(scoped, " AND rd.SchemaID = @ScopeSchemaID AND rd.RoleID = @ScopeRoleID ", String.Empty)

            Using cmd As New SqlCommand(
                "SELECT rd.RoleID, rd.SchemaID, r.RegistrationID, rd.ID AS RoleDetailID, s.DB_Table, c.name AS ColumnName " &
                "FROM dbo.FW_RoleDetails rd " &
                "JOIN dbo.FW_RoleSchema s ON s.ID = rd.SchemaID " &
                "JOIN dbo.FW_Roles r ON r.ID = rd.RoleID " &
                "CROSS APPLY (SELECT c2.name FROM sys.columns c2 " &
                "              WHERE c2.object_id = OBJECT_ID('dbo.' + s.DB_Table)) c " &
                "WHERE ISNULL(rd.DeletedFlag, 0) = 0 AND ISNULL(r.DeletedFlag, 0) = 0 " &
                "  AND OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "  AND c.name NOT IN ('CreatedBy', 'CreatedOn', 'UpdatedBy', 'UpdatedOn') " &
                "  AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic " &
                "                    JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id " &
                "                    JOIN sys.columns pc ON pc.object_id = ic.object_id AND pc.column_id = ic.column_id " &
                "                   WHERE i.is_primary_key = 1 " &
                "                     AND ic.object_id = OBJECT_ID('dbo.' + s.DB_Table) " &
                "                     AND pc.name = c.name) " &
                "  AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleFields rf " &
                "                   WHERE rf.RoleID = rd.RoleID AND rf.SchemaID = rd.SchemaID " &
                "                     AND rf.FieldName = c.name AND ISNULL(rf.DeletedFlag, 0) = 0) " &
                detailScope &
                "ORDER BY r.RegistrationID, rd.RoleID, s.DB_Table, c.name", conn)

                If scoped Then
                    cmd.Parameters.AddWithValue("@ScopeSchemaID", scopeSchemaId)
                    cmd.Parameters.AddWithValue("@ScopeRoleID", scopeRoleId)
                End If

                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        missing.Add(New MissingRoleField With {
                            .RoleId = Convert.ToInt32(reader("RoleID"), CultureInfo.InvariantCulture),
                            .SchemaId = Convert.ToInt32(reader("SchemaID"), CultureInfo.InvariantCulture),
                            .RegistrationId = Convert.ToInt32(reader("RegistrationID"), CultureInfo.InvariantCulture),
                            .RoleDetailId = Convert.ToInt32(reader("RoleDetailID"), CultureInfo.InvariantCulture),
                            .TableName = SafeString(reader("DB_Table")),
                            .FieldName = SafeString(reader("ColumnName"))
                        })
                    End While
                End Using
            End Using

            Return missing
        End Function

        ''' <summary>A permission row a role should have and does not.</summary>
        Private Class MissingRoleField
            Public Property RoleId As Integer
            Public Property SchemaId As Integer
            Public Property RegistrationId As Integer
            Public Property RoleDetailId As Integer
            Public Property TableName As String
            Public Property FieldName As String
        End Class

        ''' <summary>One statement, carrying only the parameters it actually names.</summary>
        Private Shared Function ExecuteScoped(conn As SqlConnection, sql As String,
                                              scopeSchemaId As Integer, scopeRoleId As Integer,
                                              Optional updatedBy As Integer = -1) As Integer
            Using cmd As New SqlCommand(sql, conn)
                If sql.Contains("@ScopeSchemaID") Then
                    cmd.Parameters.AddWithValue("@ScopeSchemaID", scopeSchemaId)
                    cmd.Parameters.AddWithValue("@ScopeRoleID", scopeRoleId)
                End If
                If sql.Contains("@UpdatedBy") Then
                    cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)
                End If

                Return cmd.ExecuteNonQuery()
            End Using
        End Function

        ''' <summary>
        ''' Writes the new permission rows, a hundred at a time.
        '''
        ''' Batched rather than one statement per row because the friendly name is computed here,
        ''' and batched rather than all at once because a parameterised statement is capped at 2100
        ''' parameters - eight per row puts the ceiling near 260, and a hundred leaves room.
        '''
        ''' Every row arrives inactive. A column appearing in the database is not a decision to let
        ''' anybody see it.
        ''' </summary>
        Private Shared Function InsertMissingRoleFields(conn As SqlConnection,
                                                        missing As List(Of MissingRoleField),
                                                        updatedBy As Integer) As Integer
            If missing Is Nothing OrElse missing.Count = 0 Then Return 0

            Const batchSize As Integer = 100
            Dim written = 0
            Dim index = 0

            While index < missing.Count
                Dim batch = missing.Skip(index).Take(batchSize).ToList()
                Dim values As New List(Of String)()

                Using cmd As New SqlCommand("", conn)
                    cmd.Parameters.AddWithValue("@UpdatedBy", updatedBy)

                    For position = 0 To batch.Count - 1
                        Dim row = batch(position)
                        Dim tag = position.ToString(CultureInfo.InvariantCulture)

                        values.Add("(@Reg" & tag & ", @Role" & tag & ", @Detail" & tag & ", @Schema" & tag & ", " &
                                   "@Table" & tag & ", @Field" & tag & ", @Link" & tag & ", @Friendly" & tag & ", " &
                                   "(SELECT TOP 1 OverrideCaption FROM dbo.FW_RoleFields " &
                                   " WHERE RegistrationID = @Reg" & tag & " AND SchemaID = @Schema" & tag &
                                   "   AND TableName = @Table" & tag & " AND FieldName = @Field" & tag &
                                   "   AND OverrideCaption IS NOT NULL ORDER BY UpdatedOn DESC, ID DESC), " &
                                   "1, 1, 1, 0, @UpdatedBy, GETDATE())")

                        cmd.Parameters.AddWithValue("@Reg" & tag, row.RegistrationId)
                        cmd.Parameters.AddWithValue("@Role" & tag, row.RoleId)
                        cmd.Parameters.AddWithValue("@Detail" & tag, row.RoleDetailId)
                        cmd.Parameters.AddWithValue("@Schema" & tag, row.SchemaId)
                        cmd.Parameters.AddWithValue("@Table" & tag, row.TableName)
                        cmd.Parameters.AddWithValue("@Field" & tag, row.FieldName)
                        cmd.Parameters.AddWithValue("@Link" & tag, row.TableName & "." & row.FieldName)
                        cmd.Parameters.AddWithValue("@Friendly" & tag, FormatFieldName(row.FieldName))
                    Next

                    cmd.CommandText =
                        "INSERT INTO dbo.FW_RoleFields " &
                        "(RegistrationID, RoleID, RoleDetailID, SchemaID, TableName, FieldName, FileLink, " &
                        " FriendlyFieldName, OverrideCaption, Can_Create, Can_Read, Can_Update, IsActive, CreatedBy, CreatedOn) " &
                        "VALUES " & String.Join(", ", values)

                    written += cmd.ExecuteNonQuery()
                End Using

                index += batchSize
            End While

            Return written
        End Function

        ''' <summary>
        ''' The result of a whole-database schema sweep: what changed, and where.
        ''' </summary>
        Public Class SchemaSweepResult
            Public Property RolesVisited As Integer
            Public Property Inserted As Integer
            Public Property Deleted As Integer
            Public Property Repaired As Integer
            Public Property TablesAdded As Integer
            Public Property TablesRemoved As Integer
            Public Property Failures As New List(Of String)

            Public ReadOnly Property ChangedAnything As Boolean
                Get
                    Return Inserted > 0 OrElse Deleted > 0 OrElse Repaired > 0 OrElse
                           TablesAdded > 0 OrElse TablesRemoved > 0
                End Get
            End Property
        End Class


        ''' <summary>
        ''' Whether anything about the schema has moved away from what the role fields record.
        '''
        ''' One round trip, answering only "is there work to do". The three counts are the three
        ''' things the sweep fixes, asked the way it decides them: FW_RoleSchema.DB_Table is the
        ''' authority for a table's name, and the columns the sync leaves alone - the audit stamps
        ''' and the primary key - are excluded here too. Counting a column the sweep would not add
        ''' would report drift that surviving a sweep cannot clear, and every startup would find it
        ''' again.
        ''' </summary>
        Public Shared Function HasSchemaDrifted() As Boolean
            Const sql As String =
                "SELECT " &
                " (SELECT COUNT(*) FROM sys.tables t " &
                "  WHERE t.schema_id = SCHEMA_ID('dbo') " &
                "    AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleSchema s WHERE s.DB_Table = t.name)) " &
                "+ (SELECT COUNT(*) FROM dbo.FW_RoleSchema s " &
                "  WHERE OBJECT_ID('dbo.' + s.DB_Table) IS NULL) " &
                "+ (SELECT COUNT(*) FROM dbo.FW_RoleFields rf " &
                "   JOIN dbo.FW_RoleSchema s ON s.ID = rf.SchemaID " &
                "  WHERE ISNULL(rf.DeletedFlag, 0) = 0 " &
                "    AND OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "    AND COL_LENGTH('dbo.' + s.DB_Table, rf.FieldName) IS NULL) " &
                "+ (SELECT COUNT(*) FROM dbo.FW_RoleFields rf " &
                "   JOIN dbo.FW_RoleSchema s ON s.ID = rf.SchemaID " &
                "  WHERE OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "    AND (ISNULL(rf.TableName, '') <> s.DB_Table " &
                "         OR ISNULL(rf.FileLink, '') <> s.DB_Table + '.' + rf.FieldName)) " &
                "+ (SELECT COUNT(*) FROM dbo.FW_RoleDetails rd " &
                "   JOIN dbo.FW_RoleSchema s ON s.ID = rd.SchemaID " &
                "   JOIN dbo.FW_Roles r ON r.ID = rd.RoleID " &
                "   CROSS APPLY (SELECT c.name FROM sys.columns c " &
                "                 WHERE c.object_id = OBJECT_ID('dbo.' + s.DB_Table)) c " &
                "  WHERE ISNULL(rd.DeletedFlag, 0) = 0 AND ISNULL(r.DeletedFlag, 0) = 0 " &
                "    AND OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL " &
                "    AND c.name NOT IN ('CreatedBy', 'CreatedOn', 'UpdatedBy', 'UpdatedOn') " &
                "    AND NOT EXISTS (SELECT 1 FROM sys.index_columns ic " &
                "                      JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id " &
                "                      JOIN sys.columns pc ON pc.object_id = ic.object_id AND pc.column_id = ic.column_id " &
                "                     WHERE i.is_primary_key = 1 " &
                "                       AND ic.object_id = OBJECT_ID('dbo.' + s.DB_Table) " &
                "                       AND pc.name = c.name) " &
                "    AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleFields rf " &
                "                     WHERE rf.RoleID = rd.RoleID AND rf.SchemaID = rd.SchemaID " &
                "                       AND rf.FieldName = c.name AND ISNULL(rf.DeletedFlag, 0) = 0))"

            Using conn As New SqlConnection(ConnectionString)
                conn.Open()
                Using cmd As New SqlCommand(sql, conn)
                    cmd.CommandTimeout = 30
                    Dim value = cmd.ExecuteScalar()
                    If value Is Nothing OrElse Convert.IsDBNull(value) Then Return False
                    Return Convert.ToInt32(value, CultureInfo.InvariantCulture) > 0
                End Using
            End Using
        End Function

        ''' <summary>
        ''' Brings FW_RoleSchema - the list of tables a role can be given - in step with the
        ''' database, and says how many appeared and how many went.
        '''
        ''' This ran only when Roles_U was opened, and only ever added. A new table was therefore
        ''' invisible until somebody happened to open that page, and a dropped one left its row
        ''' behind for good, along with every permission keyed to it - rows governing a table that
        ''' no longer exists, which the removal sweep in CLAUDE.md has to be run by hand to find.
        '''
        ''' A dropped table takes its permissions with it, physically, for the same reason a
        ''' dropped column does: what is left cannot be granted, cannot be revoked, and cannot be
        ''' seen. FW_RoleFields goes first, then FW_RoleDetails, then the schema row itself.
        ''' </summary>
        Private Shared Sub SyncRoleSchemaTables(conn As SqlConnection, updatedBy As Integer,
                                                ByRef addedCount As Integer, ByRef removedCount As Integer,
                                                Optional removeMissing As Boolean = True)
            addedCount = 0
            removedCount = 0

            ' Every table in dbo, whoever owns it. The prefix list was FW_ and AS_, so a CTY_ table
            ' was never registered here - and a table with no FW_RoleSchema row can be granted to
            ' nobody, which would have read as a permissions fault rather than a missing sweep.
            ' A table that should not be offered is switched off on its row instead.
            Dim missing As New List(Of String)()
            Using cmd As New SqlCommand(
                "SELECT t.name FROM sys.tables t " &
                "WHERE t.schema_id = SCHEMA_ID('dbo') " &
                "  AND NOT EXISTS (SELECT 1 FROM dbo.FW_RoleSchema s WHERE s.DB_Table = t.name) " &
                "ORDER BY t.name", conn)
                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        missing.Add(reader.GetString(0))
                    End While
                End Using
            End Using

            For Each tableName In missing
                Using cmd As New SqlCommand(
                    "INSERT INTO dbo.FW_RoleSchema (DB_Table, Table_Alias, IsActive, CreatedBy, CreatedOn) " &
                    "VALUES (@DBTable, @TableAlias, 1, @CreatedBy, GETDATE())", conn)
                    cmd.Parameters.AddWithValue("@DBTable", tableName)
                    cmd.Parameters.AddWithValue("@TableAlias", FormatTableNameAsAlias(tableName))
                    cmd.Parameters.AddWithValue("@CreatedBy", updatedBy)
                    addedCount += cmd.ExecuteNonQuery()
                End Using
            Next

            If Not removeMissing Then Return

            Dim gone As New List(Of Integer)()
            Using cmd As New SqlCommand(
                "SELECT s.ID FROM dbo.FW_RoleSchema s WHERE OBJECT_ID('dbo.' + s.DB_Table) IS NULL", conn)
                Using reader = cmd.ExecuteReader()
                    While reader.Read()
                        gone.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture))
                    End While
                End Using
            End Using

            For Each schemaId In gone
                For Each statement In {"DELETE FROM dbo.FW_RoleFields WHERE SchemaID = @SchemaID",
                                       "DELETE FROM dbo.FW_RoleDetails WHERE SchemaID = @SchemaID",
                                       "DELETE FROM dbo.FW_RoleSchema WHERE ID = @SchemaID"}
                    Using cmd As New SqlCommand(statement, conn)
                        cmd.Parameters.AddWithValue("@SchemaID", schemaId)
                        cmd.ExecuteNonQuery()
                    End Using
                Next

                removedCount += 1
            Next
        End Sub
        ''' <summary>
        ''' Brings every role's permissions back in line with the database, for every table in
        ''' every registration.
        '''
        ''' One connection and a handful of statements. The tables come first - a table dropped
        ''' since the last sweep takes its permissions with it, and the field pass must not then
        ''' work from a schema row that has just gone.
        '''
        ''' RolesVisited is what the sweep covered rather than how many times it asked the server:
        ''' the field pass is set-based and does not iterate roles at all.
        ''' </summary>
        Public Shared Function SyncAllRoleFieldsWithSchema(updatedBy As Integer) As SchemaSweepResult
            Dim result As New SchemaSweepResult()

            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()

                    Dim tablesAdded As Integer = 0
                    Dim tablesRemoved As Integer = 0
                    SyncRoleSchemaTables(conn, updatedBy, tablesAdded, tablesRemoved)
                    result.TablesAdded = tablesAdded
                    result.TablesRemoved = tablesRemoved

                    Dim inserted As Integer = 0
                    Dim deleted As Integer = 0
                    Dim repaired As Integer = 0
                    SyncRoleFieldsCore(conn, updatedBy, 0, 0, inserted, deleted, repaired)

                    result.Inserted = inserted
                    result.Deleted = deleted
                    result.Repaired = repaired
                    result.RolesVisited = CountRoleTablePairs(conn)
                End Using
            Catch ex As Exception
                result.Failures.Add(ex.Message)
            End Try

            Return result
        End Function

        ''' <summary>How many role and table pairs the sweep covers, for the report.</summary>
        Private Shared Function CountRoleTablePairs(conn As SqlConnection) As Integer
            Using cmd As New SqlCommand(
                "SELECT COUNT(*) FROM dbo.FW_RoleDetails rd " &
                "JOIN dbo.FW_RoleSchema s ON s.ID = rd.SchemaID " &
                "JOIN dbo.FW_Roles r ON r.ID = rd.RoleID " &
                "WHERE ISNULL(rd.DeletedFlag, 0) = 0 AND ISNULL(r.DeletedFlag, 0) = 0 " &
                "  AND OBJECT_ID('dbo.' + s.DB_Table) IS NOT NULL", conn)
                Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
            End Using
        End Function

        ''' <summary>
        ''' Builds the client-side filter for a page whose SQL came from FW_Pages.
        '''
        ''' <paramref name="unsupportedMessage"/> is set when a filter cannot be expressed here at
        ''' all, rather than merely matching nothing. The caller must surface it: a filter that was
        ''' quietly dropped returns every row, which reads as "everything matched" and is the
        ''' opposite of what happened.
        ''' </summary>
        Private Shared Function BuildDataViewFilterExpression(table As DataTable,
                                                              filters As Dictionary(Of String, String),
                                                              ByRef unsupportedMessage As String) As String
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
                Dim unsupported As String = Nothing
                Dim expr = BuildSingleColumnFilterExpr(col, fieldName, comparisonOperator, rawValue, unsupported)

                If Not String.IsNullOrWhiteSpace(unsupported) Then
                    unsupportedMessage = unsupported
                    Return String.Empty
                End If

                If Not String.IsNullOrEmpty(expr) Then parts.Add(expr)
            Next

            Return String.Join(" AND ", parts)
        End Function

        Private Shared Function BuildSingleColumnFilterExpr(col As DataColumn,
                                                            fieldName As String,
                                                            op As QbeComparisonOperator,
                                                            value As String,
                                                            ByRef unsupportedMessage As String) As String
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

            ' Date field. A search row offers a day and no time, and the column may carry one.
            ' The day becomes a pair of boundaries rather than an equality - QbeDateBounds holds
            ' the rule and the reasoning. Until 2026-09-21 a date fell through to the text branch
            ' below and was compared as a quoted string, which found the rows saved at midnight
            ' and silently missed every other one.
            If col.DataType Is GetType(Date) Then
                Dim chosen As Date
                If Not QbeDateBounds.TryParseFilterValue(value, chosen) Then
                    unsupportedMessage =
                        fieldName & " needs a date." & Environment.NewLine &
                        Environment.NewLine &
                        "'" & value & "' could not be read as one. Pick the date from the calendar " &
                        "rather than typing it."
                    Return String.Empty
                End If

                Return QbeDateBounds.ToDataViewExpression(fieldName, QbeDateBounds.Resolve(op, chosen))
            End If

            ' Text field. Same decision as the SQL paths make, so a page filtered here and a page
            ' filtered in the database answer a typed wildcard the same way.
            Dim comparison = ResolveTextComparison(value, op)

            If comparison.IsPatternMatch AndAlso HasInnerWildcard(comparison.Pattern) Then
                unsupportedMessage =
                    "A wildcard in the middle of a value cannot be used on this page." & Environment.NewLine &
                    Environment.NewLine &
                    "This page filters its rows after loading them, and that filter allows % only at the " &
                    "start or the end of a value. Try " & fieldName & " with the % at one end."
                Return String.Empty
            End If

            Dim escaped = comparison.Pattern.Replace("'", "''")

            Select Case comparison.SqlOperator
                Case "LIKE"     : Return quotedField & " LIKE '" & escaped & "'"
                Case "NOT LIKE" : Return "NOT (" & quotedField & " LIKE '" & escaped & "')"
                Case "<>"       : Return quotedField & " <> '" & escaped & "'"
                Case Else       : Return quotedField & " = '" & escaped & "'"
            End Select
        End Function

        Public Shared Function GetSavedQbes(registrationId As Integer, userId As Integer, tableContext As String) As List(Of SavedQbeRecord)
            Dim results As New List(Of SavedQbeRecord)()
            Try
                Using conn As New SqlConnection(ConnectionString)
                    conn.Open()
                    Dim sql As String =
                        "SELECT SavedQbeID, RegistrationID, UserID, QbeName, IsCompanyWide, TableContext, QbeData " &
                        "FROM dbo.FW_SavedQBE " &
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
            Catch telemetryEx As Exception
                ' Swallowed so the page opens with no saved searches rather than not at all, but
                ' recorded: from the outside this is indistinguishable from having saved none, and
                ' "my searches disappeared" is not a report anybody can act on without this.
                Telemetry.Error(telemetryEx, "DataAccess.GetSavedQbes")
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
                    "SELECT SavedQbeID FROM dbo.FW_SavedQBE " &
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
                        "UPDATE dbo.FW_SavedQBE SET IsCompanyWide = @IsCompanyWide, QbeData = @QbeData, UpdatedOn = GETDATE() " &
                        "WHERE SavedQbeID = @SavedQbeID"
                    Using cmd As New SqlCommand(updateSql, conn)
                        cmd.Parameters.AddWithValue("@IsCompanyWide", If(record.IsCompanyWide, 1, 0))
                        cmd.Parameters.AddWithValue("@QbeData", record.QbeData)
                        cmd.Parameters.AddWithValue("@SavedQbeID", existingId)
                        cmd.ExecuteNonQuery()
                    End Using
                Else
                    Dim insertSql As String =
                        "INSERT INTO dbo.FW_SavedQBE (RegistrationID, UserID, QbeName, IsCompanyWide, TableContext, QbeData) " &
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
                    "SELECT TOP 1 TableContext, QbeName FROM dbo.FW_SavedQBE WHERE SavedQbeID = @SavedQbeID AND UserID = @UserID", conn)
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
                    "UPDATE dbo.FW_SavedQBE " &
                    "SET DeletedFlag = 1, DeletedBy = @DeletedBy, DeletedOn = SYSUTCDATETIME() " &
                    "WHERE SavedQbeID = @SavedQbeID AND UserID = @UserID AND ISNULL(DeletedFlag, 0) = 0", conn)
                    cmd.Parameters.AddWithValue("@SavedQbeID", savedQbeId)
                    ' UserID says whose saved search it is; DeletedBy says who deleted it. One id
                    ' answered both until 2026-09-17, which named the viewed user as the deleter
                    ' of their own search while an administrator was doing it.
                    cmd.Parameters.AddWithValue("@UserID", ownerUserId)
                    cmd.Parameters.AddWithValue("@DeletedBy", SessionState.ActingUserID)
                    rowsAffected = cmd.ExecuteNonQuery()
                End Using

                If rowsAffected > 0 Then
                    LogUpdateAudit(lookupTable,
                                   "FW_SavedQBE",
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
