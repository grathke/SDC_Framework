Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld

    ''' <summary>
    ''' Lets an App Admin re-picture a dashboard icon by right-clicking it, and remembers the choice.
    '''
    ''' A controller rather than handlers on each dashboard, for the same reason the drag is one:
    ''' two separate forms with the same grid, and one behaviour should not be written twice to be
    ''' had twice. It shares the drag's key - the ActionKey each icon carries as its control name -
    ''' so a picture and a position are recorded against the same icon in the same row.
    '''
    ''' The dialog is IconPicker, the same one the page generator offers, so an icon chosen here and
    ''' one chosen for a generated page mean the same thing by the same names.
    '''
    ''' The App Admin test is applied on both dashboards, even though only App Admins can reach the
    ''' application one today. It costs one comparison there, and it means that dashboard does not
    ''' silently become editable by everybody the day its own access is widened. The write is
    ''' guarded again in DataAccess: a menu that is not offered is not authorization.
    ''' </summary>
    Public NotInheritable Class DashboardIconImageController

        Private ReadOnly owner As Form
        Private ReadOnly dashboardName As String
        Private ReadOnly userId As Integer
        Private ReadOnly iconSize As Integer
        Private ReadOnly icons As New List(Of DashboardIconButton)()

        ''' <summary>
        ''' Each icon's picture as its dashboard was written with it, kept so Reset has something to
        ''' go back to. Read from the live control, which is the only place that answer survives
        ''' once the form is built.
        ''' </summary>
        Private ReadOnly defaultImages As New Dictionary(Of String, Image)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' The icon the menu was opened on, captured while the menu opens.
        '''
        ''' A ToolStripMenuItem's Click runs after the menu has closed and SourceControl has been
        ''' cleared, so asking then would always find nothing.
        ''' </summary>
        Private menuTarget As DashboardIconButton

        Public Sub New(owner As Form, dashboardName As String, userId As Integer, iconSize As Integer)
            Me.owner = owner
            Me.dashboardName = If(dashboardName, String.Empty)
            Me.userId = userId
            Me.iconSize = iconSize
        End Sub

        ''' <summary>
        ''' Records what each icon looks like by default, applies any chosen pictures, and - for an
        ''' App Admin - hangs the right-click menu on them.
        '''
        ''' An icon with no name is left alone: there would be nothing to record a choice against,
        ''' so offering the menu would promise something that could not be kept.
        ''' </summary>
        Public Sub Attach(candidates As IEnumerable(Of DashboardIconButton))
            If candidates Is Nothing Then Return

            Dim menu As ContextMenuStrip = If(SessionState.IsApplicationAdmin, BuildMenu(), Nothing)

            For Each candidate In candidates
                If candidate Is Nothing OrElse String.IsNullOrWhiteSpace(candidate.Name) Then Continue For

                icons.Add(candidate)
                defaultImages(candidate.Name) = candidate.Image
                If menu IsNot Nothing Then candidate.ContextMenuStrip = menu
            Next

            ApplySavedImages()
        End Sub

        ''' <summary>
        ''' Puts the chosen picture on every icon that has one, and the original on every icon that
        ''' does not. Both halves matter: without the second, a Reset would not show until the next
        ''' launch.
        ''' </summary>
        Public Sub ApplySavedImages()
            Dim chosen = DataAccess.GetDashboardIconOverrides(dashboardName)

            For Each icon In icons
                Dim fileName As String = Nothing
                If chosen.TryGetValue(icon.Name, fileName) AndAlso Not String.IsNullOrWhiteSpace(fileName) Then
                    Dim fallback As Image = Nothing
                    defaultImages.TryGetValue(icon.Name, fallback)
                    icon.Image = IconScaler.Load(fileName, iconSize, fallback)
                Else
                    Dim original As Image = Nothing
                    If defaultImages.TryGetValue(icon.Name, original) Then icon.Image = original
                End If
            Next
        End Sub

        ''' One menu for every icon. Which one it acts on is read as it opens, so there is no
        ''' per-icon menu to keep in step with the icons.
        Private Function BuildMenu() As ContextMenuStrip
            Dim menu As New ContextMenuStrip()
            Dim changeItem As New ToolStripMenuItem("Change Icon...")
            Dim resetItem As New ToolStripMenuItem("Reset to Default")

            AddHandler menu.Opening, Sub() menuTarget = TryCast(menu.SourceControl, DashboardIconButton)
            AddHandler changeItem.Click, Sub() ChangeIcon(menuTarget)
            AddHandler resetItem.Click, Sub() ResetIcon(menuTarget)

            menu.Items.Add(changeItem)
            menu.Items.Add(resetItem)
            Return menu
        End Function

        Private Sub ChangeIcon(target As DashboardIconButton)
            If target Is Nothing OrElse String.IsNullOrWhiteSpace(target.Name) Then Return

            Dim current As String = String.Empty
            DataAccess.GetDashboardIconOverrides(dashboardName).TryGetValue(target.Name, current)

            Dim picked = IconPicker.Choose(owner, current)
            If String.IsNullOrWhiteSpace(picked) Then Return

            Save(target, picked)
        End Sub

        Private Sub ResetIcon(target As DashboardIconButton)
            If target Is Nothing OrElse String.IsNullOrWhiteSpace(target.Name) Then Return
            Save(target, String.Empty)
        End Sub

        ''' <summary>
        ''' Writes the choice and shows it at once.
        '''
        ''' A refusal is reported rather than swallowed. The write is guarded independently of the
        ''' menu, so somebody who reaches here without the role has to be told why nothing happened
        ''' instead of watching the icon not change.
        ''' </summary>
        Private Sub Save(target As DashboardIconButton, fileName As String)
            If Not DataAccess.SaveDashboardIconOverride(dashboardName, target.Name, fileName, userId) Then
                MessageBox.Show(owner,
                                "CHANGING A DASHBOARD ICON REQUIRES THE APPLICATION ADMIN ROLE.",
                                "NOT PERMITTED",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information)
                Return
            End If

            ApplySavedImages()
        End Sub
    End Class
End Namespace
