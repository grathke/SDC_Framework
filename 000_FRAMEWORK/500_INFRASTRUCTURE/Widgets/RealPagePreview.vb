Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Linq
Imports System.Reflection
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Opens a maintenance page that is already compiled into the application, to be looked at
    ''' rather than used.
    '''
    ''' This is the one preview that is complete, because it is not a preview of the page - it is
    ''' the page. Everything the layout preview cannot know comes with it: the role grids under an
    ''' employee's fields, the Zip Coder button, the Smarty lookup, whatever else a companion file
    ''' adds in OnFieldsBuilt. It costs a database connection and it cannot show an unsaved layout,
    ''' which is why both previews exist rather than one.
    '''
    ''' Opened modal, inside a ReadOnlyPreview scope. Modal on purpose: the latch stops this thread
    ''' writing, and a modeless preview would let a real page be opened behind it and silently
    ''' refuse that page's save.
    ''' </summary>
    Public Module RealPagePreview

        ''' <summary>Every maintenance page compiled into this application, by class name.</summary>
        Public Function AvailablePages() As List(Of String)
            Return GetType(FW_Base_U).Assembly.
                GetTypes().
                Where(Function(candidate) candidate.IsClass AndAlso
                                          Not candidate.IsAbstract AndAlso
                                          GetType(FW_Base_U).IsAssignableFrom(candidate) AndAlso
                                          candidate IsNot GetType(FW_Base_U)).
                Select(Function(candidate) candidate.Name).
                OrderBy(Function(name) name, StringComparer.OrdinalIgnoreCase).
                ToList()
        End Function

        ''' <summary>
        ''' Shows the named page read-only, or says why it could not be shown.
        '''
        ''' Constructor arguments are supplied by type rather than by name, because every page has
        ''' its own signature - New(id, user, profile), New(registrationId, createNew),
        ''' New(issueId, registrationId, page). A key of zero opens the page as a new record, which
        ''' also settles what happens on a table with no rows in it.
        ''' </summary>
        ''' <summary>The compiled page of that name, or nothing when it is not built yet.</summary>
        Friend Function FindPageType(pageName As String) As Type
            If String.IsNullOrWhiteSpace(pageName) Then Return Nothing

            Return GetType(FW_Base_U).Assembly.
                GetTypes().
                FirstOrDefault(Function(candidate) String.Equals(candidate.Name, pageName.Trim(), StringComparison.OrdinalIgnoreCase) AndAlso
                                                   GetType(FW_Base_U).IsAssignableFrom(candidate))
        End Function

        Public Sub Show(owner As IWin32Window, pageName As String, user As UserContext)
            Dim pageType = FindPageType(pageName)

            If pageType Is Nothing Then
                WideMessage.Show(owner,
                                 (pageName & " IS NOT A COMPILED MAINTENANCE PAGE." & Environment.NewLine & Environment.NewLine &
                                  "Only a page that inherits FW_Base_U and is built into this application can be opened. " &
                                  "A page that has been generated but not yet compiled is not one of them."),
                                 "Real Page Preview",
                                 MessageBoxIcon.Information)
                Return
            End If

            Dim page As Form = Nothing

            Try
                page = BuildPage(pageType, user)
            Catch ex As Exception
                ' Never an empty window with no reason. The inner exception is the useful one when
                ' a constructor threw - the reflection wrapper says nothing worth reading.
                Dim detail = If(TypeOf ex Is TargetInvocationException AndAlso ex.InnerException IsNot Nothing,
                                ex.InnerException.ToString(),
                                ex.ToString())
                WideMessage.Show(owner,
                                 (pageName & " COULD NOT BE OPENED." & Environment.NewLine & Environment.NewLine & detail),
                                 "Real Page Preview",
                                 MessageBoxIcon.Error)
                Return
            End Try

            Try
                page.Text = page.Text & "  -  READ-ONLY PREVIEW"

                ' Centred on the screen rather than on the field picker that opened it.
                page.StartPosition = FormStartPosition.CenterScreen

                ' The latch, not the buttons. OK is deliberately left working: pressed, it refuses
                ' and says why, which teaches what this window is. A greyed-out button explains
                ' nothing - and disabling it would protect nothing either, because four of the
                ' seven writes a page can make need no button at all.
                Using ReadOnlyPreview.Begin()
                    page.ShowDialog(owner)
                End Using
            Finally
                page.Dispose()
            End Try
        End Sub

        ''' <summary>
        ''' Constructs a page, filling each constructor argument from its type. The shortest
        ''' constructor wins: a page with an optional-argument overload is opened the simple way.
        ''' </summary>
        Friend Function BuildPage(pageType As Type, user As UserContext) As Form
            Dim constructors = pageType.GetConstructors().
                OrderBy(Function(candidate) candidate.GetParameters().Length).
                ToList()

            If constructors.Count = 0 Then
                Throw New InvalidOperationException(pageType.Name & " has no public constructor.")
            End If

            Dim chosen = constructors.First()
            Dim arguments = chosen.GetParameters().
                Select(Function(parameter) ArgumentFor(parameter, user)).
                ToArray()

            Return DirectCast(chosen.Invoke(arguments), Form)
        End Function

        Private Function ArgumentFor(parameter As ParameterInfo, user As UserContext) As Object
            If parameter.ParameterType Is GetType(UserContext) Then Return user
            If parameter.ParameterType Is GetType(String) Then Return String.Empty
            If parameter.ParameterType Is GetType(Integer) Then Return 0
            If parameter.ParameterType Is GetType(Boolean) Then Return False
            Return Nothing
        End Function

    End Module
End Namespace
