Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Linq
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The two-grid role picker: everything this person could have on the left, everything they
    ''' do have on the right, and arrows between.
    '''
    ''' Recovered from Users_AppAdmin_U, which carried this for years before that page was
    ''' removed. What has changed is when it writes. The old one wrote to the database on every
    ''' click, and on a new record it quietly saved the parent first to get a key - so Cancel did
    ''' not cancel, and a role could be assigned to somebody whose record was then abandoned.
    '''
    ''' This one holds the chosen set in memory and hands it to the save, which writes it in the
    ''' same transaction as the employee and the login. Create and edit behave identically,
    ''' Cancel really cancels, and nothing reaches FW_EmployeeRoles outside the transaction that
    ''' created the person it belongs to.
    '''
    ''' A widget rather than page code because a generated page is rewritten whenever its fields
    ''' change. Pages attach this in their own file, which the generator never touches.
    ''' </summary>
    Public NotInheritable Class EmployeeRolesSelector

        ''' <summary>
        ''' How much vertical room the panel needs. Public because the generator adds it to the
        ''' form height before the page opens - a page that resizes itself after the fact
        ''' flickers, and the caption band ends up in the wrong place for a frame.
        ''' </summary>
        Public Const PanelHeight As Integer = 170

        Private Const GridWidth As Integer = 260
        Private Const GridHeight As Integer = 132
        Private Const ButtonGap As Integer = 96

        ''' <summary>
        ''' How wide the whole panel is - both grids and the arrows between them. Public so a
        ''' page can centre it without knowing how it is built, which is the only way to place
        ''' it that survives the grids changing size.
        ''' </summary>
        Public Const PanelWidth As Integer = GridWidth * 2 + ButtonGap

        ''' <summary>The left edge that centres the panel in a form of the given width.</summary>
        Public Shared Function CenteredLeft(clientWidth As Integer) As Integer
            Return Math.Max(20, (clientWidth - PanelWidth) \ 2)
        End Function

        Private ReadOnly host As Form
        Private ReadOnly availableGrid As DataGridView
        Private ReadOnly assignedGrid As DataGridView
        Private ReadOnly assignButton As Button
        Private ReadOnly removeButton As Button

        ''' <summary>Every role the registration offers, by id, so a moved row keeps its name.</summary>
        Private ReadOnly roleNames As New Dictionary(Of Integer, String)()

        Private ReadOnly assigned As New List(Of Integer)()
        Private ReadOnly available As New List(Of Integer)()

        ''' <summary>
        ''' Each role's DisplayOrder, for every role on either side.
        '''
        ''' The actual value rather than the role's position in the offered list. The offered list
        ''' leaves out App Admin roles, so a held Application Admin - DisplayOrder 1 - had no
        ''' position at all, and moving it back put the first role in the order at the bottom.
        ''' </summary>
        Private ReadOnly roleDisplayOrder As New Dictionary(Of Integer, Integer)()

        ''' <summary>
        ''' Builds the panel and loads it.
        ''' </summary>
        ''' <param name="employeeId">The employee being edited, or zero when creating one.</param>
        Public Sub New(owner As Form, left As Integer, top As Integer, registrationId As Integer, employeeId As Integer)
            If owner Is Nothing Then Throw New ArgumentNullException(NameOf(owner))
            host = owner

            Dim availableLabel As New Label() With {
                .Text = "Available Roles",
                .Location = New Point(left, top),
                .Size = New Size(GridWidth, 20)
            }
            Dim assignedLabel As New Label() With {
                .Text = "Assigned Roles",
                .Location = New Point(left + GridWidth + ButtonGap, top),
                .Size = New Size(GridWidth, 20)
            }

            availableGrid = BuildGrid(left, top + 22)
            assignedGrid = BuildGrid(left + GridWidth + ButtonGap, top + 22)

            Dim buttonLeft = left + GridWidth + 18
            assignButton = New Button() With {
                .Name = "Button_AssignRole",
                .Text = ">>",
                .Location = New Point(buttonLeft, top + 52),
                .Size = New Size(60, 26)
            }
            removeButton = New Button() With {
                .Name = "Button_RemoveRole",
                .Text = "<<",
                .Location = New Point(buttonLeft, top + 86),
                .Size = New Size(60, 26)
            }

            AddHandler assignButton.Click, Sub(s, e) Move(availableGrid, available, assigned)
            AddHandler removeButton.Click, Sub(s, e) Move(assignedGrid, assigned, available)

            ' Double-click does what the arrow does. The arrows are the discoverable way and the
            ' only one that works over VirtualUI's coalesced pointer stream reliably; the
            ' double-click is for people who already know.
            AddHandler availableGrid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then Move(availableGrid, available, assigned)
            AddHandler assignedGrid.CellDoubleClick, Sub(s, e) If e.RowIndex >= 0 Then Move(assignedGrid, assigned, available)

            host.Controls.AddRange({availableLabel, assignedLabel, availableGrid, assignedGrid, assignButton, removeButton})

            Load(registrationId, employeeId)
        End Sub

        Private Shared Function BuildGrid(left As Integer, top As Integer) As DataGridView
            Dim grid As New DataGridView() With {
                .Location = New Point(left, top),
                .Size = New Size(GridWidth, GridHeight),
                .AllowUserToAddRows = False,
                .AllowUserToDeleteRows = False,
                .AllowUserToResizeRows = False,
                .AutoGenerateColumns = False,
                .ColumnHeadersVisible = False,
                .RowHeadersVisible = False,
                .MultiSelect = False,
                .ReadOnly = True,
                .SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                .BackgroundColor = SystemColors.Window,
                .TabStop = False
            }
            grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "RoleID",
                .Visible = False
            })
            grid.Columns.Add(New DataGridViewTextBoxColumn() With {
                .Name = "RoleName",
                .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            })
            Return grid
        End Function

        ''' <summary>
        ''' Fills both sides.
        '''
        ''' The left grid comes from GetSelectableRolesByRegistration, which already leaves out
        ''' deleted roles, inactive ones and Application Admin. A role somebody already holds but
        ''' which is no longer offered still shows on the right: it is a fact about them, and
        ''' hiding it would make it impossible to take away.
        ''' </summary>
        Private Sub Load(registrationId As Integer, employeeId As Integer)
            roleNames.Clear()
            assigned.Clear()
            available.Clear()
            roleDisplayOrder.Clear()

            Dim offered = DataAccess.GetSelectableRolesByRegistration(registrationId)
            If offered IsNot Nothing Then
                For Each row As DataRow In offered.Rows
                    Dim id = Convert.ToInt32(row("RoleID"))
                    roleNames(id) = Convert.ToString(row("RoleName"))
                    roleDisplayOrder(id) = Convert.ToInt32(row("DisplayOrder"))
                    available.Add(id)
                Next
            End If

            ' The left grid never offers Application Admin, so a person who already holds it is the
            ' only way that role reaches this control - and it must still count as an admin role,
            ' or taking every other role away would quietly drop the requirement with it.
            If employeeId > 0 Then
                For Each held In DataAccess.GetEmployeeRoleIds(employeeId)
                    If Not roleNames.ContainsKey(held.RoleId) Then roleNames(held.RoleId) = held.RoleName
                    roleDisplayOrder(held.RoleId) = held.DisplayOrder
                    available.Remove(held.RoleId)
                    If Not assigned.Contains(held.RoleId) Then assigned.Add(held.RoleId)
                Next
            End If

            Refill()
        End Sub

        Private Sub Move(from As DataGridView, fromList As List(Of Integer), toList As List(Of Integer))
            If from.SelectedRows.Count = 0 Then Return

            Dim roleId As Integer
            If Not Integer.TryParse(Convert.ToString(from.SelectedRows(0).Cells("RoleID").Value), roleId) Then Return

            fromList.Remove(roleId)
            If Not toList.Contains(roleId) Then toList.Add(roleId)

            ' Back on the left, a role takes its DisplayOrder place again. Appending put every role
            ' taken off an employee at the bottom, whatever its order. Ordered the way the database
            ' orders roles - unset last, then DisplayOrder, then name - so the list reads the same
            ' as when it first opened. The right-hand list keeps the order roles were given.
            If toList Is available Then
                Dim ordered = available.
                    OrderBy(Function(id) If(OrderOf(id) = 0, 1, 0)).
                    ThenBy(Function(id) OrderOf(id)).
                    ThenBy(Function(id) NameOf_(id), StringComparer.OrdinalIgnoreCase).
                    ToList()
                available.Clear()
                available.AddRange(ordered)
            End If

            Refill()
        End Sub

        Private Function OrderOf(roleId As Integer) As Integer
            Dim order As Integer
            Return If(roleDisplayOrder.TryGetValue(roleId, order), order, 0)
        End Function

        Private Function NameOf_(roleId As Integer) As String
            Dim name As String = Nothing
            Return If(roleNames.TryGetValue(roleId, name), name, String.Empty)
        End Function

        Private Sub Refill()
            Fill(availableGrid, available)
            Fill(assignedGrid, assigned)
        End Sub

        Private Sub Fill(grid As DataGridView, ids As List(Of Integer))
            grid.Rows.Clear()
            For Each id In ids
                Dim name As String = Nothing
                If Not roleNames.TryGetValue(id, name) Then name = "Role " & id.ToString()
                grid.Rows.Add(id, name)
            Next
            grid.ClearSelection()
        End Sub

        ''' <summary>The roles on the right, which is what the employee will have after the save.</summary>
        Public ReadOnly Property SelectedRoleIds As List(Of Integer)
            Get
                Return New List(Of Integer)(assigned)
            End Get
        End Property

        ''' <summary>
        ''' Whether the page may save, with the reason when it may not.
        '''
        ''' Nobody is saved without a role. An employee with none authenticates correctly and is
        ''' then turned away at login for having nothing to do - which reads as a broken account
        ''' rather than an unfinished one.
        ''' </summary>
        Public Function Validate(ByRef reason As String) As Boolean
            If assigned.Count > 0 Then
                reason = String.Empty
                Return True
            End If

            reason = "ASSIGN AT LEAST ONE ROLE"
            Return False
        End Function

        ''' <summary>Enables or disables the whole panel, for a read-only page.</summary>
        Public Sub SetEnabled(enabled As Boolean)
            assignButton.Enabled = enabled
            removeButton.Enabled = enabled
        End Sub

    End Class

End Namespace
