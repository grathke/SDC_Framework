Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Drawing
Imports System.Linq
Imports System.Text
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' What a maintenance page will look like, drawn from the same placement the generator emits
    ''' from - without generating it, compiling it, or opening a database record.
    '''
    ''' Positions only, and the title says so. The controls are real, and they are where they will
    ''' be, at the size they will be; what is in them is not. Nothing here is bound to a column,
    ''' no field permission is applied and no row is loaded, because the question this answers is
    ''' where things sit, and every one of those would cost a round trip to answer a question
    ''' nobody asked.
    '''
    ''' It is deliberately not a FW_Base_U. A real page reaches the database in its constructor,
    ''' wants a session for its caption and applies FW_RoleFields before it will show itself. A
    ''' preview that inherited all that could not be opened from a field picker on an unsaved
    ''' request, which is the one place it needs to work.
    ''' </summary>
    Public NotInheritable Class LayoutPreviewForm
        Inherits Form

        Public Const PreviewTitle As String = "Layout preview - positions only"

        Private ReadOnly page As PlacedPage
        Private ReadOnly tableName As String
        Private ReadOnly columnLengths As Dictionary(Of String, Integer)
        Private saveButton As Button
        Private permissionsBox As CheckBox
        Private hiddenByRole As Integer
        Private ReadOnly subjectName As String

        ''' <summary>
        ''' Builds the preview. Never throws: a page that cannot be drawn says why on itself,
        ''' because an empty window with no explanation is the failure this exists to replace.
        ''' </summary>
        Public Sub New(placedPage As PlacedPage, pageName As String, Optional table As String = "")
            page = placedPage
            tableName = If(table, String.Empty)

            ' Asked once, here, rather than per field: the widths are the one thing a layout
            ' preview cannot work out for itself, and one read answers every box on the page.
            Try
                columnLengths = DataAccess.GetTextColumnMaxLengths(tableName)
            Catch
                columnLengths = New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
            End Try
            subjectName = If(String.IsNullOrWhiteSpace(pageName), "Maintenance page", pageName.Trim())

            Text = PreviewTitle
            FormBorderStyle = FormBorderStyle.FixedDialog
            MaximizeBox = False
            MinimizeBox = False
            ' Centred on the screen, not on whatever opened it. A preview opened from a field
            ' picker that is itself off to one side would otherwise appear off to that side.
            StartPosition = FormStartPosition.CenterScreen
            BackColor = SystemColors.Control
            AutoScroll = True

            Try
                Build()
            Catch ex As Exception
                ShowFailure(ex.ToString())
            End Try
        End Sub

        Private Sub Build()
            If page Is Nothing OrElse page.Fields Is Nothing OrElse page.Fields.Count = 0 Then
                ShowFailure("Nothing to draw." & Environment.NewLine & Environment.NewLine &
                            "Choose a table and tick at least one field for the _U page, then preview again.")
                Return
            End If

            ' The same size the generated page is given, plus the row the caption takes. A preview
            ' that used its own arithmetic would drift from the page the first time either changed.
            ' The size the layout wants. PreviewWindow caps it to the screen and makes the rest
            ' scrollable, so a page too tall for the monitor is still readable to the bottom.
            Dim wanted = New Size(page.Width, page.Height + MaintenanceLayout.CaptionShift + StatusBarHeight)
            ClientSize = wanted

            AddCaption()

            For Each placement In page.Fields
                AddPlacement(placement)
            Next

            AddReservedBand()
            AddActionButtons()
            AddStatusBar()

            PreviewWindow.Fit(Me, wanted)
        End Sub

        Private Const StatusBarHeight As Integer = 30

        ''' <summary>
        ''' The page title, drawn where ApplySharedPageCaption puts it. Every field below is
        ''' shifted by the same amount the real page shifts them, which is why the preview and the
        ''' page agree about which row a field is on.
        ''' </summary>
        Private Sub AddCaption()
            Controls.Add(New Label() With {
                .Name = "PreviewCaption",
                .AutoSize = True,
                .Text = subjectName,
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .Location = New Point(20, 15)
            })
        End Sub

        ''' <summary>
        ''' Adds one placed field, shifted by the caption row the real page adds on Shown.
        ''' The controls themselves come from PreviewControls, which the merged preview also
        ''' uses, so a field drawn here and a field dropped onto a compiled page are the same
        ''' field.
        ''' </summary>
        Private Sub AddPlacement(placement As PlacedField)
            Dim shifted As New PlacedField() With {
                .Field = placement.Field,
                .Kind = placement.Kind,
                .Left = placement.Left,
                .Top = placement.Top + MaintenanceLayout.CaptionShift,
                .Required = placement.Required,
                .Width = placement.Width,
                .Nullable = placement.Nullable,
                .ShowTime = placement.ShowTime,
                .PlaceholderNumber = placement.PlaceholderNumber
            }

            For Each made In PreviewControls.Create(shifted, columnLengths, Font)
                Controls.Add(made)
            Next
        End Sub

        ''' <summary>
        ''' OK and Cancel where the page puts them, disabled. They are here because their absence
        ''' changes how much room the page looks like it has, and disabled because a preview that
        ''' appeared to offer a save would be a lie about what this window can do.
        ''' </summary>
        Private Sub AddActionButtons()
            Dim buttonTop = page.Height + MaintenanceLayout.CaptionShift - 46

            Controls.Add(New Button() With {
                .Text = "OK",
                .Enabled = False,
                .Location = New Point(page.Width - 270, buttonTop),
                .Size = New Size(120, 36)
            })
            Controls.Add(New Button() With {
                .Text = "Cancel",
                .Enabled = False,
                .Location = New Point(page.Width - 135, buttonTop),
                .Size = New Size(120, 36)
            })
        End Sub

        ''' <summary>
        ''' What was drawn, in figures. A preview that looks wrong is far easier to argue about
        ''' when it says how many controls it made and how many rows they sit on.
        ''' </summary>
        Private statusLabel As Label

        ''' <summary>The read-out, rebuilt whenever something on it changes.</summary>
        Private Sub RefreshStatus()
            If statusLabel Is Nothing Then Return

            Dim blanks = page.Fields.Where(Function(item) item.Kind = PlacedFieldKind.BlankLine).Count()

            statusLabel.Text = "  " & drawnControls.ToString() & " controls   " &
                               page.RowsDown.ToString() & " rows   " &
                               page.Width.ToString() & " x " & (page.Height + MaintenanceLayout.CaptionShift).ToString() &
                               If(blanks > 0, "   " & blanks.ToString() & " blank", String.Empty) &
                               If(page.TwoColumns, "   two columns", "   one column") &
                               If(hiddenByRole > 0, "   " & hiddenByRole.ToString() & " hidden by your role", String.Empty)
        End Sub

        Private drawnControls As Integer

        Private Sub AddStatusBar()
            drawnControls = Controls.Count
            Dim drawn = drawnControls
            Dim rows = page.RowsDown
            Dim blanks = page.Fields.Where(Function(item) item.Kind = PlacedFieldKind.BlankLine).Count()

            saveButton = New Button() With {
                .Name = "PreviewSavePng",
                .Text = "Save PNG",
                .Size = New Size(100, 24),
                .Location = New Point(ClientSize.Width - 112, ClientSize.Height - StatusBarHeight + 3)
            }
            AddHandler saveButton.Click, Sub(sender, e) SavePng()
            Controls.Add(saveButton)

            ' Dark text on the form background: the status bar stops short of this, so white
            ' here was white on light grey and could not be read.
            permissionsBox = New CheckBox() With {
                .Name = "PreviewApplyPermissions",
                .Text = "Apply my role's permissions",
                .AutoSize = True,
                .ForeColor = SystemColors.ControlText,
                .Location = New Point(ClientSize.Width - 300, ClientSize.Height - StatusBarHeight + 6)
            }
            AddHandler permissionsBox.CheckedChanged, Sub(sender, e) ApplyRolePermissions()
            Controls.Add(permissionsBox)

            ' The label stops short of the Save PNG button rather than running under it, so
            ' neither has to be lifted over the other.
            statusLabel = New Label() With {
                .Name = "PreviewStatus",
                .Location = New Point(0, ClientSize.Height - StatusBarHeight),
                .Size = New Size(ClientSize.Width - 310, StatusBarHeight),
                .TextAlign = ContentAlignment.MiddleLeft,
                .BackColor = SystemColors.ControlDark,
                .ForeColor = Color.White,
                .Text = "  " & drawn.ToString() & " controls   " &
                        rows.ToString() & " rows   " &
                        page.Width.ToString() & " x " & (page.Height + MaintenanceLayout.CaptionShift).ToString() &
                        If(blanks > 0, "   " & blanks.ToString() & " blank", String.Empty) &
                        If(page.TwoColumns, "   two columns", "   one column")
            }
            Controls.Add(statusLabel)

        End Sub

        ''' <summary>
        ''' Writes the preview to previews\&lt;page&gt;.png, so the layout can be looked at away from
        ''' the screen, kept beside a request, or put in front of somebody else.
        '''
        ''' Sized from the window rather than its client area, deliberately: DrawToBitmap on a Form
        ''' paints the title bar too, so a client-sized bitmap shifts everything down by the height
        ''' of that bar and silently clips the same amount off the bottom. That is how the status
        ''' bar came to be present, correct and missing from every saved image.
        ''' </summary>
        Private Sub SavePng()
            Dim folder = IO.Path.Combine(Environment.CurrentDirectory, "previews")
            Dim file = IO.Path.Combine(folder, subjectName & ".png")

            Try
                IO.Directory.CreateDirectory(folder)

                ' The button is not part of the page. Hidden for the capture and put back after,
                ' so the image shows the layout rather than the tool that saved it.
                saveButton.Visible = False
                Application.DoEvents()

                Using bitmap As New Bitmap(Width, Height)
                    DrawToBitmap(bitmap, New Rectangle(0, 0, bitmap.Width, bitmap.Height))
                    bitmap.Save(file, Imaging.ImageFormat.Png)
                End Using

                saveButton.Visible = True

                WideMessage.Show(Me,
                                 "LAYOUT PREVIEW SAVED." & Environment.NewLine & Environment.NewLine & file,
                                 "Save PNG")
            Catch ex As Exception
                saveButton.Visible = True
                WideMessage.Show(Me,
                                 "THE PREVIEW COULD NOT BE SAVED." & Environment.NewLine & Environment.NewLine & ex.Message,
                                 "Save PNG")
            End Try
        End Sub

        ''' <summary>
        ''' An outline where the page's own file will put something.
        '''
        ''' The generator reserves the room but cannot know what fills it - an employee page hosts
        ''' the two role grids, written by hand in the companion file. Without this the reserved
        ''' space reads as a mistake: a band of nothing between the last field and the buttons.
        ''' Dashed, and labelled with what is coming, so it reads as a placeholder rather than as a
        ''' control that failed to draw.
        ''' </summary>
        Private Sub AddReservedBand()
            If page.ReservedHeight <= 0 Then Return

            Dim band As New Panel() With {
                .Name = "PreviewReservedBand",
                .Location = New Point(page.ReservedLeft, page.ReservedTop + MaintenanceLayout.CaptionShift),
                .Size = New Size(page.ReservedWidth, page.ReservedHeight),
                .BackColor = Color.Transparent
            }

            AddHandler band.Paint,
                Sub(sender, e)
                    Using dashed As New Pen(SystemColors.ControlDark, 1.0F)
                        dashed.DashStyle = Drawing2D.DashStyle.Dash
                        e.Graphics.DrawRectangle(dashed, 0, 0, band.Width - 1, band.Height - 1)
                    End Using
                End Sub

            band.Controls.Add(New Label() With {
                .Dock = DockStyle.Fill,
                .TextAlign = ContentAlignment.MiddleCenter,
                .ForeColor = SystemColors.ControlDarkDark,
                .Text = page.ReservedCaption & Environment.NewLine & Environment.NewLine &
                        page.ReservedWidth.ToString() & " x " & page.ReservedHeight.ToString() &
                        "   -   use Real Page to see it"
            })

            Controls.Add(band)
        End Sub

        ''' <summary>
        ''' Applies this session's field permissions to the preview, or takes them off again.
        '''
        ''' Handed to the same DataAccess.ApplyControlUpdates every maintenance page uses, which
        ''' works because the preview names its controls to the same convention. One owner for what
        ''' a role may see; this window only asks the question.
        '''
        ''' Off by default. A layout is designed for everybody, and a preview that silently hid the
        ''' fields your own role cannot see would show a page nobody else gets.
        '''
        ''' A hidden field leaves its gap rather than closing it. Collapsing the row is FW_Base_U's
        ''' job and belongs to it - a second copy here would be the one that drifted - so the count
        ''' is reported instead.
        ''' </summary>
        Private Sub ApplyRolePermissions()
            If String.IsNullOrWhiteSpace(tableName) Then
                WideMessage.Show(Me,
                                 "THIS PREVIEW DOES NOT KNOW ITS TABLE, SO FIELD PERMISSIONS CANNOT BE LOOKED UP.",
                                 "Layout Preview")
                permissionsBox.Checked = False
                Return
            End If

            If Not permissionsBox.Checked Then
                ' Nothing is un-applied: a mask or a hidden control cannot be reliably undone, and
                ' a half-restored preview would be worse than an honest rebuild.
                WideMessage.Show(Me,
                                 "CLOSE AND REOPEN THE PREVIEW TO SEE EVERY FIELD AGAIN.",
                                 "Layout Preview")
                Return
            End If

            Dim before = VisibleFieldCount()
            DataAccess.ApplyControlUpdates(Me, subjectName, tableName, isNewRecord:=True)
            hiddenByRole = before - VisibleFieldCount()

            RefreshStatus()
        End Sub

        Private Function VisibleFieldCount() As Integer
            Return Controls.Cast(Of Control)().
                Where(Function(item) item.Visible AndAlso
                                     (item.Name.StartsWith("TextBox_", StringComparison.OrdinalIgnoreCase) OrElse
                                      item.Name.StartsWith("ComboBox_", StringComparison.OrdinalIgnoreCase) OrElse
                                      item.Name.StartsWith("CheckBox_", StringComparison.OrdinalIgnoreCase) OrElse
                                      item.Name.StartsWith("DateTimePicker_", StringComparison.OrdinalIgnoreCase))).
                Count()
        End Function

        Private Sub ShowFailure(detail As String)
            Controls.Clear()
            ClientSize = New Size(760, 420)

            Controls.Add(New TextBox() With {
                .Dock = DockStyle.Fill,
                .Multiline = True,
                .ReadOnly = True,
                .WordWrap = True,
                .ScrollBars = ScrollBars.Vertical,
                .Font = New Font("Consolas", 9.0F),
                .BackColor = Color.White,
                .Text = "THE LAYOUT PREVIEW COULD NOT BE DRAWN." & Environment.NewLine & Environment.NewLine & detail
            })
        End Sub

    End Class

    ''' <summary>
    ''' Stand-in values for a preview.
    '''
    ''' Made up on purpose rather than read from the table. A preview is about shape, a real row
    ''' costs a query per lookup to fill, and the first row of a table is somebody's actual record
    ''' - which is fine on screen for a moment and not fine saved into a PNG.
    ''' </summary>
    Public Module SampleValues

        ''' <summary>
        ''' Whether a field must never show its value, sample or otherwise.
        '''
        ''' Matched on the name because that is all a preview has. Anything that looks like a
        ''' secret is masked, and a false positive costs nothing - a masked sample value in a
        ''' preview is still the right width and the right shape.
        ''' </summary>
        Public Function IsSecret(fieldName As String) As Boolean
            If String.IsNullOrWhiteSpace(fieldName) Then Return False

            Dim name = fieldName.Trim()
            Dim markers = New String() {"Password", "Passwd", "Pwd", "Secret", "AuthToken", "Token", "ApiKey", "EmbeddedKey", "AuthID"}

            Return markers.Any(Function(marker) name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
        End Function

        ''' <summary>A plausible value for a text box, or dots for one that must not show.</summary>
        Public Function ForField(fieldName As String) As String
            If IsSecret(fieldName) Then Return "............"

            Dim name = If(fieldName, String.Empty).Trim()

            If Matches(name, "FirstName", "GivenName") Then Return "Alan"
            If Matches(name, "LastName", "Surname", "FamilyName") Then Return "Whitfield"
            If Matches(name, "UserName", "Login") Then Return "awhitfield"
            If Matches(name, "Email") Then Return "a.whitfield@example.com"
            If Matches(name, "Address1", "Address", "StreetAddress", "Street") Then Return "144 Pinecrest Avenue"
            If Matches(name, "Address2") Then Return "Suite 3"
            If Matches(name, "City") Then Return "Saraland"
            If Matches(name, "State", "StateCode", "StateAbbrev") Then Return "AL"
            If Matches(name, "Zip", "ZipCode", "PostalCode") Then Return "36571"
            If Matches(name, "Extension") Then Return "204"
            If name.IndexOf("Phone", StringComparison.OrdinalIgnoreCase) >= 0 Then Return "(251) 555-0142"

            ' Nothing recognised: say what the field is rather than inventing a fact about it. A
            ' box reading "Sample Warranty Code" is honest about being a preview, where a made-up
            ' code reads as data somebody might act on.
            Return "Sample " & DisplayNameFormatter.ToDisplayName(name, stripFrameworkPrefix:=False)
        End Function

        ''' <summary>The single item a preview combo shows, so the box is not blank.</summary>
        Public Function ForCombo(fieldName As String) As String
            Return "(" & DisplayNameFormatter.ToDisplayName(If(fieldName, String.Empty), stripFrameworkPrefix:=False) & " list)"
        End Function

        Private Function Matches(name As String, ParamArray candidates As String()) As Boolean
            Return candidates.Any(Function(candidate) String.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
        End Function

    End Module
End Namespace
