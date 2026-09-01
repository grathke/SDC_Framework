Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Security.Cryptography

Namespace HelloWorld
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

    Public Enum RoleTableAction
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
        Public Property BrowsePath As String = String.Empty
        Public Property MaintenancePath As String = String.Empty
        Public Property BrowseSource As String = String.Empty
        Public Property MaintenanceSource As String = String.Empty
        Public Property TableName As String = String.Empty
        Public Property PrimaryKey As String = String.Empty
        Public Property TableAlias As String = String.Empty
        Public Property BrowseSql As String = String.Empty
        Public Property MenuCaller As String = String.Empty
        Public Property IconFileName As String = String.Empty
        Public Property CreatedBy As Integer

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

            If plan.GenerateBrowsePage Then
                If WriteGeneratedPage(plan.BrowsePath, plan.BrowseSource, overwriteExistingPages, created, skipped) Then
                    If Not SaveBrowseBaseline(requestId, plan.BrowseSource, errors) Then
                        errors.Add("The generated browse source baseline could not be saved.")
                    End If
                End If
            End If
            If plan.GenerateMaintenancePage Then
                If WriteGeneratedPage(plan.MaintenancePath, plan.MaintenanceSource, overwriteExistingPages, created, skipped) Then
                    If Not SaveMaintenanceBaseline(requestId, plan.MaintenanceSource, errors) Then
                        errors.Add("The generated maintenance source baseline could not be saved.")
                    End If
                End If
            End If

            If plan.GenerateBrowsePage Then
                Select Case ClassifyRoleTableAction(plan.BrowsePageName, plan.TableName, plan.BrowseSql)
                    Case RoleTableAction.Insert
                        If DataAccess.UpsertRoleTableRecord(0, plan.BrowsePageName, plan.TableName, plan.TableAlias, plan.BrowseSql, plan.CreatedBy) Then
                            created.Add("FW_RoleTables:" & plan.BrowsePageName)
                        Else
                            errors.Add("The generated browse SQL could not be registered in FW_RoleTables.")
                        End If
                    Case RoleTableAction.AlreadyCurrent
                        skipped.Add("FW_RoleTables:" & plan.BrowsePageName)
                    Case RoleTableAction.UpdateSql
                        If DataAccess.UpdateRoleTableSql(plan.BrowsePageName, plan.BrowseSql) Then
                            created.Add("FW_RoleTables SQL UPDATED:" & plan.BrowsePageName)
                        Else
                            errors.Add("The existing FW_RoleTables SQL could not be updated for " & plan.BrowsePageName & ".")
                        End If
                    Case Else
                        If DataAccess.UpsertRoleTableRecord(0, plan.BrowsePageName, plan.TableName, plan.TableAlias, plan.BrowseSql, plan.CreatedBy) Then
                            created.Add("FW_RoleTables UPDATED:" & plan.BrowsePageName)
                        Else
                            errors.Add("The existing FW_RoleTables row could not be updated for " & plan.BrowsePageName & ".")
                        End If
                End Select
            End If

            If plan.GenerateBrowsePage AndAlso IsDashboardCaller(plan.MenuCaller) Then
                Try
                    EnsureDashboardIcon(workspaceRoot, plan.MenuCaller, plan.IconFileName, plan.BrowsePageName, plan.MaintenancePageName, created, skipped, errors)
                Catch ex As Exception
                    errors.Add("ICON WARNING: " & ex.Message)
                End Try
            End If

            Return New PageGenerationResult(created, skipped, errors)
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

            Dim createAsFrameworkPages = ReadGenerationFlag(request, "CreateAsFrameworkPages", False)
            plan.TableName = DbText(request("UnderlyingTableName"))
            plan.BrowsePageName = NormalizeGeneratedPageName(DbText(request("BrowsePageName")), "_B", createAsFrameworkPages)
            plan.MaintenancePageName = NormalizeGeneratedPageName(DbText(request("MaintenancePageName")), "_U", createAsFrameworkPages)
            plan.BrowseSql = DbText(request("BrowseSql"))
            plan.MenuCaller = DbText(request("MenuCaller"))
            plan.IconFileName = If(request.Table.Columns.Contains("IconFileName"), DbText(request("IconFileName")), String.Empty)
            plan.GenerateBrowsePage = ReadGenerationFlag(request, "GenerateBrowsePage", True)
            plan.GenerateMaintenancePage = ReadGenerationFlag(request, "GenerateMaintenancePage", True)
            plan.TableAlias = DeriveTableAlias(plan.TableName)
            plan.CreatedBy = If(request.Table.Columns.Contains("CreatedBy") AndAlso Not request.IsNull("CreatedBy"), Convert.ToInt32(request("CreatedBy")), 0)

            Dim browseFields = ParseFields(DbText(request("BrowseFields")))
            Dim maintenanceFields = ParseFields(DbText(request("MaintenanceFields")))
            Dim lookupFields = ParseLookupFields(DbText(request("LookupFields")), plan.Errors)
            Dim requiredFields = ParseFields(DbText(request("AdminRequiredFields")))
            Dim useQbeOnly = ReadGenerationFlag(request, "UseQbeOnly", False)

            If Not plan.GenerateBrowsePage AndAlso Not plan.GenerateMaintenancePage Then
                plan.Errors.Add("At least one page target must be selected.")
            End If
            If plan.GenerateBrowsePage Then ValidateName(plan.BrowsePageName, "Browse page name", "_B", plan.Errors)
            If plan.GenerateMaintenancePage Then ValidateName(plan.MaintenancePageName, "Maintenance page name", "_U", plan.Errors)
            If String.IsNullOrWhiteSpace(plan.TableName) Then plan.Errors.Add("The underlying table is required.")
            If plan.GenerateBrowsePage AndAlso browseFields.Count = 0 Then plan.Errors.Add("At least one _B field is required.")
            If plan.GenerateMaintenancePage AndAlso maintenanceFields.Count = 0 Then plan.Errors.Add("At least one _U field is required.")
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
                ValidateFields(maintenanceFields, schemaFields, "_U", plan.Errors)
                ValidateFields(lookupFields.Select(Function(item) item.FieldName), maintenanceFields, "Lookup", plan.Errors)
                ValidateFields(requiredFields, maintenanceFields, "Admin Required", plan.Errors)
            End If
            If Not plan.IsValid Then Return plan

            plan.BrowsePath = Path.Combine(workspaceRoot, plan.BrowsePageName & ".vb")
            plan.MaintenancePath = Path.Combine(workspaceRoot, plan.MaintenancePageName & ".vb")

            If plan.GenerateBrowsePage Then
                plan.BrowseSource = BuildBrowseSource(plan.BrowsePageName,
                                                      plan.TableName,
                                                      plan.PrimaryKey,
                                                      useQbeOnly,
                                                      plan.GenerateMaintenancePage)
            End If
            If plan.GenerateMaintenancePage Then
                plan.MaintenanceSource = BuildMaintenanceSource(plan.MaintenancePageName,
                                                                plan.TableName,
                                                                plan.PrimaryKey,
                                                                maintenanceFields,
                                                                requiredFields,
                                                                lookupFields)
            End If

            Return plan
        End Function

        ' Decides what generation would do to FW_RoleTables. Generate performs the action and
        ' Preview describes it, so the two cannot drift apart.
        Private Shared Function ClassifyRoleTableAction(browsePageName As String, tableName As String, browseSql As String) As RoleTableAction
            Dim existingRoleTable = DataAccess.GetRoleTableMetadata(browsePageName)
            If existingRoleTable Is Nothing Then Return RoleTableAction.Insert
            If Not String.Equals(DbText(existingRoleTable("DB_Table")).Trim(), tableName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                Return RoleTableAction.ReplaceRow
            End If
            If String.Equals(DbText(existingRoleTable("Table_SQL")).Trim(), browseSql.Trim(), StringComparison.Ordinal) Then
                Return RoleTableAction.AlreadyCurrent
            End If
            Return RoleTableAction.UpdateSql
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
            Else
                lines.Add("  MAINTENANCE PAGE NOT SELECTED")
            End If

            lines.Add(String.Empty)
            lines.Add("DATABASE")
            lines.Add("  UNDERLYING TABLE: " & plan.TableName & "   PRIMARY KEY: " & plan.PrimaryKey)
            If plan.GenerateBrowsePage Then
                Select Case ClassifyRoleTableAction(plan.BrowsePageName, plan.TableName, plan.BrowseSql)
                    Case RoleTableAction.Insert
                        lines.Add("  FW_RoleTables: NEW ROW FOR " & plan.BrowsePageName & " (ALIAS " & plan.TableAlias & ")")
                    Case RoleTableAction.AlreadyCurrent
                        lines.Add("  FW_RoleTables: ALREADY CURRENT, NO CHANGE")
                    Case RoleTableAction.UpdateSql
                        lines.Add("  FW_RoleTables: SQL WOULD BE UPDATED FOR " & plan.BrowsePageName)
                    Case Else
                        lines.Add("  FW_RoleTables: ROW WOULD BE REPLACED FOR " & plan.BrowsePageName & " (TABLE CHANGED)")
                End Select
            End If
            If plan.GenerateMaintenancePage Then
                lines.Add("  FW_PageGeneration: MAINTENANCE SOURCE BASELINE WOULD BE SAVED")
            End If

            lines.Add(String.Empty)
            lines.Add("DASHBOARD ICON")
            If Not plan.GenerateBrowsePage OrElse Not IsDashboardCaller(plan.MenuCaller) Then
                lines.Add("  NONE. MENU CALLER IS " & If(String.IsNullOrWhiteSpace(plan.MenuCaller), "NOT SET", plan.MenuCaller) & ", WHICH IS NOT A DASHBOARD")
            ElseIf DashboardIconExists(workspaceRoot, plan.MenuCaller, plan.BrowsePageName) Then
                lines.Add("  ALREADY PRESENT ON " & DashboardSourceFileName(plan.MenuCaller) & ", NO CHANGE")
            Else
                lines.Add("  WOULD BE ADDED TO " & DashboardSourceFileName(plan.MenuCaller) & " FOR " & plan.BrowsePageName)
                lines.Add("  IMAGE: " & If(String.IsNullOrWhiteSpace(plan.IconFileName),
                                           "NONE CHOSEN, THE DEFAULT GLYPH IS USED",
                                           plan.IconFileName))
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

            Dim referenceAssembly = Path.Combine(workspaceRoot, "bin", "Debug", "net10.0-windows", "HelloWorld.dll")
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
                    File.WriteAllText(Path.Combine(scratchRoot, plan.MaintenancePageName & ".vb"), plan.MaintenanceSource, New UTF8Encoding(False))
                End If

                File.WriteAllText(Path.Combine(scratchRoot, "PageGenPreview.vbproj"),
                                  BuildCompileCheckProject(referenceAssembly),
                                  New UTF8Encoding(False))

                Dim startInfo As New Diagnostics.ProcessStartInfo("dotnet", "build PageGenPreview.vbproj --nologo -v q") With {
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

        Private Shared Function BuildCompileCheckProject(referenceAssembly As String) As String
            Return String.Join(Environment.NewLine, {
                "<Project Sdk=""Microsoft.NET.Sdk"">",
                "  <PropertyGroup>",
                "    <OutputType>Library</OutputType>",
                "    <TargetFramework>net10.0-windows</TargetFramework>",
                "    <UseWindowsForms>true</UseWindowsForms>",
                "    <RootNamespace>HelloWorld</RootNamespace>",
                "  </PropertyGroup>",
                "  <ItemGroup>",
                "    <Reference Include=""HelloWorld"">",
                "      <HintPath>" & referenceAssembly & "</HintPath>",
                "    </Reference>",
                "  </ItemGroup>",
                "</Project>",
                ""
            })
        End Function

        ''' A dashboard is a class named Dashboard_<Name>, and its source file is
        ''' "02 FW Dashboard_<Name>.vb". Both halves are convention, so adding a dashboard needs no
        ''' edit here and none in the Menu Caller list: create the pair and it becomes a valid
        ''' target. DashboardCallers discovers the classes; DashboardSourceFileName derives the file.
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
            Return "LoadDashboardIcon(""" & EscapeLiteral(iconFileName.Trim()) & """, SystemIcons.Application.ToBitmap())"
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

        Private Shared Function IsDashboardCaller(menuCaller As String) As Boolean
            If String.IsNullOrWhiteSpace(menuCaller) Then Return False
            Return DashboardCallers().Any(Function(name) String.Equals(name, menuCaller.Trim(), StringComparison.OrdinalIgnoreCase))
        End Function

        Private Shared Function DashboardSourceFileName(menuCaller As String) As String
            If Not IsDashboardCaller(menuCaller) Then Return String.Empty
            Return "02 FW " & menuCaller.Trim() & ".vb"
        End Function

        ''' <summary>
        ''' Rewrites the .Image line of an existing generated button. Returns True when the file was
        ''' changed, False when there was nothing to change.
        ''' </summary>
        Private Shared Function UpdateDashboardIconImage(dashboardPath As String, browsePageName As String, iconFileName As String) As Boolean
            If String.IsNullOrWhiteSpace(iconFileName) OrElse Not File.Exists(dashboardPath) Then Return False

            Dim source = File.ReadAllText(dashboardPath)
            Dim anchor = ".Name = ""GeneratedPageActionKey_" & browsePageName & """"
            Dim anchorIndex = source.IndexOf(anchor, StringComparison.OrdinalIgnoreCase)
            If anchorIndex < 0 Then Return False

            ' Stay inside this button's initializer: the search stops at its closing brace so a
            ' neighbouring button's image can never be rewritten by mistake.
            Dim blockEnd = source.IndexOf("}", anchorIndex, StringComparison.Ordinal)
            If blockEnd < 0 Then Return False

            Dim imageIndex = source.IndexOf(".Image = ", anchorIndex, StringComparison.Ordinal)
            If imageIndex < 0 OrElse imageIndex > blockEnd Then Return False

            Dim lineEnd = source.IndexOf(Environment.NewLine, imageIndex, StringComparison.Ordinal)
            If lineEnd < 0 Then Return False

            Dim existingLine = source.Substring(imageIndex, lineEnd - imageIndex)
            Dim replacement = ".Image = " & DashboardImageExpression(iconFileName) & ","
            If String.Equals(existingLine.Trim(), replacement, StringComparison.Ordinal) Then Return False

            source = source.Substring(0, imageIndex) & replacement & source.Substring(lineEnd)
            File.WriteAllText(dashboardPath, source, New UTF8Encoding(False))
            Return True
        End Function

        Private Shared Function DashboardIconExists(workspaceRoot As String, menuCaller As String, browsePageName As String) As Boolean
            Dim fileName = DashboardSourceFileName(menuCaller)
            If fileName.Length = 0 Then Return False
            Dim dashboardPath = Path.Combine(workspaceRoot, fileName)
            If Not File.Exists(dashboardPath) Then Return False
            Dim source = File.ReadAllText(dashboardPath)
            Return source.IndexOf("GeneratedPageActionKey_" & browsePageName, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   source.IndexOf("New " & browsePageName & "_B", StringComparison.OrdinalIgnoreCase) >= 0
        End Function

        Private Shared Function ReadGenerationFlag(request As DataRow, columnName As String, defaultValue As Boolean) As Boolean
            If request Is Nothing OrElse request.Table Is Nothing OrElse Not request.Table.Columns.Contains(columnName) OrElse request.IsNull(columnName) Then
                Return defaultValue
            End If

            Return Convert.ToBoolean(request(columnName))
        End Function

        Private Shared Function NormalizeGeneratedPageName(pageName As String,
                                                            suffix As String,
                                                            createAsFrameworkPages As Boolean) As String
            Dim normalized = If(pageName, String.Empty).Trim()
            If normalized.StartsWith("FW_", StringComparison.OrdinalIgnoreCase) Then
                normalized = normalized.Substring(3)
            End If

            If Not normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) Then
                normalized &= suffix
            End If

            Return If(createAsFrameworkPages, "FW_", String.Empty) & normalized
        End Function

        Private Shared Sub EnsureDashboardIcon(workspaceRoot As String,
                                                menuCaller As String,
                                                iconFileName As String,
                                                browsePageName As String,
                                                maintenancePageName As String,
                                                created As List(Of String),
                                                skipped As List(Of String),
                                                errors As List(Of String))
            Dim dashboardFileName = DashboardSourceFileName(menuCaller)
            If dashboardFileName.Length = 0 Then
                errors.Add("ICON WARNING: " & menuCaller & " is not a dashboard, so no icon was generated.")
                Return
            End If

            Dim dashboardPath = Path.Combine(workspaceRoot, dashboardFileName)
            If Not File.Exists(dashboardPath) Then
                errors.Add(dashboardFileName & " could not be found for icon generation.")
                Return
            End If

            Dim source = File.ReadAllText(dashboardPath)
            Dim actionKey = "Generated_" & browsePageName
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

            Dim gridCell = FindNextDashboardGridCell(source)
            If gridCell.Y > 3 Then
                EnsureDashboardGridHeight(workspaceRoot, gridCell.Y, created, errors)
            End If

            Dim buttonField = "        Private ReadOnly generated" & browsePageName & "Button As DashboardIconButton" & Environment.NewLine
            Dim fieldAnchor = "        Private ReadOnly closeIconButton As Button" & Environment.NewLine
            source = InsertAfter(source, fieldAnchor, buttonField)

            Dim construction = String.Join(Environment.NewLine, {
                "",
                "            generated" & browsePageName & "Button = New DashboardIconButton() With {",
                "                .Name = ""GeneratedPageActionKey_" & browsePageName & """,",
                "                .Text = """ & DisplayPageCaption(browsePageName) & """,",
                "                .Location = DashboardGridLayout.CellLocation(" & gridCell.Y.ToString(Globalization.CultureInfo.InvariantCulture) & ", " & gridCell.X.ToString(Globalization.CultureInfo.InvariantCulture) & "),",
                "                .Size = New Size(150, 118),",
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
            }) & Environment.NewLine
            source = InsertBefore(source, "            AddHandler Me.Load, AddressOf " & menuCaller.Trim() & "_Load", construction)

            Dim handlers = String.Join(Environment.NewLine, {
                "            AddHandler generated" & browsePageName & "Button.MouseEnter, AddressOf IconButton_MouseEnter",
                "            AddHandler generated" & browsePageName & "Button.MouseLeave, AddressOf IconButton_MouseLeave",
                "            AddHandler generated" & browsePageName & "Button.Click, AddressOf Generated" & browsePageName & "Button_Click",
                ""
            })
            source = InsertBefore(source, "            AddHandler closeIconButton.Click, AddressOf CloseButton_Click", handlers)
            source = InsertBefore(source, "            Me.Controls.Add(topStripLabel)", "            Me.Controls.Add(generated" & browsePageName & "Button)" & Environment.NewLine)

            Dim baselineAnchor = "            rolesButton.Top = DashboardGridLayout.CellTop(1)"
            Dim baselineIndex = source.IndexOf(baselineAnchor, StringComparison.Ordinal)
            If baselineIndex < 0 Then
                errors.Add(dashboardFileName & " row baseline could not be found for icon generation.")
                Return
            End If
            Dim baselineLineEnd = source.IndexOf(Environment.NewLine, baselineIndex, StringComparison.Ordinal)
            If baselineLineEnd < 0 Then baselineLineEnd = source.Length
            source = source.Insert(baselineLineEnd,
                                   Environment.NewLine &
                                   "            generated" & browsePageName & "Button.Left = DashboardGridLayout.CellLeft(" & gridCell.X.ToString(Globalization.CultureInfo.InvariantCulture) & ")" & Environment.NewLine &
                                   "            generated" & browsePageName & "Button.Top = DashboardGridLayout.CellTop(" & gridCell.Y.ToString(Globalization.CultureInfo.InvariantCulture) & ")")

            Dim clickHandler = String.Join(Environment.NewLine, {
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

        Private Shared Sub EnsureDashboardGridHeight(workspaceRoot As String, requiredRow As Integer, created As List(Of String), errors As List(Of String))
            Dim layoutPath = Path.Combine(workspaceRoot, "DashboardGridLayout.vb")
            If Not File.Exists(layoutPath) Then
                errors.Add("DashboardGridLayout.vb could not be found while adding a dashboard row.")
                Return
            End If

            Dim source = File.ReadAllText(layoutPath)
            Dim match = Regex.Match(source, "Public Const StandardClientHeight As Integer = (?<height>\d+)")
            If Not match.Success Then
                errors.Add("The shared dashboard height could not be found while adding a dashboard row.")
                Return
            End If

            Dim currentHeight = Integer.Parse(match.Groups("height").Value, Globalization.CultureInfo.InvariantCulture)
            Dim requiredHeight = currentHeight
            If requiredRow > 3 Then
                requiredHeight = Math.Max(currentHeight, 560 + ((requiredRow - 3) * DashboardGridLayout.RowGap))
            End If
            If requiredHeight = currentHeight Then Return

            source = source.Replace(match.Value,
                                    "Public Const StandardClientHeight As Integer = " & requiredHeight.ToString(Globalization.CultureInfo.InvariantCulture),
                                    StringComparison.Ordinal)
            File.WriteAllText(layoutPath, source, New UTF8Encoding(False))
        End Sub

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

        Private Shared Function DisplayPageCaption(pageName As String) As String
            Return If(pageName.EndsWith("_B", StringComparison.OrdinalIgnoreCase), pageName.Substring(0, pageName.Length - 2), pageName)
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

        Private Shared Function BuildBrowseSource(pageName As String,
                              tableName As String,
                              primaryKey As String,
                              useQbeOnly As Boolean,
                              generateMaintenancePage As Boolean) As String
            Return String.Join(Environment.NewLine, {
                "Option Strict On",
                "Option Explicit On",
                "",
                "Imports System.Windows.Forms",
                "",
                "Namespace HelloWorld",
                "    Public Class " & pageName,
                "        Inherits FW_Base_B",
                "",
                "        Private ReadOnly currentUser As UserContext",
                "        Private ReadOnly accessProfile As AccessProfile",
                "",
                "        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)",
                "            MyBase.New(user, profile, """ & EscapeLiteral(tableName) & """)",
                "            currentUser = user",
                "            accessProfile = profile",
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
                       "        Protected Overrides Function HandleDefaultCreateAction() As Boolean",
                       "            Using page As New " & maintenancePageNameForBrowse(pageName) & "(0, currentUser, accessProfile)",
                       "                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then RefreshGridForCustomAction(page.SavedRecordId)",
                       "            End Using",
                       "            Return True",
                       "        End Function",
                       "",
                       "        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean",
                       "            Using page As New " & maintenancePageNameForBrowse(pageName) & "(recordId, currentUser, accessProfile)",
                       "                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then RefreshGridForCustomAction(recordId)",
                       "            End Using",
                       "            Return True",
                       "        End Function"
                   }),
                   String.Empty),
                "        ''' <summary>",
                "        ''' Soft-deletes the selected record. Without this the Delete button falls through to",
                "        ''' the base placeholder and silently does nothing.",
                "        ''' </summary>",
                "        Protected Overrides Function HandleDefaultDeleteAction() As Boolean",
                "            Dim recordId = GetSelectedRecordIdForCustomAction()",
                "            If Not recordId.HasValue Then Return False",
                "",
                "            Dim summary = GetSelectedRowSummary()",
                "            Dim prompt = If(String.IsNullOrWhiteSpace(summary), ""Delete the selected record?"", ""Delete "" & summary & ""?"")",
                "            If MessageBox.Show(Me,",
                "                               (prompt & Environment.NewLine & Environment.NewLine &",
                "                                ""It will be removed from this list."").ToUpperInvariant(),",
                "                               ""CONFIRM DELETE"",",
                "                               MessageBoxButtons.YesNo,",
                "                               MessageBoxIcon.Question) <> DialogResult.Yes Then",
                "                Return True",
                "            End If",
                "",
                "            Dim failure = DataAccess.SoftDeleteGeneratedPageRecord(""" & EscapeLiteral(tableName) & """,",
                "                                                                  """ & EscapeLiteral(primaryKey) & """,",
                "                                                                  recordId.Value,",
                "                                                                  If(SessionState.IsActive, SessionState.Current.Value.UserID, 0),",
                "                                                                  NameOf(" & pageName & "))",
                "            If Not String.IsNullOrWhiteSpace(failure) Then",
                "                MessageBox.Show(Me, failure.ToUpperInvariant(), ""DELETE FAILED"", MessageBoxButtons.OK, MessageBoxIcon.Warning)",
                "                Return True",
                "            End If",
                "",
                "            RefreshGridForCustomAction()",
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

        Private Shared Function BuildMaintenanceSource(pageName As String, tableName As String, primaryKey As String, fields As List(Of String), requiredFields As List(Of String), lookupFields As List(Of LookupFieldSpec)) As String
            Dim output As New StringBuilder()
            output.AppendLine("Option Strict On")
            output.AppendLine("Option Explicit On")
            output.AppendLine()
            output.AppendLine("Imports System.Collections.Generic")
            output.AppendLine("Imports System.ComponentModel")
            output.AppendLine("Imports System.Data")
            output.AppendLine("Imports System.Drawing")
            output.AppendLine("Imports System.Windows.Forms")
            output.AppendLine()
            output.AppendLine("Namespace HelloWorld")
            output.AppendLine("    Public Class " & pageName)
            output.AppendLine("        Inherits FW_Base_U")
            output.AppendLine()
            output.AppendLine("        Private ReadOnly recordId As Integer")
            output.AppendLine("        Private ReadOnly currentUser As UserContext")
            output.AppendLine("        Private ReadOnly accessProfile As AccessProfile")
            output.AppendLine("        Private ReadOnly tableName As String = """ & EscapeLiteral(tableName) & """")
            output.AppendLine("        Private ReadOnly primaryKey As String = """ & EscapeLiteral(primaryKey) & """")
            output.AppendLine("        Private record As DataRow")
            output.AppendLine("        Private ReadOnly formBindingSource As New BindingSource()")
            output.AppendLine("        Private originalRowVersion As Byte()")
            For Each field In fields
                If IsLookupField(field, lookupFields) Then
                    output.AppendLine("        Private ReadOnly " & LookupControlVariable(field) & " As ComboBox")
                Else
                    output.AppendLine("        Private ReadOnly " & ControlVariable(field) & " As TextBox")
                End If
            Next
            output.AppendLine()
            output.AppendLine("        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)")
            output.AppendLine("            MyBase.New()")
            output.AppendLine("            recordId = id")
            output.AppendLine("            currentUser = user")
            output.AppendLine("            accessProfile = profile")
            ' No Text assignment: Base_U builds the caption from the page name and the mode, so a
            ' generated page opens as "Edit Entity X" rather than "EntityX_U". A generated page that
            ' needs its own wording overrides BuildMaintenanceTitle.
            output.AppendLine("            ClientSize = New Size(600, " & Math.Max(120, 55 + fields.Count * 42).ToString() & ")")
            output.AppendLine("            okButton.Location = New Point(ClientSize.Width - 270, ClientSize.Height - 46)")
            output.AppendLine("            cancelActionButton.Location = New Point(ClientSize.Width - 135, ClientSize.Height - 46)")
            Dim y = 20
            For Each field In fields
                Dim isRequired = requiredFields.Any(Function(item) String.Equals(item, field, StringComparison.OrdinalIgnoreCase))

                If IsLookupField(field, lookupFields) Then
                    ' AddField only produces TextBoxes, so a foreign key is built by hand to the
                    ' same geometry: Label_<Field> at 20, the control at 150, 42px row pitch.
                    Dim comboVariable = LookupControlVariable(field)
                    output.AppendLine("            Controls.Add(New Label() With {.Name = ""Label_" & EscapeLiteral(field) & """, .Text = DisplayNameFormatter.ToDisplayName(""" & EscapeLiteral(field) & """, False)" &
                                      If(isRequired, " & "" *""", String.Empty) & ", .Location = New Point(20, " & y.ToString() & "), .Size = New Size(120, 26), .TextAlign = ContentAlignment.MiddleLeft})")
                    output.AppendLine("            " & comboVariable & " = New ComboBox() With {.Name = ""ComboBox_" & EscapeLiteral(field) & """, .Location = New Point(150, " & y.ToString() & "), .Size = New Size(320, 26), .DropDownStyle = ComboBoxStyle.DropDownList, .BackColor = SystemColors.Window}")
                    If isRequired Then output.AppendLine("            " & comboVariable & ".Tag = ""Required""")
                    output.AppendLine("            Controls.Add(" & comboVariable & ")")
                Else
                    output.AppendLine("            " & ControlVariable(field) & " = AddField(""" & EscapeLiteral(field) & """, " & y.ToString() & ", False, " & If(isRequired, "True", "False") & ")")
                End If

                y += 42
            Next
            output.AppendLine("            SetManualTabOrder(" & String.Join(", ", fields.Select(Function(field) FieldControlVariable(field, lookupFields)).Concat({"okButton", "cancelActionButton"})) & ")")
            output.AppendLine("            BindToForm()")
            output.AppendLine("            ApplyMode()")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        Public ReadOnly Property SavedRecordId As Integer")
            output.AppendLine("            Get")
            output.AppendLine("                If record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) OrElse record.IsNull(primaryKey) Then Return 0")
            output.AppendLine("                Return Convert.ToInt32(record(primaryKey), Globalization.CultureInfo.InvariantCulture)")
            output.AppendLine("            End Get")
            output.AppendLine("        End Property")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Function GetPageName() As String")
            output.AppendLine("            Return NameOf(" & pageName & ")")
            output.AppendLine("        End Function")
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
            Dim textFields = fields.Where(Function(field) Not IsLookupField(field, lookupFields)).ToList()
            If textFields.Count > 0 Then
                output.AppendLine("            For Each control In New Control() {" & String.Join(", ", textFields.Select(Function(field) ControlVariable(field))) & "}")
                output.AppendLine("                Dim fieldName = control.Name.Substring(""TextBox_"".Length)")
                output.AppendLine("                control.DataBindings.Clear()")
                output.AppendLine("                control.DataBindings.Add(""Text"", formBindingSource, fieldName, True, DataSourceUpdateMode.Never)")
                output.AppendLine("                If record.Table.Columns.Contains(fieldName) Then control.Text = If(record(fieldName) Is DBNull.Value, String.Empty, Convert.ToString(record(fieldName)))")
                output.AppendLine("            Next")
            End If

            For Each spec In lookupFields.Where(Function(item) fields.Any(Function(field) String.Equals(field, item.FieldName, StringComparison.OrdinalIgnoreCase)))
                output.AppendLine("            ConfigureLookupCombo(" & LookupControlVariable(spec.FieldName) &
                                  ", DataAccess.GetLookupTable(""" & EscapeLiteral(spec.LookupTable) & """, """ & EscapeLiteral(spec.ValueColumn) & """, """ & EscapeLiteral(spec.DisplayColumn) & """)" &
                                  ", """ & EscapeLiteral(spec.ValueColumn) & """, """ & EscapeLiteral(spec.DisplayColumn) & """, CurrentLookupId(""" & EscapeLiteral(spec.FieldName) & """))")
            Next
            If fields.Any(Function(field) String.Equals(field, "RegistrationID", StringComparison.OrdinalIgnoreCase)) Then
                output.AppendLine("            If recordId <= 0 AndAlso record.Table.Columns.Contains(""RegistrationID"") AndAlso SessionState.IsActive AndAlso SessionState.Current.HasValue Then")
                output.AppendLine("                Dim sessionRegistrationId = SessionState.Current.Value.RegistrationID")
                output.AppendLine("                If sessionRegistrationId > 0 AndAlso String.IsNullOrWhiteSpace(registrationIDTextBox.Text) Then registrationIDTextBox.Text = sessionRegistrationId.ToString(Globalization.CultureInfo.InvariantCulture)")
                output.AppendLine("            End If")
            End If
            output.AppendLine("            If record.Table.Columns.Contains(""RowVersion"") AndAlso Not record.IsNull(""RowVersion"") Then originalRowVersion = CType(DirectCast(record(""RowVersion""), Byte()).Clone(), Byte())")
            output.AppendLine("            CaptureOriginalRowVersion(originalRowVersion)")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Sub ApplyMode()")
            output.AppendLine("            For Each control In Controls.OfType(Of TextBox)()")
            output.AppendLine("                control.ReadOnly = String.Equals(control.Name, ""TextBox_"" & primaryKey, StringComparison.OrdinalIgnoreCase)")
            output.AppendLine("            Next")
            output.AppendLine("        End Sub")
            output.AppendLine()
            output.AppendLine("        Protected Overrides Function TryBuildRecord() As Boolean")
            output.AppendLine("            Return True")
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
                If IsLookupField(field, lookupFields) Then
                    output.AppendLine("            values(""" & EscapeLiteral(field) & """) = GetComboSelectedIdOrZero(" & LookupControlVariable(field) & ")")
                Else
                    output.AppendLine("            values(""" & EscapeLiteral(field) & """) = " & ControlVariable(field) & ".Text")
                End If
            Next
            output.AppendLine("            Dim savedId As Integer = recordId")
            output.AppendLine("            If savedId <= 0 AndAlso record.Table.Columns.Contains(primaryKey) AndAlso Not record.IsNull(primaryKey) Then Integer.TryParse(Convert.ToString(record(primaryKey)), savedId)")
            output.AppendLine("            Dim updatedBy = If(SessionState.IsActive, SessionState.Current.Value.UserID, 0)")
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
                                        "^\s*(?<field>\w+)\s*->\s*(?<table>\w+)\s*\.\s*(?<value>\w+)\s+displayed\s+as\s+(?<display>\w+)\s*$",
                                        RegexOptions.IgnoreCase)

                If Not match.Success Then
                    errors.Add("Lookup field is not in the expected format '<Field> -> <Table>.<ValueColumn> displayed as <DisplayColumn>': " & entry)
                    Continue For
                End If

                specs.Add(New LookupFieldSpec With {
                    .FieldName = match.Groups("field").Value,
                    .LookupTable = match.Groups("table").Value,
                    .ValueColumn = match.Groups("value").Value,
                    .DisplayColumn = match.Groups("display").Value
                })
            Next

            Return specs
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

        Private Shared Function IsLookupField(field As String, lookupFields As List(Of LookupFieldSpec)) As Boolean
            Return lookupFields IsNot Nothing AndAlso
                   lookupFields.Any(Function(spec) String.Equals(spec.FieldName, field, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>The generated variable name for a field, whichever control type it becomes.</summary>
        Private Shared Function FieldControlVariable(field As String, lookupFields As List(Of LookupFieldSpec)) As String
            Return If(IsLookupField(field, lookupFields), LookupControlVariable(field), ControlVariable(field))
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
