Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The default occupant of the main menu's left region: a product banner and a tabbed board.
    '''
    ''' It exists because of a rule worth keeping: **every region always has an occupant.** A
    ''' Messages button that can only ever show Messages has nothing to return to, and a hidden
    ''' region leaves a third of the page empty - the cell stays whether or not anything is in it.
    ''' With a default in place the button is a selector rather than a toggle, and the same control
    ''' answers "what is here when messaging is switched off".
    '''
    ''' The tabs are empty on purpose. This is the shape the eventual content has to fit - a
    ''' schedule, a count, a short list - and the shape is the part worth agreeing before anything
    ''' is written to fill it.
    '''
    ''' It lives under 100_PROJECTS\SDC rather than in the framework because which default a given
    ''' application wants is MenuFormInitializer's decision. A second application on this framework
    ''' writes its own and changes nothing in 000_FRAMEWORK.
    ''' </summary>
    Public Class QDeskWindowControl
        Inherits UserControl
        Implements IAccessControlledControl

        ''' The banner, from the repository's images rather than embedded, so it can be replaced
        ''' without a rebuild.
        Private Const BannerImagePath As String = "QDesk\QDeskAdministration.png"

        ''' The strip the banner sits in. The picture is left-aligned inside it rather than docked,
        ''' because a docked PictureBox in Zoom mode centres its image in whatever width it has -
        ''' which put the logo in the middle of the region instead of above the tabs.
        Private ReadOnly bannerHost As Panel
        Private ReadOnly bannerBox As PictureBox
        Private ReadOnly boardPanel As Panel
        Private ReadOnly boardTabs As TabControl

        Public Sub New()
            Me.Dock = DockStyle.Fill
            Me.BackColor = Color.White
            ' No inset. The region hides its caption bar for this control, so the banner starts at
            ' the top edge and the tabs run to the bottom - the point of filling the cell.
            Me.Padding = New Padding(6)

            ' Zoom, not Stretch: the banner has its own proportions and a region that is wider on
            ' one screen than another must not squash it.
            Const bannerHeight As Integer = 78

            bannerBox = New PictureBox() With {
                .Location = New Point(0, 0),
                .Height = bannerHeight,
                .SizeMode = PictureBoxSizeMode.Zoom,
                .Image = AssetImages.Load(BannerImagePath)
            }

            ' Only as wide as the picture needs at that height, so "left aligned" means the logo's
            ' own left edge rather than the left edge of a box with the logo centred in it.
            If bannerBox.Image IsNot Nothing AndAlso bannerBox.Image.Height > 0 Then
                Dim aspect = bannerBox.Image.Width / CDbl(bannerBox.Image.Height)
                bannerBox.Width = CInt(Math.Ceiling(bannerHeight * aspect))
            End If

            bannerHost = New Panel() With {
                .Dock = DockStyle.Top,
                .Height = bannerHeight,
                .BackColor = Color.White
            }
            bannerHost.Controls.Add(bannerBox)

            ' A missing image leaves the space rather than a broken-looking empty frame.
            If bannerBox.Image Is Nothing Then bannerHost.Height = 0

            boardTabs = New TabControl() With {
                .Dock = DockStyle.Fill,
                .Padding = New Point(12, 4)
            }
            boardTabs.TabPages.Add(New TabPage("Today's Schedules") With {.BackColor = Color.White, .Padding = New Padding(8)})
            boardTabs.TabPages.Add(New TabPage("Permits Sold") With {.BackColor = Color.White, .Padding = New Padding(8)})

            boardPanel = New Panel() With {
                .Dock = DockStyle.Fill,
                .BackColor = Color.White,
                .BorderStyle = BorderStyle.FixedSingle,
                .Padding = New Padding(0),
                .Margin = New Padding(0)
            }
            boardPanel.Controls.Add(boardTabs)

            ' Fill before Top, because a docked child added later sits inside what is already
            ' docked - the banner has to be added after the panel to end up above it.
            Me.Controls.Add(boardPanel)
            Me.Controls.Add(bannerHost)
        End Sub

        ''' <summary>
        ''' Nothing to restrict yet. The tabs show no data, so there is nothing a role could be
        ''' allowed or denied - but the interface is implemented so this control can be loaded by
        ''' the same path as every other region control rather than needing a special case. When
        ''' the tabs carry real figures, this is where their permissions belong.
        ''' </summary>
        Public Sub ApplyAccess(profile As AccessProfile, tableName As String) Implements IAccessControlledControl.ApplyAccess
        End Sub

    End Class

End Namespace
