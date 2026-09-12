Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The default occupant of the Messages region, and a placeholder for whatever eventually
    ''' belongs there.
    '''
    ''' It exists because of a rule worth keeping: **every region always has an occupant.** A
    ''' Messages button that can only ever show Messages has nothing to return to, and a hidden
    ''' region leaves a third of the page empty - the cell stays whether or not anything is in it.
    ''' With a default in place the button is a selector rather than a toggle, and the same control
    ''' answers "what is here when messaging is switched off".
    '''
    ''' **It reports its own size on purpose.** There is no designer in this project - every control
    ''' is built in code - so the way to decide what fits in this region is to look at how much room
    ''' it has. The readout is not decoration: it is the measurement that the eventual content, a
    ''' chart or a set of figures or a short list, has to be designed against. Delete it when
    ''' something real goes in here.
    '''
    ''' It lives under 100_PROJECTS\SDC rather than in the framework because which default a given
    ''' application wants is MenuFormInitializer's decision. A second application on this framework
    ''' writes its own and changes nothing in 000_FRAMEWORK.
    ''' </summary>
    Public Class OverviewWindowControl
        Inherits UserControl
        Implements IAccessControlledControl

        Private ReadOnly titleLabel As Label
        Private ReadOnly sizeLabel As Label
        Private ReadOnly noteLabel As Label

        Public Sub New()
            Me.Dock = DockStyle.Fill
            Me.BackColor = Color.White
            Me.Padding = New Padding(8)

            titleLabel = New Label() With {
                .Text = "Nothing here yet",
                .Location = New Point(12, 16),
                .AutoSize = True,
                .Font = New Font("Segoe UI", 15.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(76, 84, 94)
            }

            sizeLabel = New Label() With {
                .Text = "0 x 0",
                .Location = New Point(12, 52),
                .AutoSize = True,
                .Font = New Font("Consolas", 12.0F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(58, 133, 197)
            }

            noteLabel = New Label() With {
                .Text = "This is the room a chart, a summary or a short list would have.",
                .Location = New Point(12, 82),
                .AutoSize = False,
                .Size = New Size(320, 40),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right,
                .Font = New Font("Segoe UI", 9.5F, FontStyle.Regular),
                .ForeColor = Color.FromArgb(120, 128, 138)
            }

            Me.Controls.Add(titleLabel)
            Me.Controls.Add(sizeLabel)
            Me.Controls.Add(noteLabel)

            AddHandler Me.Resize, AddressOf Overview_Resize
        End Sub

        ''' <summary>
        ''' Kept current on every resize rather than read once, because the region is a percentage
        ''' of the window and the number is only useful if it matches what is on screen.
        ''' </summary>
        Private Sub Overview_Resize(sender As Object, e As EventArgs)
            sizeLabel.Text = Me.ClientSize.Width.ToString() & " x " & Me.ClientSize.Height.ToString()
            noteLabel.Width = Math.Max(120, Me.ClientSize.Width - 24)
        End Sub

        ''' <summary>
        ''' Nothing to restrict. A placeholder shows no data, so there is nothing a role could be
        ''' allowed or denied - but the interface is implemented so this control can be loaded by
        ''' the same path as every other region control rather than needing a special case.
        ''' </summary>
        Public Sub ApplyAccess(profile As AccessProfile, tableName As String) Implements IAccessControlledControl.ApplyAccess
        End Sub

    End Class

End Namespace
