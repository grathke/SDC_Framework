Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports System.Windows.Forms

Namespace HelloWorld

    Public MustInherit Class FW_Base_U
        Inherits Form

        Protected loading As Boolean
        Protected hasUnsavedChanges As Boolean
        Private originalRowVersion As Byte()
        Private baselineControlSnapshotJson As String = String.Empty
        Private baselineRecordSnapshotJson As String = String.Empty
        Private bypassCancelCloseCheck As Boolean
        Private requiredValidationActivated As Boolean
        Private ReadOnly requiredBorderPanels As New Dictionary(Of Control, Panel)()
        Private ReadOnly focusOriginalBackColors As New Dictionary(Of Control, Color)()
        Private ReadOnly focusBorderPanels As New Dictionary(Of Control, Panel)()
        Private ReadOnly readOnlyMouseHandled As New HashSet(Of Control)()
        Protected ReadOnly okButton As Button
        Protected ReadOnly cancelActionButton As Button
        Protected ReadOnly enumButton As Button
        Private tabOrderToggleButton As Button
        Private tabOrderPanel As Panel
        Private tabOrderList As CheckedListBox
        Private tabOrderUpButton As Button
        Private tabOrderDownButton As Button
        Private tabOrderSaveButton As Button
        Private tabOrderHideButton As Button
        Private loadingTabOrderManager As Boolean
        Private tabOrderCheckClickIndex As Integer = -1
        Private tabOrderPanelDragging As Boolean
        Private tabOrderPanelDragStart As Point
        Private tabOrderPanelLocationStart As Point
        Private pageCaptionApplied As Boolean
        Private Shared ReadOnly TabOrderCollapsedText As String = "Tab Order " & ChrW(&H25BC)
        Private Shared ReadOnly TabOrderExpandedText As String = "Tab Order " & ChrW(&H25B2)

        Protected Sub New()
            Me.StartPosition = FormStartPosition.CenterParent
            Me.FormBorderStyle = FormBorderStyle.FixedDialog
            Me.MaximizeBox = False
            Me.MinimizeBox = False

            okButton = New Button() With {
                .Text = OkButtonText(),
                .Location = New Point(330, 500),
                .Size = New Size(120, 36)
            }
            cancelActionButton = New Button() With {
                .Text = "Cancel",
                .Location = New Point(465, 500),
                .Size = New Size(120, 36)
            }
            enumButton = New Button() With {
                .Text = "Enum",
                .Location = New Point(20, 500),
                .Size = New Size(70, 36)
            }

            AddHandler okButton.Click, AddressOf OkButton_Click
            AddHandler cancelActionButton.Click, AddressOf CancelButton_Click
            AddHandler enumButton.Click, AddressOf EnumButton_Click
            AddHandler Me.FormClosing, AddressOf FW_Base_U_FormClosing
            AddHandler Me.Shown, AddressOf FW_Base_U_Shown

            Me.Controls.Add(okButton)
            Me.Controls.Add(cancelActionButton)
            Me.Controls.Add(enumButton)
            Me.CancelButton = cancelActionButton
        End Sub

        Private Sub FW_Base_U_Shown(sender As Object, e As EventArgs)
            BeginInvoke(New Action(Sub()
                                       ApplySharedPageCaption()
                                       RemoveReadOnlyControlsFromTabOrder(Me)
                                       WireFocusIndicators(Me)
                                       enumButton.TabStop = False
                                       okButton.TabStop = False
                                       cancelActionButton.TabStop = False
                                       ApplySavedTabOrder()
                                       InitializeTabOrderManager()
                                       BeginInvoke(New Action(Sub()
                                                                  SetInitialFieldFocus()
                                                                  ResetPendingRecordBaseline()
                                                              End Sub))
                                   End Sub))
        End Sub

        Private Sub ApplySharedPageCaption()
            If pageCaptionApplied Then Return
            pageCaptionApplied = True

            Dim captionLabel As Label = Nothing
            Dim existingTitle = Controls.Find("Label_UserTitle", True)
            If existingTitle.Length > 0 Then
                captionLabel = TryCast(existingTitle(0), Label)
            End If

            If captionLabel IsNot Nothing Then
                captionLabel.Text = Me.Text
                captionLabel.Font = New Font("Segoe UI", 14.0F, FontStyle.Bold)
                captionLabel.Location = New Point(20, 15)
                captionLabel.BringToFront()
                Return
            End If

            If captionLabel Is Nothing Then
                captionLabel = New Label() With {
                    .Name = "Label_PageTitle",
                    .Text = Me.Text,
                    .AutoSize = True
                }
                Controls.Add(captionLabel)
            Else
                captionLabel.Text = Me.Text
            End If

            captionLabel.Font = New Font("Segoe UI", 14.0F, FontStyle.Bold)
            captionLabel.Location = New Point(20, 15)
            captionLabel.BringToFront()

            For Each control As Control In Controls
                If control Is captionLabel OrElse control.Location.Y < 0 Then Continue For
                control.Location = New Point(control.Location.X, control.Location.Y + 42)
            Next

            ClientSize = New Size(ClientSize.Width, ClientSize.Height + 42)
        End Sub

        Protected Sub SetManualTabOrder(ParamArray orderedControls() As Control)
            If orderedControls Is Nothing Then Return

            Dim tabIndex = 0
            For Each control In orderedControls
                If control Is Nothing Then Continue For

                Dim isReadOnlyText = TypeOf control Is TextBoxBase AndAlso DirectCast(control, TextBoxBase).ReadOnly
                If isReadOnlyText Then
                    control.TabStop = False
                    Continue For
                End If

                control.TabStop = True
                control.TabIndex = tabIndex
                tabIndex += 1
            Next
        End Sub

        Private Sub InitializeTabOrderManager()
            If Not IsApplicationAdminSession() OrElse tabOrderToggleButton IsNot Nothing Then Return

            tabOrderToggleButton = New Button() With {
                .Name = "Button_TabOrderManager",
                .Text = TabOrderCollapsedText,
                .Size = New Size(100, 28),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .TabStop = False
            }
            tabOrderToggleButton.Location = New Point(ClientSize.Width - tabOrderToggleButton.Width - 10, 10)
            AddHandler tabOrderToggleButton.Click, AddressOf TabOrderToggleButton_Click
            Controls.Add(tabOrderToggleButton)
            Dim tabOrderToggleToolTip As New ToolTip()
            tabOrderToggleToolTip.SetToolTip(tabOrderToggleButton, "Configure tab order")

            tabOrderPanel = New Panel() With {
                .Name = "Panel_TabOrderManager",
                .Size = New Size(300, Math.Min(420, Math.Max(260, ClientSize.Height - 30))),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .BorderStyle = BorderStyle.FixedSingle,
                .BackColor = SystemColors.Control,
                .Visible = False,
                .TabStop = False
            }
            tabOrderPanel.Location = New Point(ClientSize.Width - tabOrderPanel.Width - 10, 42)

            tabOrderHideButton = New Button() With {.Text = "Close", .Size = New Size(60, 26), .Location = New Point(228, 8), .TabStop = False}
            tabOrderUpButton = New Button() With {.Text = "Up", .Size = New Size(60, 26), .Location = New Point(8, 8), .TabStop = False}
            tabOrderDownButton = New Button() With {.Text = "Down", .Size = New Size(60, 26), .Location = New Point(74, 8), .TabStop = False}
            tabOrderSaveButton = New Button() With {.Text = "Save", .Size = New Size(60, 26), .Location = New Point(140, 8), .TabStop = False}
            tabOrderList = New CheckedListBox() With {
                .Name = "CheckedListBox_TabOrder",
                .Location = New Point(8, 42),
                .Size = New Size(282, tabOrderPanel.Height - 52),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right,
                .CheckOnClick = True,
                .TabStop = False
            }

            AddHandler tabOrderHideButton.Click, AddressOf TabOrderHideButton_Click
            AddHandler tabOrderUpButton.Click, AddressOf TabOrderUpButton_Click
            AddHandler tabOrderDownButton.Click, AddressOf TabOrderDownButton_Click
            AddHandler tabOrderSaveButton.Click, AddressOf TabOrderSaveButton_Click
            AddHandler tabOrderList.SelectedIndexChanged, AddressOf TabOrderList_SelectedIndexChanged
            AddHandler tabOrderList.KeyDown, AddressOf TabOrderList_KeyDown
            AddHandler tabOrderList.MouseDown, AddressOf TabOrderList_MouseDown
            AddHandler tabOrderList.MouseUp, AddressOf TabOrderList_MouseUp
            AddHandler tabOrderList.ItemCheck, AddressOf TabOrderList_ItemCheck
            AddHandler tabOrderPanel.MouseDown, AddressOf TabOrderPanel_MouseDown
            AddHandler tabOrderPanel.MouseMove, AddressOf TabOrderPanel_MouseMove
            AddHandler tabOrderPanel.MouseUp, AddressOf TabOrderPanel_MouseUp

            tabOrderPanel.Controls.AddRange({tabOrderHideButton, tabOrderUpButton, tabOrderDownButton, tabOrderSaveButton, tabOrderList})
            Controls.Add(tabOrderPanel)
            tabOrderPanel.BringToFront()
            tabOrderToggleButton.BringToFront()
            LoadTabOrderManager()
        End Sub

        Private Shared Function IsApplicationAdminSession() As Boolean
            Return SessionState.IsActive AndAlso SessionState.Current.HasValue AndAlso
                   SessionState.Current.Value.IsApplicationAdminRole
        End Function

        Private Sub LoadTabOrderManager()
            If tabOrderList Is Nothing Then Return

            Dim controls = ApplySavedTabOrder()

            loadingTabOrderManager = True
            Try
                For Each control In controls
                    Dim displayName = GetControlDisplayName(control)
                    Dim itemIndex = tabOrderList.Items.Add(New TabOrderManagerItem With {.Control = control, .DisplayName = displayName})
                    tabOrderList.SetItemChecked(itemIndex, control.TabStop)
                Next
            Finally
                loadingTabOrderManager = False
            End Try
            UpdateTabOrderManagerButtons()
        End Sub

        Private Sub TabOrderPanel_MouseDown(sender As Object, e As MouseEventArgs)
            If e.Button <> MouseButtons.Left OrElse e.Y > 40 Then Return
            tabOrderPanelDragging = True
            tabOrderPanelDragStart = Cursor.Position
            tabOrderPanelLocationStart = tabOrderPanel.Location
        End Sub

        Private Sub TabOrderPanel_MouseMove(sender As Object, e As MouseEventArgs)
            If Not tabOrderPanelDragging Then Return

            Dim delta = Point.Subtract(Cursor.Position, New Size(tabOrderPanelDragStart))
            Dim newX = Math.Max(0, Math.Min(ClientSize.Width - tabOrderPanel.Width, tabOrderPanelLocationStart.X + delta.X))
            Dim newY = Math.Max(0, Math.Min(ClientSize.Height - tabOrderPanel.Height, tabOrderPanelLocationStart.Y + delta.Y))
            tabOrderPanel.Location = New Point(newX, newY)
        End Sub

        Private Sub TabOrderPanel_MouseUp(sender As Object, e As MouseEventArgs)
            If e.Button = MouseButtons.Left Then tabOrderPanelDragging = False
        End Sub

        Private Function ApplySavedTabOrder() As List(Of Control)
            Dim controls = GetTabOrderControls()
            Dim savedSettings = DataAccess.GetTabOrderSettings(GetPageName())
            Dim savedByName = savedSettings.ToDictionary(Function(setting) setting.ControlName, StringComparer.OrdinalIgnoreCase)
            Dim savedControls = controls.Where(Function(control) savedByName.ContainsKey(control.Name)).
                OrderBy(Function(control) savedByName(control.Name).TabOrder).ToList()
            Dim newControls = controls.Where(Function(control) Not savedByName.ContainsKey(control.Name)).
                OrderBy(Function(control) control.TabIndex).ToList()
            Dim orderedControls = savedControls.Concat(newControls).ToList()

            Dim appliedTabIndex = 0
            For Each control In orderedControls
                Dim setting As TabOrderSetting = Nothing
                If savedByName.TryGetValue(control.Name, setting) Then
                    control.TabStop = setting.TabStop
                End If
                control.TabIndex = appliedTabIndex
                appliedTabIndex += 1
            Next

            Return orderedControls
        End Function

        Private Function GetTabOrderControls() As List(Of Control)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)
            Return allControls.Where(Function(control) IsEligibleTabOrderControl(control)).
                OrderBy(Function(control) control.TabIndex).ToList()
        End Function

        Private Function IsEligibleTabOrderControl(control As Control) As Boolean
            If IsTabOrderManagerControl(control) OrElse String.IsNullOrWhiteSpace(control.Name) OrElse
               Not control.Visible OrElse Not control.Enabled Then
                Return False
            End If

            If control Is enumButton OrElse control Is okButton OrElse control Is cancelActionButton Then
                Return False
            End If

            If TypeOf control Is TextBoxBase Then
                Return Not DirectCast(control, TextBoxBase).ReadOnly
            End If

            Return TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox OrElse
                   TypeOf control Is Button OrElse TypeOf control Is DateTimePicker OrElse
                   TypeOf control Is NumericUpDown
        End Function

        Private Function IsTabOrderManagerControl(control As Control) As Boolean
            Dim current = control
            While current IsNot Nothing
                If current Is tabOrderToggleButton OrElse current Is tabOrderPanel Then Return True
                current = current.Parent
            End While
            Return False
        End Function

        Private Shared Function GetControlDisplayName(control As Control) As String
            Dim controlName = control.Name
            Dim suffix As String = String.Empty
            If controlName.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(8)
            ElseIf controlName.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(9)
            ElseIf controlName.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(9)
            ElseIf controlName.StartsWith("DateTimePicker_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(15)
            ElseIf controlName.StartsWith("NumericUpDown_", StringComparison.OrdinalIgnoreCase) Then
                suffix = controlName.Substring(14)
            End If

            If suffix <> String.Empty Then
                Dim labels = control.FindForm().Controls.Find("Label_" & suffix, True)
                If labels.Length > 0 AndAlso TypeOf labels(0) Is Label Then
                    Return DirectCast(labels(0), Label).Text.TrimEnd(" "c, "*"c).Trim()
                End If
            End If

            Return controlName
        End Function

        Private Sub TabOrderToggleButton_Click(sender As Object, e As EventArgs)
            tabOrderPanel.Visible = Not tabOrderPanel.Visible
            UpdateTabOrderToggleButton()
            tabOrderPanel.BringToFront()
        End Sub

        Private Sub TabOrderHideButton_Click(sender As Object, e As EventArgs)
            tabOrderPanel.Visible = False
            UpdateTabOrderToggleButton()
        End Sub

        Private Sub UpdateTabOrderToggleButton()
            If tabOrderToggleButton Is Nothing OrElse tabOrderPanel Is Nothing Then Return
            tabOrderToggleButton.Text = If(tabOrderPanel.Visible, TabOrderExpandedText, TabOrderCollapsedText)
        End Sub

        Private Sub TabOrderUpButton_Click(sender As Object, e As EventArgs)
            MoveTabOrderManagerItem(-1)
        End Sub

        Private Sub TabOrderDownButton_Click(sender As Object, e As EventArgs)
            MoveTabOrderManagerItem(1)
        End Sub

        Private Sub MoveTabOrderManagerItem(delta As Integer)
            Dim selectedIndex = tabOrderList.SelectedIndex
            Dim targetIndex = selectedIndex + delta
            If selectedIndex < 0 OrElse targetIndex < 0 OrElse targetIndex >= tabOrderList.Items.Count Then Return

            Dim item = tabOrderList.Items(selectedIndex)
            Dim wasChecked = tabOrderList.GetItemChecked(selectedIndex)
            tabOrderList.Items.RemoveAt(selectedIndex)
            tabOrderList.Items.Insert(targetIndex, item)
            tabOrderList.SetItemChecked(targetIndex, wasChecked)
            tabOrderList.SelectedIndex = targetIndex
            UpdateTabOrderManagerButtons()
        End Sub

        Private Sub TabOrderSaveButton_Click(sender As Object, e As EventArgs)
            Dim settings As New List(Of TabOrderSetting)()
            For index As Integer = 0 To tabOrderList.Items.Count - 1
                Dim item = TryCast(tabOrderList.Items(index), TabOrderManagerItem)
                If item Is Nothing OrElse item.Control Is Nothing Then Continue For
                item.Control.TabIndex = index
                item.Control.TabStop = tabOrderList.GetItemChecked(index)
                settings.Add(New TabOrderSetting With {.ControlName = item.Control.Name, .TabOrder = index, .TabStop = item.Control.TabStop})
            Next

            Dim updatedBy = If(SessionState.IsActive AndAlso SessionState.Current.HasValue, SessionState.Current.Value.UserID, 0)
            DataAccess.SaveTabOrderSettings(GetPageName(), settings, updatedBy)
            tabOrderHideButton.PerformClick()
            MessageBox.Show(Me, "Tab order saved for this page.", "Tab Order", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        Private Sub TabOrderList_SelectedIndexChanged(sender As Object, e As EventArgs)
            UpdateTabOrderManagerButtons()
        End Sub

        Private Sub TabOrderList_KeyDown(sender As Object, e As KeyEventArgs)
            If e.KeyCode = Keys.Up Then MoveTabOrderManagerItem(-1)
            If e.KeyCode = Keys.Down Then MoveTabOrderManagerItem(1)
        End Sub

        Private Sub TabOrderList_MouseDown(sender As Object, e As MouseEventArgs)
            tabOrderCheckClickIndex = -1
            If e.Button <> MouseButtons.Left Then Return

            Dim itemIndex = tabOrderList.IndexFromPoint(e.Location)
            If itemIndex < 0 Then Return

            Dim itemBounds = tabOrderList.GetItemRectangle(itemIndex)
            If e.X <= itemBounds.Left + SystemInformation.MenuCheckSize.Width + 4 Then
                tabOrderCheckClickIndex = itemIndex
            End If
        End Sub

        Private Sub TabOrderList_MouseUp(sender As Object, e As MouseEventArgs)
            tabOrderCheckClickIndex = -1
        End Sub

        Private Sub TabOrderList_ItemCheck(sender As Object, e As ItemCheckEventArgs)
            If loadingTabOrderManager Then Return

            If tabOrderCheckClickIndex <> e.Index Then
                e.NewValue = e.CurrentValue
                Return
            End If

            BeginInvoke(New MethodInvoker(Sub()
                                              Dim item = TryCast(tabOrderList.Items(e.Index), TabOrderManagerItem)
                                              If item IsNot Nothing Then item.Control.TabStop = e.NewValue = CheckState.Checked
                                          End Sub))
        End Sub

        Private Sub UpdateTabOrderManagerButtons()
            If tabOrderList Is Nothing Then Return
            tabOrderUpButton.Enabled = tabOrderList.SelectedIndex > 0
            tabOrderDownButton.Enabled = tabOrderList.SelectedIndex >= 0 AndAlso tabOrderList.SelectedIndex < tabOrderList.Items.Count - 1
        End Sub

        Private Sub SetInitialFieldFocus()
            Dim firstField = FindFirstFocusableField(Me)
            If firstField Is Nothing Then Return

            Me.ActiveControl = firstField
            firstField.Select()
            firstField.Focus()
            ClearTextSelection(firstField)
            ApplyFocusIndicator(firstField)
        End Sub

        Private Sub RemoveReadOnlyControlsFromTabOrder(container As Control)
            For Each control As Control In container.Controls
                If TypeOf control Is TextBoxBase AndAlso DirectCast(control, TextBoxBase).ReadOnly Then
                    control.TabStop = False
                    If Not readOnlyMouseHandled.Contains(control) Then
                        readOnlyMouseHandled.Add(control)
                        AddHandler control.MouseDown, AddressOf ReadOnlyControl_MouseDown
                    End If
                End If

                If control.HasChildren Then
                    RemoveReadOnlyControlsFromTabOrder(control)
                End If
            Next
        End Sub

        Private Sub ReadOnlyControl_MouseDown(sender As Object, e As MouseEventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return

            SelectNextControl(control, True, True, True, True)
        End Sub

        Private Sub WireFocusIndicators(container As Control)
            Dim childControls As New List(Of Control)()
            For Each control As Control In container.Controls
                childControls.Add(control)
            Next

            For Each control As Control In childControls
                If IsFocusIndicatorControl(control) Then
                    If Not focusOriginalBackColors.ContainsKey(control) Then
                        focusOriginalBackColors(control) = control.BackColor
                        Dim borderPanel = FindExistingRequiredBorderPanel(control)
                        If borderPanel Is Nothing Then
                            borderPanel = New Panel() With {
                                .Name = "FocusBorder_" & control.Name,
                                .BackColor = Color.FromArgb(55, 180, 105),
                                .Visible = False,
                                .TabStop = False
                            }
                            control.Parent.Controls.Add(borderPanel)
                        End If
                        borderPanel.Location = New Point(Math.Max(0, control.Left - 2), Math.Max(0, control.Top - 2))
                        borderPanel.Size = New Size(control.Width + 4, control.Height + 4)
                        borderPanel.SendToBack()
                        control.BringToFront()
                        focusBorderPanels(control) = borderPanel
                        AddHandler control.Enter, AddressOf FocusIndicator_Enter
                        AddHandler control.Leave, AddressOf FocusIndicator_Leave
                        AddHandler control.MouseEnter, AddressOf EditableControl_MouseEnter
                        AddHandler control.MouseLeave, AddressOf EditableControl_MouseLeave
                        If TypeOf control Is TextBoxBase Then
                            AddHandler control.TextChanged, AddressOf FocusIndicator_ValueChanged
                        ElseIf TypeOf control Is ComboBox Then
                            AddHandler control.TextChanged, AddressOf FocusIndicator_ValueChanged
                            AddHandler DirectCast(control, ComboBox).SelectedIndexChanged, AddressOf FocusIndicator_ValueChanged
                        End If
                    End If
                End If

                If control.HasChildren Then
                    WireFocusIndicators(control)
                End If
            Next
        End Sub

        Private Shared Function FindExistingRequiredBorderPanel(control As Control) As Panel
            If control Is Nothing OrElse control.Parent Is Nothing Then Return Nothing

            Dim expectedLocalTag = String.Empty
            If control.Name.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(8)
            ElseIf control.Name.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(9)
            ElseIf control.Name.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase) Then
                expectedLocalTag = "LocalRequiredBorder_" & control.Name.Substring(9)
            End If
            Dim expectedPermissionTag = "RequiredBorder_" & control.Name

            For Each sibling As Control In control.Parent.Controls
                Dim panel = TryCast(sibling, Panel)
                If panel Is Nothing OrElse panel.Tag Is Nothing Then Continue For

                Dim tagText = panel.Tag.ToString()
                If String.Equals(tagText, expectedLocalTag, StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(tagText, expectedPermissionTag, StringComparison.OrdinalIgnoreCase) Then
                    Return panel
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function IsFocusIndicatorControl(control As Control) As Boolean
            Return TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse
                   TypeOf control Is CheckBox OrElse TypeOf control Is DateTimePicker OrElse
                   TypeOf control Is NumericUpDown
        End Function

        Private Shared Function IsEmptyRequiredControl(control As Control) As Boolean
            If control Is Nothing OrElse Not String.Equals(If(control.Tag, String.Empty).ToString(), "Required", StringComparison.OrdinalIgnoreCase) Then
                Return False
            End If

            If TypeOf control Is ComboBox Then
                Dim combo = DirectCast(control, ComboBox)
                Dim selectedValue = 0
                If combo.SelectedValue IsNot Nothing AndAlso Not IsDBNull(combo.SelectedValue) Then
                    Integer.TryParse(combo.SelectedValue.ToString(), selectedValue)
                End If
                Return combo.SelectedIndex < 0 OrElse selectedValue <= 0 OrElse String.IsNullOrWhiteSpace(combo.Text)
            End If

            Return String.IsNullOrWhiteSpace(control.Text)
        End Function

        Private Sub FocusIndicator_Enter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return
            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then control.BackColor = originalColor
            ApplyFocusIndicator(control)
        End Sub

        Private Sub ApplyFocusIndicator(control As Control)
            If control Is Nothing Then Return
            Dim borderPanel As Panel = Nothing
            If focusBorderPanels.TryGetValue(control, borderPanel) Then
                borderPanel.BackColor = If(IsEmptyRequiredControl(control), Color.Red, Color.FromArgb(55, 180, 105))
                borderPanel.Visible = True
                borderPanel.BringToFront()
                control.BringToFront()
            End If
        End Sub

        Private Sub FocusIndicator_Leave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing Then Return
            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then control.BackColor = originalColor
            Dim borderPanel As Panel = Nothing
            If focusBorderPanels.TryGetValue(control, borderPanel) Then borderPanel.Visible = False
        End Sub

        Private Sub FocusIndicator_ValueChanged(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control IsNot Nothing AndAlso control.Focused Then
                ApplyFocusIndicator(control)
            End If
        End Sub

        Private Sub EditableControl_MouseEnter(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse control.Focused Then Return
            control.BackColor = Color.FromArgb(221, 235, 247)
        End Sub

        Private Sub EditableControl_MouseLeave(sender As Object, e As EventArgs)
            Dim control = TryCast(sender, Control)
            If control Is Nothing OrElse control.Focused Then Return

            Dim originalColor As Color
            If focusOriginalBackColors.TryGetValue(control, originalColor) Then
                control.BackColor = originalColor
            End If
        End Sub

        Private Shared Sub ClearTextSelection(control As Control)
            Dim textControl = TryCast(control, TextBoxBase)
            If textControl IsNot Nothing Then
                textControl.SelectionStart = textControl.TextLength
                textControl.SelectionLength = 0
            End If
        End Sub

        Private Shared Function FindFirstFocusableField(container As Control) As Control
            Dim firstControl As Control = Nothing
            Dim lowestTabIndex = Integer.MaxValue
            For Each control As Control In container.Controls
                Dim isFocusableType = TypeOf control Is TextBoxBase OrElse TypeOf control Is ComboBox OrElse TypeOf control Is CheckBox OrElse TypeOf control Is DateTimePicker OrElse TypeOf control Is NumericUpDown
                Dim isEditableText = Not TypeOf control Is TextBoxBase OrElse Not DirectCast(control, TextBoxBase).ReadOnly
                If control.Visible AndAlso control.Enabled AndAlso control.TabStop AndAlso isFocusableType AndAlso isEditableText AndAlso control.TabIndex < lowestTabIndex Then
                    firstControl = control
                    lowestTabIndex = control.TabIndex
                End If
            Next

            Return firstControl
        End Function

        ' ── Overridable behaviour ─────────────────────────────────────────

        Protected Overridable Function OkButtonText() As String
            Return "OK"
        End Function

        Protected Overridable Function ShouldWarnOnCancel() As Boolean
            Return False
        End Function

        Protected Overridable Function SaveRecord() As Boolean
            ' Default implementation: no persistence required. Override in child classes to persist to database.
            Return True
        End Function

        Protected Overridable Function IsViewOnly() As Boolean
            Return False
        End Function

        ''' <summary>
        ''' True when the page is creating a record rather than editing an existing one.
        ''' Field-level permissions use this to choose between Can_Create and Can_Update.
        ''' Override on any page that has a create mode; the default is safe for pages that
        ''' only ever edit.
        ''' </summary>
        Protected Overridable Function IsCreatingNewRecord() As Boolean
            Return False
        End Function

        Protected Overridable Function GetAdditionalValidationMessageLines() As IEnumerable(Of String)
            Return Array.Empty(Of String)()
        End Function

        Protected Overridable Function ResolveAuditRecordKey() As String
            Return String.Empty
        End Function

        Protected Overridable Function ResolveAuditOperationType() As String
            Return String.Empty
        End Function

        Protected Function ConfirmConcurrencyOverwrite() As Boolean
            Return MessageBox.Show(Me,
                                   "This record was changed by another user after you opened it." & Environment.NewLine & Environment.NewLine &
                                   "Do you want to overwrite that newer version with your current changes?",
                                   "Record Changed",
                                   MessageBoxButtons.YesNo,
                                   MessageBoxIcon.Warning) = DialogResult.Yes
        End Function

        Protected Sub ShowConcurrencyUnavailable()
            MessageBox.Show(Me,
                            "This record cannot be saved safely because its table does not have a RowVersion column.",
                            "Concurrency Protection Unavailable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Protected Sub CaptureOriginalRowVersion(rowVersion As Byte())
            If rowVersion Is Nothing Then
                originalRowVersion = Nothing
            Else
                originalRowVersion = CType(rowVersion.Clone(), Byte())
            End If
        End Sub

        Protected Function CopyOriginalRowVersion() As Byte()
            If originalRowVersion Is Nothing Then
                Return Nothing
            End If

            Return CType(originalRowVersion.Clone(), Byte())
        End Function

        ' ── Must override in child page ───────────────────────────────────

        Protected MustOverride Sub BindToFormInternal()
        Protected MustOverride Sub ApplyMode()
        Protected MustOverride Function TryBuildRecord() As Boolean

        ' ── Shared form wiring ────────────────────────────────────────────

        Protected Sub BindToForm()
            loading = True
            WarnIfMissingRowVersion()
            BindToFormInternal()
            loading = False
            hasUnsavedChanges = False
            DataAccess.ApplyControlUpdates(Me, Me.GetType().Name, IsCreatingNewRecord())
            NormalizeTextInputsForSave()
            baselineControlSnapshotJson = CaptureControlSnapshotJson()
            ResetPendingRecordBaseline()
        End Sub

        Protected Overridable Sub WarnIfMissingRowVersion()
            Dim tableName = ResolveTableNameForConcurrency()
            If String.IsNullOrWhiteSpace(tableName) OrElse DataAccess.TableHasRowVersion(tableName) Then
                Return
            End If

            MessageBox.Show(Me,
                            "Table dbo." & tableName & " does not have a RowVersion column. Concurrent edit protection is unavailable for this page.",
                            "Concurrency Protection Unavailable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
        End Sub

        Protected Overridable Function ResolveTableNameForConcurrency() As String
            Dim tableName = If(GetTableNameOverride(), String.Empty).Trim()
            If tableName <> String.Empty Then
                Return tableName
            End If

            Dim pageName = GetPageName()
            If pageName.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                Return "FW_" & pageName.Substring(0, pageName.Length - 2)
            End If

            Return String.Empty
        End Function

        Protected Sub MarkDirty(sender As Object, e As EventArgs)
            If Not loading Then hasUnsavedChanges = True
        End Sub

        Protected Sub CloseAfterSuccessfulCommand()
            hasUnsavedChanges = False
            Me.DialogResult = DialogResult.OK
            bypassCancelCloseCheck = True
            Me.Close()
        End Sub

        Private Sub OkButton_Click(sender As Object, e As EventArgs)
            If IsViewOnly() Then
                Me.DialogResult = DialogResult.OK
                bypassCancelCloseCheck = True
                Me.Close()
                Return
            End If

            If Not ExecuteSaveWorkflow() Then
                Return
            End If

            hasUnsavedChanges = False
            Me.DialogResult = DialogResult.OK
            bypassCancelCloseCheck = True
            Me.Close()
        End Sub

        Protected Function ExecuteSaveWorkflow() As Boolean
            If Not ValidateAndBuildForSave() Then
                Return False
            End If

            Return SaveRecordWithAudit()
        End Function

        Protected Function ValidateAndBuildForSave() As Boolean
            NormalizeTextInputsForSave()
            requiredValidationActivated = True
            RefreshLocalRequiredBorders()
            Dim errorMsg As String = String.Empty
            Dim validationLines As New List(Of String)()

            If Not DataAccess.ValidateRequiredControls(Me, errorMsg) Then
                If Not String.IsNullOrWhiteSpace(errorMsg) Then
                    validationLines.AddRange(errorMsg.Split({Environment.NewLine}, StringSplitOptions.None))
                End If
            End If

            Dim uniqueErrorMsg As String = String.Empty
            If Not DataAccess.ValidateUniqueFields(Me,
                                                   GetPageName(),
                                                   ResolveTableNameForConstraints(),
                                                   IsCreatingNewRecord(),
                                                   ResolveAuditRecordKey(),
                                                   uniqueErrorMsg) Then
                If Not String.IsNullOrWhiteSpace(uniqueErrorMsg) Then
                    validationLines.AddRange(uniqueErrorMsg.Split({Environment.NewLine}, StringSplitOptions.None))
                End If
            End If

            Dim additionalLines = GetAdditionalValidationMessageLines()
            If additionalLines IsNot Nothing Then
                For Each line In additionalLines
                    If line Is Nothing Then
                        Continue For
                    End If
                    If String.IsNullOrWhiteSpace(line) Then
                        validationLines.Add(String.Empty)
                    Else
                        validationLines.Add(line.Trim())
                    End If
                Next
            End If

            If validationLines.Count > 0 Then
                Dim validationMessage = String.Join(Environment.NewLine, validationLines)
                Dim sqlMarkerIndex = validationMessage.IndexOf("SQL VALIDATION", StringComparison.OrdinalIgnoreCase)
                If sqlMarkerIndex > 0 Then
                    validationMessage = validationMessage.Substring(0, sqlMarkerIndex).TrimEnd() &
                                        Environment.NewLine & Environment.NewLine &
                                        validationMessage.Substring(sqlMarkerIndex)
                End If
                MessageBox.Show(validationMessage, "The Following Occurred", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return False
            End If

            ' Masked fields display a placeholder, never their value. Restore the real values so
            ' TryBuildRecord reads what was loaded, then mask again. Without this the mask text
            ' itself would be written to the database.
            FieldPermissions.UnmaskForSave(Me)
            Try
                If Not TryBuildRecord() Then
                    Return False
                End If
            Finally
                FieldPermissions.RemaskAfterSave(Me)
            End Try

            Return True
        End Function

        Protected Function SaveRecordWithAudit() As Boolean
            Dim pageName = GetPageName()
            Dim tableName = ResolveTableNameForConstraints()
            Dim beforeRecordKey = ResolveRecordKeyForAudit()
            Dim operationType = If(ResolveAuditOperationType(), String.Empty).Trim()
            If operationType = String.Empty Then
                operationType = ResolveOperationType(beforeRecordKey)
            End If
            Dim beforeSnapshot = If(baselineControlSnapshotJson, String.Empty)
            If beforeSnapshot = String.Empty Then
                beforeSnapshot = CaptureControlSnapshotJson()
            End If

            DataAccess.LogUpdateAudit(pageName,
                                      tableName,
                                      operationType,
                                      "BeforeSave",
                                      beforeRecordKey,
                                      beforeSnapshot)

            Dim saveSucceeded = False
            Dim saveError As String = String.Empty
            Try
                saveSucceeded = SaveRecord()
            Catch ex As Exception
                saveError = ex.Message
            End Try

            Dim afterRecordKey = ResolveRecordKeyForAudit()
            Dim afterSnapshot = CaptureControlSnapshotJson()

            DataAccess.LogUpdateAudit(pageName,
                                      tableName,
                                      operationType,
                                      "AfterSave",
                                      afterRecordKey,
                                      afterSnapshot,
                                      saveSucceeded)

            If saveSucceeded Then
                hasUnsavedChanges = False
                baselineControlSnapshotJson = CaptureControlSnapshotJson()
                ResetPendingRecordBaseline()
            Else
                Dim message = If(String.IsNullOrWhiteSpace(saveError),
                                 "THE RECORD COULD NOT BE SAVED.",
                                 "SAVE FAILED: " & saveError)
                MessageBox.Show(Me, message.ToUpperInvariant(), "SAVE FAILED", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End If

            Return saveSucceeded
        End Function

        Private Sub CancelButton_Click(sender As Object, e As EventArgs)
            If Not TryConfirmCancelClose() Then
                Me.DialogResult = DialogResult.None
                Return
            End If
            Me.DialogResult = DialogResult.Cancel
            bypassCancelCloseCheck = True
            Me.Close()
        End Sub

        Private Function TryConfirmCancelClose() As Boolean
            If ShouldWarnOnCancel() AndAlso HasNetUnsavedChanges() Then
                Dim result = MessageBox.Show("You have unsaved changes. Discard them?", "Unsaved Changes",
                                             MessageBoxButtons.YesNo, MessageBoxIcon.Warning)
                If result = DialogResult.No Then
                    Return False
                End If
            End If

            Return True
        End Function

        Private Function HasNetUnsavedChanges() As Boolean
            Dim currentSnapshot = CapturePendingRecordSnapshotJson()
            Dim baselineSnapshot = If(baselineRecordSnapshotJson, String.Empty)
            Dim hasChanges = Not String.Equals(currentSnapshot, baselineSnapshot, StringComparison.Ordinal)

            If Not hasChanges Then
                hasUnsavedChanges = False
            Else
                hasUnsavedChanges = True
            End If

            Return hasChanges
        End Function

        Private Sub ResetPendingRecordBaseline()
            baselineRecordSnapshotJson = CapturePendingRecordSnapshotJson()
            hasUnsavedChanges = False
        End Sub

        Private Sub FW_Base_U_FormClosing(sender As Object, e As FormClosingEventArgs)
            If bypassCancelCloseCheck Then
                Return
            End If

            If e.CloseReason <> CloseReason.UserClosing Then
                Return
            End If

            If Not TryConfirmCancelClose() Then
                e.Cancel = True
                Me.DialogResult = DialogResult.None
                Return
            End If

            Me.DialogResult = DialogResult.Cancel
        End Sub

        Private Sub EnumButton_Click(sender As Object, e As EventArgs)
            Dim pageName = GetPageName()
            DataAccess.EnumeratePageControls_U(Me, pageName, GetTableNameOverride())
            MessageBox.Show("ALL CONTROLS HAVE BEEN ENUMERATED" & vbCrLf & vbCrLf & "Page: " & pageName,
                            "Enumeration Complete", MessageBoxButtons.OK, MessageBoxIcon.Information)
        End Sub

        ''' <summary>Override to specify a custom page name for enumeration/business rules. Defaults to class name.</summary>
        Protected Overridable Function GetPageName() As String
            Return Me.GetType().Name
        End Function

        ''' <summary>Override to specify a custom DB table name for enumeration FileLink. Nothing = auto-derived.</summary>
        Protected Overridable Function GetTableNameOverride() As String
            Return Nothing
        End Function

        Private Sub NormalizeTextInputsForSave()
            Dim tableName = ResolveTableNameForConstraints()
            Dim pageName = GetPageName()
            Dim columnLengths = DataAccess.GetTextColumnMaxLengths(tableName)
            Dim controlMap = DataAccess.GetPageControlFieldMap(pageName, tableName)

            If columnLengths Is Nothing OrElse columnLengths.Count = 0 Then
                TrimTextControlsOnly(Me)
                Return
            End If

            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)

            For Each ctrl In allControls
                If TypeOf ctrl Is TextBox Then
                    Dim tb = DirectCast(ctrl, TextBox)
                    NormalizeTextControl(tb, controlMap, columnLengths)
                ElseIf TypeOf ctrl Is MaskedTextBox Then
                    Dim mb = DirectCast(ctrl, MaskedTextBox)
                    NormalizeTextControl(mb, controlMap, columnLengths)
                ElseIf TypeOf ctrl Is RichTextBox Then
                    Dim rb = DirectCast(ctrl, RichTextBox)
                    NormalizeTextControl(rb, controlMap, columnLengths)
                End If
            Next
        End Sub

        Private Sub TrimTextControlsOnly(container As Control)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(container, allControls)
            For Each ctrl In allControls
                If TypeOf ctrl Is TextBoxBase Then
                    Dim tb = DirectCast(ctrl, TextBoxBase)
                    tb.Text = If(tb.Text, String.Empty).Trim()
                End If
            Next
        End Sub

        Private Sub NormalizeTextControl(ctrl As TextBoxBase,
                                         controlMap As Dictionary(Of String, String),
                                         columnLengths As Dictionary(Of String, Integer))
            Dim textValue = If(ctrl.Text, String.Empty).Trim()
            Dim maxLength = ResolveColumnMaxLength(ctrl.Name, controlMap, columnLengths)

            If maxLength > 0 AndAlso textValue.Length > maxLength Then
                textValue = textValue.Substring(0, maxLength)
            End If

            ctrl.Text = textValue

            If TypeOf ctrl Is TextBox AndAlso maxLength > 0 Then
                DirectCast(ctrl, TextBox).MaxLength = maxLength
            ElseIf TypeOf ctrl Is RichTextBox AndAlso maxLength > 0 Then
                DirectCast(ctrl, RichTextBox).MaxLength = maxLength
            End If
        End Sub

        Private Function ResolveColumnMaxLength(controlName As String,
                                                controlMap As Dictionary(Of String, String),
                                                columnLengths As Dictionary(Of String, Integer)) As Integer
            Dim mappedColumn As String = Nothing
            If controlMap IsNot Nothing AndAlso controlMap.TryGetValue(controlName, mappedColumn) AndAlso
               Not String.IsNullOrWhiteSpace(mappedColumn) Then
                If columnLengths.ContainsKey(mappedColumn) Then
                    Return columnLengths(mappedColumn)
                End If
            End If

            Dim fallbackColumn = InferColumnNameFromControlName(controlName)
            If fallbackColumn <> String.Empty AndAlso columnLengths.ContainsKey(fallbackColumn) Then
                Return columnLengths(fallbackColumn)
            End If

            Return 0
        End Function

        Private Function InferColumnNameFromControlName(controlName As String) As String
            If String.IsNullOrWhiteSpace(controlName) Then
                Return String.Empty
            End If

            Dim prefixes = New String() {"TextBox_", "MaskedTextBox_", "RichTextBox_"}
            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return controlName.Substring(prefix.Length)
                End If
            Next

            Return String.Empty
        End Function

        Private Function ResolveTableNameForConstraints() As String
            Dim tableName = If(GetTableNameOverride(), String.Empty).Trim()
            If tableName <> String.Empty Then
                Return tableName
            End If

            Dim pageName = GetPageName()
            If pageName.EndsWith("_U", StringComparison.OrdinalIgnoreCase) Then
                Dim baseName = pageName.Substring(0, pageName.Length - 2)
                If baseName <> String.Empty Then
                    Return "FW_" & baseName
                End If
            End If

            Return String.Empty
        End Function

        Private Function ResolveOperationType(beforeRecordKey As String) As String
            If String.Equals(OkButtonText(), "Delete", StringComparison.OrdinalIgnoreCase) Then
                Return "Delete"
            End If

            Dim parsedId As Integer
            If Integer.TryParse(beforeRecordKey, parsedId) AndAlso parsedId > 0 Then
                Return "Modify"
            End If

            Return "Create"
        End Function

        Private Function ResolveRecordKeyFromControls() As String
            Dim keyControlNames = New String() {"TextBox_ID", "TextBox_UserID", "TextBox_RegistrationID"}
            For Each keyControlName In keyControlNames
                Dim matches = Me.Controls.Find(keyControlName, True)
                If matches Is Nothing OrElse matches.Length = 0 Then
                    Continue For
                End If

                Dim textValue = If(matches(0).Text, String.Empty).Trim()
                If textValue <> String.Empty Then
                    Return textValue
                End If
            Next

            Return String.Empty
        End Function

        Private Function ResolveRecordKeyForAudit() As String
            Dim overrideValue = If(ResolveAuditRecordKey(), String.Empty).Trim()
            If overrideValue <> String.Empty Then
                Return overrideValue
            End If

            Return ResolveRecordKeyFromControls()
        End Function

        Private Function CaptureControlSnapshotJson() As String
            Dim snapshot As New SortedDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)

            For Each ctrl In allControls
                If String.IsNullOrWhiteSpace(ctrl.Name) Then
                    Continue For
                End If

                If ctrl.Name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    snapshot(ctrl.Name) = "[REDACTED]"
                    Continue For
                End If

                If TypeOf ctrl Is TextBoxBase Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, TextBoxBase).Text
                ElseIf TypeOf ctrl Is ComboBox Then
                    Dim combo = DirectCast(ctrl, ComboBox)
                    Dim selectedDisplayText = ResolveComboDisplayText(combo)
                    snapshot(ctrl.Name) = selectedDisplayText
                ElseIf TypeOf ctrl Is CheckBox Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, CheckBox).Checked.ToString()
                ElseIf TypeOf ctrl Is DateTimePicker Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, DateTimePicker).Value.ToString("o")
                ElseIf TypeOf ctrl Is NumericUpDown Then
                    snapshot(ctrl.Name) = DirectCast(ctrl, NumericUpDown).Value.ToString()
                End If
            Next

            Return JsonSerializer.Serialize(snapshot)
        End Function

        Private Function CapturePendingRecordSnapshotJson() As String
            Dim snapshot As New SortedDictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
            Dim allControls As New List(Of Control)()
            DataAccess.CollectAllControls(Me, allControls)

            For Each ctrl In allControls
                Dim fieldName = ResolvePendingSnapshotFieldName(ctrl)
                If String.IsNullOrWhiteSpace(fieldName) Then
                    Continue For
                End If

                Dim controlName = If(ctrl.Name, String.Empty)
                If fieldName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 OrElse
                   controlName.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0 Then
                    snapshot(fieldName) = CaptureSensitivePendingSnapshotValue(ctrl)
                    Continue For
                End If

                snapshot(fieldName) = CapturePendingSnapshotValue(ctrl)
            Next

            Return JsonSerializer.Serialize(snapshot)
        End Function

        Private Shared Function ResolvePendingSnapshotFieldName(control As Control) As String
            If control Is Nothing Then
                Return String.Empty
            End If

            For Each binding As Binding In control.DataBindings
                Dim fieldName = If(binding.BindingMemberInfo.BindingField, String.Empty).Trim()
                If fieldName <> String.Empty Then
                    Return fieldName
                End If
            Next

            Return InferPendingSnapshotFieldName(control.Name)
        End Function

        Private Shared Function InferPendingSnapshotFieldName(controlName As String) As String
            If String.IsNullOrWhiteSpace(controlName) Then
                Return String.Empty
            End If

            Dim prefixes = New String() {"TextBox_", "MaskedTextBox_", "RichTextBox_", "ComboBox_", "CheckBox_", "DateTimePicker_", "NumericUpDown_"}
            For Each prefix In prefixes
                If controlName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                    Return controlName.Substring(prefix.Length)
                End If
            Next

            Return String.Empty
        End Function

        Private Shared Function CapturePendingSnapshotValue(control As Control) As String
            If TypeOf control Is TextBoxBase Then
                Return If(DirectCast(control, TextBoxBase).Text, String.Empty).Trim()
            End If

            If TypeOf control Is ComboBox Then
                Dim combo = DirectCast(control, ComboBox)
                If combo.SelectedValue IsNot Nothing AndAlso Not IsDBNull(combo.SelectedValue) Then
                    Return combo.SelectedValue.ToString().Trim()
                End If

                Return ResolveComboDisplayText(combo)
            End If

            If TypeOf control Is CheckBox Then
                Return DirectCast(control, CheckBox).Checked.ToString()
            End If

            If TypeOf control Is DateTimePicker Then
                Return DirectCast(control, DateTimePicker).Value.ToString("o", Globalization.CultureInfo.InvariantCulture)
            End If

            If TypeOf control Is NumericUpDown Then
                Return DirectCast(control, NumericUpDown).Value.ToString(Globalization.CultureInfo.InvariantCulture)
            End If

            If TypeOf control Is Label Then
                Return If(DirectCast(control, Label).Text, String.Empty).Trim()
            End If

            Return If(control.Text, String.Empty).Trim()
        End Function

        Private Shared Function CaptureSensitivePendingSnapshotValue(control As Control) As String
            Dim value = CapturePendingSnapshotValue(control)
            Dim hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))
            Return "[REDACTED:" & Convert.ToBase64String(hashBytes) & "]"
        End Function

        Private Shared Function ResolveComboDisplayText(combo As ComboBox) As String
            If combo Is Nothing Then
                Return String.Empty
            End If

            Dim text = If(combo.Text, String.Empty).Trim()
            If text <> String.Empty Then
                Return text
            End If

            Dim selectedRowView = TryCast(combo.SelectedItem, DataRowView)
            If selectedRowView IsNot Nothing Then
                Dim displayMember = If(combo.DisplayMember, String.Empty).Trim()
                If displayMember <> String.Empty AndAlso selectedRowView.Row IsNot Nothing AndAlso selectedRowView.Row.Table IsNot Nothing AndAlso selectedRowView.Row.Table.Columns.Contains(displayMember) Then
                    Return If(selectedRowView(displayMember), String.Empty).ToString().Trim()
                End If

                Return selectedRowView.Row(0).ToString().Trim()
            End If

            Return String.Empty
        End Function

        ' ── Shared helpers ────────────────────────────────────────────────

        Protected Function AddField(caption As String, y As Integer, [readOnly] As Boolean,
                                    Optional required As Boolean = False,
                                    Optional fieldLeft As Integer = 20) As TextBox
            Dim lbl As New Label() With {
                .Name = "Label_" & caption,
                .Text = ToPascalCaseDisplay(caption),
                .Location = New Point(fieldLeft, y + 5),
                .Size = New Size(120, 26)
            }

            If required Then
                If Not lbl.Text.EndsWith(" *", StringComparison.Ordinal) Then
                    lbl.Text &= " *"
                End If
            End If

            Me.Controls.Add(lbl)

            Dim txt As New TextBox() With {
                .Name = "TextBox_" & caption,
                .Location = New Point(fieldLeft + 130, y),
                .Size = New Size(320, 26),
                .ReadOnly = [readOnly],
                .TabStop = Not [readOnly],
                .BorderStyle = BorderStyle.FixedSingle,
                .BackColor = SystemColors.Window
            }

            Me.Controls.Add(txt)

            If required Then
                txt.Tag = "Required"

                Dim borderPanel As New Panel() With {
                    .BackColor = SystemColors.Control,
                    .Location = New Point(txt.Left - 1, txt.Top - 1),
                    .Size = New Size(txt.Width + 2, txt.Height + 2),
                    .Tag = "LocalRequiredBorder_" & caption
                }

                Me.Controls.Add(borderPanel)
                borderPanel.Visible = False
                borderPanel.BringToFront()
                txt.BringToFront()
                requiredBorderPanels(txt) = borderPanel
                AddHandler txt.TextChanged,
                    Sub(borderSender, borderEventArgs)
                        If requiredValidationActivated Then RefreshLocalRequiredBorders()
                    End Sub
            End If
            Return txt
        End Function

        Private Sub RefreshLocalRequiredBorders()
            For Each pair In requiredBorderPanels
                Dim isEmpty = String.IsNullOrWhiteSpace(pair.Key.Text)
                pair.Value.BackColor = If(isEmpty, Color.Red, SystemColors.Control)
                pair.Value.Visible = requiredValidationActivated AndAlso isEmpty
            Next
        End Sub

        Protected Sub ConfigureLookupCombo(combo As ComboBox,
                                           source As DataTable,
                                           valueMember As String,
                                           displayMember As String,
                                           selectedId As Integer,
                                           Optional placeholderText As String = "Make a Selection",
                                           Optional placeholderValue As Integer = 0)
            If combo Is Nothing Then
                Return
            End If

            If source Is Nothing OrElse source.Columns Is Nothing OrElse
               Not source.Columns.Contains(valueMember) OrElse
               Not source.Columns.Contains(displayMember) Then
                combo.DataSource = Nothing
                combo.Items.Clear()
                Return
            End If

            Dim table = source.Copy()
            Dim placeholderRow = table.NewRow()
            placeholderRow(valueMember) = placeholderValue
            placeholderRow(displayMember) = placeholderText
            table.Rows.InsertAt(placeholderRow, 0)

            combo.DataSource = table
            combo.DisplayMember = displayMember
            combo.ValueMember = valueMember

            If selectedId > 0 Then
                combo.SelectedValue = selectedId
                If combo.SelectedIndex < 0 Then
                    combo.SelectedIndex = 0
                End If
            Else
                combo.SelectedValue = placeholderValue
                If combo.SelectedIndex < 0 Then
                    combo.SelectedIndex = 0
                End If
                If String.IsNullOrWhiteSpace(combo.Text) Then
                    combo.Text = placeholderText
                End If
            End If
        End Sub

        Protected Function GetComboSelectedIdOrZero(combo As ComboBox) As Integer
            If combo Is Nothing OrElse combo.SelectedValue Is Nothing Then
                Return 0
            End If

            Dim selectedId As Integer
            If Not Integer.TryParse(combo.SelectedValue.ToString(), selectedId) Then
                Return 0
            End If

            If selectedId <= 0 Then
                Return 0
            End If

            Return selectedId
        End Function

        ''' <summary>
        ''' Control captions are already field names, so the FW_ prefix is not stripped.
        ''' Formatting itself is owned by DisplayNameFormatter.
        ''' </summary>
        Protected Shared Function ToPascalCaseDisplay(name As String) As String
            If String.IsNullOrWhiteSpace(name) Then Return name
            Return DisplayNameFormatter.ToDisplayName(name, stripFrameworkPrefix:=False)
        End Function

        Protected Shared Function ParseIntOrZero(value As String) As Integer
            Dim parsed As Integer
            If Integer.TryParse(value, parsed) Then Return parsed
            Return 0
        End Function

    End Class

End Namespace
