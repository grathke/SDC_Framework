Option Strict On
Option Explicit On

Imports System.Data
Imports System.Drawing
Imports System.Text.RegularExpressions
Imports System.Windows.Forms

Namespace SDC.Framework
    Public Class Roles_B
        Inherits FW_Base_B

        Private Const DefaultSelectSql As String = "SELECT RoleID, RegistrationID, RoleName, IsActive, UpdatedOn FROM dbo.FW_Roles WHERE RegistrationID = @RegistrationID"

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Private ReadOnly accessTableName As String
        Private ReadOnly titleLabel As Label
        Private activeSqlText As String = DefaultSelectSql
        Private currentDbTableName As String = String.Empty
        Private ReadOnly registrationLabel As Label
        Private ReadOnly sqlTextBox As TextBox
        Private ReadOnly registrationComboBox As ComboBox
        Private ReadOnly rolesGrid As DataGridView
        Private ReadOnly showDeletedButton As Button
        Private ReadOnly restoreDeletedButton As Button
        Private ReadOnly showNormalButton As Button
        Private ReadOnly newButton As Button
        Private ReadOnly modifyButton As Button
        Private ReadOnly deleteButton As Button
        Private ReadOnly closeButton As Button
        Private showDeletedRecordsOnly As Boolean = False
        Private isLoadingRegistrations As Boolean = False
        Private missingMaintenanceKeyInResult As Boolean = False
        Private missingMaintenanceKeyWarningShown As Boolean = False
        Private Shared ReadOnly ShowDeletedText As String = "Show Deleted"
        Private Shared ReadOnly ShowNormalText As String = "Show Normal"
        Private ReadOnly initialRegistrationId As Integer

        Private Structure GridViewState
            Public HasSelection As Boolean
            Public SelectedRoleId As Integer
            Public SelectedRowOffsetFromTop As Integer
            Public FallbackFirstDisplayedIndex As Integer

            ''' <summary>
            ''' Whether the restored row should flash, on the same terms as FW_Base_B: set only
            ''' when a caller named a record, which happens when one has just been saved, and never
            ''' on an ordinary refresh where the selection is being put back rather than pointed at.
            ''' </summary>
            Public FlashSelection As Boolean
        End Structure

        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing, Optional tableName As String = "ROLES")
            MyBase.New(user, profile, tableName, False)

            currentUser = user
            accessProfile = profile
            accessTableName = If(String.IsNullOrWhiteSpace(tableName), "ROLES", tableName)
            initialRegistrationId = GetSessionRegistrationId()

            Me.Text = "Roles Listing"
            Me.StartPosition = FormStartPosition.CenterParent
            Me.MinimumSize = New Size(560, 350)
            Me.ClientSize = New Size(620, 600)
            ' No BackColor is set here. FW_Base_B's constructor has already applied this page's
            ' stored colour, and assigning white a line later painted over it - which looked like
            ' the colour never being saved, when it was saved and then immediately discarded.
            ' With nothing stored the picker applies its own default, which is near white.

            titleLabel = New Label() With {
                .Text = "ROLES LISTING",
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .AutoSize = True,
                .Location = New Point(20, 15)
            }
            registrationLabel = New Label() With {
                .Text = "Registration:",
                .AutoSize = True,
                .Location = New Point(300, 18),
                .ForeColor = Color.DimGray
            }

            registrationComboBox = New ComboBox() With {
                .Location = New Point(390, 14),
                .Size = New Size(225, 26),
                .DropDownStyle = ComboBoxStyle.DropDownList,
                .DropDownWidth = 280
            }

            sqlTextBox = New TextBox() With {
                .Location = New Point(-10000, -10000),
                .Size = New Size(1, 1),
                .Visible = False,
                .Enabled = False,
                .Multiline = True,
                .Text = DefaultSelectSql
            }

            rolesGrid = New DataGridView() With {
                .Location = New Point(20, 140),
                .Size = New Size(Me.ClientSize.Width - 40, Me.ClientSize.Height - 260),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
                .ReadOnly = True,
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .MultiSelect = False,
                .RowHeadersVisible = False,
                .AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                .AutoGenerateColumns = True
            }
            ApplyBrowseGridStandard(rolesGrid)

            newButton = New Button() With {.Text = "New", .Size = New Size(80, 32), .Location = New Point(108, 84)}
            modifyButton = New Button() With {.Text = "Modify", .Size = New Size(80, 32), .Location = New Point(196, 84)}
            deleteButton = New Button() With {.Text = "Delete", .Size = New Size(80, 32), .Location = New Point(284, 84)}
            showDeletedButton = New Button() With {.Text = ShowDeletedText, .Size = New Size(130, 32), .Location = New Point(372, 84)}
            restoreDeletedButton = New Button() With {.Text = "Restore", .Size = New Size(64, 32), .Location = New Point(372, 84), .Visible = False}
            showNormalButton = New Button() With {.Text = "Normal", .Size = New Size(64, 32), .Location = New Point(438, 84), .Visible = False}
            closeButton = New Button() With {.Text = "Close", .Size = New Size(80, 32), .Location = New Point(372, 84)}


            AddHandler registrationComboBox.SelectedIndexChanged, AddressOf RegistrationComboBox_SelectedIndexChanged

            ' The caption follows the combo rather than being placed beside where it used to be.
            ' This page moves the combo on every resize and now narrows it to its content as well,
            ' so a fixed x for the label lands on top of the combo.
            AddHandler registrationLabel.TextChanged, Sub(sender, e) SeatRegistrationLabel()
            AddHandler registrationComboBox.SizeChanged, Sub(sender, e) SeatRegistrationLabel()
            AddHandler registrationComboBox.LocationChanged, Sub(sender, e) SeatRegistrationLabel()
            AddHandler newButton.Click, AddressOf NewButton_Click
            AddHandler modifyButton.Click, AddressOf ModifyButton_Click
            AddHandler deleteButton.Click, AddressOf DeleteButton_Click
            AddHandler showDeletedButton.Click, AddressOf ShowDeletedButton_Click
            AddHandler restoreDeletedButton.Click, AddressOf RestoreDeletedButton_Click
            AddHandler showNormalButton.Click, AddressOf ShowNormalButton_Click
            AddHandler closeButton.Click, AddressOf CloseButton_Click
            AddHandler rolesGrid.CellDoubleClick, AddressOf RolesGrid_CellDoubleClick
            AddHandler rolesGrid.CellFormatting, AddressOf RolesGrid_CellFormatting
            AddHandler rolesGrid.SelectionChanged, AddressOf RolesGrid_SelectionChanged

            AddHandler Me.Resize, AddressOf RolesForm_Resize
            AddHandler Me.FormClosing, AddressOf Roles_B_FormClosing
            AddHandler Me.Load, AddressOf Roles_B_Load
            AddHandler Me.Shown, AddressOf Roles_B_Shown

            Me.Controls.Add(titleLabel)
            Me.Controls.Add(registrationLabel)
            Me.Controls.Add(registrationComboBox)
            Me.Controls.Add(sqlTextBox)
            Me.Controls.Add(rolesGrid)
            Me.Controls.Add(newButton)
            Me.Controls.Add(modifyButton)
            Me.Controls.Add(deleteButton)
            Me.Controls.Add(showDeletedButton)
            Me.Controls.Add(restoreDeletedButton)
            Me.Controls.Add(showNormalButton)
            Me.Controls.Add(closeButton)

            RolesForm_Resize(Me, EventArgs.Empty)
        End Sub

        Private Sub Roles_B_Shown(sender As Object, e As EventArgs)
            ' The picker is built and shown or hidden by FW_Base_B's constructor, and placed by this
            ' page's own layout. Doing both again once the form is on screen means neither depends on
            ' when the other ran, and BringToFront settles the z-order: the button is added to the
            ' form during the base constructor, so every control this page adds afterwards sits in
            ' front of it.
            If PageColorPicker IsNot Nothing Then
                PageColorPicker.UpdateVisibility()
                PageColorPicker.Button.BringToFront()
                RolesForm_Resize(Me, EventArgs.Empty)
            End If

            BeginInvoke(New MethodInvoker(AddressOf FocusRolesGridOnEntry))
        End Sub

        Private Sub FocusRolesGridOnEntry()
            FocusGridForBrowseEntry(rolesGrid)
        End Sub

        Private Sub LoadInternalSqlFromPages()
            Try
                Dim activeSession = SessionState.Current
                If Not activeSession.HasValue OrElse activeSession.Value.RegistrationID <= 0 Then
                    activeSqlText = DefaultSelectSql
                    sqlTextBox.Text = activeSqlText
                    Return
                End If

                Dim registrationId = activeSession.Value.RegistrationID
                Dim pageName = Me.GetType().Name
                currentDbTableName = DataAccess.GetPageDbTableByWindowOrPage(registrationId, pageName).Trim()
                Dim displayTitle = PageTitleHelper.BuildListingTitle(registrationId, currentDbTableName, "Roles")
                Me.Text = displayTitle
                titleLabel.Text = displayTitle
                Dim sql = DataAccess.GetPageSqlByWindowOrPage(registrationId, pageName)

                If Not String.IsNullOrWhiteSpace(sql) Then
                    sql = sql.Trim()
                    activeSqlText = sql
                    sqlTextBox.Text = activeSqlText
                    Return
                End If

                activeSqlText = DefaultSelectSql
                sqlTextBox.Text = activeSqlText
            Catch
                currentDbTableName = String.Empty
                activeSqlText = DefaultSelectSql
                sqlTextBox.Text = activeSqlText
            End Try
        End Sub


        Private Sub SeatRegistrationLabel()
            RegistrationComboHelper.SeatLabel(registrationLabel, registrationComboBox)
        End Sub
        Private Sub RolesForm_Resize(sender As Object, e As EventArgs)
            Dim gridRightEdge As Integer = Me.ClientSize.Width - 20

            ' Less the Help Desk button, which this page inherits from FW_Base_B and which places
            ' itself against the form's right edge. Measured from the form here rather than from a
            ' centred content width, so the two met at every window size rather than only at narrow
            ' ones - the same collision the base page had, arrived at by a different route.
            Dim comboRightEdge As Integer = gridRightEdge - 20 - HelpDeskLauncher.ReservedWidth
            registrationComboBox.Left = comboRightEdge - registrationComboBox.Width

            Dim buttonTop As Integer = 84
            Dim buttonLeft As Integer = 20
            Dim buttonGap As Integer = 8

            ' The CRUD group closes up among itself when a permission hides one of its buttons: the
            ' chain this replaced read each button's Right whether or not it was on screen, so a
            ' hidden button left a hole behind it.
            '
            ' What sits to the right of the group does not move. Its position is measured from the
            ' group's full width rather than from wherever the last visible button happens to end,
            ' so Show Deleted stays where the eye expects it however many CRUD buttons are showing.
            Dim crudButtons As Button() = {newButton, modifyButton, deleteButton}
            Dim nextLeft As Integer = buttonLeft
            Dim reservedRight As Integer = buttonLeft

            For Each crudButton As Button In crudButtons
                reservedRight += crudButton.Width + buttonGap

                If Not crudButton.Visible Then
                    Continue For
                End If

                crudButton.Top = buttonTop
                crudButton.Left = nextLeft
                nextLeft = crudButton.Right + buttonGap
            Next

            showDeletedButton.Top = buttonTop
            showDeletedButton.Left = reservedRight
            restoreDeletedButton.Top = buttonTop
            restoreDeletedButton.Left = showDeletedButton.Left
            showNormalButton.Top = buttonTop
            showNormalButton.Left = restoreDeletedButton.Right + 2

            closeButton.Top = buttonTop
            closeButton.Left = gridRightEdge - closeButton.Width

            ' This page lays out its own action row, so FW_Base_B never places the picker it built
            ' for us. Positioned from Close rather than from the CRUD group, so it holds its place
            ' when a permission hides one of those buttons.
            If PageColorPicker IsNot Nothing Then
                ' Matched to Close so the two sit on one baseline. The picker is built at the
                ' standard 36 high and this page's buttons are 32, which put it 4px out.
                PageColorPicker.Button.Height = closeButton.Height
                PageColorPicker.Button.Top = buttonTop
                PageColorPicker.Button.Left = closeButton.Left - PageColorPicker.Button.Width - buttonGap
                PageColorPicker.PositionPanel()
            End If

            rolesGrid.Width = Me.ClientSize.Width - 40
            rolesGrid.Height = Me.ClientSize.Height - 260
            GridColumnsManager.FitVisibleColumnsToAvailableWidth(rolesGrid)
        End Sub

        ''' <summary>
        ''' Shows only the CRUD buttons this role is allowed to use.
        ''' </summary>
        ''' <remarks>
        ''' This page has its own New/Modify/Delete buttons rather than FW_Base_B's, so the base's
        ''' permission gating never reached them: the profile was taken in the constructor, stored,
        ''' and never read. A role with Can_Create withheld on FW_Roles still got a New button.
        '''
        ''' The capability lookup is AccessProfile's, not a second rule - and it is the same call
        ''' FW_Base_B makes. NormalizeTableKey reduces FW_Roles and ROLES to one key, so the name
        ''' this page was constructed with resolves correctly.
        '''
        ''' A missing profile hides everything. The constructor takes it as optional, so a caller
        ''' that omits it must end up with no buttons rather than all of them.
        ''' </remarks>
        ''' <summary>
        ''' Drops the Application Admin roles from the loaded table for a session that is not itself
        ''' an Application Admin, before the grid ever sees them.
        ''' </summary>
        ''' <remarks>
        ''' The row is removed rather than hidden, so it cannot be selected, modified or deleted -
        ''' there is nothing to select. That is why no matching check was added to the commands.
        '''
        ''' The ids are fetched rather than read from the row. This page's SQL lives in FW_Pages and
        ''' is editable, so Typ_AppAdmin cannot be assumed to be among the columns it selects - it is
        ''' not among them today. Costs one query, and only for a session that is not already an
        ''' Application Admin.
        '''
        ''' If the result carries no PK column the rows cannot be identified and none are removed.
        ''' The page already warns about that case and disables maintenance for it.
        ''' </remarks>
        ''' <summary>
        ''' Numbers the order column 1, 2, 3 down the grid, at the moment each cell is drawn.
        ''' </summary>
        ''' <remarks>
        ''' Nothing is written. The first version of this rewrote the loaded DataTable, which worked
        ''' - the grid is read only, and the table is thrown away - but it still meant the rows in
        ''' memory no longer said what the database said. Formatting the value on its way to the
        ''' screen leaves the data alone entirely.
        '''
        ''' The number is the row's position, so it stays 1..n with no gaps whatever the stored
        ''' DisplayOrder values are, and the hole left by a hidden Application Admin role never
        ''' shows. The stored DisplayOrder still orders the rows and is still what Roles_U edits.
        ''' </remarks>
        Private Sub RolesGrid_CellFormatting(sender As Object, e As DataGridViewCellFormattingEventArgs)
            If e.RowIndex < 0 OrElse e.ColumnIndex < 0 Then
                Return
            End If

            If Not String.Equals(rolesGrid.Columns(e.ColumnIndex).Name, "DisplayOrder", StringComparison.OrdinalIgnoreCase) Then
                Return
            End If

            e.Value = (e.RowIndex + 1).ToString(Globalization.CultureInfo.InvariantCulture)
            e.FormattingApplied = True
        End Sub

        Private Sub RemoveAppAdminRoles(table As DataTable, registrationId As Integer)
            If table Is Nothing OrElse IsAppAdminSession() Then
                Return
            End If

            If Not table.Columns.Contains("PK") Then
                Return
            End If

            Dim hiddenRoleIds = DataAccess.GetAppAdminRoleIds(registrationId)
            If hiddenRoleIds.Count = 0 Then
                Return
            End If

            For index As Integer = table.Rows.Count - 1 To 0 Step -1
                Dim rawKey = table.Rows(index)("PK")
                If rawKey Is Nothing OrElse rawKey Is DBNull.Value Then
                    Continue For
                End If

                Dim roleId As Integer
                If Integer.TryParse(Convert.ToString(rawKey, Globalization.CultureInfo.InvariantCulture), roleId) AndAlso
                   hiddenRoleIds.Contains(roleId) Then
                    table.Rows.RemoveAt(index)
                End If
            Next

            table.AcceptChanges()
        End Sub

        Private Sub ApplyAccess()
            newButton.Visible = CanDo(AccessCapability.Create)
            modifyButton.Visible = CanDo(AccessCapability.Update)
            deleteButton.Visible = CanDo(AccessCapability.Delete)

            newButton.Enabled = newButton.Visible
            modifyButton.Enabled = modifyButton.Visible
            deleteButton.Enabled = deleteButton.Visible

            Dim showRegistrationPicker = IsAppAdminSession()
            registrationLabel.Visible = showRegistrationPicker
            registrationComboBox.Visible = showRegistrationPicker
            registrationComboBox.Enabled = showRegistrationPicker

            UpdateShowDeletedButtonState()
        End Sub

        ''' <summary>Whether this role holds a capability on the roles table. Closed when unknown.</summary>
        Private Function CanDo(required As AccessCapability) As Boolean
            Return accessProfile IsNot Nothing AndAlso accessProfile.Can(accessTableName, required)
        End Function

        ''' <summary>
        ''' Refuses the action when the role does not hold the capability.
        ''' </summary>
        ''' <remarks>
        ''' Hiding a button is not authorization - a hidden button can still be reached by a
        ''' keyboard shortcut, by code, or by a later edit that forgets why it was hidden. The
        ''' command checks for itself.
        ''' </remarks>
        Private Function EnsureCapability(required As AccessCapability, actionName As String) As Boolean
            If CanDo(required) Then
                Return True
            End If

            MessageBox.Show(Me,
                            "YOUR ROLE DOES NOT ALLOW YOU TO " & actionName & " ROLES.",
                            "NOT PERMITTED",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
            Return False
        End Function

        Private Sub RolesGrid_SelectionChanged(sender As Object, e As EventArgs)
            Dim disableForMissingKey = missingMaintenanceKeyInResult

            If showDeletedRecordsOnly Then
                newButton.Enabled = False
                modifyButton.Enabled = False
                deleteButton.Enabled = False
                restoreDeletedButton.Enabled = restoreDeletedButton.Visible AndAlso rolesGrid.SelectedRows.Count > 0 AndAlso Not disableForMissingKey
                Return
            End If

            If rolesGrid.SelectedRows.Count > 0 Then
                modifyButton.Enabled = Not disableForMissingKey
                deleteButton.Enabled = Not disableForMissingKey
            Else
                modifyButton.Enabled = False
                deleteButton.Enabled = False
            End If

            restoreDeletedButton.Enabled = restoreDeletedButton.Visible AndAlso rolesGrid.SelectedRows.Count > 0 AndAlso Not disableForMissingKey
        End Sub

        Private Sub ConfigureIsActiveColumn(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing Then Return
            If Not grid.Columns.Contains("IsActive") Then Return
            Dim col = grid.Columns("IsActive")
            If Not TypeOf col Is DataGridViewCheckBoxColumn Then
                grid.Columns.Remove(col)
                Dim checkCol As New DataGridViewCheckBoxColumn() With {
                    .DataPropertyName = "IsActive",
                    .HeaderText = "Is Active",
                    .Width = 70,
                    .AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                    .ReadOnly = False
                }
                checkCol.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                grid.Columns.Add(checkCol)
            Else
                col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter
                col.ReadOnly = False
            End If
        End Sub

        Private Sub ConfigureRoleNameColumn(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing Then Return
            If Not grid.Columns.Contains("RoleName") Then Return
            Dim col = grid.Columns("RoleName")
            col.ReadOnly = True
        End Sub




        Private Sub RolesGrid_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
            If e.RowIndex < 0 Then
                Return
            End If

            rolesGrid.ClearSelection()
            rolesGrid.Rows(e.RowIndex).Selected = True
            rolesGrid.CurrentCell = rolesGrid.Rows(e.RowIndex).Cells(e.ColumnIndex)

            If modifyButton.Visible AndAlso modifyButton.Enabled Then
                modifyButton.PerformClick()
            End If
        End Sub

        Private Sub Roles_B_Load(sender As Object, e As EventArgs)
            LoadInternalSqlFromPages()
            If IsAppAdminSession() Then
                LoadRegistrations()
            End If
            ApplyAccess()
            RefreshGrid()
        End Sub

        Private Sub LoadRegistrations()
            Try
                isLoadingRegistrations = True
                
                RegistrationComboHelper.Populate(registrationComboBox, initialRegistrationId, True)
                RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
            Catch ex As Exception
                MessageBox.Show("Error loading registrations: " & ex.Message, "Error")
            Finally
                isLoadingRegistrations = False
            End Try
        End Sub

        Private Sub RegistrationComboBox_SelectedIndexChanged(sender As Object, e As EventArgs)
            If Not isLoadingRegistrations Then
                RegistrationComboHelper.UpdateLabelForSelection(registrationLabel, registrationComboBox)
                RefreshGrid()
            End If
        End Sub

        Private Function PromptForTableName() As String
            Dim dlg As New Form()
            dlg.Text = "Select Database Table"
            dlg.StartPosition = FormStartPosition.CenterParent
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog
            dlg.MaximizeBox = False
            dlg.MinimizeBox = False
            dlg.ClientSize = New Size(320, 140)

            Dim lbl As New Label() With {
                .Text = "Select Table:",
                .Location = New Point(15, 15),
                .AutoSize = True
            }
            dlg.Controls.Add(lbl)

            ' Create ComboBox dropdown
            Dim combo As New ComboBox() With {
                .Location = New Point(15, 40),
                .Size = New Size(290, 25),
                .DropDownStyle = ComboBoxStyle.DropDownList
            }
            
            ' Load database tables with aliases
            Dim tablesWithAliases = DataAccess.GetDatabaseTablesWithAliases()
            For Each tableData In tablesWithAliases
                combo.Items.Add(tableData.TableName)
            Next
            ComboWidth.FitToContent(combo)
            
            If combo.Items.Count > 0 Then
                combo.SelectedIndex = 0
            End If
            
            dlg.Controls.Add(combo)

            Dim okBtn As New Button() With {
                .Text = "OK",
                .Location = New Point(145, 85),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.OK
            }
            dlg.Controls.Add(okBtn)

            Dim cancelBtn As New Button() With {
                .Text = "Cancel",
                .Location = New Point(230, 85),
                .Size = New Size(75, 32),
                .DialogResult = DialogResult.Cancel
            }
            dlg.Controls.Add(cancelBtn)

            dlg.AcceptButton = okBtn
            dlg.CancelButton = cancelBtn

            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso combo.SelectedIndex >= 0 Then
                Return combo.SelectedItem.ToString()
            Else
                Return String.Empty
            End If
        End Function

        Private Sub RefreshGrid(Optional selectedRoleId As Integer? = Nothing)
            Dim viewState = CaptureGridViewState()
            If selectedRoleId.HasValue Then
                viewState.HasSelection = True
                viewState.SelectedRoleId = selectedRoleId.Value
                viewState.FlashSelection = True
            End If

            Try
                Dim registrationId As Integer
                If Not TryGetActiveRegistrationId(registrationId) Then
                    rolesGrid.DataSource = Nothing
                    rolesGrid.Rows.Clear()
                    rolesGrid.Refresh()
                    rolesGrid.ClearSelection()
                    Return
                End If

                ApplyCrudButtonCaptions(registrationId)
                UpdateShowDeletedButtonState()

                Dim activeSql = GetActiveBaseSql()
                Dim dt = DataAccess.ExecuteCustomQuery(activeSql,
                                                       registrationId,
                                                       showDeletedRecordsOnly,
                                                       ResolveCurrentRoleFieldTableName(),
                                                       "RoleID")
                RemoveAppAdminRoles(dt, registrationId)
                rolesGrid.DataSource = dt
                ApplyFriendlyColumnHeaders(rolesGrid)
                MaintenanceKeyGuard.HidePkColumn(rolesGrid)
                HideRegistrationIdColumn(rolesGrid)
                HideSoftDeleteColumns(rolesGrid)
                UpdateMaintenanceKeyAvailability()
                ConfigureRoleNameColumn(rolesGrid)
                ConfigureIsActiveColumn(rolesGrid)
                GridColumnsManager.FitVisibleColumnsToAvailableWidth(rolesGrid)
                titleLabel.Text = "ROLES LISTING"
                RestoreGridViewState(viewState)
                UpdateShowDeletedButtonState()
            Catch ex As Exception
                MessageBox.Show("Failed to load roles: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Protected Overrides Sub ApplyCrudButtonCaptions(registrationId As Integer)
            ' Roles_B uses simple CRUD captions from Registration only
            If registrationId <= 0 Then
                Return
            End If

            Dim captions = DataAccess.GetCrudButtonCaptions(registrationId)
            newButton.Text = captions.CreateCaption
            modifyButton.Text = captions.UpdateCaption
            deleteButton.Text = captions.DeleteCaption
        End Sub

        Private Sub HideRegistrationIdColumn(grid As DataGridView)
            Return
        End Sub

        Private Function CaptureGridViewState() As GridViewState
            Dim state As New GridViewState With {
                .HasSelection = False,
                .SelectedRoleId = 0,
                .SelectedRowOffsetFromTop = 0,
                .FallbackFirstDisplayedIndex = 0
            }

            If rolesGrid.Rows Is Nothing OrElse rolesGrid.Rows.Count = 0 Then
                Return state
            End If

            If rolesGrid.FirstDisplayedScrollingRowIndex >= 0 Then
                state.FallbackFirstDisplayedIndex = rolesGrid.FirstDisplayedScrollingRowIndex
            End If

            Dim selectedId = SelectedRoleId()
            If selectedId.HasValue Then
                state.HasSelection = True
                state.SelectedRoleId = selectedId.Value

                If rolesGrid.FirstDisplayedScrollingRowIndex >= 0 AndAlso rolesGrid.SelectedRows.Count > 0 Then
                    state.SelectedRowOffsetFromTop = rolesGrid.SelectedRows(0).Index - rolesGrid.FirstDisplayedScrollingRowIndex
                End If
            End If

            Return state
        End Function

        Private Sub RestoreGridViewState(state As GridViewState)
            If rolesGrid.Rows Is Nothing OrElse rolesGrid.Rows.Count = 0 Then
                Return
            End If

            rolesGrid.ClearSelection()

            If state.HasSelection Then
                Dim selectedIndex = FindRowIndexByRoleId(state.SelectedRoleId)
                If selectedIndex >= 0 Then
                    rolesGrid.Rows(selectedIndex).Selected = True
                    
                    ' Find first visible column
                    Dim firstVisibleCell As DataGridViewCell = Nothing
                    For Each col As DataGridViewColumn In rolesGrid.Columns
                        If col.Visible Then
                            firstVisibleCell = rolesGrid.Rows(selectedIndex).Cells(col.Index)
                            Exit For
                        End If
                    Next
                    
                    If firstVisibleCell IsNot Nothing Then
                        rolesGrid.CurrentCell = firstVisibleCell
                    End If

                    Dim targetTop = selectedIndex - state.SelectedRowOffsetFromTop
                    targetTop = Math.Max(0, Math.Min(targetTop, rolesGrid.RowCount - 1))

                    Try
                        rolesGrid.FirstDisplayedScrollingRowIndex = targetTop
                    Catch telemetryEx As Exception
                        Telemetry.Error(telemetryEx, "Roles_B.RestoreGridViewState")
                    End Try

                    ' After the scroll, not before: a row flashing off-screen says nothing.
                    If state.FlashSelection Then GridRowFlash.Flash(rolesGrid, rolesGrid.Rows(selectedIndex))
                    Return
                End If
            End If

            ' If no selection exists, select the first row
            If rolesGrid.Rows.Count > 0 Then
                rolesGrid.Rows(0).Selected = True
                
                ' Find first visible column for CurrentCell
                Dim firstVisibleCell As DataGridViewCell = Nothing
                For Each col As DataGridViewColumn In rolesGrid.Columns
                    If col.Visible Then
                        firstVisibleCell = rolesGrid.Rows(0).Cells(col.Index)
                        Exit For
                    End If
                Next
                
                If firstVisibleCell IsNot Nothing Then
                    rolesGrid.CurrentCell = firstVisibleCell
                End If
            End If

            Dim fallbackTop = Math.Max(0, Math.Min(state.FallbackFirstDisplayedIndex, rolesGrid.RowCount - 1))
            Try
                rolesGrid.FirstDisplayedScrollingRowIndex = fallbackTop
            Catch telemetryEx As Exception
                Telemetry.Error(telemetryEx, "Roles_B.RestoreGridViewState")
            End Try
        End Sub

        Private Function FindRowIndexByRoleId(roleId As Integer) As Integer
            Dim keyColumnName = ResolveMaintenanceKeyColumnName()
            If rolesGrid.Rows Is Nothing OrElse rolesGrid.Rows.Count = 0 OrElse String.IsNullOrWhiteSpace(keyColumnName) OrElse Not rolesGrid.Columns.Contains(keyColumnName) Then
                Return -1
            End If

            For i As Integer = 0 To rolesGrid.Rows.Count - 1
                Dim row = rolesGrid.Rows(i)
                Dim raw = row.Cells(keyColumnName).Value
                If raw Is Nothing OrElse IsDBNull(raw) Then
                    Continue For
                End If

                Dim rowId As Integer
                If Integer.TryParse(raw.ToString(), rowId) AndAlso rowId = roleId Then
                    Return i
                End If
            Next

            Return -1
        End Function

        Private Function SelectedRoleId() As Integer?
            If rolesGrid.SelectedRows.Count = 0 Then
                Return Nothing
            End If

            Dim row = rolesGrid.SelectedRows(0)
            Dim keyColumnName = ResolveMaintenanceKeyColumnName()
            If row Is Nothing OrElse String.IsNullOrWhiteSpace(keyColumnName) OrElse Not rolesGrid.Columns.Contains(keyColumnName) Then
                Return Nothing
            End If

            Dim raw = row.Cells(keyColumnName).Value
            If raw Is Nothing OrElse IsDBNull(raw) Then
                Return Nothing
            End If

            Dim roleId As Integer
            If Integer.TryParse(raw.ToString(), roleId) Then
                Return roleId
            End If

            Return Nothing
        End Function

        Private Function ResolveMaintenanceKeyColumnName() As String
            Dim table = TryCast(rolesGrid.DataSource, DataTable)
            If table IsNot Nothing AndAlso table.Columns IsNot Nothing Then
                If table.Columns.Contains("PK") Then
                    Return "PK"
                End If

                If table.Columns.Contains("RoleID") Then
                    Return "RoleID"
                End If
            End If

            Return MaintenanceKeyGuard.ResolveMaintenanceKeyColumnName(rolesGrid,
                                                                       New String() {"PK"})
        End Function

        Private Sub UpdateMaintenanceKeyAvailability()
            missingMaintenanceKeyInResult = String.IsNullOrWhiteSpace(ResolveMaintenanceKeyColumnName())
            MaintenanceKeyGuard.UpdateAvailabilityAndMaybeWarn(missingMaintenanceKeyInResult,
                                                               missingMaintenanceKeyWarningShown,
                                                               Me)
        End Sub

        Private Function EnsureMaintenanceKeyAvailable(actionName As String) As Boolean
            Return MaintenanceKeyGuard.EnsureAvailable(missingMaintenanceKeyInResult, actionName, Me)
        End Function

        Protected Overrides Function GetActiveBaseSql() As String
            If String.IsNullOrWhiteSpace(activeSqlText) Then
                Return DefaultSelectSql
            End If

            Return activeSqlText
        End Function

        Protected Overrides Function TryGetActiveRegistrationId(ByRef registrationId As Integer) As Boolean
            If registrationComboBox IsNot Nothing AndAlso registrationComboBox.Visible Then
                If TryResolveRegistrationIdFromCombo(registrationId) Then
                    Return True
                End If

                Return False
            End If

            If IsCompanyAdminOnlySession() Then
                registrationId = GetSessionRegistrationId()
                Return registrationId > 0
            End If

            If TryResolveRegistrationIdFromCombo(registrationId) Then
                Return True
            End If

            registrationId = GetSessionRegistrationId()
            Return registrationId > 0
        End Function

        Private Function TryResolveRegistrationIdFromCombo(ByRef registrationId As Integer) As Boolean
            registrationId = 0

            If registrationComboBox Is Nothing Then
                Return False
            End If

            If registrationComboBox.SelectedValue Is Nothing OrElse IsDBNull(registrationComboBox.SelectedValue) Then
                Return False
            End If

            If TypeOf registrationComboBox.SelectedValue Is Integer Then
                registrationId = CInt(registrationComboBox.SelectedValue)
                Return registrationId > 0
            End If

            If TypeOf registrationComboBox.SelectedValue Is Long Then
                registrationId = CInt(CLng(registrationComboBox.SelectedValue))
                Return registrationId > 0
            End If

            If TypeOf registrationComboBox.SelectedValue Is Decimal Then
                registrationId = Decimal.ToInt32(CDec(registrationComboBox.SelectedValue))
                Return registrationId > 0
            End If

            If TypeOf registrationComboBox.SelectedValue Is DataRowView Then
                Dim rowView = DirectCast(registrationComboBox.SelectedValue, DataRowView)
                Dim idValue = rowView("ID")
                If Integer.TryParse(Convert.ToString(idValue), registrationId) Then
                    Return registrationId > 0
                End If

                registrationId = GetSessionRegistrationId()
                Return registrationId > 0
            End If

            Try
                If Integer.TryParse(registrationComboBox.SelectedValue.ToString(), registrationId) Then
                    Return registrationId > 0
                End If
            Catch
                Return False
            End Try

            Return False
        End Function

        Private Function IsCompanyAdminOnlySession() As Boolean
            Dim activeSession = SessionState.Current
            If Not activeSession.HasValue Then
                Return False
            End If

            Return activeSession.Value.IsCompanyAdminRole AndAlso Not activeSession.Value.IsApplicationAdminRole
        End Function

        Protected Overrides Function ResolveCurrentRoleFieldTableName() As String
            If Not String.IsNullOrWhiteSpace(currentDbTableName) Then
                Return currentDbTableName.Trim()
            End If

            If accessTableName.Equals("ROLES", StringComparison.OrdinalIgnoreCase) Then
                Return "FW_Roles"
            End If

            Return accessTableName
        End Function

        Private Sub NewButton_Click(sender As Object, e As EventArgs)
            If Not EnsureCapability(AccessCapability.Create, "CREATE") Then
                Return
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                Return
            End If

            Try
                Using dlg As New Roles_C(registrationId)
                    If dlg.ShowDialog(Me) = DialogResult.OK Then
                        ' The role it just created, so the grid points at it. This refreshed with
                        ' nothing in mind before, and the new role arrived somewhere in the list
                        ' with nothing selected - the same complaint as a capped browse page,
                        ' arriving by a different route.
                        If dlg.SavedRecordId > 0 Then
                            RefreshGrid(dlg.SavedRecordId)
                        Else
                            RefreshGrid()
                        End If
                    End If
                End Using
            Catch ex As Exception
                MessageBox.Show("Error creating role: " & ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ModifyButton_Click(sender As Object, e As EventArgs)
            If Not EnsureCapability(AccessCapability.Update, "MODIFY") Then
                Return
            End If

            If Not EnsureMaintenanceKeyAvailable("Modify") Then
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "Roles Maintenance", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Try
                OpenRoleMaintenance(rolesGrid.SelectedRows(0))
            Catch ex As Exception
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub OpenRoleMaintenance(row As DataGridViewRow)
            If row Is Nothing Then
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "Roles Maintenance", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim roleName = Convert.ToString(row.Cells("RoleName").Value)
            If String.IsNullOrWhiteSpace(roleName) Then
                roleName = ""
            End If

            Dim registrationId As Integer
            If Not TryGetActiveRegistrationId(registrationId) Then
                MessageBox.Show("No active registration.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If

            Using dlg As New Roles_U(roleId.Value, registrationId, roleName)
                dlg.ShowDialog(Me)
            End Using

            RefreshGrid(roleId.Value)
        End Sub

        Private Sub DeleteButton_Click(sender As Object, e As EventArgs)
            If Not EnsureCapability(AccessCapability.Delete, "DELETE") Then
                Return
            End If

            If Not EnsureMaintenanceKeyAvailable("Delete") Then
                Return
            End If

            If Not DeletedViewGuard.TableSupportsDeletedView(ResolveCurrentRoleFieldTableName()) OrElse Not DeletedViewGuard.ResultHasDeletedFlagColumn(rolesGrid) Then
                MessageBox.Show("Delete is unavailable because the current table does not support DeletedFlag.", "Delete Unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            Dim selectedRow = rolesGrid.SelectedRows(0)
            Dim roleName = selectedRow.Cells("RoleName").Value.ToString()

            ' Find out what depends on the role before anything is written. Deleting takes its table
            ' permissions, its field permissions and every user assignment with it, so the user is
            ' told what that means before agreeing to it.
            Dim usage = DataAccess.GetRoleUsage(roleId.Value)

            If usage.IsRegistrationAdminRole Then
                MessageBox.Show(Me,
                                ("THIS ROLE CANNOT BE DELETED." & Environment.NewLine & Environment.NewLine &
                                 roleName & " IS THE COMPANY ADMIN ROLE FOR " &
                                 usage.RegistrationCount.ToString(Globalization.CultureInfo.InvariantCulture) &
                                 If(usage.RegistrationCount = 1, " REGISTRATION.", " REGISTRATIONS.") & Environment.NewLine & Environment.NewLine &
                                 "DELETING IT WOULD LEAVE THAT REGISTRATION WITHOUT AN ADMINISTRATOR. " &
                                 "ASSIGN A DIFFERENT COMPANY ADMIN ROLE FIRST.").ToUpperInvariant(),
                                "DELETE ROLE",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning)
                Return
            End If

            Dim confirmText As String
            If usage.IsHeldByUsers Then
                confirmText = (roleName & " IS HELD BY " &
                               usage.UserCount.ToString(Globalization.CultureInfo.InvariantCulture) &
                               If(usage.UserCount = 1, " USER.", " USERS.") & Environment.NewLine & Environment.NewLine &
                               "DELETING THE ROLE WILL REMOVE IT FROM " &
                               If(usage.UserCount = 1, "THAT USER", "THOSE USERS") &
                               " AND DELETE ITS TABLE AND FIELD PERMISSIONS." & Environment.NewLine & Environment.NewLine &
                               "DELETE THE ROLE?").ToUpperInvariant()
            Else
                confirmText = (roleName & " IS NOT HELD BY ANY USER." & Environment.NewLine & Environment.NewLine &
                               "DELETING THE ROLE WILL ALSO DELETE ITS TABLE AND FIELD PERMISSIONS." & Environment.NewLine & Environment.NewLine &
                               "DELETE THE ROLE?").ToUpperInvariant()
            End If

            If MessageBox.Show(Me, confirmText, "DELETE ROLE", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) = DialogResult.Yes Then
                Try
                    Dim updatedBy = SessionState.ActingUserID
                    DataAccess.DeleteRole(roleId.Value, updatedBy)
                    RefreshGrid()
                    AutoClosingMessage.Show(Me, "Role deleted.", "Delete", MessageBoxIcon.Information, 1000)
                Catch ex As Exception
                    MessageBox.Show($"Error deleting role: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End If
        End Sub

        Private Sub ShowDeletedButton_Click(sender As Object, e As EventArgs)
            If Not showDeletedButton.Visible OrElse Not showDeletedButton.Enabled Then
                Return
            End If

            showDeletedRecordsOnly = True
            UpdateShowDeletedButtonState()
            RefreshGrid()
        End Sub

        Private Sub RestoreDeletedButton_Click(sender As Object, e As EventArgs)
            If Not restoreDeletedButton.Visible OrElse Not showDeletedRecordsOnly Then
                Return
            End If

            If Not EnsureMaintenanceKeyAvailable("Restore") Then
                Return
            End If

            Dim roleId = SelectedRoleId()
            If Not roleId.HasValue Then
                MessageBox.Show("Select a role first.", "No Selection", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If

            If MessageBox.Show("Restore this role?", "Confirm Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then
                Return
            End If

            Try
                Dim updatedBy = SessionState.ActingUserID
                DataAccess.RestoreRole(roleId.Value, updatedBy)
                RefreshGrid()
                AutoClosingMessage.Show(Me, "Role restored.", "Restore", MessageBoxIcon.Information, 1000)
            Catch ex As Exception
                MessageBox.Show($"Error restoring role: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Sub

        Private Sub ShowNormalButton_Click(sender As Object, e As EventArgs)
            If Not showNormalButton.Visible Then
                Return
            End If

            showDeletedRecordsOnly = False
            UpdateShowDeletedButtonState()
            RefreshGrid()
        End Sub

        Private Sub CloseButton_Click(sender As Object, e As EventArgs)
            Me.Close()
        End Sub

        Private Sub Roles_B_FormClosing(sender As Object, e As FormClosingEventArgs)
            ' Central close pipeline for both Close button and window X.
        End Sub

        Private Sub UpdateShowDeletedButtonState()
            Dim supportsDeletedView = DeletedViewGuard.TableSupportsDeletedView(ResolveCurrentRoleFieldTableName()) AndAlso DeletedViewGuard.ResultHasDeletedFlagColumn(rolesGrid)
            If Not supportsDeletedView Then
                showDeletedRecordsOnly = False
            End If

            ' The deleted view goes with the Delete button. A role that cannot delete a record has
            ' no business in the bin those deletions land in, and the only action the view offers -
            ' Restore - undoes a deletion it was never allowed to make.
            '
            ' Note this is stricter than FW_Base_B, which gates the same view on Update rather than
            ' Delete, on the reading that restoring a record is an update to it. The two disagree
            ' for a role holding Update without Delete.
            ' Two conditions, deliberately, and the first is the backstop. The role must hold
            ' Delete - but the session must also be an Application Admin, so that a registration
            ' whose permissions were never set up cannot hand the deleted view to a Company Admin
            ' by omission. A missing permission row should fail closed, not open.
            Dim isAdmin = IsAppAdminSession()
            Dim canUseDeletedView = isAdmin AndAlso CanDo(AccessCapability.Delete)
            Dim showSplitDeletedActions = canUseDeletedView AndAlso supportsDeletedView AndAlso showDeletedRecordsOnly
            showDeletedButton.Visible = canUseDeletedView AndAlso Not showSplitDeletedActions
            showDeletedButton.Enabled = canUseDeletedView AndAlso supportsDeletedView
            showDeletedButton.Text = If(supportsDeletedView, ShowDeletedText, "Deleted N/A")

            restoreDeletedButton.Visible = showSplitDeletedActions
            showNormalButton.Visible = showSplitDeletedActions
            restoreDeletedButton.Enabled = showSplitDeletedActions AndAlso rolesGrid.SelectedRows.Count > 0 AndAlso Not missingMaintenanceKeyInResult
            showNormalButton.Enabled = showSplitDeletedActions

            newButton.Enabled = Not showDeletedRecordsOnly
            RolesGrid_SelectionChanged(Me, EventArgs.Empty)

            RolesForm_Resize(Me, EventArgs.Empty)
        End Sub

    End Class
End Namespace
