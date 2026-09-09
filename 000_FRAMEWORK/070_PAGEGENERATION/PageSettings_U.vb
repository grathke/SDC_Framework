Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' What can be changed about a page once it exists.
    '''
    ''' Opened by Modify on FW_PageGeneration_B, and keyed on the page NAME rather than a request id
    ''' - so it also serves a page nobody generated, which is most of the framework's own. See
    ''' PAGE_MAINTENANCE_SPEC.md.
    '''
    ''' Everything here is either data the running page reads from FW_Pages, or the generated half of
    ''' a maintenance page. Nothing on it can leave the application half-moved, which is why the page
    ''' names, the underlying table, the menu caller and the icon are deliberately absent: changing
    ''' one of those describes a different page, and that is the generation request's job.
    '''
    ''' Inherits Form rather than FW_Base_U. It maintains a page rather than a record, has no table
    ''' of its own, no RowVersion and no CRUD row - the contract FW_Base_U exists to enforce does not
    ''' apply. A documented exception, like Roles_U.
    ''' </summary>
    Public Class FW_PageSettings_U
        Inherits Form

        Private ReadOnly pageName As String
        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly requestId As Integer

        Private ReadOnly captionTextBox As TextBox
        Private ReadOnly hotFieldsCheckBox As CheckBox
        Private ReadOnly hotFieldsGrid As DataGridView
        Private ReadOnly applyFieldsButton As Button
        Private ReadOnly saveButton As Button
        Private ReadOnly closeButton As Button
        Private ReadOnly maintenanceHeading As Label
        Private ReadOnly resultLabel As Label

        Public Sub New(pageName As String, user As UserContext, Optional profile As AccessProfile = Nothing)
            Me.pageName = If(pageName, String.Empty).Trim()
            currentUser = user
            accessProfile = profile
            requestId = DataAccess.GetPageGenerationIdForPage(Me.pageName)

            Text = "PAGE SETTINGS: " & Me.pageName.ToUpperInvariant()
            StartPosition = FormStartPosition.CenterParent
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ClientSize = New Size(620, 560)
            BackColor = Color.White

            ' Which half each part of this screen touches, said on the screen. The caption and Hot
            ' Fields belong to the browse page and take effect on its next open; Apply Fields
            ' rewrites the maintenance page's code and needs a build. One title naming the browse
            ' page, over a button that rewrites the other one, is a screen that has to be explained.
            Dim browseHeading As New Label() With {
                .Text = "BROWSE PAGE - " & pageName.ToUpperInvariant() & "   (applies on next open, no rebuild)",
                .AutoSize = True,
                .Location = New Point(20, 0),
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(24, 45, 78)
            }

            Dim captionLabel As New Label() With {
                .Text = "PAGE CAPTION:",
                .AutoSize = True,
                .Location = New Point(20, 24),
                .ForeColor = Color.DimGray
            }

            ' The caption every surface resolves through - the page's title, the button that opens
            ' it, and the table list on the diagnostic page. One value, so they cannot disagree.
            captionTextBox = New TextBox() With {
                .Name = "TextBox_Table_Alias",
                .Location = New Point(140, 20),
                .Size = New Size(300, 26),
                .BorderStyle = BorderStyle.FixedSingle
            }

            hotFieldsCheckBox = New CheckBox() With {
                .Name = "CheckBox_UseHotFields",
                .Text = "Show the Hot Fields button on this page",
                .Location = New Point(140, 58),
                .AutoSize = True
            }

            Dim hotFieldsLabel As New Label() With {
                .Text = "HOT FIELDS:",
                .AutoSize = True,
                .Location = New Point(20, 60),
                .ForeColor = Color.DimGray
            }

            ' The live list, the same value the panel's own ticks write. Editing it from two screens
            ' is ordinary; what would be wrong is a save here restoring the request's older answer,
            ' which is why the request applies its ticks on Generate alone.
            hotFieldsGrid = New DataGridView() With {
                .Name = "Grid_HotFields",
                .Location = New Point(140, 88),
                .Size = New Size(440, 330),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .RowHeadersVisible = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .BackgroundColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle,
                .AutoGenerateColumns = False
            }
            hotFieldsGrid.Columns.Add(New DataGridViewCheckBoxColumn() With {
                .Name = "Shown",
                .HeaderText = "Show",
                .Width = 60
            })
            hotFieldsGrid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "FieldName",
                .HeaderText = "Field",
                .ReadOnly = True,
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })

            maintenanceHeading = New Label() With {
                .AutoSize = True,
                .Location = New Point(20, 428),
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Bold),
                .ForeColor = Color.FromArgb(24, 45, 78)
            }

            applyFieldsButton = New Button() With {
                .Text = "APPLY FIELDS",
                .Location = New Point(20, 452),
                .Size = New Size(130, 30)
            }
            AddHandler applyFieldsButton.Click, AddressOf ApplyFieldsButton_Click

            saveButton = New Button() With {
                .Text = "SAVE",
                .Location = New Point(370, 452),
                .Size = New Size(100, 30)
            }
            AddHandler saveButton.Click, AddressOf SaveButton_Click

            closeButton = New Button() With {
                .Text = "CLOSE",
                .Location = New Point(480, 452),
                .Size = New Size(100, 30),
                .DialogResult = DialogResult.Cancel
            }

            resultLabel = New Label() With {
                .AutoSize = False,
                .Location = New Point(20, 492),
                .Size = New Size(560, 50),
                .ForeColor = Color.FromArgb(52, 60, 70)
            }

            Controls.AddRange({browseHeading, captionLabel, captionTextBox, hotFieldsLabel, hotFieldsCheckBox,
                               hotFieldsGrid, maintenanceHeading, applyFieldsButton, saveButton, closeButton, resultLabel})

            LoadSettings()
        End Sub

        ''' <summary>
        ''' Reads what the page shows now. Everything comes from the FW_Pages row the running page
        ''' reads, so what is on screen here is what the page is actually doing.
        ''' </summary>
        Private Sub LoadSettings()
            captionTextBox.Text = DataAccess.GetPageAliasByWindowOrPage(0, pageName)
            hotFieldsCheckBox.Checked = DataAccess.GetPageUsesHotFields(pageName)

            Dim tableName = DataAccess.GetPageDbTableByWindowOrPage(0, pageName)
            Dim selected = DataAccess.GetPageHotFields(pageName)

            hotFieldsGrid.Rows.Clear()
            If String.IsNullOrWhiteSpace(tableName) Then
                Report("This page has no FW_Pages row yet, so there is nothing to show. Open the page once and it will create one.")
                saveButton.Enabled = False
                Return
            End If

            For Each fieldName In DataAccess.GetTableFieldNames(tableName)
                hotFieldsGrid.Rows.Add(selected.Contains(fieldName), fieldName)
            Next

            ' Apply Fields needs a request to read the field list from. A hand-written page has none,
            ' and saying so is better than a button that fails when pressed.
            applyFieldsButton.Enabled = requestId > 0

            Dim maintenancePageName = ResolveMaintenancePageName()
            maintenanceHeading.Text = If(maintenancePageName = String.Empty,
                                         "MAINTENANCE PAGE - none for this page",
                                         "MAINTENANCE PAGE - " & maintenancePageName.ToUpperInvariant() & "   (rewrites its fields, needs a rebuild)")

            If requestId <= 0 Then
                Report("Caption and Hot Fields can be changed here. Apply Fields is unavailable: this page has no generation request.")
            Else
                Report("Caption and Hot Fields take effect the next time the page opens. Apply Fields rewrites the maintenance page's generated fields and needs a rebuild.")
            End If
        End Sub

        Private Sub SaveButton_Click(sender As Object, e As EventArgs)
            ' Not merely a hidden button: this page changes what every user of that page sees.
            If Not SessionState.IsApplicationAdmin Then
                Report("Only an App Admin can change a page's settings.")
                Return
            End If

            Dim updatedBy = If(SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
            Dim applied As New List(Of String)()

            Dim selected As New List(Of String)()
            For Each row As DataGridViewRow In hotFieldsGrid.Rows
                If Convert.ToBoolean(row.Cells("Shown").Value) Then
                    selected.Add(Convert.ToString(row.Cells("FieldName").Value))
                End If
            Next

            If DataAccess.SavePageHotFields(pageName, selected, updatedBy) Then
                applied.Add(selected.Count.ToString() & " Hot Fields")
            End If

            If DataAccess.SetPageUsesHotFields(pageName, hotFieldsCheckBox.Checked) Then
                applied.Add(If(hotFieldsCheckBox.Checked, "Hot Fields on", "Hot Fields off"))
            End If

            Dim caption = captionTextBox.Text.Trim()
            If caption <> String.Empty AndAlso DataAccess.SavePageCaption(pageName, caption, updatedBy) Then
                applied.Add("the caption")
            End If

            If applied.Count = 0 Then
                Report("Nothing was saved.")
                Return
            End If

            Report("SAVED: " & String.Join(", ", applied) & "." & Environment.NewLine &
                   "These take effect the next time " & pageName & " is opened. No rebuild is needed.")
        End Sub

        ''' <summary>
        ''' Rewrites the maintenance page's generated half from the request's field list.
        '''
        ''' The hand-written half is not touched and not read, which is what the split bought. It
        ''' does need a rebuild afterwards, unlike everything else on this page - fields are code.
        ''' </summary>
        Private Sub ApplyFieldsButton_Click(sender As Object, e As EventArgs)
            If Not SessionState.IsApplicationAdmin Then
                Report("Only an App Admin can apply fields.")
                Return
            End If

            If requestId <= 0 Then
                Report("This page has no generation request, so there is no field list to apply.")
                Return
            End If

            Dim result = PageGenerator.ApplyMaintenanceFields(requestId, Environment.CurrentDirectory)
            If Not result.Succeeded Then
                Report("APPLY FIELDS FAILED:" & Environment.NewLine & String.Join(Environment.NewLine, result.Errors))
                Return
            End If

            Report("APPLIED: " & String.Join(", ", result.CreatedFiles) & "." & Environment.NewLine &
                   "Your own half of the page was not touched. Rebuild to see the change.")
        End Sub

        ''' <summary>
        ''' The maintenance page this browse page pairs with, from the request. Empty where there is
        ''' no request or the request generates no _U - a browse-only page has nothing to apply
        ''' fields to, and the heading says so rather than naming a page that does not exist.
        ''' </summary>
        Private Function ResolveMaintenancePageName() As String
            If requestId <= 0 Then Return String.Empty

            Dim request = DataAccess.GetPageGenerationById(requestId)
            If request Is Nothing OrElse Not request.Table.Columns.Contains("MaintenancePageName") OrElse request.IsNull("MaintenancePageName") Then
                Return String.Empty
            End If

            Return Convert.ToString(request("MaintenancePageName")).Trim()
        End Function

        Private Sub Report(message As String)
            resultLabel.Text = message.ToUpperInvariant()
        End Sub
    End Class

End Namespace
