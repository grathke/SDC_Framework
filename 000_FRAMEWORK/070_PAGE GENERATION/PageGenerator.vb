Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.Json
Imports System.Text.RegularExpressions
Imports System.Security.Cryptography

Namespace SDC.Framework
    Public NotInheritable Class PageGenerationResult
        Public ReadOnly Property CreatedFiles As IReadOnlyList(Of String)
        Public ReadOnly Property SkippedFiles As IReadOnlyList(Of String)
        Public ReadOnly Property Errors As IReadOnlyList(Of String)

        Public ReadOnly Property Succeeded As Boolean
            Get
                Return Errors.Count = 0
            End Get
        End Property

        Public Sub New(createdFiles As IEnumerable(Of String), skippedFiles As IEnumerable(Of String), errors As IEnumerable(Of String))
            Me.CreatedFiles = createdFiles.ToList().AsReadOnly()
            Me.SkippedFiles = skippedFiles.ToList().AsReadOnly()
            Me.Errors = errors.ToList().AsReadOnly()
        End Sub
    End Class

    Public Enum PageAction
        None = 0
        Insert = 1
        AlreadyCurrent = 2
        UpdateSql = 3
        ReplaceRow = 4
    End Enum

    ''' <summary>
    ''' The validated request plus the exact source that would be written. Generate writes this;
    ''' Preview shows it without touching disk or the database.
    ''' </summary>
    Public NotInheritable Class PageGenerationPlan
        Public ReadOnly Property Errors As New List(Of String)()
        Public Property GenerateBrowsePage As Boolean
        Public Property GenerateMaintenancePage As Boolean
        Public Property BrowsePageName As String = String.Empty
        Public Property MaintenancePageName As String = String.Empty
        ''' <summary>
        ''' The folder of the owner these pages belong to - "000_FRAMEWORK", "100_CTY" - which
        ''' decides both the prefix on their names and the 999_GENERATED they are written to.
        ''' </summary>
        Public Property OwnerFolder As String = String.Empty

        Public Property BrowsePath As String = String.Empty
        Public Property MaintenancePath As String = String.Empty
        Public Property BrowseSource As String = String.Empty
        Public Property MaintenanceSource As String = String.Empty

        ''' <summary>
        ''' The half of a maintenance page that belongs to whoever is building it - the file the
        ''' generator writes once and never reads or rewrites again.
        '''
        ''' A generated page used to be one file, which meant anything added to it by hand was
        ''' lost the next time the fields changed. The triple confirmation before regenerating
        ''' existed to warn about exactly that. Split in two, the warning is not needed for this
        ''' half: the generator cannot reach it.
        ''' </summary>
        Public Property MaintenanceCompanionPath As String = String.Empty
        Public Property MaintenanceCompanionSource As String = String.Empty
        Public Property TableName As String = String.Empty
        Public Property PrimaryKey As String = String.Empty
        Public Property TableAlias As String = String.Empty
        Public Property BrowseSql As String = String.Empty
        Public Property MenuCaller As String = String.Empty
        Public Property IconFileName As String = String.Empty
        Public Property CreatedBy As Integer

        ''' <summary>Whether the generated browse page shows the Hot Fields strip.</summary>
        Public Property UseHotFields As Boolean

        ''' <summary>
        ''' Which fields the Hot Fields panel shows. Empty means every field, which is what the panel
        ''' does for a page nobody has curated.
        ''' </summary>
        Public Property HotFields As New List(Of String)()

        Public ReadOnly Property IsValid As Boolean
            Get
                Return Errors.Count = 0
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Result of compiling the planned source against the built application assembly. This is the
    ''' only check that proves the emitted pages compile; the preview text alone does not.
    ''' </summary>
    Public NotInheritable Class PageCompileResult
        Public ReadOnly Property Succeeded As Boolean
        Public ReadOnly Property Messages As IReadOnlyList(Of String)

        Public Sub New(succeeded As Boolean, messages As IEnumerable(Of String))
            Me.Succeeded = succeeded
            Me.Messages = messages.ToList().AsReadOnly()
        End Sub
    End Class

    Public NotInheritable Class PageGenerator
        Private Sub New()
        End Sub

        Public Shared Function Generate(requestId As Integer,
                        workspaceRoot As String,
                        Optional overwriteExistingPages As Boolean = False) As PageGenerationResult
            Dim created As New List(Of String)()
            Dim skipped As New List(Of String)()

            Dim plan = BuildPlan(requestId, workspaceRoot)
            If Not plan.IsValid Then
                Return New PageGenerationResult(created, skipped, plan.Errors)
            End If

            Dim errors As New List(Of String)()

            ' Generating a page writes source files to disk and creates or overwrites an FW_Pages
            ' row, including that page's SQL. Until this pair existed the audit trail recorded only
            ' that someone had edited the generation request - never that a page was actually
            ' generated, and never that an existing page's SQL had been replaced.
            Dim auditKey = If(String.IsNullOrWhiteSpace(plan.BrowsePageName), plan.MaintenancePageName, plan.BrowsePageName)
            DataAccess.LogUpdateAudit("PageGenerator", "FW_Pages", "Generate", "BeforeSave",
                                      auditKey, BuildGenerationSnapshot(plan, overwriteExistingPages, Nothing, Nothing, Nothing),
                                      Nothing, Nothing, plan.CreatedBy)

            If plan.GenerateBrowsePage Then
                If WriteGeneratedPage(plan.BrowsePath, plan.BrowseSource, overwriteExistingPages, created, skipped) Then
                    If Not SaveBrowseBaseline(requestId, plan.BrowseSource, errors) Then
                        errors.Add("The generated browse source baseline could not be saved.")
                    End If
                End If
            End If
            If plan.GenerateMaintenancePage Then
                ' The companion goes down only when there is nothing there, or when what is
                ' there is a whole page from before the split - which would otherwise sit beside
                ' the generated half declaring the same class twice.
                WriteCompanionPage(plan.MaintenanceCompanionPath, plan.MaintenanceCompanionSource, created, skipped)

                If WriteGeneratedPage(plan.MaintenancePath, plan.MaintenanceSource, overwriteExistingPages, created, skipped) Then
                    If Not SaveMaintenanceBaseline(requestId, plan.MaintenanceSource, errors) Then
                        errors.Add("The generated maintenance source baseline could not be saved.")
                    End If
                End If
            End If

            If plan.GenerateBrowsePage Then
                Select Case ClassifyPageAction(plan.BrowsePageName, plan.TableName, plan.BrowseSql)
                    Case PageAction.Insert
                        If DataAccess.UpsertPageRecord(0, plan.BrowsePageName, plan.TableName, plan.TableAlias, plan.BrowseSql, plan.CreatedBy) Then
                            created.Add("FW_Pages:" & plan.BrowsePageName)
                        Else
                            errors.Add("The generated browse SQL could not be registered in FW_Pages.")
                        End If
                    Case PageAction.AlreadyCurrent
                        skipped.Add("FW_Pages:" & plan.BrowsePageName)
                    Case PageAction.UpdateSql
                        If DataAccess.UpdatePageSql(plan.BrowsePageName, plan.BrowseSql) Then
                            created.Add("FW_Pages SQL UPDATED:" & plan.BrowsePageName)
                        Else
                            errors.Add("The existing FW_Pages SQL could not be updated for " & plan.BrowsePageName & ".")
                        End If
                    Case Else
                        If DataAccess.UpsertPageRecord(0, plan.BrowsePageName, plan.TableName, plan.TableAlias, plan.BrowseSql, plan.CreatedBy) Then
                            created.Add("FW_Pages UPDATED:" & plan.BrowsePageName)
                        Else
                            errors.Add("The existing FW_Pages row could not be updated for " & plan.BrowsePageName & ".")
                        End If
                End Select
            End If

            ' The running page reads FW_Pages.UseHotFields, not the request, so the answer is
            ' written onto the page's own row here. Done after registration so the row exists, and
            ' reported rather than silent - a page that quietly lacked its Hot Fields button would
            ' look like the feature was broken rather than unticked.
            If plan.GenerateBrowsePage AndAlso Not String.IsNullOrWhiteSpace(plan.BrowsePageName) Then
                If DataAccess.SetPageUsesHotFields(plan.BrowsePageName, plan.UseHotFields) Then
                    If plan.UseHotFields Then
                        created.Add("HOT FIELDS ENABLED:" & plan.BrowsePageName)
                    End If
                Else
                    errors.Add("The Hot Fields setting could not be written for " & plan.BrowsePageName & ".")
                End If

                ' The field list is written on generation and not on save, because an App Admin owns
                ' it once the page exists - ticking on the panel is how it changes thereafter, and a
                ' save here would wipe that with no warning. Generating is the deliberate act that
                ' takes it back to what the request says.
                If DataAccess.SavePageHotFields(plan.BrowsePageName, plan.HotFields, plan.CreatedBy) Then
                    If plan.HotFields.Count > 0 Then
                        created.Add("HOT FIELDS SET:" & plan.BrowsePageName & " (" & plan.HotFields.Count.ToString() & " fields)")
                    End If
                Else
                    errors.Add("The Hot Fields selection could not be written for " & plan.BrowsePageName & ".")
                End If
            End If

            ' Before placing anything, and only ever reporting: a button this page already has
            ' somewhere else is about to become a second way in, and nothing here will remove it.
            If plan.GenerateBrowsePage Then
                Try
                    ReportButtonsOnOtherSurfaces(workspaceRoot, plan.BrowsePageName, plan.MenuCaller, skipped)
                Catch ex As Exception
                    skipped.Add("Could not check the other surfaces for an existing button: " & ex.Message)
                End Try
            End If

            If plan.GenerateBrowsePage AndAlso IsDashboardCaller(plan.MenuCaller) Then
                Try
                    EnsureDashboardIcon(workspaceRoot, plan.MenuCaller, plan.TableName, plan.IconFileName, plan.BrowsePageName, plan.MaintenancePageName, plan.TableAlias, created, skipped, errors)
                Catch ex As Exception
                    errors.Add("ICON WARNING: " & ex.Message)
                End Try
            ElseIf plan.GenerateBrowsePage AndAlso IsMainMenuCaller(plan.MenuCaller) Then
                Dim before = errors.Count
                Try
                    EnsureMainMenuTile(workspaceRoot, plan.BrowsePageName, plan.TableName, plan.IconFileName, plan.TableAlias, created, skipped, errors)
                Catch ex As Exception
                    errors.Add("MAIN MENU WARNING: " & ex.Message)
                End Try

                ' The ribbon was full, or could not be written to. The page still needs a way in, so
                ' it goes to the App Admin dashboard and the report says so - rather than leaving a
                ' generated page reachable from nowhere, which is what naming Main Menu did before
                ' this branch existed.
                If errors.Count > before Then
                    Try
                        EnsureDashboardIcon(workspaceRoot, "Dashboard_Application", plan.TableName, plan.IconFileName, plan.BrowsePageName, plan.MaintenancePageName, plan.TableAlias, created, skipped, errors)
                        created.Add("PLACED ON THE APP ADMIN DASHBOARD INSTEAD: " & plan.BrowsePageName)
                    Catch ex As Exception
                        errors.Add("ICON WARNING: the App Admin dashboard fallback also failed - " & ex.Message)
                    End Try
                End If
            End If

            DataAccess.LogUpdateAudit("PageGenerator", "FW_Pages", "Generate", "AfterSave",
                                      auditKey, BuildGenerationSnapshot(plan, overwriteExistingPages, created, skipped, errors),
                                      errors.Count = 0, Nothing, plan.CreatedBy)

            Return New PageGenerationResult(created, skipped, errors)
        End Function

        ''' <summary>
        ''' The generation snapshot, in the flat string map FW_Base_U uses, so the audit page can
        ''' diff a Before against an After the same way it does for an ordinary save.
        ''' </summary>
        Private Shared Function BuildGenerationSnapshot(plan As PageGenerationPlan,
                                                        overwriteExistingPages As Boolean,
                                                        created As List(Of String),
                                                        skipped As List(Of String),
                                                        errors As List(Of String)) As String
            Dim snapshot As New SortedDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            snapshot("BrowsePageName") = If(plan.BrowsePageName, String.Empty)
            snapshot("MaintenancePageName") = If(plan.MaintenancePageName, String.Empty)
            snapshot("TableName") = If(plan.TableName, String.Empty)
            snapshot("TableAlias") = If(plan.TableAlias, String.Empty)
            snapshot("BrowsePath") = If(plan.BrowsePath, String.Empty)
            snapshot("MaintenancePath") = If(plan.MaintenancePath, String.Empty)
            snapshot("GenerateBrowsePage") = plan.GenerateBrowsePage.ToString()
            snapshot("GenerateMaintenancePage") = plan.GenerateMaintenancePage.ToString()
            snapshot("OverwriteExistingPages") = overwriteExistingPages.ToString()
            snapshot("BrowseSql") = If(plan.BrowseSql, String.Empty)

            ' Present only on the After row, which is what makes the Delta pane show the work done
            ' rather than repeating the request.
            If created IsNot Nothing Then
                snapshot("Created") = String.Join(" | ", created)
            End If
            If skipped IsNot Nothing Then
                snapshot("Skipped") = String.Join(" | ", skipped)
            End If
            If errors IsNot Nothing Then
                snapshot("Errors") = String.Join(" | ", errors)
            End If

            Return JsonSerializer.Serialize(snapshot)
        End Function

        ' Builds the same plan Generate would act on, without writing a file, a database row or a
        ' dashboard icon. Use this to show exactly what generation would produce.
        Public Shared Function Preview(requestId As Integer, workspaceRoot As String) As PageGenerationPlan
            Return BuildPlan(requestId, workspaceRoot)
        End Function

        ' Single owner of request validation and source emission. Generate and Preview both go
        ' through here so a preview can never disagree with what generation writes.
        Private Shared Function BuildPlan(requestId As Integer, workspaceRoot As String) As PageGenerationPlan
            Dim plan As New PageGenerationPlan()

            If requestId <= 0 Then
                plan.Errors.Add("A saved Page Generation request is required.")
                Return plan
            End If
            If String.IsNullOrWhiteSpace(workspaceRoot) OrElse Not Directory.Exists(workspaceRoot) Then
                plan.Errors.Add("The workspace folder could not be found.")
                Return plan
            End If

            Dim request = DataAccess.GetPageGenerationById(requestId)
            If request Is Nothing Then
                plan.Errors.Add("The Page Generation request could not be found.")
                Return plan
            End If

            ' Who the pages belong to, which decides both their prefix and where they are written.
            '
            ' Two ways this fails, and both are said plainly rather than guessed past. Without a
            ' repository under the working directory there are no owner folders to choose from -
            ' the application is running from bin, or served from a deploy folder - and generation
            ' would write source files somewhere nobody will ever compile them. Without an owner on
            ' the request there is no prefix and no destination, and picking one would name every
            ' table and page in this request after a guess.
            Dim owners = PageOwners.All(workspaceRoot)
            If owners.Count = 0 Then
                plan.Errors.Add("Pages cannot be generated from here: no owner folders were found under " &
                                workspaceRoot & ". Run the application from the repository.")
                Return plan
            End If

            Dim ownerFolder = If(request.Table.Columns.Contains("Owner"), DbText(request("Owner")), String.Empty)
            Dim owner = PageOwners.ByFolder(workspaceRoot, ownerFolder)
            If owner Is Nothing Then
                plan.Errors.Add(If(String.IsNullOrWhiteSpace(ownerFolder),
                                   "This request has no owner. Choose who the pages belong to before generating.",
                                   "This request names the owner folder '" & ownerFolder &
                                   "', which no longer exists. Choose an owner before generating."))
                Return plan
            End If

            plan.OwnerFolder = owner.FolderName
            plan.TableName = DbText(request("UnderlyingTableName"))
            plan.BrowsePageName = NormalizeGeneratedPageName(DbText(request("BrowsePageName")), "_B", owner.Prefix)
            plan.MaintenancePageName = NormalizeGeneratedPageName(DbText(request("MaintenancePageName")), "_U", owner.Prefix)
            plan.BrowseSql = DbText(request("BrowseSql"))
            plan.MenuCaller = DbText(request("MenuCaller"))
            plan.IconFileName = If(request.Table.Columns.Contains("IconFileName"), DbText(request("IconFileName")), String.Empty)
            plan.GenerateBrowsePage = ReadGenerationFlag(request, "GenerateBrowsePage", True)
            plan.GenerateMaintenancePage = ReadGenerationFlag(request, "GenerateMaintenancePage", True)
            ' The request's caption where it has one, otherwise derived from the table as before.
            ' Deriving every time is what made a chosen caption temporary: UpsertPageRecord writes
            ' Table_Alias on each run, so the derived name was stamped back over anything anybody
            ' had renamed the page to, and only when they next generated it.
            Dim requestedAlias = If(request.Table.Columns.Contains("TableAlias"), DbText(request("TableAlias")).Trim(), String.Empty)
            plan.TableAlias = If(requestedAlias <> String.Empty, requestedAlias, DeriveTableAlias(plan.TableName))
            plan.CreatedBy = If(request.Table.Columns.Contains("CreatedBy") AndAlso Not request.IsNull("CreatedBy"), Convert.ToInt32(request("CreatedBy")), 0)

            Dim browseFields = ParseFields(DbText(request("BrowseFields")))
            Dim maintenanceFields = ParseFields(DbText(request("MaintenanceFields")))
            Dim lookupFields = ParseLookupFields(DbText(request("LookupFields")), plan.Errors)
            Dim requiredFields = ParseFields(DbText(request("AdminRequiredFields")))
            ' Guarded like the other columns added after the table was in use: a request read
            ' before sql/088 has been applied has no column map, which is exactly the same thing
            ' as an empty one - a single-column page.
            Dim columnTwoFields = ParseFields(If(request.Table.Columns.Contains("Column2Fields"), DbText(request("Column2Fields")), String.Empty))
            Dim useQbeOnly = ReadGenerationFlag(request, "UseQbeOnly", False)
            ' Ticking a field is asking for the panel, so it turns it on by itself. Two switches that
            ' can disagree is how somebody picks their fields, forgets the checkbox, and gets no Hot
            ' Fields button at all - with nothing on screen to say the ticks were the wrong half.
            plan.HotFields = ParseFields(If(request.Table.Columns.Contains("HotFields"), DbText(request("HotFields")), String.Empty))
            plan.UseHotFields = ReadGenerationFlag(request, "UseHotFields", False) OrElse plan.HotFields.Count > 0

            If Not plan.GenerateBrowsePage AndAlso Not plan.GenerateMaintenancePage Then
                plan.Errors.Add("At least one page target must be selected.")
            End If
            If plan.GenerateBrowsePage Then ValidateName(plan.BrowsePageName, "Browse page name", "_B", plan.Errors)
            If plan.GenerateMaintenancePage Then ValidateName(plan.MaintenancePageName, "Maintenance page name", "_U", plan.Errors)
            If String.IsNullOrWhiteSpace(plan.TableName) Then plan.Errors.Add("The underlying table is required.")
            If plan.GenerateBrowsePage AndAlso browseFields.Count = 0 Then plan.Errors.Add("At least one _B field is required.")
            ' Counted past the placeholders. A request holding nothing but blank lines would
            ' otherwise generate a page of empty rows and no fields at all.
            If plan.GenerateMaintenancePage AndAlso
               Not maintenanceFields.Any(Function(field) Not IsPlaceholderField(field)) Then
                plan.Errors.Add("At least one _U field is required.")
            End If
            If plan.GenerateBrowsePage AndAlso String.IsNullOrWhiteSpace(plan.BrowseSql) Then plan.Errors.Add("Browse SQL is required.")

            ' Only an explicit alias named PK is accepted as the row key - there is no fallback to
            ' ID or <Table>ID. Without it the generated page opens with a missing-key warning and
            ' Read, Update and Delete hidden, which is far cheaper to catch here than at runtime.
            If plan.GenerateBrowsePage AndAlso Not String.IsNullOrWhiteSpace(plan.BrowseSql) AndAlso
               Not Regex.IsMatch(plan.BrowseSql, "\bAS\s+PK\b", RegexOptions.IgnoreCase) Then
                plan.Errors.Add("Browse SQL must alias the primary key as PK, for example " &
                                "'" & plan.TableName & ".SomeID AS PK'. Without it the browse page cannot open a record.")
            End If
            If Not plan.IsValid Then Return plan

            Dim schemaFields = DataAccess.GetTableFieldNames(plan.TableName)
            If schemaFields.Count = 0 Then
                plan.Errors.Add("The underlying dbo table does not exist or has no columns: " & plan.TableName)
                Return plan
            End If
            plan.PrimaryKey = DataAccess.GetPrimaryKeyFieldName(plan.TableName)
            If String.IsNullOrWhiteSpace(plan.PrimaryKey) Then plan.Errors.Add("The underlying table does not have a primary key: " & plan.TableName)
            If plan.GenerateBrowsePage Then ValidateFields(browseFields, schemaFields, "_B", plan.Errors)
            If plan.GenerateMaintenancePage Then
                ' Placeholders name no column and must not be measured against the schema. They
                ' stay in the list every line below this reads - only the existence check skips
                ' them.
                ValidateFields(maintenanceFields.Where(Function(field) Not IsPlaceholderField(field)), schemaFields, "_U", plan.Errors)
                ValidateFields(lookupFields.Select(Function(item) item.FieldName), maintenanceFields, "Lookup", plan.Errors)
                ValidateFields(requiredFields, maintenanceFields, "Admin Required", plan.Errors)
            End If
            If Not plan.IsValid Then Return plan

            plan.BrowsePath = GeneratedPagePath(workspaceRoot, plan.BrowsePageName, plan.OwnerFolder)
            ' The companion is the page's real name and the anchor for finding it; the generated
            ' half sits beside it with .Generated before the extension. Looking the companion up
            ' first is what lets a filed page stay where somebody put it.
            plan.MaintenanceCompanionPath = GeneratedPagePath(workspaceRoot, plan.MaintenancePageName, plan.OwnerFolder)
            plan.MaintenancePath = GeneratedHalfPath(plan.MaintenanceCompanionPath)

            If plan.GenerateBrowsePage Then
                plan.BrowseSource = BuildBrowseSource(plan.BrowsePageName,
                                                      plan.TableName,
                                                      useQbeOnly,
                                                      plan.GenerateMaintenancePage)
            End If
            If plan.GenerateMaintenancePage Then
                plan.MaintenanceCompanionSource = BuildMaintenanceCompanionSource(plan.MaintenancePageName)
                plan.MaintenanceSource = BuildMaintenanceSource(plan.MaintenancePageName,
                                                                plan.TableName,
                                                                plan.PrimaryKey,
                                                                maintenanceFields,
                                                                requiredFields,
                                                                lookupFields,
                                                                columnTwoFields)
            End If

            Return plan
        End Function

        ' Decides what generation would do to FW_Pages. Generate performs the action and
        ' Preview describes it, so the two cannot drift apart.
        Private Shared Function ClassifyPageAction(browsePageName As String, tableName As String, browseSql As String) As PageAction
            Dim existingPage = DataAccess.GetPageMetadata(browsePageName)
            If existingPage Is Nothing Then Return PageAction.Insert
            If Not String.Equals(DbText(existingPage("DB_Table")).Trim(), tableName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return PageAction.ReplaceRow
            End If
            If String.Equals(DbText(existingPage("Table_SQL")).Trim(), browseSql.Trim(), StringComparison.Ordinal) Then
                Return PageAction.AlreadyCurrent
            End If
            Return PageAction.UpdateSql
        End Function

        ' Everything generation would change, in the order it would change it, so the preview covers
        ' the database row and the dashboard icon and not only the page files.
        Public Shared Function DescribePlannedWork(plan As PageGenerationPlan, workspaceRoot As String) As List(Of String)
            Dim lines As New List(Of String)()
            If plan Is Nothing Then Return lines

            lines.Add("PAGE FILES")
            If plan.GenerateBrowsePage Then
                lines.Add("  " & Path.GetFileName(plan.BrowsePath) & If(File.Exists(plan.BrowsePath), "   (EXISTS - WOULD BE OVERWRITTEN)", "   (NEW FILE)"))
            Else
                lines.Add("  BROWSE PAGE NOT SELECTED")
            End If
            If plan.GenerateMaintenancePage Then
                lines.Add("  " & Path.GetFileName(plan.MaintenancePath) & If(File.Exists(plan.MaintenancePath), "   (EXISTS - WOULD BE OVERWRITTEN)", "   (NEW FILE)"))

                ' Named separately because the two halves are treated differently, and somebody
                ' reading this list is entitled to know which of their files is at risk.
                If Not String.IsNullOrWhiteSpace(plan.MaintenanceCompanionPath) Then
                    Dim companionNote As String
                    If Not File.Exists(plan.MaintenanceCompanionPath) Then
                        companionNote = "   (NEW FILE - YOURS, WRITTEN ONCE)"
                    ElseIf File.ReadAllText(plan.MaintenanceCompanionPath).IndexOf("Partial Public Class", StringComparison.OrdinalIgnoreCase) >= 0 Then
                        companionNote = "   (YOURS - LEFT ALONE)"
                    Else
                        companionNote = "   (WHOLE PAGE FROM BEFORE THE SPLIT - WOULD BE REPLACED, OLD COPY KEPT)"
                    End If
                    lines.Add("  " & Path.GetFileName(plan.MaintenanceCompanionPath) & companionNote)
                End If
            Else
                lines.Add("  MAINTENANCE PAGE NOT SELECTED")
            End If

            lines.Add(String.Empty)
            lines.Add("DATABASE")
            lines.Add("  UNDERLYING TABLE: " & plan.TableName & "   PRIMARY KEY: " & plan.PrimaryKey)
            If plan.GenerateBrowsePage Then
                Select Case ClassifyPageAction(plan.BrowsePageName, plan.TableName, plan.BrowseSql)
                    Case PageAction.Insert
                        lines.Add("  FW_Pages: NEW ROW FOR " & plan.BrowsePageName & " (ALIAS " & plan.TableAlias & ")")
                    Case PageAction.AlreadyCurrent
                        lines.Add("  FW_Pages: ALREADY CURRENT, NO CHANGE")
                    Case PageAction.UpdateSql
                        lines.Add("  FW_Pages: SQL WOULD BE UPDATED FOR " & plan.BrowsePageName)
                    Case Else
                        lines.Add("  FW_Pages: ROW WOULD BE REPLACED FOR " & plan.BrowsePageName & " (TABLE CHANGED)")
                End Select
            End If
            If plan.GenerateMaintenancePage Then
                lines.Add("  FW_PageGeneration: MAINTENANCE SOURCE BASELINE WOULD BE SAVED")
            End If

            lines.Add(String.Empty)
            lines.Add("DASHBOARD ICON")
            If plan.GenerateBrowsePage AndAlso IsMainMenuCaller(plan.MenuCaller) Then
                ' Main Menu used to fall into the "not a dashboard" line below, which read as though
                ' nothing had been asked for. It says what will happen now, including the number the
                ' decision turns on.
                Dim inUse = CountMovableRibbonTiles(workspaceRoot)
                Dim capacity = FW_MainMenu.MovableTileCapacityAtMinimumWidth()
                If inUse >= capacity Then
                    lines.Add("  MAIN MENU IS FULL - " & inUse.ToString() & " of " &
                              capacity.ToString() & " movable tiles at the narrowest window.")
                    lines.Add("  " & plan.BrowsePageName & " WOULD GO ON THE APP ADMIN DASHBOARD INSTEAD.")
                Else
                    lines.Add("  WOULD BE ADDED TO THE MAIN MENU RIBBON FOR " & plan.BrowsePageName &
                              " (" & (inUse + 1).ToString() & " of " & capacity.ToString() & " tiles)")
                    lines.Add("  IMAGE: " & If(String.IsNullOrWhiteSpace(plan.IconFileName),
                                               "NONE CHOSEN, THE DEFAULT GLYPH IS USED",
                                               plan.IconFileName))
                End If
            ElseIf Not plan.GenerateBrowsePage OrElse Not IsDashboardCaller(plan.MenuCaller) Then
                lines.Add("  NONE. MENU CALLER IS " & If(String.IsNullOrWhiteSpace(plan.MenuCaller), "NOT SET", plan.MenuCaller) & ", WHICH IS NOT A DASHBOARD")
            ElseIf DashboardIconExists(workspaceRoot, plan.MenuCaller, plan.BrowsePageName) Then
                lines.Add("  ALREADY PRESENT ON " & DashboardSourceFileName(workspaceRoot, plan.MenuCaller) & ", NO CHANGE")
            Else
                lines.Add("  WOULD BE ADDED TO " & DashboardSourceFileName(workspaceRoot, plan.MenuCaller) & " FOR " & plan.BrowsePageName)
                lines.Add("  IMAGE: " & If(String.IsNullOrWhiteSpace(plan.IconFileName),
                                           "NONE CHOSEN, THE DEFAULT GLYPH IS USED",
                                           plan.IconFileName))
            End If

            ' Said here as well as in the generation report, because this is the one place it can
            ' still be acted on. Generation only adds to the surface named, and removes from none.
            If plan.GenerateBrowsePage Then
                Dim elsewhere As New List(Of String)()
                Try
                    ReportButtonsOnOtherSurfaces(workspaceRoot, plan.BrowsePageName, plan.MenuCaller, elsewhere)
                Catch ex As Exception
                    elsewhere.Add("Could not check the other surfaces for an existing button: " & ex.Message)
                End Try

                For Each warning In elsewhere
                    lines.Add("  " & warning.ToUpperInvariant())
                Next
            End If

            Return lines
        End Function

        ' Compiles the planned source against the built application assembly in a scratch folder.
        ' Nothing in the workspace is written. This is what proves the pages would build.
        Public Shared Function CompileCheck(plan As PageGenerationPlan, workspaceRoot As String) As PageCompileResult
            Dim messages As New List(Of String)()
            If plan Is Nothing OrElse Not plan.IsValid Then
                messages.Add("THE REQUEST MUST BE VALID BEFORE THE GENERATED SOURCE CAN BE COMPILED.")
                Return New PageCompileResult(False, messages)
            End If

            Dim referenceAssembly = Path.Combine(workspaceRoot, "bin", "Debug", "net10.0-windows", "SDC.Framework.dll")
            If Not File.Exists(referenceAssembly) Then
                messages.Add("THE APPLICATION ASSEMBLY WAS NOT FOUND AT " & referenceAssembly & ".")
                messages.Add("BUILD THE PROJECT ONCE, THEN RUN THE COMPILE CHECK AGAIN.")
                Return New PageCompileResult(False, messages)
            End If

            ' Both pages compile together rather than one per tab: the generated browse page
            ' constructs the maintenance page to open a record, so compiling it alone would fail on
            ' a type that is not there.
            Dim targets As New List(Of String)()
            If plan.GenerateBrowsePage Then targets.Add(plan.BrowsePageName & ".vb")
            If plan.GenerateMaintenancePage Then targets.Add(plan.MaintenancePageName & ".vb")
            Dim targetText = String.Join(" AND ", targets).ToUpperInvariant()

            Dim scratchRoot = Path.Combine(workspaceRoot, "obj", "pagegen-preview")
            Try
                If Directory.Exists(scratchRoot) Then Directory.Delete(scratchRoot, True)
                Directory.CreateDirectory(scratchRoot)

                If plan.GenerateBrowsePage Then
                    File.WriteAllText(Path.Combine(scratchRoot, plan.BrowsePageName & ".vb"), plan.BrowseSource, New UTF8Encoding(False))
                End If
                If plan.GenerateMaintenancePage Then
                    File.WriteAllText(Path.Combine(scratchRoot, plan.MaintenancePageName & ".Generated.vb"), plan.MaintenanceSource, New UTF8Encoding(False))

                    ' Both halves, always. The generated half is a partial class with no
                    ' constructor and no base class, so compiling it alone proves nothing and
                    ' fails on everything.
                    '
                    ' The companion compiled here is the freshly built one rather than whatever
                    ' is on disk, deliberately: the check is asking whether what the generator
                    ' produces is sound, not whether somebody's own code happens to compile
                    ' against a reference assembly built before they wrote it.
                    File.WriteAllText(Path.Combine(scratchRoot, plan.MaintenancePageName & ".vb"),
                                      plan.MaintenanceCompanionSource, New UTF8Encoding(False))
                End If

                File.WriteAllText(Path.Combine(scratchRoot, "PageGenPreview.vbproj"),
                                  BuildCompileCheckProject(referenceAssembly),
                                  New UTF8Encoding(False))

                Dim startInfo As New Diagnostics.ProcessStartInfo(ResolveDotnetPath(), "build PageGenPreview.vbproj --nologo -v q") With {
                    .WorkingDirectory = scratchRoot,
                    .UseShellExecute = False,
                    .RedirectStandardOutput = True,
                    .RedirectStandardError = True,
                    .CreateNoWindow = True
                }

                Using compiler = Diagnostics.Process.Start(startInfo)
                    Dim standardOutput = compiler.StandardOutput.ReadToEnd()
                    Dim standardError = compiler.StandardError.ReadToEnd()
                    compiler.WaitForExit()

                    Dim reported = (standardOutput & Environment.NewLine & standardError).
                        Split({Environment.NewLine, vbLf}, StringSplitOptions.None).
                        Select(Function(line) line.Trim()).
                        Where(Function(line) line.Length > 0).
                        ToList()

                    Dim succeeded = compiler.ExitCode = 0
                    messages.Add(If(succeeded, "COMPILE CHECK PASSED.", "COMPILE CHECK FAILED."))
                    messages.Add(String.Empty)

                    ' The pages compile as one project, but the report is split per page: every
                    ' diagnostic names its file, so which page is broken is the useful answer.
                    Dim accountedFor As New List(Of String)()
                    For Each target In targets
                        Dim pageLines = reported.
                            Where(Function(line) line.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0 AndAlso IsDiagnosticLine(line)).
                            Select(Function(line) FormatDiagnostic(line, scratchRoot, target)).
                            ToList()
                        accountedFor.AddRange(reported.Where(Function(line) line.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0))

                        messages.Add(target.ToUpperInvariant() & "   " &
                                     If(pageLines.Count = 0,
                                        "OK",
                                        pageLines.Count.ToString(Globalization.CultureInfo.InvariantCulture) & If(pageLines.Count = 1, " ERROR", " ERRORS")))
                        For Each pageLine In pageLines.Take(15)
                            messages.Add("    " & pageLine)
                        Next
                        If pageLines.Count > 15 Then
                            messages.Add("    ... AND " & (pageLines.Count - 15).ToString(Globalization.CultureInfo.InvariantCulture) & " MORE")
                        End If
                    Next

                    Dim unattributed = reported.
                        Where(Function(line) IsDiagnosticLine(line) AndAlso Not accountedFor.Contains(line)).
                        ToList()
                    If unattributed.Count > 0 Then
                        messages.Add(String.Empty)
                        messages.Add("NOT TIED TO A PAGE:")
                        For Each line In unattributed.Take(10)
                            messages.Add("    " & FormatDiagnostic(line, scratchRoot, String.Empty))
                        Next
                    End If

                    If succeeded Then
                        messages.Add(String.Empty)
                        messages.Add("THE GENERATED SOURCE BUILDS AGAINST THE APPLICATION.")
                    End If
                    If targets.Count > 1 Then
                        messages.Add(String.Empty)
                        messages.Add("THE PAGES COMPILE AS ONE PROJECT BECAUSE THE BROWSE PAGE OPENS THE MAINTENANCE PAGE.")
                    End If

                    Return New PageCompileResult(succeeded, messages)
                End Using
            Catch ex As Exception
                messages.Add("THE COMPILE CHECK COULD NOT BE RUN: " & ex.Message)
                Return New PageCompileResult(False, messages)
            End Try
        End Function

        Private Shared Function IsDiagnosticLine(line As String) As Boolean
            Return line.IndexOf(": error ", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   line.IndexOf(": warning ", StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        ' Turns a full MSBuild diagnostic into the part worth reading: the scratch path and the
        ' trailing project reference say nothing the reader does not already know.
        Private Shared Function FormatDiagnostic(line As String, scratchRoot As String, pageFileName As String) As String
            Dim trimmed = line.Trim()
            Dim pathStart = trimmed.IndexOf(scratchRoot, StringComparison.OrdinalIgnoreCase)
            If pathStart >= 0 Then
                trimmed = trimmed.Substring(pathStart + scratchRoot.Length).TrimStart("\"c, "/"c)
            End If
            If pageFileName.Length > 0 AndAlso trimmed.StartsWith(pageFileName, StringComparison.OrdinalIgnoreCase) Then
                trimmed = trimmed.Substring(pageFileName.Length)
            End If
            Dim projectStart = trimmed.LastIndexOf(" [", StringComparison.Ordinal)
            If projectStart > 0 AndAlso trimmed.EndsWith("]", StringComparison.Ordinal) Then
                trimmed = trimmed.Substring(0, projectStart)
            End If
            Return trimmed.Trim()
        End Function

        ''' <summary>
        ''' Where dotnet.exe actually is, rather than trusting PATH to say.
        '''
        ''' "dotnet" alone failed with "access is denied" in a VirtualUI session on 2026-09-14: the
        ''' process inherits an environment the shell never touched, and PATH resolution is not
        ''' something a launched application should rely on. The install location is checked first,
        ''' then DOTNET_ROOT, and only then the bare name - which still works everywhere it worked
        ''' before.
        ''' </summary>
        Private Shared Function ResolveDotnetPath() As String
            Dim candidates As New List(Of String)()

            Dim dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
            If Not String.IsNullOrWhiteSpace(dotnetRoot) Then candidates.Add(Path.Combine(dotnetRoot, "dotnet.exe"))

            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe"))
            candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet", "dotnet.exe"))

            For Each candidate In candidates
                If File.Exists(candidate) Then Return candidate
            Next

            Return "dotnet"
        End Function

        Private Shared Function BuildCompileCheckProject(referenceAssembly As String) As String
            Return String.Join(Environment.NewLine, {
                "<Project Sdk=""Microsoft.NET.Sdk"">",
                "  <PropertyGroup>",
                "    <OutputType>Library</OutputType>",
                "    <TargetFramework>net10.0-windows</TargetFramework>",
                "    <UseWindowsForms>true</UseWindowsForms>",
                "    <RootNamespace></RootNamespace>",
                "  </PropertyGroup>",
                "  <ItemGroup>",
                "    <Reference Include=""SDC.Framework"">",
                "      <HintPath>" & referenceAssembly & "</HintPath>",
                "    </Reference>",
                "  </ItemGroup>",
                "</Project>",
                ""
            })
        End Function

        ''' A dashboard is a class named Dashboard_<Name>, and its source file is the file that
        ''' declares it. Adding a dashboard needs no edit here and none in the Menu Caller list:
        ''' create the pair and it becomes a valid target. DashboardCallers discovers the classes;
        ''' ResolveDashboardSourcePath finds the file.
        '''
        ''' The path is searched for rather than derived. It used to be built as
        ''' "02_FW_" & menuCaller & ".vb" at the workspace root, and the folder reorganisation on
        ''' 2026-09-04 invalidated both halves at once - the prefix went and the file moved into
        ''' 000_FRAMEWORK\020_DASHBOARDS. A derived path fails on the next move too, and it fails
        ''' where nothing is watching: the build cannot see it, and the running application never
        ''' executes this code. A search survives any arrangement of folders.
        Public Const DashboardCallerPrefix As String = "Dashboard_"

        ''' <summary>
        ''' Marks an icon choice as one of the built-in Windows glyphs rather than a file in
        ''' assets\images. Stored in the request so the two can never be confused: a bare name that
        ''' happens to match no file would otherwise look like a missing image.
        ''' </summary>
        Public Const SystemIconPrefix As String = "system:"

        ''' The built-in glyphs offered alongside the image files.
        Public Shared ReadOnly SystemIconNames As String() = {
            "Application", "Asterisk", "Error", "Exclamation", "Hand",
            "Information", "Question", "Shield", "Warning", "WinLogo"
        }

        Public Shared Function IsSystemIcon(iconFileName As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(iconFileName) AndAlso
                   iconFileName.Trim().StartsWith(SystemIconPrefix, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' The glyph name from a system: choice, or empty when the choice is not one, or names a
        ''' glyph that does not exist.
        Public Shared Function SystemIconName(iconFileName As String) As String
            If Not IsSystemIcon(iconFileName) Then Return String.Empty
            Dim name = iconFileName.Trim().Substring(SystemIconPrefix.Length).Trim()
            Return If(SystemIconNames.Any(Function(candidate) String.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)), name, String.Empty)
        End Function

        ''' The VB expression a generated dashboard button uses for its picture.
        ''' How a chosen icon reads in the generation result.
        Public Shared Function DescribeIcon(iconFileName As String) As String
            Dim systemName = SystemIconName(iconFileName)
            If systemName.Length > 0 Then Return systemName & " (system)"
            If String.IsNullOrWhiteSpace(iconFileName) Then Return "default glyph"
            Return iconFileName.Trim()
        End Function

        Public Shared Function DashboardImageExpression(iconFileName As String) As String
            Dim systemName = SystemIconName(iconFileName)
            If systemName.Length > 0 Then Return "SystemIcons." & systemName & ".ToBitmap()"
            If String.IsNullOrWhiteSpace(iconFileName) Then Return "SystemIcons.Application.ToBitmap()"
            ' IconScaler.Load, not LoadDashboardIcon: that wrapper is private to Dashboard_Application,
            ' so an icon generated onto Dashboard_Company did not compile. Every dashboard declares
            ' DashboardIconSize.
            Return "IconScaler.Load(""" & EscapeLiteral(iconFileName.Trim()) & """, DashboardIconSize, SystemIcons.Application.ToBitmap())"
        End Function

        Public Shared Function DashboardCallers() As List(Of String)
            Dim candidates As Type()
            Try
                candidates = GetType(PageGenerator).Assembly.GetTypes()
            Catch ex As Reflection.ReflectionTypeLoadException
                ' One unloadable type must not empty the Menu Caller list. Whatever did load is
                ' still a truthful answer, and a dashboard that failed to load could not be a
                ' target anyway.
                candidates = ex.Types.Where(Function(candidate) candidate IsNot Nothing).ToArray()
            End Try

            Return candidates.
                Where(Function(candidate) candidate.IsClass AndAlso
                                          candidate.Name.StartsWith(DashboardCallerPrefix, StringComparison.Ordinal)).
                Select(Function(candidate) candidate.Name).
                Distinct(StringComparer.OrdinalIgnoreCase).
                OrderBy(Function(name) name, StringComparer.OrdinalIgnoreCase).
                ToList()
        End Function

        ''' <summary>
        ''' Where a generated page is written, and the one place that decides it.
        '''
        ''' The framework's own waiting room: 000_FRAMEWORK\999_GENERATED. A generated pair is a
        ''' draft - it is reviewed and then filed by hand into a band, and which band depends on
        ''' what the page turns out to be, which the generator cannot know. It does not guess, but
        ''' it should not scatter loose files across the framework either, where an unfiled page
        ''' looks like part of it rather than something waiting on a decision. One folder says
        ''' both: these are generated, and none of them has been filed.
        '''
        ''' Inside the owner rather than at the repository root, decided 2026-09-18. A project gets
        ''' its own 999_GENERATED under its own folder, so the number means the same thing in every
        ''' tree - at the bottom, waiting - and a new project brings its waiting room with it
        ''' instead of adding one more folder to the root. Which owner a page belongs to is the
        ''' generation request's to say; until it does, everything lands here.
        '''
        ''' The SDK glob compiles the folder like any other, so a page works before it is filed.
        '''
        ''' PageGeneration_U built this path itself in four places. They agreed until they didn't:
        ''' this move would have changed the generator's idea of where a page lives and left the
        ''' page's "has it been edited by hand" checks looking at the old spot.
        ''' </summary>
        Public Const GeneratedPagesFolder As String = "000_FRAMEWORK\999_GENERATED"

        ''' <summary>
        ''' The caller that means the main ribbon rather than a dashboard. It has been in the Menu
        ''' Caller list all along and the generator did nothing with it: a request naming it was
        ''' accepted, the pages were written, and the button was never placed. The only trace was a
        ''' line in Preview Code reading "NOT A DASHBOARD", which reads as information rather than
        ''' as a refusal.
        ''' </summary>
        Public Const MainMenuCaller As String = "Main Menu"

        ' How many movable tiles the ribbon holds is FW_MainMenu.MovableTileCapacityAtMinimumWidth().
        ' It used to be the constant MainMenuMovableTileCapacity = 8 here, kept in step by hand with
        ' the geometry that decides it - tile pitch, panel inset, pinned tile count, minimum width -
        ' every one of which lives on the menu form. Nothing failed when the two disagreed; the
        ' ribbon simply stopped drawing a tile.

        ''' Tiles that live in the pinned row on the right and so cost nothing from the movable row.
        Private Shared ReadOnly PinnedRibbonKeys As String() =
            {"my-profile", "login-as-substitute", "select-role", "help-desk"}

        ''' <summary>
        ''' Where a page's file is, searched within one owner's tree.
        '''
        ''' <paramref name="searchRoot"/> is the owner's folder, not the repository, and that is the
        ''' whole point: a file drops its owner's prefix, so FW_Test_B and CTY_Test_B are both
        ''' Test_B.vb. Searching the repository would find the framework's file while generating
        ''' CTY's page and rewrite it - two owners could not share a base name, and the second
        ''' generation would destroy the first rather than say anything.
        '''
        ''' An empty search root means the repository, for the callers that only ask where a page
        ''' is rather than where one should be written.
        ''' </summary>
        Private Shared Function ResolveSourceFile(workspaceRoot As String, fileName As String, Optional searchRoot As String = "") As String
            Dim root = If(String.IsNullOrWhiteSpace(searchRoot), workspaceRoot, searchRoot)
            If String.IsNullOrWhiteSpace(root) OrElse Not Directory.Exists(root) Then Return String.Empty

            Try
                Return If(Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories).
                                    Where(Function(path) Not IsInNonSourceFolder(path)).
                                    OrderBy(Function(path) path, StringComparer.OrdinalIgnoreCase).
                                    FirstOrDefault(), String.Empty)
            Catch ex As IOException
                Return String.Empty
            Catch ex As UnauthorizedAccessException
                Return String.Empty
            End Try
        End Function

        ''' <summary>
        ''' Every movable ribbon tile, counted from source rather than guessed at.
        '''
        ''' Two files register tiles and they have to be read together: FW_MainMenu adds the built-in
        ''' ones, and the application's MenuFormInitializer adds its own. An UpsertActionTile naming
        ''' a key FW_MainMenu already registered replaces that tile rather than adding one, so the
        ''' keys are unioned and not summed - counting the calls would say eight where the ribbon
        ''' holds six, and refuse a tile that fits.
        ''' </summary>
        Private Shared Function CountMovableRibbonTiles(workspaceRoot As String) As Integer
            Dim keys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For Each fileName In {"MainMenu.vb", "MenuFormInitializer.vb"}
                Dim path = ResolveSourceFile(workspaceRoot, fileName)
                If path.Length = 0 Then Continue For

                Dim source = File.ReadAllText(path)
                For Each match As Match In Regex.Matches(source,
                                                         "(?:AddActionTile\(|UpsertActionTile\(\s*actionKey:=)""([^""]+)""",
                                                         RegexOptions.IgnoreCase)
                    keys.Add(match.Groups(1).Value)
                Next
            Next

            For Each pinned In PinnedRibbonKeys
                keys.Remove(pinned)
            Next

            Return keys.Count
        End Function

        ''' <summary>
        ''' Where this page is written: where it already lives if it has been filed, and GENERATED
        ''' PAGES if it has not.
        '''
        ''' Filing a generated page is the expected next step - it is a draft, and it belongs in a
        ''' band under 000_FRAMEWORK or in an application folder such as 100_CTY once you know which. Writing
        ''' by name alone made that a one-way door: regenerating a filed page put a second copy in
        ''' 999_GENERATED, two files declaring the same class in the same namespace, which is a
        ''' hard compile error rather than a duplicate anybody would spot.
        '''
        ''' So the page is looked for before it is placed, the same way a dashboard is. Move a page
        ''' anywhere in the tree and regeneration follows it.
        ''' </summary>
        ''' <summary>
        ''' Where a generated page lives, so regeneration rewrites it where it was filed rather than
        ''' dropping a second copy in 999_GENERATED.
        '''
        ''' **A file is named exactly what its page is called**, prefix included: FW_Employees_B
        ''' lives in FW_Employees_B.vb, whether that is still in its owner's 999_GENERATED or filed
        ''' under 000_FRAMEWORK\030_EMPLOYEES.
        '''
        ''' It dropped the prefix until 2026-09-18, on the grounds that the folder said who owned
        ''' the page. That held while there was one owner and failed with two: FW_Test_B and
        ''' CTY_Test_B would both have been Test_B.vb - indistinguishable in a file list, an editor
        ''' tab or a search, and the second generation would have rewritten the first.
        '''
        ''' The unprefixed name is still looked for, because pages generated under the old rule
        ''' carry it, and regeneration must rewrite those where they are rather than leave one copy
        ''' behind and write another.
        ''' </summary>
        ''' <summary>
        ''' Where a page's file is, or where a new one goes: its owner's 999_GENERATED.
        '''
        ''' <paramref name="ownerFolder"/> is the owner's root folder - "000_FRAMEWORK", "100_CTY".
        ''' Empty falls back to the framework's, which is what a caller asking only "where is this
        ''' page?" wants: the answer for an existing page comes from the search below, and the
        ''' fallback is only reached for a page that does not exist yet.
        ''' </summary>
        Public Shared Function GeneratedPagePath(workspaceRoot As String,
                                                  pageName As String,
                                                  Optional ownerFolder As String = "") As String
            Dim trimmedName = pageName.Trim()

            ' The file is named exactly what the page is called, prefix included. It dropped the
            ' prefix until 2026-09-18, on the grounds that the folder said who owned it - true
            ' while there was one owner, and false the moment there were two: FW_Test_B and
            ' CTY_Test_B would both have been Test_B.vb, indistinguishable in every list of files,
            ' every editor tab and every search.
            Dim fileName = trimmedName & ".vb"

            ' Within this owner's folder when one is given. Two owners can hold a page with the
            ' same base name, and their files are both <base>.vb - so a repository-wide search
            ' would answer with somebody else's page and regenerate over it.
            Dim searchRoot = If(String.IsNullOrWhiteSpace(ownerFolder), String.Empty, Path.Combine(workspaceRoot, ownerFolder))

            Dim existing = ResolveSourceFile(workspaceRoot, fileName, searchRoot)
            If existing.Length > 0 Then Return existing

            ' Pages written while the file dropped its prefix - Employees_B.vb for FW_Employees_B.
            ' Regeneration has to rewrite those where they are rather than leave one copy behind
            ' and write another under the new name.
            Dim unprefixed = StripOwnerPrefix(trimmedName) & ".vb"
            If Not String.Equals(unprefixed, fileName, StringComparison.OrdinalIgnoreCase) Then
                Dim legacy = ResolveSourceFile(workspaceRoot, unprefixed, searchRoot)
                If legacy.Length > 0 Then Return legacy
            End If

            Dim destination = If(String.IsNullOrWhiteSpace(ownerFolder), GeneratedPagesFolder,
                                 Path.Combine(ownerFolder, PageOwners.GeneratedFolderName))

            Return Path.Combine(workspaceRoot, destination, fileName)
        End Function

        ''' <summary>
        ''' A page name with its owner's prefix removed, which is what the file is called. FW_ is
        ''' recognised whether or not the framework folder is present, because pages named that way
        ''' exist in every copy of this repository.
        ''' </summary>
        Private Shared Function StripOwnerPrefix(pageName As String) As String
            Dim trimmed = If(pageName, String.Empty).Trim()

            If trimmed.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then
                Return trimmed.Substring(3)
            End If

            Dim underscore = trimmed.IndexOf("_"c)
            If underscore > 0 Then
                Dim leading = trimmed.Substring(0, underscore)
                If KnownOwnerPrefixes().Contains(leading, StringComparer.OrdinalIgnoreCase) Then
                    Return trimmed.Substring(underscore + 1)
                End If
            End If

            Return trimmed
        End Function

        Private Shared Function IsMainMenuCaller(menuCaller As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(menuCaller) AndAlso
                   String.Equals(menuCaller.Trim(), MainMenuCaller, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Puts a generated browse page on the main ribbon, or says why it could not and puts it on
        ''' the App Admin dashboard instead.
        '''
        ''' The fallback is the point. A ribbon that is full is not a failure the generator can fix,
        ''' and refusing outright would leave a page with no way in - so the button goes somewhere
        ''' real and the report says where, rather than the request half-succeeding in silence the
        ''' way naming Main Menu used to.
        '''
        ''' The tile is written into MenuFormInitializer, not FW_MainMenu. Which tiles an
        ''' application's ribbon carries is the application's decision; the framework's menu form
        ''' serves whichever application configures it, and a generated page belongs to one
        ''' application.
        ''' </summary>
        Private Shared Sub EnsureMainMenuTile(workspaceRoot As String,
                                              browsePageName As String,
                                              tableName As String,
                                              iconFileName As String,
                                              tableAlias As String,
                                              created As List(Of String),
                                              skipped As List(Of String),
                                              errors As List(Of String))
            Dim initializerPath = ResolveSourceFile(workspaceRoot, "MenuFormInitializer.vb")
            If initializerPath.Length = 0 Then
                errors.Add("MenuFormInitializer.vb could not be found under " & workspaceRoot & ", so no main menu button was placed.")
                Return
            End If

            Dim actionKey = "generated-" & browsePageName.ToLowerInvariant()
            Dim source = File.ReadAllText(initializerPath)

            If source.IndexOf("""" & actionKey & """", StringComparison.OrdinalIgnoreCase) >= 0 Then
                skipped.Add("Main Menu button: " & actionKey & " is already on the ribbon, unchanged.")
                Return
            End If

            Dim inUse = CountMovableRibbonTiles(workspaceRoot)
            Dim capacity = FW_MainMenu.MovableTileCapacityAtMinimumWidth()
            If inUse >= capacity Then
                errors.Add("MAIN MENU FULL: the ribbon holds " & capacity.ToString() &
                           " movable tiles at the narrowest window and " & inUse.ToString() &
                           " are in use, so " & browsePageName & " was not added to it. " &
                           "Free a tile, or choose a dashboard as the Menu Caller.")
                Return
            End If

            ' A marker put there for this, rather than whatever line happened to be last. The anchor
            ' was AddMenuTestTile(menu) until 2026-09-05, so removing a demonstration tile would have
            ' silently stopped every future page reaching the ribbon.
            Dim anchor = "            ' PAGEGEN RIBBON ANCHOR"
            If source.IndexOf(anchor, StringComparison.Ordinal) < 0 Then
                errors.Add("MenuFormInitializer.vb has no recognised place to add a ribbon tile, so " &
                           browsePageName & " was not added to the main menu.")
                Return
            End If

            Dim newLine = SourceNewLine(source)
            Dim tile = String.Join(newLine, {
                "            menu.UpsertActionTile(",
                "                actionKey:=""" & actionKey & """,",
                "                caption:=""" & DisplayPageCaption(browsePageName, tableAlias).Replace("""", """""") & """,",
                "                onClick:=Sub(sender, e)",
                "                             Using frm As New " & browsePageName & "(user, profile)",
                "                                 frm.ShowDialog(menu)",
                "                             End Using",
                "                         End Sub,",
                "                iconFileName:=""" & If(String.IsNullOrWhiteSpace(iconFileName), "users.png", iconFileName) & """,",
                "                fallbackIcon:=SystemIcons.Application.ToBitmap(),",
                "                isVisible:=True,",
                "                isEnabled:=True)",
                "            menu.SetActionPage(""" & actionKey & """, """ & EscapeLiteral(browsePageName) & """)",
                "",
                anchor
            })

            source = source.Replace(anchor, tile)
            File.WriteAllText(initializerPath, source, New UTF8Encoding(False))
            created.Add("Main Menu button: " & actionKey & " - " & DescribeIcon(iconFileName) &
                        " (" & (inUse + 1).ToString() & " of " & capacity.ToString() & " tiles)")
        End Sub

        Private Shared Function IsDashboardCaller(menuCaller As String) As Boolean
            If String.IsNullOrWhiteSpace(menuCaller) Then Return False
            Return DashboardCallers().Any(Function(name) String.Equals(name, menuCaller.Trim(), StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>
        ''' Folders that hold no source worth searching. bin and obj would return a copy, and the
        ''' rest are archives of files that used to be real - matching one of those would patch a
        ''' dashboard nobody compiles.
        ''' </summary>
        Private Shared ReadOnly NonSourceFolders As String() =
            {"bin", "obj", "restore-points", "project-backup", "tests", "900_SANDBOX"}

        Private Shared Function IsInNonSourceFolder(path As String) As Boolean
            Dim parts = path.Split({IO.Path.DirectorySeparatorChar, IO.Path.AltDirectorySeparatorChar})
            Return parts.Any(Function(part) NonSourceFolders.Contains(part, StringComparer.OrdinalIgnoreCase))
        End Function

        ''' <summary>
        ''' The file that declares this dashboard class, wherever it currently lives, or an empty
        ''' string when there is no such file. Searched rather than derived - see the note on
        ''' DashboardCallerPrefix.
        '''
        ''' Matched on file name first, because the convention is that a file is named for the class
        ''' it declares. Where that finds nothing the declaration itself is searched for, so a file
        ''' named against convention is still found rather than silently reported missing.
        ''' </summary>
        Private Shared Function ResolveDashboardSourcePath(workspaceRoot As String, menuCaller As String) As String
            If Not IsDashboardCaller(menuCaller) Then Return String.Empty
            If String.IsNullOrWhiteSpace(workspaceRoot) OrElse Not Directory.Exists(workspaceRoot) Then Return String.Empty

            Dim className = menuCaller.Trim()

            Try
                Dim byName = Directory.EnumerateFiles(workspaceRoot, className & ".vb", SearchOption.AllDirectories).
                                       Where(Function(path) Not IsInNonSourceFolder(path)).
                                       OrderBy(Function(path) path, StringComparer.OrdinalIgnoreCase).
                                       FirstOrDefault()
                If Not String.IsNullOrEmpty(byName) Then Return byName

                Dim declaration = New Regex("(^|\s)Class\s+" & Regex.Escape(className) & "(\s|$)",
                                            RegexOptions.IgnoreCase Or RegexOptions.Multiline)
                Return Directory.EnumerateFiles(workspaceRoot, "*.vb", SearchOption.AllDirectories).
                                 Where(Function(path) Not IsInNonSourceFolder(path)).
                                 OrderBy(Function(path) path, StringComparer.OrdinalIgnoreCase).
                                 FirstOrDefault(Function(path) declaration.IsMatch(File.ReadAllText(path)))
            Catch ex As IOException
                ' A locked or vanished file must not take the whole search down: the caller reports
                ' "could not be found", which is the truth from here.
                Return String.Empty
            Catch ex As UnauthorizedAccessException
                Return String.Empty
            End Try
        End Function

        ''' <summary>
        ''' The dashboard's file name for reporting. Resolved from the real file where one exists,
        ''' so a report never names a path that does not.
        ''' </summary>
        Private Shared Function DashboardSourceFileName(workspaceRoot As String, menuCaller As String) As String
            Dim resolved = ResolveDashboardSourcePath(workspaceRoot, menuCaller)
            If resolved.Length > 0 Then Return Path.GetFileName(resolved)
            If Not IsDashboardCaller(menuCaller) Then Return String.Empty
            Return menuCaller.Trim() & ".vb"
        End Function

        ''' <summary>
        ''' Rewrites the .Image line of an existing generated button. Returns True when the file was
        ''' changed, False when there was nothing to change.
        ''' </summary>
        Private Shared ReadOnly NewLineCharacters As Char() = {ChrW(13), ChrW(10)}

        ''' <summary>
        ''' The line ending the file already uses, rather than this machine's.
        '''
        ''' Source files here are a mix: this dashboard is LF throughout while its sibling is CRLF.
        ''' An anchor built with Environment.NewLine is simply not present in an LF file, and the
        ''' edit then reports nothing to do instead of failing - which is how a chosen icon could be
        ''' saved, generated without complaint, and never actually appear on the button.
        ''' </summary>
        Private Shared Function SourceNewLine(source As String) As String
            Return If(If(source, String.Empty).Contains(vbCrLf, StringComparison.Ordinal), vbCrLf, vbLf)
        End Function

        Private Shared Function UpdateDashboardIconImage(dashboardPath As String, browsePageName As String, iconFileName As String) As Boolean
            If String.IsNullOrWhiteSpace(iconFileName) OrElse Not File.Exists(dashboardPath) Then Return False

            Dim source = File.ReadAllText(dashboardPath)
            Dim anchor = ".Name = ""ActionKey_" & browsePageName & """"
            Dim anchorIndex = source.IndexOf(anchor, StringComparison.OrdinalIgnoreCase)
            If anchorIndex < 0 Then Return False

            ' Stay inside this button's initializer: the search stops at its closing brace so a
            ' neighbouring button's image can never be rewritten by mistake.
            Dim blockEnd = source.IndexOf("}", anchorIndex, StringComparison.Ordinal)
            If blockEnd < 0 Then Return False

            Dim imageIndex = source.IndexOf(".Image = ", anchorIndex, StringComparison.Ordinal)
            If imageIndex < 0 OrElse imageIndex > blockEnd Then Return False

            Dim lineEnd = source.IndexOfAny(NewLineCharacters, imageIndex)
            If lineEnd < 0 Then lineEnd = source.Length

            Dim existingLine = source.Substring(imageIndex, lineEnd - imageIndex)
            Dim replacement = ".Image = " & DashboardImageExpression(iconFileName) & ","
            If String.Equals(existingLine.Trim(), replacement, StringComparison.Ordinal) Then Return False

            source = source.Substring(0, imageIndex) & replacement & source.Substring(lineEnd)
            File.WriteAllText(dashboardPath, source, New UTF8Encoding(False))
            Return True
        End Function

        ''' <summary>
        ''' Whether this page already has a ribbon tile, whatever the request now asks for.
        '''
        ''' The same test EnsureMainMenuTile makes before adding one, lifted out so the surfaces can
        ''' be asked about each other rather than only about themselves.
        ''' </summary>
        Private Shared Function MainMenuTileExists(workspaceRoot As String, browsePageName As String) As Boolean
            Dim initializerPath = ResolveSourceFile(workspaceRoot, "MenuFormInitializer.vb")
            If initializerPath.Length = 0 OrElse Not File.Exists(initializerPath) Then Return False

            Dim actionKey = "generated-" & browsePageName.ToLowerInvariant()
            Return File.ReadAllText(initializerPath).IndexOf("""" & actionKey & """", StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        ''' <summary>
        ''' Reports a button this page already has somewhere the request is no longer asking for.
        '''
        ''' Generation only ever *adds* to the surface currently named, and nothing removes a button
        ''' from anywhere - a button is source, so removing one means deleting a field, a constructor
        ''' block, a layout line and a click handler by text manipulation. Generate a page onto a
        ''' dashboard, regenerate it naming Main Menu, and both buttons open it.
        '''
        ''' Each surface is already idempotent about itself, which is what makes this easy to miss:
        ''' it behaves perfectly until the surface changes. The ribbon-full fallback reaches the same
        ''' place without anyone changing anything - a page put on the App Admin dashboard because
        ''' the ribbon was full gains a tile as well once a slot is freed.
        '''
        ''' This does not fix it. It stops the duplicate being silent, which is the part that costs
        ''' somebody an afternoon.
        ''' </summary>
        Private Shared Sub ReportButtonsOnOtherSurfaces(workspaceRoot As String,
                                                        browsePageName As String,
                                                        menuCaller As String,
                                                        skipped As List(Of String))
            If String.IsNullOrWhiteSpace(browsePageName) Then Return

            If Not IsMainMenuCaller(menuCaller) AndAlso MainMenuTileExists(workspaceRoot, browsePageName) Then
                skipped.Add("ALREADY ON THE MAIN MENU: " & browsePageName &
                            " has a ribbon tile from an earlier generation. Remove it from " &
                            "MenuFormInitializer.vb by hand, or the page will have two buttons.")
            End If

            For Each dashboard In DashboardCallers()
                If String.Equals(dashboard, menuCaller, StringComparison.OrdinalIgnoreCase) Then Continue For
                If Not DashboardIconExists(workspaceRoot, dashboard, browsePageName) Then Continue For

                skipped.Add("ALREADY ON " & dashboard.ToUpperInvariant() & ": " & browsePageName &
                            " has an icon there from an earlier generation. Remove it from " &
                            dashboard & ".vb by hand, or the page will have two buttons.")
            Next
        End Sub

        Private Shared Function DashboardIconExists(workspaceRoot As String, menuCaller As String, browsePageName As String) As Boolean
            Dim dashboardPath = ResolveDashboardSourcePath(workspaceRoot, menuCaller)
            If dashboardPath.Length = 0 OrElse Not File.Exists(dashboardPath) Then Return False
            Dim source = File.ReadAllText(dashboardPath)
            Return source.IndexOf("ActionKey_" & browsePageName, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   source.IndexOf("New " & browsePageName & "_B", StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Private Shared Function ReadGenerationFlag(request As DataRow, columnName As String, defaultValue As Boolean) As Boolean
            If request Is Nothing OrElse request.Table Is Nothing OrElse Not request.Table.Columns.Contains(columnName) OrElse request.IsNull(columnName) Then
                Return defaultValue
            End If

            Return Convert.ToBoolean(request(columnName))
        End Function

        ''' <summary>
        ''' The page's name as it will be written: the owner's prefix, the base name, the suffix.
        '''
        ''' Any owner's prefix already on the name is taken off first, so a request switched from
        ''' one owner to another is renamed rather than stacked - FW_Widget_B picked as CTY becomes
        ''' CTY_Widget_B, not CTY_FW_Widget_B.
        ''' </summary>
        Private Shared Function NormalizeGeneratedPageName(pageName As String,
                                                            suffix As String,
                                                            ownerPrefix As String) As String
            Dim normalized = If(pageName, String.Empty).Trim()

            Dim underscore = normalized.IndexOf("_"c)
            If underscore > 0 Then
                Dim leading = normalized.Substring(0, underscore)
                ' Only a prefix that is really an owner's, so Users_AppAdmin_B keeps its name.
                If KnownOwnerPrefixes().Contains(leading, StringComparer.OrdinalIgnoreCase) Then
                    normalized = normalized.Substring(underscore + 1)
                End If
            End If

            If Not normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                normalized &= suffix
            End If

            Return If(String.IsNullOrWhiteSpace(ownerPrefix), String.Empty, ownerPrefix & "_") & normalized
        End Function

        ''' <summary>
        ''' Every owner's prefix, for recognising one on a name. Read from the folders, so an
        ''' application added later is recognised without this list being touched.
        ''' </summary>
        Private Shared Function KnownOwnerPrefixes() As List(Of String)
            Return PageOwners.All(Environment.CurrentDirectory).Select(Function(item) item.Prefix).ToList()
        End Function

        Private Shared Sub EnsureDashboardIcon(workspaceRoot As String,
                                                menuCaller As String,
                                                tableName As String,
                                                iconFileName As String,
                                                browsePageName As String,
                                                maintenancePageName As String,
                                                tableAlias As String,
                                                created As List(Of String),
                                                skipped As List(Of String),
                                                errors As List(Of String))
            If Not IsDashboardCaller(menuCaller) Then
                errors.Add("ICON WARNING: " & menuCaller & " is not a dashboard, so no icon was generated.")
                Return
            End If

            Dim dashboardPath = ResolveDashboardSourcePath(workspaceRoot, menuCaller)
            If dashboardPath.Length = 0 OrElse Not File.Exists(dashboardPath) Then
                errors.Add(menuCaller & ".vb could not be found under " & workspaceRoot & " for icon generation.")
                Return
            End If

            Dim dashboardFileName = Path.GetFileName(dashboardPath)

            Dim source = File.ReadAllText(dashboardPath)
            Dim newLine = SourceNewLine(source)
            Dim actionKey = "ActionKey_" & browsePageName   ' the control name, so the report names what the dashboard addresses
            If DashboardIconExists(workspaceRoot, menuCaller, browsePageName) Then
                ' The button is already on the dashboard, so it is not rebuilt - that would move it
                ' to another grid cell. Only the picture is brought up to date, in place, and only
                ' when the request now names a different one.
                If UpdateDashboardIconImage(dashboardPath, browsePageName, iconFileName) Then
                    created.Add(menuCaller & " icon image updated: " & actionKey & " - " & DescribeIcon(iconFileName))
                Else
                    skipped.Add(menuCaller & " icon: " & actionKey & " - " & DescribeIcon(iconFileName) & ", unchanged")
                End If
                Return
            End If

            ' No height to arrange. The dashboard measures its own icons when it opens and grows to
            ' fit the lowest one, so a fourth row needs nothing written here - and needs no rebuild
            ' before it can be seen, which editing a constant in DashboardGridLayout.vb did.
            Dim gridCell = FindNextDashboardGridCell(source)

            Dim buttonField = "        Private ReadOnly generated" & browsePageName & "Button As DashboardIconButton" & newLine
            Dim fieldAnchor = "        Private ReadOnly closeIconButton As Button" & newLine
            source = InsertAfter(source, fieldAnchor, buttonField)

            Dim construction = String.Join(newLine, {
                "",
                "            generated" & browsePageName & "Button = New DashboardIconButton() With {",
                "                .Name = ""ActionKey_" & browsePageName & """,",
                "                .PageName = """ & EscapeLiteral(browsePageName) & """,",
                "                .Text = """ & DisplayPageCaption(browsePageName, tableAlias) & """,",
                "                .Location = DashboardGridLayout.CellLocation(" & gridCell.Y.ToString(Globalization.CultureInfo.InvariantCulture) & ", " & gridCell.X.ToString(Globalization.CultureInfo.InvariantCulture) & "),",
                "                .Size = New Size(DashboardGridLayout.IconWidth, DashboardGridLayout.IconHeight),",
                "                .BackColor = Color.Transparent,",
                "                .UseVisualStyleBackColor = False,",
                "                .FlatStyle = FlatStyle.Flat,",
                "                .Font = New Font(""Segoe UI"", 13.0F, FontStyle.Regular),",
                "                .Image = " & DashboardImageExpression(iconFileName) & ",",
                "                .TextImageRelation = TextImageRelation.ImageAboveText,",
                "                .ImageAlign = ContentAlignment.TopCenter,",
                "                .TextAlign = ContentAlignment.BottomCenter,",
                "                .TabStop = False",
                "            }",
                "            generated" & browsePageName & "Button.FlatAppearance.BorderSize = 0",
                "            generated" & browsePageName & "Button.FlatAppearance.MouseOverBackColor = Color.Transparent",
                "            generated" & browsePageName & "Button.FlatAppearance.MouseDownBackColor = Color.Transparent"
            }) & newLine
            source = InsertBefore(source, "            AddHandler Me.Load, AddressOf " & menuCaller.Trim() & "_Load", construction)

            Dim handlers = String.Join(newLine, {
                "            AddHandler generated" & browsePageName & "Button.MouseEnter, AddressOf IconButton_MouseEnter",
                "            AddHandler generated" & browsePageName & "Button.MouseLeave, AddressOf IconButton_MouseLeave",
                "            AddHandler generated" & browsePageName & "Button.Click, AddressOf Generated" & browsePageName & "Button_Click",
                ""
            })
            source = InsertBefore(source, "            AddHandler closeIconButton.Click, AddressOf CloseButton_Click", handlers)
            source = InsertBefore(source, "            Me.Controls.Add(topStripLabel)", "            Me.Controls.Add(generated" & browsePageName & "Button)" & newLine)

            Dim baselineAnchor = "            rolesButton.Top = DashboardGridLayout.CellTop(1)"
            Dim baselineIndex = source.IndexOf(baselineAnchor, StringComparison.Ordinal)
            If baselineIndex < 0 Then
                errors.Add(dashboardFileName & " row baseline could not be found for icon generation.")
                Return
            End If
            Dim baselineLineEnd = source.IndexOf(newLine, baselineIndex, StringComparison.Ordinal)
            If baselineLineEnd < 0 Then baselineLineEnd = source.Length
            source = source.Insert(baselineLineEnd,
                                   newLine &
                                   "            generated" & browsePageName & "Button.Left = DashboardGridLayout.CellLeft(" & gridCell.X.ToString(Globalization.CultureInfo.InvariantCulture) & ")" & newLine &
                                   "            generated" & browsePageName & "Button.Top = DashboardGridLayout.CellTop(" & gridCell.Y.ToString(Globalization.CultureInfo.InvariantCulture) & ")")

            Dim clickHandler = String.Join(newLine, {
                "",
                "        Private Sub Generated" & browsePageName & "Button_Click(sender As Object, e As EventArgs)",
                "            ResetIconButtonVisuals()",
                "            Using page As New " & browsePageName & "(currentUser, accessProfile)",
                "                page.ShowDialog(Me)",
                "            End Using",
                "        End Sub",
                ""
            })
            source = InsertBefore(source, "        Private Sub IconButton_MouseEnter", clickHandler)
            File.WriteAllText(dashboardPath, source, New UTF8Encoding(False))
            created.Add(menuCaller & " icon: " & actionKey & " - " & DescribeIcon(iconFileName))
        End Sub

        Private Shared Function FindNextDashboardGridCell(source As String) As Point
            For row As Integer = 1 To 20
                For column As Integer = 1 To 5
                    Dim cellText = "DashboardGridLayout.CellLocation(" & row.ToString(Globalization.CultureInfo.InvariantCulture) & ", " & column.ToString(Globalization.CultureInfo.InvariantCulture) & ")"
                    If source.IndexOf(cellText, StringComparison.Ordinal) < 0 Then
                        Return New Point(column, row)
                    End If
                Next
            Next
            Throw New InvalidOperationException("No dashboard grid position is available for the generated icon.")
        End Function

        Private Shared Function InsertAfter(source As String, anchor As String, insertion As String) As String
            Dim index = source.IndexOf(anchor, StringComparison.Ordinal)
            If index < 0 Then Throw New InvalidOperationException("Dashboard source anchor was not found: " & anchor.Trim())
            Return source.Insert(index + anchor.Length, insertion)
        End Function

        Private Shared Function InsertBefore(source As String, anchor As String, insertion As String) As String
            Dim index = source.IndexOf(anchor, StringComparison.Ordinal)
            If index < 0 Then Throw New InvalidOperationException("Dashboard source anchor was not found: " & anchor.Trim())
            Return source.Insert(index, insertion)
        End Function

        ''' <summary>
        ''' What a button opening this page says.
        '''
        ''' The page's alias where the request gave one, which is also the page's own title - so a
        ''' button and the page it opens say the same thing, set once. Without an alias it falls
        ''' back to the page name, stripped of its owner's prefix and its _B: FW_Test_B reads
        ''' "Test".
        '''
        ''' **A prefix never reaches a caption.** FW_ and CTY_ say who owns the source; on a button
        ''' they are noise to whoever is reading it, and on two buttons they are the only thing
        ''' telling them apart, which is a caption doing a folder's job.
        ''' </summary>
        Private Shared Function DisplayPageCaption(pageName As String, tableAlias As String) As String
            Dim chosen = If(tableAlias, String.Empty).Trim()
            If chosen <> String.Empty Then Return StripOwnerPrefix(chosen)

            Dim name = StripOwnerPrefix(If(pageName, String.Empty).Trim())
            If name.EndsWith("_B", StringComparison.OrdinalIgnoreCase) Then
                name = name.Substring(0, name.Length - 2)
            End If

            Return name.Replace("_", " ", StringComparison.Ordinal)
        End Function

        Private Shared Sub ValidateName(value As String, description As String, suffix As String, errors As List(Of String))
            If String.IsNullOrWhiteSpace(value) OrElse Not value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) OrElse
               Not value.All(Function(character) Char.IsLetterOrDigit(character) OrElse character = "_"c) Then
                errors.Add(description & " must be a valid identifier ending in " & suffix & ".")
            End If
        End Sub

        Private Shared Sub ValidateFields(selectedFields As IEnumerable(Of String), schemaFields As List(Of String), category As String, errors As List(Of String))
            For Each field In selectedFields
                If Not schemaFields.Any(Function(schemaField) String.Equals(schemaField, field, StringComparison.OrdinalIgnoreCase)) Then
                    errors.Add(category & " field does not exist in the underlying table: " & field)
                End If
            Next
        End Sub

        ''' <summary>The generated half's path, given the companion's.</summary>
        Friend Shared Function GeneratedHalfPath(companionPath As String) As String
            If String.IsNullOrWhiteSpace(companionPath) Then Return String.Empty

            Dim folder = System.IO.Path.GetDirectoryName(companionPath)
            Dim stem = System.IO.Path.GetFileNameWithoutExtension(companionPath)
            Return System.IO.Path.Combine(If(folder, String.Empty), stem & ".Generated.vb")
        End Function

        ''' <summary>
        ''' Writes the hand-written half, once.
        '''
        ''' Never overwritten when it already holds a companion: that file is the reason the
        ''' split exists, and rewriting it would throw away the code the split was meant to
        ''' protect. It carries no overwrite prompt for the same reason - there is nothing to
        ''' ask about.
        '''
        ''' The exception is a page from before the split, which is a whole class rather than a
        ''' partial one. Left alone it would declare the same class as the generated half and
        ''' the build would fail on a duplicate. Recognised by the absence of "Partial", and
        ''' replaced - the one case where this file is rewritten, and only once.
        ''' </summary>
        Private Shared Function WriteCompanionPage(path As String,
                                                   content As String,
                                                   created As List(Of String),
                                                   skipped As List(Of String)) As Boolean
            If String.IsNullOrWhiteSpace(path) OrElse String.IsNullOrWhiteSpace(content) Then Return False

            Dim name = System.IO.Path.GetFileName(path)
            If File.Exists(path) Then
                Dim existing = File.ReadAllText(path)
                If existing.IndexOf("Partial Public Class", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    skipped.Add(name & "   (YOURS - LEFT ALONE)")
                    Return False
                End If

                Dim folderForBackup = System.IO.Path.GetDirectoryName(path)
                Dim backup = System.IO.Path.Combine(If(folderForBackup, String.Empty),
                                                    System.IO.Path.GetFileNameWithoutExtension(path) & ".before-split.vb.txt")
                File.WriteAllText(backup, existing, New UTF8Encoding(False))
                File.WriteAllText(path, content, New UTF8Encoding(False))
                created.Add("SPLIT: " & name & "   (previous page kept as " & System.IO.Path.GetFileName(backup) & ")")
                Return True
            End If

            Dim folder = System.IO.Path.GetDirectoryName(path)
            If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then
                Directory.CreateDirectory(folder)
            End If

            File.WriteAllText(path, content, New UTF8Encoding(False))
            created.Add(name & "   (YOURS - written once, never rewritten)")
            Return True
        End Function

        Private Shared Function WriteGeneratedPage(path As String,
                                               content As String,
                                               overwriteExistingPage As Boolean,
                                               created As List(Of String),
                                               skipped As List(Of String)) As Boolean
            Dim existed = File.Exists(path)
            If existed AndAlso Not overwriteExistingPage Then
                skipped.Add(System.IO.Path.GetFileName(path))
                Return False
            End If

            ' The pages now go in a folder rather than at the root, and the first generation on a
            ' fresh clone would otherwise fail on a directory nothing has created yet.
            Dim folder = System.IO.Path.GetDirectoryName(path)
            If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then
                Directory.CreateDirectory(folder)
            End If

            File.WriteAllText(path, content, New UTF8Encoding(False))
            created.Add(If(existed, "OVERWRITTEN: ", String.Empty) & System.IO.Path.GetFileName(path))
            Return True
        End Function

        Private Shared Function SaveBrowseBaseline(requestId As Integer,
                                                   source As String,
                                                   errors As List(Of String)) As Boolean
            Try
                Dim hasher As SHA256 = SHA256.Create()
                Using hasher
                    Dim hash = Convert.ToHexString(hasher.ComputeHash(Encoding.UTF8.GetBytes(source)))
                    Return DataAccess.SavePageGenerationBrowseBaseline(requestId, hash)
                End Using
            Catch ex As Exception
                errors.Add("Browse baseline error: " & ex.Message)
                Return False
            End Try
        End Function

        Private Shared Function SaveMaintenanceBaseline(requestId As Integer,
                                                         source As String,
                                                         errors As List(Of String)) As Boolean
            Try
                Dim hasher As SHA256 = SHA256.Create()
                Using hasher
                    Dim hash = Convert.ToHexString(hasher.ComputeHash(Encoding.UTF8.GetBytes(source)))
                    Return DataAccess.SavePageGenerationMaintenanceBaseline(requestId, source, hash)
                End Using
            Catch ex As Exception
                errors.Add("Maintenance baseline error: " & ex.Message)
                Return False
            End Try
        End Function

        ''' <summary>
        ''' The whole of a generated browse page.
        '''
        ''' It used to be about ninety lines here, producing a sixty-six line page of which fifty
        ''' were identical in every page ever generated - Create, Update and Delete handlers whose
        ''' only page-specific fact was the name of the _U partner. Two pages generated from this
        ''' template were byte-identical apart from the class name.
        '''
        ''' Those handlers now live in FW_Base_B, so what is left here is genuinely page-specific:
        ''' the class name, the table, and which maintenance page to open.
        ''' </summary>
        Private Shared Function BuildBrowseSource(pageName As String,
                              tableName As String,
                              useQbeOnly As Boolean,
                              generateMaintenancePage As Boolean) As String
            Return String.Join(Environment.NewLine, {
                "Option Strict On",
                "Option Explicit On",
                "",
                "Namespace SDC.Framework",
                "    Public Class " & pageName,
                "        Inherits FW_Base_B",
                "",
                "        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)",
                "            MyBase.New(user, profile, """ & EscapeLiteral(tableName) & """)",
                "        End Sub",
                "",
                If(useQbeOnly,
                   String.Join(Environment.NewLine, {
                       "        Protected Overrides Function OnlyUseQbe() As Boolean",
                       "            Return True",
                       "        End Function",
                       ""
                   }),
                   String.Empty),
                If(generateMaintenancePage,
                   String.Join(Environment.NewLine, {
                       "        Protected Overrides Function CreateMaintenancePage(recordId As Integer) As FW_Base_U",
                       "            Return New " & maintenancePageNameForBrowse(pageName) & "(recordId, CurrentUserContext, CurrentAccessProfile)",
                       "        End Function",
                       ""
                   }),
                   String.Empty),
                "        Protected Overrides Function UsesStandardSoftDelete() As Boolean",
                "            Return True",
                "        End Function",
                "    End Class",
                "End Namespace",
                ""
            })
        End Function

        Private Shared Function maintenancePageNameForBrowse(browsePageName As String) As String
            Return If(browsePageName.EndsWith("_B", StringComparison.OrdinalIgnoreCase), browsePageName.Substring(0, browsePageName.Length - 2) & "_U", browsePageName & "_U")
        End Function

        ''' <summary>
        ''' Which of this page's fields the database computes for itself.
        '''
        ''' They are still put on the page, because FirstLast is worth reading on a saved record,
        ''' but they are never editable and never required. SQL Server refuses any write naming a
        ''' computed column, and on a new record the value does not exist until after the save - so
        ''' a required computed field can never be satisfied and would block the save on its own.
        ''' </summary>
        ''' <summary>
        ''' The first of the page's fields whose name matches one of the candidates, or empty.
        '''
        ''' Names rather than types, because that is all the generator knows: a column called City
        ''' is a city. Case-insensitive, and the candidates are tried in order, so the most usual
        ''' spelling wins when a table carries two.
        ''' </summary>
        ''' <summary>
        ''' The address block among a page's fields - the street line, City, State and Zip - by the
        ''' names this generator recognises.
        '''
        ''' Public because the field picker suggests a column split and must not break this group
        ''' across it: Smarty types ahead on the street line and fills the other three, and the Zip
        ''' Coder button sits against Zip, so a City on one side of the page and its Zip on the
        ''' other is worse than an uneven split. One list, read by the code that wires the
        ''' controllers and by the code that decides where they sit.
        ''' </summary>
        Friend Shared Function AddressGroupFields(fields As List(Of String)) As List(Of String)
            Dim group As New List(Of String)()
            For Each candidates In New String()() {
                New String() {"Address1", "Address", "StreetAddress", "Street"},
                New String() {"Address2"},
                New String() {"City"},
                New String() {"State", "StateCode", "StateAbbrev"},
                New String() {"Zip", "ZipCode", "PostalCode"}}
                Dim match = MatchField(fields, candidates)
                If match <> String.Empty Then group.Add(match)
            Next

            ' The street line alone is not a block worth protecting - it is the four together that
            ' the controllers act on, and a table with only an Address column has nothing to split.
            Return If(group.Count >= 3, group, New List(Of String)())
        End Function

        Private Shared Function MatchField(fields As List(Of String), ParamArray candidates As String()) As String
            For Each candidate In candidates
                Dim match = fields.FirstOrDefault(Function(field) String.Equals(field, candidate, StringComparison.OrdinalIgnoreCase))
                If match IsNot Nothing Then Return match
            Next

            Return String.Empty
        End Function

        ''' <param name="columnTwoFields">
        ''' The fields that go in the second column. Empty is a one-column page, which is what
        ''' every request written before the column map existed reads as.
        '''
        ''' Order is not taken from here. <paramref name="fields"/> already carries the order the
        ''' request arranged, and each column reads that one list - so the fields cannot be in one
        ''' order down the page and another in the tab sequence.
        ''' </param>
        ''' <summary>
        ''' The half of a maintenance page that belongs to whoever is building it.
        '''
        ''' Written once, on the first generation, and never rewritten - which is the whole point.
        ''' A generated page used to be a single file, so anything added by hand was lost the next
        ''' time the fields changed, and the only protection was three confirmations warning that
        ''' it was about to happen. Here there is nothing to warn about.
        '''
        ''' Deliberately small. Everything that could be regenerated is in the other file, and
        ''' this one holds only what a constructor needs, so it cannot go stale as the generator
        ''' learns new tricks. Custom code goes below, reaching the generated half through the
        ''' hooks declared at the end of it.
        ''' </summary>
        Private Shared Function BuildMaintenanceCompanionSource(pageName As String) As String
            Dim output As New StringBuilder()
            output.AppendLine("Option Strict On")
            output.AppendLine("Option Explicit On")
            output.AppendLine()
            output.AppendLine("Imports System.Collections.Generic")
            output.AppendLine("Imports System.Data")
            output.AppendLine("Imports System.Drawing")
            output.AppendLine("Imports System.Windows.Forms")
            output.AppendLine()
            output.AppendLine("Namespace SDC.Framework")
            output.AppendLine()
            output.AppendLine("    ''' <summary>")
            output.AppendLine("    ''' This file is yours. The page generator writes " & pageName & ".Generated.vb and never")
            output.AppendLine("    ''' reads this one, so anything added here survives the page gaining or losing fields.")
            output.AppendLine("    '''")
            output.AppendLine("    ''' To reach into the generated half, implement OnFieldsBuilt, OnRecordBound,")
            output.AppendLine("    ''' OnValidating or OnBeforeSave - all four are declared at the end of that file.")
            output.AppendLine("    ''' </summary>")
            output.AppendLine("    Partial Public Class " & pageName)
            output.AppendLine("        Inherits FW_Base_U")
            output.AppendLine()
            output.AppendLine("        Private ReadOnly recordId As Integer")
            output.AppendLine("        Private ReadOnly currentUser As UserContext")
            output.AppendLine("        Private ReadOnly accessProfile As AccessProfile")
            output.AppendLine()
            output.AppendLine("        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)")
            output.AppendLine("            MyBase.New()")
            output.AppendLine("            recordId = id")
            output.AppendLine("            currentUser = user")
            output.AppendLine("            accessProfile = profile")
            output.AppendLine("            BuildGeneratedFields()")
            output.AppendLine("            BindToForm()")
            output.AppendLine("            ApplyMode()")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("    End Class")
            output.AppendLine("End Namespace")
            Return output.ToString()
        End Function

        Private Shared Function BuildMaintenanceSource(pageName As String, tableName As String, primaryKey As String, fields As List(Of String), requiredFields As List(Of String), lookupFields As List(Of LookupFieldSpec), Optional columnTwoFields As List(Of String) = Nothing) As String
            Dim computedColumns = DataAccess.GetComputedColumnNames(tableName)
            Dim computedOnPage = fields.Where(Function(field) computedColumns.Contains(field)).ToList()

            ' Which fields are dates, from the schema rather than from their names. A date column
            ' used to get a plain text box - which is how an empty Termination Date reached a
            ' datetime parameter as "" and failed the save naming no field at all.
            '
            ' A lookup wins: a foreign key that happens to point at a date table is still a
            ' choice from a list, and a computed date is never typed into.
            ' The employee table carries a login, and a login is useless without a role. The
            ' page therefore asks for one, which no column on the table could have told the
            ' field picker - a role lives in FW_EmployeeRoles. Special-cased the same way the
            ' address block and the password already are.
            Dim carriesLogin = String.Equals(tableName.Trim(), "FW_Employees", StringComparison.OrdinalIgnoreCase)

            Dim dateKinds = DataAccess.GetDateColumnKinds(tableName)
            Dim nullableColumns = DataAccess.GetNullableColumnNames(tableName)
            Dim dateFields = fields.Where(Function(field) dateKinds.ContainsKey(field) AndAlso
                                                          Not IsLookupField(field, lookupFields) AndAlso
                                                          Not computedColumns.Contains(field)).ToList()

            ' A bit column is a yes or a no, and it gets a check box. As a text box it asked
            ' somebody to type True and would accept anything.
            Dim bitColumns = DataAccess.GetBitColumnNames(tableName)
            Dim bitFields = fields.Where(Function(field) bitColumns.Contains(field) AndAlso
                                                         Not IsLookupField(field, lookupFields) AndAlso
                                                         Not computedColumns.Contains(field)).ToList()
            ' Worked out before anything is written, because the class needs to declare where its
            ' fields stop and that is a field declaration - it cannot wait for the layout section
            ' further down.
            Dim rightFields = If(columnTwoFields Is Nothing,
                                 New List(Of String)(),
                                 fields.Where(Function(field) columnTwoFields.Any(Function(item) String.Equals(item, field, StringComparison.OrdinalIgnoreCase))).ToList())
            Dim leftFields = fields.Where(Function(field) Not rightFields.Contains(field)).ToList()
            Dim twoColumns = rightFields.Count > 0
            Dim rowsDown = Math.Max(leftFields.Count, rightFields.Count)

            Dim output As New StringBuilder()
            output.AppendLine("' <auto-generated>")
            output.AppendLine("' Written by the page generator. Every edit here is lost the next time the fields")
            output.AppendLine("' are applied. Put your own code in " & pageName & ".vb, which is never rewritten,")
            output.AppendLine("' and use the hooks at the end of this file to reach into what is generated.")
            output.AppendLine("' </auto-generated>")
            output.AppendLine("Option Strict On")
            output.AppendLine("Option Explicit On")
            output.AppendLine()
            output.AppendLine("Imports System.Collections.Generic")
            output.AppendLine("Imports System.ComponentModel")
            output.AppendLine("Imports System.Data")
            output.AppendLine("Imports System.Drawing")
            output.AppendLine("Imports System.Windows.Forms")
            output.AppendLine()
            output.AppendLine("Namespace SDC.Framework")
            output.AppendLine("    Partial Public Class " & pageName)
            output.AppendLine()
            output.AppendLine("        Private ReadOnly tableName As String = """ & EscapeLiteral(tableName) & """")
            output.AppendLine("        Private ReadOnly primaryKey As String = """ & EscapeLiteral(primaryKey) & """")
            If computedOnPage.Count > 0 Then
                output.AppendLine("        Private ReadOnly computedFields As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {" &
                                  String.Join(", ", computedOnPage.Select(Function(field) """" & EscapeLiteral(field) & """")) & "}")
            Else
                output.AppendLine("        Private ReadOnly computedFields As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)")
            End If
            ' Where the generated fields stop, for whatever the companion puts underneath. Told
            ' rather than guessed: the companion cannot work it out without knowing the column
            ' split, and a hard-coded number in that file would be wrong the moment a field is
            ' added - the exact drift the split exists to prevent.
            output.AppendLine("        Protected ReadOnly GeneratedFieldsBottom As Integer = " & (20 + rowsDown * 42).ToString())
            output.AppendLine("        Private record As DataRow")
            output.AppendLine("        Private ReadOnly formBindingSource As New BindingSource()")
            output.AppendLine("        Private originalRowVersion As Byte()")

            ' An address page gets the address behaviour the framework already has, without anybody
            ' remembering to ask for it: Smarty type-ahead on the street line, and the Zip Coder
            ' button beside Zip for when Smarty is switched off. Both exist as controllers and are
            ' wired exactly as Registration_U wires them.
            Dim addressField = MatchField(fields, "Address1", "Address", "StreetAddress", "Street")
            Dim cityField = MatchField(fields, "City")
            Dim stateField = MatchField(fields, "State", "StateCode", "StateAbbrev")
            Dim zipField = MatchField(fields, "Zip", "ZipCode", "PostalCode")
            Dim hasCityStateZip = cityField <> String.Empty AndAlso stateField <> String.Empty AndAlso zipField <> String.Empty
            Dim wantsZipCoder = hasCityStateZip AndAlso
                                Not IsLookupField(cityField, lookupFields) AndAlso
                                Not IsLookupField(stateField, lookupFields) AndAlso
                                Not IsLookupField(zipField, lookupFields)
            Dim wantsSmarty = wantsZipCoder AndAlso addressField <> String.Empty AndAlso Not IsLookupField(addressField, lookupFields)

            If wantsZipCoder Then
                output.AppendLine("        Private zipCoderController As ZipCoderController")
            End If
            If wantsSmarty Then
                output.AppendLine("        Private smartyAddressLookupController As SmartyAddressLookupController")
            End If
            For Each field In fields
                ' A placeholder is a row, not a control the page holds a reference to. The blank
                ' line emits nothing at all and the dividing line is added and forgotten.
                If IsPlaceholderField(field) Then Continue For

                If IsLookupField(field, lookupFields) Then
                    output.AppendLine("        Private " & LookupControlVariable(field) & " As ComboBox")
                ElseIf dateFields.Contains(field) Then
                    output.AppendLine("        Private " & DateControlVariable(field) & " As DateTimePicker")
                ElseIf bitFields.Contains(field) Then
                    output.AppendLine("        Private " & CheckControlVariable(field) & " As CheckBox")
                Else
                    output.AppendLine("        Private " & ControlVariable(field) & " As TextBox")
                End If
            Next
            output.AppendLine()
            output.AppendLine("        ''' <summary>")
            output.AppendLine("        ''' Every control on the page, laid out. Called from the constructor in " & pageName & ".vb.")
            output.AppendLine("        ''' </summary>")
            output.AppendLine("        Private Sub BuildGeneratedFields()")
            ' No Text assignment: Base_U builds the caption from the page name and the mode, so a
            ' generated page opens as "Edit Entity X" rather than "EntityX_U". A generated page that
            ' needs its own wording overrides BuildMaintenanceTitle.
            ' Two columns when the request put something on the right, one when it did not -
            ' which is every request written before the column map existed, so those generate the
            ' page they always did, to the pixel.
            '
            ' Each column reads the same field list in the same order, so the page runs down the
            ' left and then down the right, and the tab order is that same sequence. There is no
            ' second ordering to keep in step with the first.
            ' A column is the label (120), the gap to its control (10) and the control (320).
            Const ColumnWidth As Integer = 450
            ' The Zip Coder button sits past the right edge of the Zip box, so whichever column
            ' holds Zip needs the room: between the two columns, or past the edge of the form.
            Dim zipOnTheRight = wantsZipCoder AndAlso rightFields.Any(Function(field) String.Equals(field, zipField, StringComparison.OrdinalIgnoreCase))
            Dim columnTwoLeft = 20 + ColumnWidth + If(wantsZipCoder AndAlso Not zipOnTheRight, 110, 40)
            Dim formWidth = If(twoColumns, columnTwoLeft + ColumnWidth + If(zipOnTheRight, 140, 30), 600)
            ' Room below the fields for whatever the page's own file puts there. An employee
            ' page hosts the role selector; every other page leaves it empty and costs nothing,
            ' because the generator cannot know what a companion will add and a page that has to
            ' resize itself afterwards flickers on every open.
            Dim extraBelowFields = If(carriesLogin, EmployeeRolesSelector.PanelHeight + 16, 0)

            output.AppendLine("            ClientSize = New Size(" & formWidth.ToString() & ", " & (Math.Max(120, 55 + rowsDown * 42) + extraBelowFields).ToString() & ")")
            output.AppendLine("            okButton.Location = New Point(ClientSize.Width - 270, ClientSize.Height - 46)")
            output.AppendLine("            cancelActionButton.Location = New Point(ClientSize.Width - 135, ClientSize.Height - 46)")

            Dim emitColumn =
                Sub(columnFields As List(Of String), fieldLeft As Integer)
                    Dim y = 20
                    For Each field In columnFields
                        ' A computed field is never required, whatever the request says. An older
                        ' saved request can still carry one, so the refusal is here as well as in
                        ' the grid that offers the tick - the page must not be generated with a
                        ' rule it cannot satisfy.
                        Dim isRequired = requiredFields.Any(Function(item) String.Equals(item, field, StringComparison.OrdinalIgnoreCase)) AndAlso
                                         Not computedColumns.Contains(field)

                        If IsBlankLinePlaceholder(field) Then
                            ' Nothing is emitted. The row exists because y moves on, which is all
                            ' a blank line is - and it costs the page no control to collapse, no
                            ' name to map and nothing for the unmapped-field report to find.
                            output.AppendLine("            ' " & field & " - vertical space, no control")
                            y += 42
                            Continue For
                        ElseIf IsDividerPlaceholder(field) Then
                            Dim dividerName = "Label_Divider" & PlaceholderNumber(field).ToString()

                            ' At exactly y, not centred in the row. The row's Top is the pitch:
                            ' CollapseHiddenFieldRows consumes RowTop(next) - RowTop(current) when
                            ' a row above is hidden, so a rule dropped 12 pixels to look centred
                            ' would have that row consume 54 instead of 42 and pull every row
                            ' below it out of step.
                            '
                            ' Named Label_ deliberately, though it names no column. IsFieldControl
                            ' matches on that prefix, and a control the field grid does not
                            ' recognise is treated as trailing furniture and moved as a block
                            ' below the fields the moment a permission hides anything. The price
                            ' is the unmapped-field report, which DeclareUnboundField answers.
                            output.AppendLine("            Controls.Add(New Label() With {")
                            output.AppendLine("                .Name = """ & dividerName & """,")
                            output.AppendLine("                .AutoSize = False,")
                            output.AppendLine("                .Text = String.Empty,")
                            output.AppendLine("                .Location = New Point(" & fieldLeft.ToString() & ", " & y.ToString() & "),")
                            output.AppendLine("                .Size = New Size(450, 2),")
                            output.AppendLine("                .BackColor = SystemColors.ControlDark")
                            output.AppendLine("            })")
                            output.AppendLine("            DeclareUnboundField(""" & dividerName & """, ""A dividing line between groups of fields. It names no column."")")
                            y += 42
                            Continue For
                        End If

                        If bitFields.Contains(field) Then
                            output.AppendLine("            " & CheckControlVariable(field) & " = AddCheckField(""" & EscapeLiteral(field) & """, " &
                                              y.ToString() & ", " & fieldLeft.ToString() & ")")
                        ElseIf dateFields.Contains(field) Then
                            ' Date-only unless the column carries a time. A hire date shown as
                            ' "15/03/2026 00:00" puts a time on screen that nobody entered.
                            Dim showTime = Not String.Equals(dateKinds(field), "date", StringComparison.OrdinalIgnoreCase)
                            Dim nullable = nullableColumns.Contains(field)
                            output.AppendLine("            " & DateControlVariable(field) & " = AddDateField(""" & EscapeLiteral(field) & """, " & y.ToString() & ", " &
                                              If(isRequired, "True", "False") & ", " & fieldLeft.ToString() & ", " &
                                              If(nullable, "True", "False") & ", " & If(showTime, "True", "False") & ")")
                        ElseIf IsLookupField(field, lookupFields) Then
                            ' A foreign key goes through AddComboField for the same reason a plain
                            ' field goes through AddField: the helper paints the App Admin blue
                            ' when required, adds the marker, registers the required border and
                            ' names both controls to the convention. Built by hand, as this used
                            ' to be, a required lookup got the asterisk but never the blue - and
                            ' since ShouldSkipBrRequiredStyling decides App Admin ownership by
                            ' that blue, the field silently lost its precedence.
                            output.AppendLine("            " & LookupControlVariable(field) & " = AddComboField(""" & EscapeLiteral(field) & """, " & y.ToString() & ", " & If(isRequired, "True", "False") & ", " & fieldLeft.ToString() & ", 320)")
                        Else
                            output.AppendLine("            " & ControlVariable(field) & " = AddField(""" & EscapeLiteral(field) & """, " & y.ToString() & ", False, " & If(isRequired, "True", "False") & ", " & fieldLeft.ToString() & ")")
                        End If

                        y += 42
                    Next
                End Sub

            emitColumn(leftFields, 20)
            If twoColumns Then emitColumn(rightFields, columnTwoLeft)

            ' A single Role combo lived here briefly and came out again: one combo cannot show
            ' somebody holding two roles, and it put the question on the generator's side of the
            ' line where it could not be arranged to suit a page. Roles are chosen through the
            ' EmployeeRolesSelector widget, attached in the page's own file.
            ' Placeholders hold no control and take no tab stop. Left in, the emitted call would
            ' name a variable that was never declared.
            Dim tabOrder = leftFields.Concat(rightFields).
                Where(Function(field) Not IsPlaceholderField(field)).
                Select(Function(field) FieldControlVariable(field, lookupFields, dateFields, bitFields)).ToList()
            output.AppendLine("            SetManualTabOrder(" & String.Join(", ", tabOrder.Concat({"okButton", "cancelActionButton"})) & ")")
            output.AppendLine("            BindToForm()")
            output.AppendLine("            ApplyMode()")

            ' After ApplyMode, as Registration_U does: the Zip Coder button places itself against
            ' the Zip box's final position, and Smarty's suggestion list is sized from the address
            ' box - both need the layout settled.
            If wantsSmarty Then
                output.AppendLine("            smartyAddressLookupController = New SmartyAddressLookupController(")
                output.AppendLine("                Me,")
                output.AppendLine("                " & ControlVariable(addressField) & ",")
                output.AppendLine("                " & ControlVariable(cityField) & ",")
                output.AppendLine("                " & ControlVariable(stateField) & ",")
                output.AppendLine("                " & ControlVariable(zipField) & ",")
                output.AppendLine("                Function() SmartyAddressLookupController.IsSessionLookupEnabled(),")
                output.AppendLine("                Function() SmartyAddressLookupController.GetSessionEmbeddedKey())")
            End If
            If wantsZipCoder Then
                ' The button hides itself when Smarty is on - the controller applies that rule - so
                ' the two never both offer to fill the same three boxes.
                output.AppendLine("            zipCoderController = New ZipCoderController(Me, " &
                                  ControlVariable(cityField) & ", " &
                                  ControlVariable(stateField) & ", " &
                                  ControlVariable(zipField) & ")")
            End If

            output.AppendLine()
            output.AppendLine("            OnFieldsBuilt()")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Function GetTableNameOverride() As String")
            output.AppendLine("            Return tableName")
            output.AppendLine("        End Function")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Sub BindToFormInternal()")
            output.AppendLine("            Dim loaded = DataAccess.LoadGeneratedPageRecord(tableName, primaryKey, recordId)")
            output.AppendLine("            If loaded IsNot Nothing Then")
            output.AppendLine("                record = loaded")
            output.AppendLine("            Else")
            output.AppendLine("                Dim schema = DataAccess.GetGeneratedPageSchema(tableName)")
            output.AppendLine("                record = schema.NewRow()")
            output.AppendLine("                schema.Rows.Add(record)")
            output.AppendLine("            End If")
            output.AppendLine("            formBindingSource.DataSource = record.Table")
            output.AppendLine("            formBindingSource.Position = record.Table.Rows.IndexOf(record)")
            ' Everything that is not a lookup, a date or a check box gets a text box - which a
            ' placeholder would fall into by default, and it has no control to bind.
            Dim textFields = fields.Where(Function(field) Not IsPlaceholderField(field) AndAlso
                                                          Not IsLookupField(field, lookupFields) AndAlso
                                                          Not dateFields.Contains(field) AndAlso
                                                          Not bitFields.Contains(field)).ToList()
            If textFields.Count > 0 Then
                output.AppendLine("            For Each control In New Control() {" & String.Join(", ", textFields.Select(Function(field) ControlVariable(field))) & "}")
                output.AppendLine("                Dim fieldName = control.Name.Substring(""TextBox_"".Length)")
                output.AppendLine("                control.DataBindings.Clear()")
                output.AppendLine("                control.DataBindings.Add(""Text"", formBindingSource, fieldName, True, DataSourceUpdateMode.Never)")
                output.AppendLine("                If record.Table.Columns.Contains(fieldName) Then control.Text = If(record(fieldName) Is DBNull.Value, String.Empty, Convert.ToString(record(fieldName)))")
                output.AppendLine("            Next")
            End If

            ' Set rather than data-bound. A DateTimePicker has no Text binding worth having: it
            ' cannot hold an empty string, and its check box - not its value - is what says the
            ' column is null.
            For Each field In dateFields
                output.AppendLine("            If record.Table.Columns.Contains(""" & EscapeLiteral(field) & """) Then SetDateField(" &
                                  DateControlVariable(field) & ", record(""" & EscapeLiteral(field) & """))")
            Next

            For Each field In bitFields
                output.AppendLine("            If record.Table.Columns.Contains(""" & EscapeLiteral(field) & """) Then SetCheckField(" &
                                  CheckControlVariable(field) & ", record(""" & EscapeLiteral(field) & """))")
            Next

            For Each spec In lookupFields.Where(Function(item) fields.Any(Function(field) String.Equals(field, item.FieldName, StringComparison.OrdinalIgnoreCase)))
                output.AppendLine("            ConfigureLookupCombo(" & LookupControlVariable(spec.FieldName) &
                                  ", DataAccess.GetLookupTable(""" & EscapeLiteral(spec.LookupTable) & """, """ & EscapeLiteral(spec.ValueColumn) & """, """ & EscapeLiteral(spec.DisplayColumn) & """, " &
                                  If(spec.FilterByRegistration, "True", "False") &
                                  ", CurrentLookupId(""" & EscapeLiteral(spec.FieldName) & """))" &
                                  ", """ & EscapeLiteral(spec.ValueColumn) & """, """ & EscapeLiteral(spec.DisplayColumn) & """, CurrentLookupId(""" & EscapeLiteral(spec.FieldName) & """))")
            Next
            If fields.Any(Function(field) String.Equals(field, "RegistrationID", StringComparison.OrdinalIgnoreCase)) Then
                output.AppendLine("            If recordId <= 0 AndAlso record.Table.Columns.Contains(""RegistrationID"") AndAlso SessionState.IsActive AndAlso SessionState.Current.HasValue Then")
                output.AppendLine("                Dim sessionRegistrationId = SessionState.WorkingRegistrationID()")
                output.AppendLine("                If sessionRegistrationId > 0 AndAlso String.IsNullOrWhiteSpace(registrationIDTextBox.Text) Then registrationIDTextBox.Text = sessionRegistrationId.ToString(Globalization.CultureInfo.InvariantCulture)")
                output.AppendLine("            End If")
            End If
            output.AppendLine("            If record.Table.Columns.Contains(""RowVersion"") AndAlso Not record.IsNull(""RowVersion"") Then originalRowVersion = CType(DirectCast(record(""RowVersion""), Byte()).Clone(), Byte())")
            output.AppendLine("            CaptureOriginalRowVersion(originalRowVersion)")
            output.AppendLine()
            output.AppendLine("            OnRecordBound()")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        ''' <summary>")
            output.AppendLine("        ''' The key and any computed column are shown but never edited. Typing into a computed")
            output.AppendLine("        ''' column invites a value the database would refuse and then discard.")
            output.AppendLine("        ''' </summary>")
            output.AppendLine("        Protected Overrides Sub ApplyMode()")
            output.AppendLine("            For Each control In Controls.OfType(Of TextBox)()")
            output.AppendLine("                Dim columnName = If(control.Name.StartsWith(""TextBox_"", StringComparison.OrdinalIgnoreCase), control.Name.Substring(""TextBox_"".Length), String.Empty)")
            output.AppendLine("                Dim isComputed = computedFields.Contains(columnName)")
            output.AppendLine("                control.ReadOnly = String.Equals(columnName, primaryKey, StringComparison.OrdinalIgnoreCase) OrElse isComputed")
            output.AppendLine("                If isComputed Then ShowComputedFieldHint(control)")
            output.AppendLine("            Next")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Function TryBuildRecord() As Boolean")
            output.AppendLine("            Dim allowSave = True")
            output.AppendLine("            OnValidating(allowSave)")
            output.AppendLine("            Return allowSave")
            output.AppendLine("        End Function")
            output.AppendLine()
            output.AppendLine("        ''' <summary>Warns before discarding edits. Without this Cancel would discard silently.</summary>")
            output.AppendLine("        Protected Overrides Function ShouldWarnOnCancel() As Boolean")
            output.AppendLine("            Return True")
            output.AppendLine("        End Function")
            output.AppendLine()
            output.AppendLine("        ''' <summary>")
            output.AppendLine("        ''' Field-level permissions choose Can_Create over Can_Update from this. Without it")
            output.AppendLine("        ''' every new record would be evaluated as an update and Can_Create would never apply.")
            output.AppendLine("        ''' </summary>")
            output.AppendLine("        Protected Overrides Function IsCreatingNewRecord() As Boolean")
            output.AppendLine("            Return recordId <= 0")
            output.AppendLine("        End Function")
            output.AppendLine()

            If lookupFields.Any(Function(item) fields.Any(Function(field) String.Equals(field, item.FieldName, StringComparison.OrdinalIgnoreCase))) Then
                output.AppendLine("        Private Function CurrentLookupId(fieldName As String) As Integer")
                output.AppendLine("            If record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(fieldName) OrElse record.IsNull(fieldName) Then Return 0")
                output.AppendLine("            Dim value As Integer")
                output.AppendLine("            Return If(Integer.TryParse(Convert.ToString(record(fieldName)), value), value, 0)")
                output.AppendLine("        End Function")
                output.AppendLine()
            End If

            output.AppendLine("        Protected Overrides Function SaveRecord() As Boolean")
            output.AppendLine("            Dim values As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)")
            For Each field In fields
                ' A placeholder writes no column. Left in, the save would name one the table does
                ' not have and take its value from a control that was never declared.
                If IsPlaceholderField(field) Then Continue For

                If IsLookupField(field, lookupFields) Then
                    output.AppendLine("            values(""" & EscapeLiteral(field) & """) = GetComboSelectedIdOrNull(" & LookupControlVariable(field) & ")")
                ElseIf dateFields.Contains(field) Then
                    output.AppendLine("            values(""" & EscapeLiteral(field) & """) = DateFieldValue(" & DateControlVariable(field) & ")")
                ElseIf bitFields.Contains(field) Then
                    output.AppendLine("            values(""" & EscapeLiteral(field) & """) = CheckFieldValue(" & CheckControlVariable(field) & ")")
                Else
                    output.AppendLine("            values(""" & EscapeLiteral(field) & """) = " & ControlVariable(field) & ".Text")
                End If
            Next
            output.AppendLine()
            output.AppendLine("            OnBeforeSave(values)")
            output.AppendLine()
            output.AppendLine("            Dim savedId As Integer = recordId")
            output.AppendLine("            If savedId <= 0 AndAlso record.Table.Columns.Contains(primaryKey) AndAlso Not record.IsNull(primaryKey) Then Integer.TryParse(Convert.ToString(record(primaryKey)), savedId)")
            output.AppendLine("            ' The acting user: while an administrator views as somebody else, the")
            output.AppendLine("            ' change is theirs and is stamped with their id.")
            output.AppendLine("            Dim updatedBy = SessionState.ActingUserID")
            output.AppendLine()
            output.AppendLine("            Dim outcome As SaveResult")
            output.AppendLine("            Dim savedRecordId = DataAccess.TrySaveGeneratedPageRecord(tableName, primaryKey, savedId, values, originalRowVersion, updatedBy, outcome)")
            output.AppendLine()
            output.AppendLine("            If outcome = SaveResult.RecordDeleted Then")
            output.AppendLine("                MessageBox.Show(Me, ""The record no longer exists."", ""Save Failed"", MessageBoxButtons.OK, MessageBoxIcon.Warning)")
            output.AppendLine("                Return False")
            output.AppendLine("            End If")
            output.AppendLine()
            output.AppendLine("            ' A conflict is the user's decision, never last-saved-wins. Declining keeps the")
            output.AppendLine("            ' page open with the edits intact.")
            output.AppendLine("            If outcome = SaveResult.RecordChanged Then")
            output.AppendLine("                If Not ConfirmConcurrencyOverwrite() Then Return False")
            output.AppendLine()
            output.AppendLine("                Dim latest = DataAccess.LoadGeneratedPageRecord(tableName, primaryKey, savedId)")
            output.AppendLine("                If latest Is Nothing Then")
            output.AppendLine("                    MessageBox.Show(Me, ""The record no longer exists."", ""Save Failed"", MessageBoxButtons.OK, MessageBoxIcon.Warning)")
            output.AppendLine("                    Return False")
            output.AppendLine("                End If")
            output.AppendLine()
            output.AppendLine("                originalRowVersion = If(latest.Table.Columns.Contains(""RowVersion"") AndAlso Not latest.IsNull(""RowVersion""),")
            output.AppendLine("                                        CType(DirectCast(latest(""RowVersion""), Byte()).Clone(), Byte()), Nothing)")
            output.AppendLine("                CaptureOriginalRowVersion(originalRowVersion)")
            output.AppendLine("                savedRecordId = DataAccess.TrySaveGeneratedPageRecord(tableName, primaryKey, savedId, values, originalRowVersion, updatedBy, outcome)")
            output.AppendLine("            End If")
            output.AppendLine()
            output.AppendLine("            If outcome <> SaveResult.Succeeded OrElse savedRecordId <= 0 Then Return False")
            output.AppendLine("            If record Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) Then Return False")
            output.AppendLine("            record(primaryKey) = savedRecordId")
            output.AppendLine("            Return True")
            output.AppendLine("        End Function")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Function ResolveAuditRecordKey() As String")
            output.AppendLine("            Return If(record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) OrElse record.IsNull(primaryKey), String.Empty, Convert.ToString(record(primaryKey)))")
            output.AppendLine("        End Function")
            output.AppendLine()
            output.AppendLine("        ' Hooks. Implement any of these in " & pageName & ".vb to reach into what is")
            output.AppendLine("        ' generated above. One nobody implements compiles away to nothing, and a page")
            output.AppendLine("        ' using none of them carries no cost at all.")
            output.AppendLine()
            output.AppendLine("        ''' <summary>Every generated control exists and the tab order is set.</summary>")
            output.AppendLine("        Partial Private Sub OnFieldsBuilt()")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        ''' <summary>The record is loaded and every generated control is bound to it.</summary>")
            output.AppendLine("        Partial Private Sub OnRecordBound()")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        ''' <summary>")
            output.AppendLine("        ''' A save has been asked for and nothing is written yet. Set allowSave to False")
            output.AppendLine("        ''' to refuse it, which leaves the page open with its edits intact.")
            output.AppendLine("        '''")
            output.AppendLine("        ''' ByRef rather than a return value, because a VB partial method cannot return")
            output.AppendLine("        ''' one - an unimplemented partial method leaves no call site to take a result")
            output.AppendLine("        ''' from.")
            output.AppendLine("        ''' </summary>")
            output.AppendLine("        Partial Private Sub OnValidating(ByRef allowSave As Boolean)")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        ''' <summary>The values are built and nothing is written. Add, change or remove entries.</summary>")
            output.AppendLine("        Partial Private Sub OnBeforeSave(values As Dictionary(Of String, Object))")
            output.AppendLine("        End Sub")
            output.AppendLine("    End Class")
            output.AppendLine("End Namespace")
            Return output.ToString()
        End Function

        ''' <summary>
        ''' A foreign-key field and where its list comes from, parsed from the page request format
        ''' documented in new-page-request-manual.md:
        '''     ManagerID -&gt; FW_Users.UserID displayed as FirstLast
        ''' </summary>
        Private Class LookupFieldSpec
            Public Property FieldName As String
            Public Property LookupTable As String
            Public Property ValueColumn As String
            Public Property DisplayColumn As String

            ''' <summary>
            ''' Whether the list is scoped to the session's registration. Written as an optional
            ''' " filtered by registration" on the end of the spec, so every request written before
            ''' this existed still parses and still filters - the default is on, because a lookup
            ''' table with a RegistrationID almost always means it.
            ''' </summary>
            Public Property FilterByRegistration As Boolean = True
        End Class

        ''' <summary>
        ''' Parses lookup specifications. Anything that does not match the documented format is
        ''' reported rather than ignored: before this, the whole line was treated as a field name,
        ''' failed the "field does not exist" check, and lookups silently became plain text boxes.
        ''' </summary>
        Private Shared Function ParseLookupFields(value As String, errors As List(Of String)) As List(Of LookupFieldSpec)
            Dim specs As New List(Of LookupFieldSpec)()

            For Each entry In value.Split({","c, ";"c, ChrW(10), ChrW(13)}, StringSplitOptions.RemoveEmptyEntries).
                                    Select(Function(item) item.Trim()).
                                    Where(Function(item) item <> String.Empty AndAlso
                                                         Not String.Equals(item, "Not specified", StringComparison.OrdinalIgnoreCase) AndAlso
                                                         Not String.Equals(item, "None", StringComparison.OrdinalIgnoreCase))

                Dim match = Regex.Match(entry,
                                        "^\s*(?<field>\w+)\s*->\s*(?<table>\w+)\s*\.\s*(?<value>\w+)\s+displayed\s+as\s+(?<display>\w+)(?<scope>\s+(?:filtered|not\s+filtered)\s+by\s+registration)?\s*$",
                                        RegexOptions.IgnoreCase)

                If Not match.Success Then
                    errors.Add("Lookup field is not in the expected format '<Field> -> <Table>.<ValueColumn> displayed as <DisplayColumn>', optionally followed by 'filtered by registration' or 'not filtered by registration': " & entry)
                    Continue For
                End If

                ' Absent means filtered. Every spec written before the suffix existed was filtered
                ' by the runtime rule, so reading them as unfiltered would change what those pages
                ' show without anyone editing them.
                Dim scope = match.Groups("scope").Value
                specs.Add(New LookupFieldSpec With {
                    .FieldName = match.Groups("field").Value,
                    .LookupTable = match.Groups("table").Value,
                    .ValueColumn = match.Groups("value").Value,
                    .DisplayColumn = match.Groups("display").Value,
                    .FilterByRegistration = Not Regex.IsMatch(scope, "not\s+filtered", RegexOptions.IgnoreCase)
                })
            Next

            Return specs
        End Function

        ''' <summary>
        ''' How many of each placeholder the _U field grid offers. A fixed pool rather than a
        ''' button that makes another: the pool needs no new control, no removal path, and it
        ''' reaches the grid through the same seeding loop every field does.
        ''' </summary>
        Public Const PlaceholderPoolSize As Integer = 4

        Private Const BlankLinePlaceholderPrefix As String = "(blank line "
        Private Const DividerPlaceholderPrefix As String = "(dividing line "

        ''' <summary>
        ''' Vertical space on a generated _U, carried in MaintenanceFields as though it were a
        ''' field. That is the whole trick: an entry in that list is ordered by Move Up and Move
        ''' Down and assigned a column by the Column cell, so a placeholder inherits both without
        ''' either being taught about it.
        '''
        ''' The cost is the one the Column cell's own comment warns about - a row in the grid that
        ''' is not a field has to be skipped by every loop that reads a field name. The loops that
        ''' matter are the schema validation, the control declarations, the field emitter, the tab
        ''' order and the form binding; each one names this helper rather than testing the text.
        '''
        ''' Numbered because MaintenanceFields is keyed by name - ParseFields applies Distinct, and
        ''' OrderSelectionGrid keys its saved positions by name, so two blank lines sharing one
        ''' would collapse into a single row.
        ''' </summary>
        Public Shared Function BlankLinePlaceholder(number As Integer) As String
            Return BlankLinePlaceholderPrefix & number.ToString() & ")"
        End Function

        Public Shared Function DividerPlaceholder(number As Integer) As String
            Return DividerPlaceholderPrefix & number.ToString() & ")"
        End Function

        Public Shared Function IsBlankLinePlaceholder(field As String) As Boolean
            Return field IsNot Nothing AndAlso field.Trim().StartsWith(BlankLinePlaceholderPrefix, StringComparison.OrdinalIgnoreCase)
        End Function

        Public Shared Function IsDividerPlaceholder(field As String) As Boolean
            Return field IsNot Nothing AndAlso field.Trim().StartsWith(DividerPlaceholderPrefix, StringComparison.OrdinalIgnoreCase)
        End Function

        Public Shared Function IsPlaceholderField(field As String) As Boolean
            Return IsBlankLinePlaceholder(field) OrElse IsDividerPlaceholder(field)
        End Function

        ''' <summary>
        ''' The number inside a placeholder token, which becomes the suffix of the divider's
        ''' control name. Zero when there is none, which cannot happen through the grid.
        ''' </summary>
        Public Shared Function PlaceholderNumber(field As String) As Integer
            Dim digits = New String(If(field, String.Empty).Where(AddressOf Char.IsDigit).ToArray())
            Dim number As Integer
            Return If(Integer.TryParse(digits, number), number, 0)
        End Function

        Private Shared Function ParseFields(value As String) As List(Of String)
            Return value.Split({","c, ";"c}, StringSplitOptions.RemoveEmptyEntries).
                Select(Function(item) item.Trim()).
                Where(Function(item) item <> String.Empty AndAlso Not String.Equals(item, "Not specified", StringComparison.OrdinalIgnoreCase)).
                Distinct(StringComparer.OrdinalIgnoreCase).
                ToList()
        End Function

        Private Shared Function ControlVariable(field As String) As String
            Return Char.ToLowerInvariant(field(0)) & field.Substring(1) & "TextBox"
        End Function

        Private Shared Function LookupControlVariable(field As String) As String
            Return Char.ToLowerInvariant(field(0)) & field.Substring(1) & "ComboBox"
        End Function

        Private Shared Function CheckControlVariable(field As String) As String
            Return Char.ToLowerInvariant(field(0)) & field.Substring(1) & "CheckBox"
        End Function

        Private Shared Function DateControlVariable(field As String) As String
            Return Char.ToLowerInvariant(field(0)) & field.Substring(1) & "DateTimePicker"
        End Function

        Private Shared Function IsLookupField(field As String, lookupFields As List(Of LookupFieldSpec)) As Boolean
            Return lookupFields IsNot Nothing AndAlso
                   lookupFields.Any(Function(spec) String.Equals(spec.FieldName, field, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>The generated variable name for a field, whichever control type it becomes.</summary>
        Private Shared Function FieldControlVariable(field As String,
                                                     lookupFields As List(Of LookupFieldSpec),
                                                     Optional dateFields As List(Of String) = Nothing,
                                                     Optional bitFields As List(Of String) = Nothing) As String
            If IsLookupField(field, lookupFields) Then Return LookupControlVariable(field)
            If dateFields IsNot Nothing AndAlso dateFields.Contains(field) Then Return DateControlVariable(field)
            If bitFields IsNot Nothing AndAlso bitFields.Contains(field) Then Return CheckControlVariable(field)
            Return ControlVariable(field)
        End Function

        Private Shared Function DeriveTableAlias(tableName As String) As String
            Dim aliasValue = If(tableName, String.Empty).Trim()
            If aliasValue.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then
                aliasValue = aliasValue.Substring(3)
            End If
            Return aliasValue.Replace("_", " ", StringComparison.Ordinal)
        End Function

        Private Shared Function EscapeLiteral(value As String) As String
            Return value.Replace("""", """""", StringComparison.Ordinal)
        End Function

        Private Shared Function DbText(value As Object) As String
            Return If(value Is Nothing OrElse Convert.IsDBNull(value), String.Empty, Convert.ToString(value, Globalization.CultureInfo.InvariantCulture))
        End Function
    End Class
End Namespace
