Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld

    ''' <summary>
    ''' Lets an App Admin re-picture an icon by right-clicking it, and remembers the choice.
    '''
    ''' A controller rather than handlers on each form: the two dashboards and the main menu's
    ''' ribbon are separate forms wanting one behaviour, and it should not be written three times to
    ''' be had three times. Named for icons rather than dashboards since the ribbon joined, though
    ''' the choices still live in FW_DashboardLayouts, keyed by surface name and ActionKey.
    '''
    ''' The dialog is IconPicker, the same one the page generator offers, so an icon chosen here and
    ''' one chosen for a generated page mean the same thing by the same names.
    '''
    ''' Two things are the caller's to supply, because they are the only things that differ:
    '''
    ''' - The ActionKey for each button. A dashboard icon carries its key as its control name; a
    '''   ribbon tile is named "ACTION_users" and its key is "users". Taking the key explicitly
    '''   keeps that prefix out of the database.
    ''' - How a file name becomes an image. A dashboard scales to its icon size; the ribbon scales
    '''   and then centres on a fixed canvas. Passing the loader in is what let the ribbon share
    '''   this at all.
    '''
    ''' The App Admin test is applied wherever the menu is offered, and again in DataAccess: a menu
    ''' that is not offered is not authorization.
    ''' </summary>
    Public NotInheritable Class IconImageController

        Private ReadOnly owner As Form
        Private ReadOnly surfaceName As String
        Private ReadOnly userId As Integer
        Private ReadOnly loadImage As Func(Of String, Image, Image)

        Private ReadOnly buttonsByKey As New Dictionary(Of String, ButtonBase)(StringComparer.OrdinalIgnoreCase)
        Private ReadOnly keysByButton As New Dictionary(Of ButtonBase, String)()

        ''' <summary>
        ''' Each icon's picture as its form was written with it, kept so Reset has something to go
        ''' back to. Read from the live control, which is the only place that answer survives once
        ''' the form is built.
        ''' </summary>
        Private ReadOnly defaultImages As New Dictionary(Of String, Image)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' The icon the menu was opened on, captured while the menu opens.
        '''
        ''' A ToolStripMenuItem's Click runs after the menu has closed and SourceControl has been
        ''' cleared, so asking then would always find nothing.
        ''' </summary>
        Private menuTarget As ButtonBase

        Private sharedMenu As ContextMenuStrip

        Public Sub New(owner As Form,
                       surfaceName As String,
                       userId As Integer,
                       loadImage As Func(Of String, Image, Image))
            Me.owner = owner
            Me.surfaceName = If(surfaceName, String.Empty)
            Me.userId = userId
            Me.loadImage = loadImage
        End Sub

        ''' <summary>
        ''' Records what each icon looks like by default, applies any chosen pictures, and - for an
        ''' App Admin - hangs the right-click menu on them.
        '''
        ''' An icon with a blank key is left alone: there would be nothing to record a choice
        ''' against, so offering the menu would promise something that could not be kept.
        '''
        ''' Safe to call again. A key already known is skipped rather than re-wired, so a form that
        ''' gains a tile after its first pass picks it up without hanging a second menu on the rest.
        ''' </summary>
        Public Sub Attach(candidates As IEnumerable(Of KeyValuePair(Of String, ButtonBase)))
            If candidates Is Nothing Then Return

            For Each candidate In candidates
                Dim key = candidate.Key
                Dim button = candidate.Value
                If button Is Nothing OrElse String.IsNullOrWhiteSpace(key) Then Continue For
                If buttonsByKey.ContainsKey(key) Then Continue For

                buttonsByKey(key) = button
                keysByButton(button) = key
                defaultImages(key) = button.Image
            Next

            ApplyRoleAffordances()
            ApplySavedImages()
        End Sub

        ''' <summary>
        ''' Offers or withdraws the right-click menu to match the role the session is running under
        ''' now.
        '''
        ''' Run on every pass, not only the first. The main menu offers a role switch and
        ''' reconfigures itself in place, so a session can become an App Admin - or stop being one -
        ''' without the form being rebuilt. Attaching once at startup meant a user who switched into
        ''' an App Admin role got no menu until the application was restarted, and one who switched
        ''' out kept it.
        '''
        ''' The dashboards call this once and are unaffected either way; the write is guarded in
        ''' DataAccess regardless, so the menu is an invitation rather than the permission.
        ''' </summary>
        Private Sub ApplyRoleAffordances()
            If Not SessionState.IsApplicationAdmin Then
                For Each button In buttonsByKey.Values
                    button.ContextMenuStrip = Nothing
                Next
                Return
            End If

            If sharedMenu Is Nothing Then sharedMenu = BuildMenu()

            For Each button In buttonsByKey.Values
                button.ContextMenuStrip = sharedMenu
            Next
        End Sub

        ''' <summary>
        ''' Puts the chosen picture on every icon that has one, and the original on every icon that
        ''' does not. Both halves matter: without the second, a Reset would not show until the next
        ''' launch.
        '''
        ''' Public because two places on the main menu re-assert a hard-coded picture - the pinned
        ''' row on every resize, and the role tile on every role change - and a chosen picture has
        ''' to be laid back over the top afterwards or it would silently revert.
        ''' </summary>
        Public Sub ApplySavedImages()
            Dim chosen = DataAccess.GetDashboardIconOverrides(surfaceName)

            For Each pair In buttonsByKey
                Dim fileName As String = Nothing
                Dim fallback As Image = Nothing
                defaultImages.TryGetValue(pair.Key, fallback)

                If chosen.TryGetValue(pair.Key, fileName) AndAlso Not String.IsNullOrWhiteSpace(fileName) Then
                    pair.Value.Image = loadImage(fileName, fallback)
                Else
                    pair.Value.Image = fallback
                End If
            Next
        End Sub

        ''' One menu for every icon. Which one it acts on is read as it opens, so there is no
        ''' per-icon menu to keep in step with the icons.
        Private Function BuildMenu() As ContextMenuStrip
            Dim menu As New ContextMenuStrip()
            Dim changeItem As New ToolStripMenuItem("Change Icon...")
            Dim resetItem As New ToolStripMenuItem("Reset to Default")

            AddHandler menu.Opening, Sub() menuTarget = TryCast(menu.SourceControl, ButtonBase)
            AddHandler changeItem.Click, Sub() ChangeIcon(menuTarget)
            AddHandler resetItem.Click, Sub() ResetIcon(menuTarget)

            menu.Items.Add(changeItem)
            menu.Items.Add(resetItem)
            Return menu
        End Function

        Private Function KeyFor(target As ButtonBase) As String
            If target Is Nothing Then Return String.Empty

            Dim key As String = Nothing
            If keysByButton.TryGetValue(target, key) Then Return key
            Return String.Empty
        End Function

        Private Sub ChangeIcon(target As ButtonBase)
            Dim key = KeyFor(target)
            If key = String.Empty Then Return

            Dim current As String = String.Empty
            DataAccess.GetDashboardIconOverrides(surfaceName).TryGetValue(key, current)

            Dim picked = IconPicker.Choose(owner, current)
            If String.IsNullOrWhiteSpace(picked) Then Return

            Save(key, picked)
        End Sub

        Private Sub ResetIcon(target As ButtonBase)
            Dim key = KeyFor(target)
            If key = String.Empty Then Return

            Save(key, String.Empty)
        End Sub

        ''' <summary>
        ''' Writes the choice and shows it at once.
        '''
        ''' A refusal is reported rather than swallowed. The write is guarded independently of the
        ''' menu, so somebody who reaches here without the role has to be told why nothing happened
        ''' instead of watching the icon not change.
        ''' </summary>
        Private Sub Save(actionKey As String, fileName As String)
            If Not DataAccess.SaveDashboardIconOverride(surfaceName, actionKey, fileName, userId) Then
                MessageBox.Show(owner,
                                "CHANGING AN ICON REQUIRES THE APPLICATION ADMIN ROLE.",
                                "NOT PERMITTED",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information)
                Return
            End If

            ApplySavedImages()
        End Sub
    End Class
End Namespace
