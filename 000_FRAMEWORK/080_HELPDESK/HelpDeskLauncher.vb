Option Strict On
Option Explicit On

Imports System.Drawing
Imports System.Linq
Imports System.IO
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' The Help Desk button every _B and _U page carries. One owner, so the button looks the same,
    ''' sits in the same place, and reports the same page name whichever page raised it.
    '''
    ''' Reporting from a page is what makes a defect actionable: the page name is captured rather
    ''' than remembered, which is the difference between a report that can be worked and one that
    ''' starts with "which screen was this on?".
    ''' </summary>
    Public NotInheritable Class HelpDeskLauncher

        Private Sub New()
        End Sub

        Public Const ButtonName As String = "Button_HelpDesk"
        Private Const ButtonWidth As Integer = 110
        Private Const ButtonHeight As Integer = 28
        ' Widened from 10 on 2026-09-07: ten pixels put the button hard against the window frame,
        ' reading as though it had been pushed off the page rather than placed on it.
        Private Const EdgeMargin As Integer = 28
        Private Const NeighbourGap As Integer = 12
        Private Const TopMargin As Integer = 10

        ''' The one page the button must not appear on: it opens this listing, so a button here
        ''' would only reopen the page the user is already looking at. Every other page keeps it,
        ''' the help desk's own maintenance and admin pages included.
        Private Shared ReadOnly HelpDeskPages As String() = {
            NameOf(FW_HD_Issues_B),
            NameOf(FW_HD_Issues_U)
        }

        Public Shared Function IsHelpDeskPage(pageName As String) As Boolean
            Dim name = If(pageName, String.Empty).Trim()
            Return HelpDeskPages.Any(Function(candidate) String.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' Adds the button to a page, anchored to the top right corner.
        Public Shared Function Attach(owner As Form, pageName As String) As Button
            If owner Is Nothing OrElse IsHelpDeskPage(pageName) Then Return Nothing

            Dim button As New Button() With {
                .Name = ButtonName,
                .Text = "Help Desk",
                .Size = New Size(ButtonWidth, ButtonHeight),
                .Anchor = AnchorStyles.Top Or AnchorStyles.Right,
                .TabStop = False,
                .TextImageRelation = TextImageRelation.ImageBeforeText,
                .ImageAlign = ContentAlignment.MiddleLeft,
                .TextAlign = ContentAlignment.MiddleRight
            }
            button.Location = New Point(owner.ClientSize.Width - ButtonWidth - EdgeMargin, TopMargin)
            button.Image = IconScaler.Load("helpdesk.png", 18, Nothing)

            Dim tip As New ToolTip()
            tip.SetToolTip(button, "Report a problem or ask a question about this page")

            ' Opens a listing rather than a blank ticket: what has already been raised about this
            ' page is the first thing worth seeing, and Create is one button away from there.
            '
            ' Raised from a page, the listing is about that page - the session user, the session
            ' registration and this page - whoever is logged in. The registration-wide support view
            ' an administrator needs is the Help Desk entry on the main menu, not this button.
            ' The class name identifies the page for storing and filtering; the page's own title is
            ' what the user recognises, so both are passed. Read at click time because a page builds
            ' its caption during layout, after this button is created.
            AddHandler button.Click,
                Sub()
                    Using page As New FW_HD_Issues_B(pageName, owner.Text)
                        page.ShowDialog(owner)
                    End Using
                End Sub

            owner.Controls.Add(button)
            button.BringToFront()
            Return button
        End Function

        ''' <summary>
        ''' Centres the button on the page caption, so the two read as one header line. Called after
        ''' the caption is positioned, because a browse page centres its title in a header band whose
        ''' height is not known until then.
        ''' </summary>
        Public Shared Sub AlignToCaption(owner As Form, caption As Control)
            If owner Is Nothing OrElse caption Is Nothing Then Return

            Dim matches = owner.Controls.Find(ButtonName, True)
            If matches.Length = 0 Then Return

            Dim button = matches(0)
            button.Top = Math.Max(0, caption.Top + ((caption.Height - button.Height) \ 2))
        End Sub

        ''' <summary>
        ''' The gap between the Help Desk button's right edge and the window frame.
        '''
        ''' Exposed so a row of buttons on a lower line can finish where this button finishes. Those
        ''' rows do not collide with it - it sits above them - so they want its right edge rather
        ''' than ReservedWidth, which stops short of the button to keep a neighbour off it.
        ''' </summary>
        Public Shared ReadOnly Property TrailingMargin As Integer
            Get
                Return EdgeMargin
            End Get
        End Property

        ''' The width a neighbouring top-right button must leave clear.
        Public Shared ReadOnly Property ReservedWidth As Integer
            Get
                Return ButtonWidth + EdgeMargin + NeighbourGap
            End Get
        End Property

        ''' <summary>
        ''' Whether this session handles tickets rather than raises them. Shared so the button on a
        ''' page and the Help Desk entry on the main menu can never disagree about who sees what.
        ''' </summary>
        Public Shared Function OpensSupportListing() As Boolean
            If Not SessionState.IsActive OrElse Not SessionState.Current.HasValue Then Return False
            Dim session = SessionState.Current.Value
            Return session.IsCompanyAdminRole OrElse session.IsApplicationAdminRole
        End Function

        Private Shared Function ResolveRegistrationId() As Integer
            If SessionState.IsActive AndAlso SessionState.Current.HasValue Then
                Return SessionState.Current.Value.RegistrationID
            End If
            Return 0
        End Function

        Private Shared Function LoadIcon(fileName As String) As Image
            Dim candidates As String() = {
                Path.Combine(Application.StartupPath, "assets", "images", fileName),
                Path.Combine(Application.StartupPath, "..", "..", "..", "assets", "images", fileName),
                Path.Combine(Application.StartupPath, "..", "..", "..", "..", "assets", "images", fileName)
            }

            For Each candidate In candidates
                Dim fullPath = Path.GetFullPath(candidate)
                If File.Exists(fullPath) Then
                    Try
                        ' Through a stream so the file is not locked for the life of the process.
                        Using stream As New FileStream(fullPath, FileMode.Open, FileAccess.Read)
                            Return New Bitmap(Image.FromStream(stream), New Size(18, 18))
                        End Using
                    Catch
                        Return Nothing
                    End Try
                End If
            Next

            Return Nothing
        End Function

    End Class

End Namespace