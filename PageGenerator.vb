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

    Public NotInheritable Class PageGenerator
        Private Sub New()
        End Sub

        Public Shared Function Generate(requestId As Integer,
                        workspaceRoot As String,
                        Optional overwriteExistingPages As Boolean = False) As PageGenerationResult
            Dim created As New List(Of String)()
            Dim skipped As New List(Of String)()
            Dim errors As New List(Of String)()

            If requestId <= 0 Then
                errors.Add("A saved Page Generation request is required.")
                Return New PageGenerationResult(created, skipped, errors)
            End If
            If String.IsNullOrWhiteSpace(workspaceRoot) OrElse Not Directory.Exists(workspaceRoot) Then
                errors.Add("The workspace folder could not be found.")
                Return New PageGenerationResult(created, skipped, errors)
            End If

            Dim request = DataAccess.GetPageGenerationById(requestId)
            If request Is Nothing Then
                errors.Add("The Page Generation request could not be found.")
                Return New PageGenerationResult(created, skipped, errors)
            End If

            Dim tableName = DbText(request("UnderlyingTableName"))
            Dim browsePageName = DbText(request("BrowsePageName"))
            Dim maintenancePageName = DbText(request("MaintenancePageName"))
            Dim createAsFrameworkPages = ReadGenerationFlag(request, "CreateAsFrameworkPages", False)
            browsePageName = NormalizeGeneratedPageName(browsePageName, "_B", createAsFrameworkPages)
            maintenancePageName = NormalizeGeneratedPageName(maintenancePageName, "_U", createAsFrameworkPages)
            Dim browseFields = ParseFields(DbText(request("BrowseFields")))
            Dim maintenanceFields = ParseFields(DbText(request("MaintenanceFields")))
            Dim lookupFields = ParseFields(DbText(request("LookupFields")))
            Dim requiredFields = ParseFields(DbText(request("AdminRequiredFields")))
            Dim browseSql = DbText(request("BrowseSql"))
            Dim menuCaller = DbText(request("MenuCaller"))
            Dim generateBrowsePage = ReadGenerationFlag(request, "GenerateBrowsePage", True)
            Dim generateMaintenancePage = ReadGenerationFlag(request, "GenerateMaintenancePage", True)
            Dim useQbeOnly = ReadGenerationFlag(request, "UseQbeOnly", False)
            Dim createdBy = If(request.Table.Columns.Contains("CreatedBy") AndAlso Not request.IsNull("CreatedBy"), Convert.ToInt32(request("CreatedBy")), 0)
            Dim tableAlias = DeriveTableAlias(tableName)

            If Not generateBrowsePage AndAlso Not generateMaintenancePage Then
                errors.Add("At least one page target must be selected.")
            End If
            If generateBrowsePage Then ValidateName(browsePageName, "Browse page name", "_B", errors)
            If generateMaintenancePage Then ValidateName(maintenancePageName, "Maintenance page name", "_U", errors)
            If String.IsNullOrWhiteSpace(tableName) Then errors.Add("The underlying table is required.")
            If generateBrowsePage AndAlso browseFields.Count = 0 Then errors.Add("At least one _B field is required.")
            If generateMaintenancePage AndAlso maintenanceFields.Count = 0 Then errors.Add("At least one _U field is required.")
            If generateBrowsePage AndAlso String.IsNullOrWhiteSpace(browseSql) Then errors.Add("Browse SQL is required.")
            If errors.Count > 0 Then Return New PageGenerationResult(created, skipped, errors)

            Dim schemaFields = DataAccess.GetTableFieldNames(tableName)
            If schemaFields.Count = 0 Then
                errors.Add("The underlying dbo table does not exist or has no columns: " & tableName)
                Return New PageGenerationResult(created, skipped, errors)
            End If
            Dim primaryKey = DataAccess.GetPrimaryKeyFieldName(tableName)
            If String.IsNullOrWhiteSpace(primaryKey) Then errors.Add("The underlying table does not have a primary key: " & tableName)
            If generateBrowsePage Then ValidateFields(browseFields, schemaFields, "_B", errors)
            If generateMaintenancePage Then
                ValidateFields(maintenanceFields, schemaFields, "_U", errors)
                ValidateFields(lookupFields, maintenanceFields, "Lookup", errors)
                ValidateFields(requiredFields, maintenanceFields, "Admin Required", errors)
            End If
            If errors.Count > 0 Then Return New PageGenerationResult(created, skipped, errors)

            Dim browsePath = Path.Combine(workspaceRoot, browsePageName & ".vb")
            Dim maintenancePath = Path.Combine(workspaceRoot, maintenancePageName & ".vb")

            If generateBrowsePage Then
                WriteGeneratedPage(browsePath, BuildBrowseSource(browsePageName,
                                                                  tableName,
                                                                  useQbeOnly,
                                                                  generateMaintenancePage),
                                   overwriteExistingPages,
                                   created,
                                   skipped)
            End If
            If generateMaintenancePage Then
                Dim maintenanceSource = BuildMaintenanceSource(maintenancePageName, tableName, primaryKey, maintenanceFields, requiredFields, lookupFields)
                If WriteGeneratedPage(maintenancePath, maintenanceSource, overwriteExistingPages, created, skipped) Then
                    If Not SaveMaintenanceBaseline(requestId, maintenanceSource, errors) Then
                        errors.Add("The generated maintenance source baseline could not be saved.")
                    End If
                End If
            End If

            If generateBrowsePage Then
                Dim existingRoleTable = DataAccess.GetRoleTableMetadata(browsePageName)
                If existingRoleTable Is Nothing Then
                    If DataAccess.UpsertRoleTableRecord(0, browsePageName, tableName, tableAlias, browseSql, createdBy) Then
                        created.Add("FW_RoleTables:" & browsePageName)
                    Else
                        errors.Add("The generated browse SQL could not be registered in FW_RoleTables.")
                    End If
                ElseIf String.Equals(DbText(existingRoleTable("DB_Table")).Trim(), tableName.Trim(), StringComparison.OrdinalIgnoreCase) Then
                    If String.Equals(DbText(existingRoleTable("Table_SQL")).Trim(), browseSql.Trim(), StringComparison.Ordinal) Then
                        skipped.Add("FW_RoleTables:" & browsePageName)
                    ElseIf DataAccess.UpdateRoleTableSql(browsePageName, browseSql) Then
                        created.Add("FW_RoleTables SQL UPDATED:" & browsePageName)
                    Else
                        errors.Add("The existing FW_RoleTables SQL could not be updated for " & browsePageName & ".")
                    End If
                Else
                    If DataAccess.UpsertRoleTableRecord(0, browsePageName, tableName, tableAlias, browseSql, createdBy) Then
                        created.Add("FW_RoleTables UPDATED:" & browsePageName)
                    Else
                        errors.Add("The existing FW_RoleTables row could not be updated for " & browsePageName & ".")
                    End If
                End If
            End If

            If generateBrowsePage AndAlso String.Equals(menuCaller, "Dashboard_Application", StringComparison.OrdinalIgnoreCase) Then
                Try
                    EnsureDashboardIcon(workspaceRoot, browsePageName, maintenancePageName, created, skipped, errors)
                Catch ex As Exception
                    errors.Add("ICON WARNING: " & ex.Message)
                End Try
            End If

            Return New PageGenerationResult(created, skipped, errors)
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
                                                browsePageName As String,
                                                maintenancePageName As String,
                                                created As List(Of String),
                                                skipped As List(Of String),
                                                errors As List(Of String))
            Dim dashboardPath = Path.Combine(workspaceRoot, "02 FW Dashboard_Application.vb")
            If Not File.Exists(dashboardPath) Then
                errors.Add("Dashboard_Application.vb could not be found for icon generation.")
                Return
            End If

            Dim source = File.ReadAllText(dashboardPath)
            Dim actionKey = "Generated_" & browsePageName
            If source.IndexOf("GeneratedPageActionKey_" & browsePageName, StringComparison.OrdinalIgnoreCase) >= 0 OrElse
               source.IndexOf("New " & browsePageName & "_B", StringComparison.OrdinalIgnoreCase) >= 0 Then
                skipped.Add("Dashboard_Application icon: " & actionKey)
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
                "                .Image = SystemIcons.Application.ToBitmap(),",
                "                .TextImageRelation = TextImageRelation.ImageAboveText,",
                "                .ImageAlign = ContentAlignment.TopCenter,",
                "                .TextAlign = ContentAlignment.BottomCenter,",
                "                .TabStop = False",
                "            }",
                "            generated" & browsePageName & "Button.FlatAppearance.BorderSize = 0",
                "            generated" & browsePageName & "Button.FlatAppearance.MouseOverBackColor = Color.Transparent",
                "            generated" & browsePageName & "Button.FlatAppearance.MouseDownBackColor = Color.Transparent"
            }) & Environment.NewLine
            source = InsertBefore(source, "            AddHandler Me.Load, AddressOf Dashboard_Application_Load", construction)

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
                errors.Add("Dashboard_Application row baseline could not be found for icon generation.")
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
            created.Add("Dashboard_Application icon: " & actionKey)
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
                       "                If page.ShowDialog(Me) = DialogResult.OK Then RefreshGridForCustomAction(page.SavedRecordId)",
                       "            End Using",
                       "            Return True",
                       "        End Function",
                       "",
                       "        Protected Overrides Function HandleDefaultUpdateAction(recordId As Integer) As Boolean",
                       "            Using page As New " & maintenancePageNameForBrowse(pageName) & "(recordId, currentUser, accessProfile)",
                       "                If page.ShowDialog(Me) = DialogResult.OK Then RefreshGridForCustomAction(recordId)",
                       "            End Using",
                       "            Return True",
                       "        End Function"
                   }),
                   String.Empty),
                "    End Class",
                "End Namespace",
                ""
            })
        End Function

        Private Shared Function maintenancePageNameForBrowse(browsePageName As String) As String
            Return If(browsePageName.EndsWith("_B", StringComparison.OrdinalIgnoreCase), browsePageName.Substring(0, browsePageName.Length - 2) & "_U", browsePageName & "_U")
        End Function

        Private Shared Function BuildMaintenanceSource(pageName As String, tableName As String, primaryKey As String, fields As List(Of String), requiredFields As List(Of String), lookupFields As List(Of String)) As String
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
                output.AppendLine("        Private ReadOnly " & ControlVariable(field) & " As TextBox")
            Next
            output.AppendLine()
            output.AppendLine("        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)")
            output.AppendLine("            MyBase.New()")
            output.AppendLine("            recordId = id")
            output.AppendLine("            currentUser = user")
            output.AppendLine("            accessProfile = profile")
            output.AppendLine("            Text = """ & EscapeLiteral(pageName) & """")
            output.AppendLine("            ClientSize = New Size(600, " & Math.Max(120, 55 + fields.Count * 42).ToString() & ")")
            output.AppendLine("            okButton.Location = New Point(ClientSize.Width - 270, ClientSize.Height - 46)")
            output.AppendLine("            cancelActionButton.Location = New Point(ClientSize.Width - 135, ClientSize.Height - 46)")
            output.AppendLine("            enumButton.Location = New Point(20, ClientSize.Height - 46)")
            Dim y = 20
            For Each field In fields
                output.AppendLine("            " & ControlVariable(field) & " = AddField(""" & EscapeLiteral(field) & """, " & y.ToString() & ", False, " & If(requiredFields.Any(Function(item) String.Equals(item, field, StringComparison.OrdinalIgnoreCase)), "True", "False") & ")")
                y += 42
            Next
                output.AppendLine("            SetManualTabOrder(" & String.Join(", ", fields.Select(Function(field) ControlVariable(field)).Concat({"enumButton", "okButton", "cancelActionButton"})) & ")")
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
            output.AppendLine("            For Each control In New Control() {" & String.Join(", ", fields.Select(Function(field) ControlVariable(field))) & "}")
            output.AppendLine("                Dim fieldName = control.Name.Substring(""TextBox_"".Length)")
            output.AppendLine("                control.DataBindings.Clear()")
            output.AppendLine("                control.DataBindings.Add(""Text"", formBindingSource, fieldName, True, DataSourceUpdateMode.Never)")
            output.AppendLine("                If record.Table.Columns.Contains(fieldName) Then control.Text = If(record(fieldName) Is DBNull.Value, String.Empty, Convert.ToString(record(fieldName)))")
            output.AppendLine("            Next")
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
            output.AppendLine("        Protected Overrides Function SaveRecord() As Boolean")
            output.AppendLine("            Dim values As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)")
            For Each field In fields
                output.AppendLine("            values(""" & EscapeLiteral(field) & """) = " & ControlVariable(field) & ".Text")
            Next
            output.AppendLine("            Dim savedId As Integer = recordId")
            output.AppendLine("            If savedId <= 0 AndAlso record.Table.Columns.Contains(primaryKey) AndAlso Not record.IsNull(primaryKey) Then Integer.TryParse(Convert.ToString(record(primaryKey)), savedId)")
            output.AppendLine("            Dim savedRecordId = DataAccess.SaveGeneratedPageRecordWithId(tableName, primaryKey, savedId, values, originalRowVersion, If(SessionState.IsActive, SessionState.Current.Value.UserID, 0))")
            output.AppendLine("            If savedRecordId <= 0 OrElse record Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) Then Return False")
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
