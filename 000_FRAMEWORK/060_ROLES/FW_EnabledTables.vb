Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Which tables the application offers, as a checklist.
    '''
    ''' A tick is FW_RoleSchema.IsActive, shown as it is stored rather than inverted. That is the
    ''' one switch three places already read: Roles_U's left grid, the access diagnostic, and page
    ''' generation's Select Table. Until this existed it could only be set with a SQL statement, so
    ''' the list of what the application offers was one nobody could edit from the application.
    '''
    ''' Ticked means available, which is how every table starts - the sweep registers them active.
    ''' Unticking one takes it out of those three lists and nothing else: permission resolution
    ''' reads FW_RoleDetails without consulting IsActive, so a role that already holds the table
    ''' keeps working, and a page built on it still opens. What is lost is the ability to change
    ''' either. The two counts say how much of that there is before the decision is made.
    '''
    ''' Not a _B/_U pair. There is one record per table and one field worth setting on it, and a
    ''' page pair for that would mean opening a record to tick a box - the work is comparing the
    ''' whole list, which is what a checklist shows and a browse grid does not.
    '''
    ''' Nothing is written until OK. A tick is a decision about access, and a list that saved as
    ''' you went would revoke on the way to somewhere else.
    ''' </summary>
    Public Class FW_EnabledTables
        Inherits Form

        Private ReadOnly currentUser As UserContext
        Private ReadOnly grid As DataGridView
        Private ReadOnly originalEnablement As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' The user is optional because Roles_U has none to give - it is opened with a role id and
        ''' a registration id and nothing else. The audit writer falls back to the session's acting
        ''' user, which is the same person either way.
        ''' </summary>
        Public Sub New(Optional user As UserContext = Nothing)
            currentUser = user

            Me.Text = "Enabled Tables"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False
            Me.ClientSize = New Size(640, 560)
            Me.BackColor = Color.White

            Dim heading As New Label With {
                .Text = "A TICK MEANS THE TABLE IS AVAILABLE." & Environment.NewLine &
                        "UNTICKING IT REMOVES THE TABLE FROM ROLES, FROM THE ACCESS DIAGNOSTIC," & Environment.NewLine &
                        "AND FROM PAGE GENERATION. PERMISSIONS AND PAGES ALREADY IN PLACE KEEP WORKING.",
                .Dock = DockStyle.Top,
                .Height = 62,
                .Padding = New Padding(12, 10, 12, 0),
                .Font = New Font("Segoe UI", 9.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(42, 42, 42)
            }

            grid = New DataGridView With {
                .Dock = DockStyle.Fill,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .RowHeadersVisible = False,
                .MultiSelect = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                .BackgroundColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle
            }

            grid.Columns.Add(New DataGridViewCheckBoxColumn() With {
                .Name = "IsEnabled",
                .HeaderText = "Enabled",
                .FillWeight = 18
            })
            grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "Table_Alias",
                .HeaderText = "Table",
                .ReadOnly = True,
                .FillWeight = 42
            })
            grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "DB_Table",
                .HeaderText = "Database Table",
                .ReadOnly = True,
                .FillWeight = 42
            })
            grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "RoleCount",
                .HeaderText = "Roles",
                .ReadOnly = True,
                .FillWeight = 14,
                .DefaultCellStyle = New DataGridViewCellStyle With {.Alignment = DataGridViewContentAlignment.MiddleRight}
            })
            grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "PageCount",
                .HeaderText = "Pages",
                .ReadOnly = True,
                .FillWeight = 14,
                .DefaultCellStyle = New DataGridViewCellStyle With {.Alignment = DataGridViewContentAlignment.MiddleRight}
            })

            Dim actions As New Panel With {.Dock = DockStyle.Bottom, .Height = 52, .Padding = New Padding(12, 10, 12, 10)}
            Dim okButton As New Button With {.Text = "OK", .Size = New Size(96, 30), .DialogResult = DialogResult.None}
            Dim cancelButton As New Button With {.Text = "Cancel", .Size = New Size(96, 30), .DialogResult = DialogResult.Cancel}
            okButton.Location = New Point(Me.ClientSize.Width - 220, 10)
            cancelButton.Location = New Point(Me.ClientSize.Width - 116, 10)
            actions.Controls.Add(okButton)
            actions.Controls.Add(cancelButton)

            Me.Controls.Add(grid)
            Me.Controls.Add(actions)
            Me.Controls.Add(heading)
            Me.AcceptButton = okButton
            Me.CancelButton = cancelButton

            AddHandler grid.CurrentCellDirtyStateChanged, AddressOf Grid_CurrentCellDirtyStateChanged
            AddHandler okButton.Click, AddressOf OkButton_Click

            LoadTables()
        End Sub

        ''' <summary>
        ''' A check box commits on leaving the cell, so a tick would not be seen until the row was
        ''' left. Every other grid in the framework does this for the same reason.
        ''' </summary>
        Private Sub Grid_CurrentCellDirtyStateChanged(sender As Object, e As EventArgs)
            If grid.IsCurrentCellDirty Then
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit)
            End If
        End Sub

        Private Sub LoadTables()
            grid.Rows.Clear()
            originalEnablement.Clear()

            Dim tables As DataTable
            Try
                tables = DataAccess.GetTableEnablement()
            Catch ex As Exception
                MessageBox.Show(Me, "THE TABLE LIST COULD NOT BE READ." & Environment.NewLine & ex.Message,
                                "Enabled Tables", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End Try

            For Each row As DataRow In tables.Rows
                Dim dbTable = Convert.ToString(row("DB_Table"))
                If String.IsNullOrWhiteSpace(dbTable) Then Continue For

                Dim enabled = row.IsNull("IsEnabled") OrElse Convert.ToBoolean(row("IsEnabled"))
                Dim roleCount = If(row.IsNull("RoleCount"), 0, Convert.ToInt32(row("RoleCount")))
                Dim pageCount = If(row.IsNull("PageCount"), 0, Convert.ToInt32(row("PageCount")))

                originalEnablement(dbTable) = enabled
                grid.Rows.Add(enabled, Convert.ToString(row("Table_Alias")), dbTable, roleCount, pageCount)
            Next
        End Sub

        ''' <summary>
        ''' Saves what changed, after saying plainly what disabling a granted table costs.
        '''
        ''' Only tables being disabled are named in the warning. Putting one back is not a loss of
        ''' access and does not need a decision.
        ''' </summary>
        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            Dim changes As New Dictionary(Of String, Boolean)(StringComparer.OrdinalIgnoreCase)
            Dim grantedBeingDisabled As New List(Of String)()

            For Each row As DataGridViewRow In grid.Rows
                Dim dbTable = Convert.ToString(row.Cells("DB_Table").Value)
                If String.IsNullOrWhiteSpace(dbTable) Then Continue For

                Dim enabled = row.Cells("IsEnabled").Value IsNot Nothing AndAlso Convert.ToBoolean(row.Cells("IsEnabled").Value)
                Dim wasEnabled As Boolean
                If Not originalEnablement.TryGetValue(dbTable, wasEnabled) Then Continue For
                If enabled = wasEnabled Then Continue For

                changes(dbTable) = enabled

                If Not enabled Then
                    Dim roleCount = If(row.Cells("RoleCount").Value Is Nothing, 0, Convert.ToInt32(row.Cells("RoleCount").Value))
                    Dim pageCount = If(row.Cells("PageCount").Value Is Nothing, 0, Convert.ToInt32(row.Cells("PageCount").Value))
                    If roleCount > 0 OrElse pageCount > 0 Then
                        Dim parts As New List(Of String)()
                        If roleCount > 0 Then parts.Add(roleCount.ToString() & If(roleCount = 1, " role", " roles"))
                        If pageCount > 0 Then parts.Add(pageCount.ToString() & If(pageCount = 1, " page", " pages"))
                        grantedBeingDisabled.Add(dbTable & " (" & String.Join(", ", parts) & ")")
                    End If
                End If
            Next

            If changes.Count = 0 Then
                Me.DialogResult = DialogResult.Cancel
                Me.Close()
                Return
            End If

            If grantedBeingDisabled.Count > 0 Then
                Dim warning = "THESE TABLES ARE STILL IN USE:" &
                              Environment.NewLine & Environment.NewLine &
                              String.Join(Environment.NewLine, grantedBeingDisabled) &
                              Environment.NewLine & Environment.NewLine &
                              "EXISTING PERMISSIONS KEEP WORKING AND EXISTING PAGES STILL OPEN." &
                              Environment.NewLine &
                              "WHAT STOPS IS CHANGING THEM: THE TABLE LEAVES ROLES, THE ACCESS" &
                              Environment.NewLine &
                              "DIAGNOSTIC, AND THE TABLE LIST IN PAGE GENERATION." &
                              Environment.NewLine & Environment.NewLine &
                              "NOTHING IS DELETED, SO ENABLING A TABLE AGAIN PUTS IT ALL BACK." &
                              Environment.NewLine & Environment.NewLine &
                              "CONTINUE?"

                If MessageBox.Show(Me, warning, "Enabled Tables",
                                   MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then
                    Return
                End If
            End If

            Try
                Dim written = DataAccess.SetTableEnablement(changes)

                DataAccess.LogUpdateAudit("FW_EnabledTables",
                                          "FW_RoleSchema",
                                          "UPDATE",
                                          "AfterSave",
                                          recordKey:=String.Join(", ", changes.Select(Function(pair) pair.Key & "=" & If(pair.Value, "enabled", "disabled"))),
                                          saveSucceeded:=True,
                                          userId:=If(currentUser Is Nothing, CType(Nothing, Integer?), CType(currentUser.UserId, Integer?)))

                MessageBox.Show(Me, written.ToString() & If(written = 1, " TABLE WAS CHANGED.", " TABLES WERE CHANGED."),
                                "Enabled Tables", MessageBoxButtons.OK, MessageBoxIcon.Information)

                Me.DialogResult = DialogResult.OK
                Me.Close()
            Catch ex As Exception
                MessageBox.Show(Me, "NOTHING WAS CHANGED." & Environment.NewLine & ex.Message,
                                "Enabled Tables", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub
    End Class
End Namespace
