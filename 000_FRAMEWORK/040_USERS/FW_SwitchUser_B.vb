Option Strict On
Option Explicit On

Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' Finds the account an administrator wants to become, as a browse page.
    '''
    ''' It replaces SwitchUserDialog, whose one text box searched the user name and the two places
    ''' an email can live, and nothing else - "alan" matched every @saraland.org address and no
    ''' first or last name. This page browses FW_SwitchUser through QBE, so any field it shows can
    ''' be searched on, saved searches work, and picking one person out of several is the grid
    ''' rather than a list box.
    '''
    ''' **It only finds.** Checking that the caller may switch, starting the session and writing
    ''' the audit row belong to the menu's Switch User action and to SessionStarter, exactly as
    ''' they did with the dialog. The contract is the same too - ChosenUser, once the page closes
    ''' with OK - so the menu swapped one line.
    '''
    ''' Browse-only: no maintenance page, no create, no delete. "Switch to" is the page's Read
    ''' command rather than a button of its own, and double-clicking a row invokes that command -
    ''' the one everybody can see, rather than a second path nobody can.
    ''' </summary>
    Public Class FW_SwitchUser_B
        Inherits FW_Base_B

        ''' <summary>The administrator's own account, which is never offered: becoming yourself is not a switch.</summary>
        Private ReadOnly excludeUserId As Integer

        Private chosen As UserContext

        ''' <summary>The account chosen, once the page closes with OK.</summary>
        Public ReadOnly Property ChosenUser As UserContext
            Get
                Return chosen
            End Get
        End Property

        Public Sub New(user As UserContext, profile As AccessProfile, excludeUserId As Integer)
            MyBase.New(user, profile, "FW_SwitchUser")
            Me.excludeUserId = excludeUserId
        End Sub

        ''' <summary>
        ''' No rows until a search. There will be hundreds of people, and listing all of them is a
        ''' page nobody can read and rows nobody asked for - the administrator already knows whose
        ''' problem they are looking into.
        ''' </summary>
        Protected Overrides Function StartsEmptyOnInitialLoad() As Boolean
            Return True
        End Function

        ''' <summary>
        ''' The selector offers All Registrations here, because an administrator looking into
        ''' somebody's problem knows the person, not which company they are in - and the dialog
        ''' this page replaced searched every registration, so scoping to one would have narrowed
        ''' what an App Admin could already do.
        '''
        ''' Still only for an App Admin, and still only with View All Records granted on the table.
        ''' </summary>
        Protected Overrides Function AllowAllRegistrations() As Boolean
            Return True
        End Function

        ''' <summary>
        ''' Brings the snapshot up to date before every read, so a Find is always against the
        ''' people who exist now rather than the people who existed when the page opened.
        '''
        ''' FW_SwitchUser is a copy of dbo.FW_UserPeople, which is the single place that says who
        ''' a login belongs to. The refresh writes only what has changed, so a Find that finds
        ''' nothing new writes nothing at all.
        '''
        ''' A failure is swallowed deliberately. The page then lists the last snapshot, which is
        ''' the right answer for a snapshot: an administrator looking into somebody's problem is
        ''' better served by slightly old rows than by an empty page and an error.
        ''' </summary>
        Protected Overrides Sub PrepareBrowseSource()
            Try
                DataAccess.RefreshSwitchUserSnapshot()
            Catch
            End Try
        End Sub

        ''' <summary>
        ''' The page's SQL with its registration predicate already answered.
        '''
        ''' The column is not in the select list: it is the scope, the dropdown above the grid says
        ''' what it is, and a page that shows it puts an internal number in the grid and an
        ''' unanswerable field in QBE. But it has to appear somewhere in the SQL, because the
        ''' framework decides whether to scope a page by reading its text - and with All
        ''' Registrations chosen the framework substitutes nothing, which would leave a parameter
        ''' behind and fail.
        '''
        ''' So the page answers its own predicate: one registration becomes that id, and All
        ''' Registrations becomes the column compared with itself, which is every row. The
        ''' framework may then rewrite the same predicate to the same value, which changes nothing.
        '''
        ''' Done here rather than in Base_B deliberately. The text-matching rule works everywhere
        ''' else, and this is the only page that has to mean "all of them".
        ''' </summary>
        Protected Overrides Function GetActiveBaseSql() As String
            Dim sql = MyBase.GetActiveBaseSql()
            If String.IsNullOrWhiteSpace(sql) OrElse sql.IndexOf("@RegistrationID", StringComparison.OrdinalIgnoreCase) < 0 Then
                Return sql
            End If

            Dim registrationId As Integer = 0
            Dim replacement = If(TryGetActiveRegistrationId(registrationId) AndAlso registrationId > 0,
                                 registrationId.ToString(Globalization.CultureInfo.InvariantCulture),
                                 "s.[RegistrationID]")

            Return sql.Replace("@RegistrationID", replacement, StringComparison.OrdinalIgnoreCase)
        End Function

        ''' <summary>
        ''' Everybody but the administrator themselves. Applied in the database rather than by
        ''' hiding a row afterwards, so the count and the grid agree.
        ''' </summary>
        Protected Overrides Function GetBrowseUserScopePredicate() As String
            Return "UserId <> @UserID"
        End Function

        Protected Overrides Function GetBrowseUserId() As Integer
            Return excludeUserId
        End Function

        ''' <summary>
        ''' The Read button says what pressing it does. Whatever the registration calls Read -
        ''' "Read", "View" - describes looking at a record, and this one becomes a person.
        ''' </summary>
        Protected Overrides Function ResolveCrudCaption(action As AccessCapability, caption As String) As String
            If action = AccessCapability.Read Then Return "Switch To"

            Return MyBase.ResolveCrudCaption(action, caption)
        End Function

        ''' <summary>No maintenance page. The page is a picker, and there is nothing to edit here.</summary>
        Protected Overrides Function CreateMaintenancePage(recordId As Integer) As FW_Base_U
            Return Nothing
        End Function

        ''' <summary>Double-clicking somebody means switching to them, so the gesture invokes Read.</summary>
        Protected Overrides Function DoubleClickCommandButton() As Button
            Return readButton
        End Function

        ''' <summary>
        ''' "Switch to": takes the selected row as the answer and closes.
        '''
        ''' The Read command, because reading is all this page does to a record. It was the Update
        ''' command first, only so that Base_B's double-click - which invokes the Update button -
        ''' would work. That put an Update permission on a table nothing updates, which misdescribes
        ''' the page to whoever grants it, so the gesture was disabled instead.
        '''
        ''' The permission this page needs is therefore Read, and QBE to search with. Nothing else.
        '''
        ''' Double-click works again as of 2026-09-19, through DoubleClickCommandButton rather than
        ''' by borrowing a command this page does not want: the gesture now invokes Read, which is
        ''' "Switch To" here. The original objection was to the Update permission, never to the
        ''' gesture.
        '''
        ''' The selected row is identified by the PK alias, which this page's SQL aliases from
        ''' SwitchUserID - the snapshot row, not the login. PK is the row's identity to the
        ''' framework, so it is the table's own key; the login it stands for is read from it.
        ''' </summary>
        Protected Overrides Function HandleCustomReadAction() As Boolean
            Dim selected = GetSelectedRecordIdForCustomAction()
            If Not selected.HasValue OrElse selected.Value <= 0 Then
                MessageBox.Show(Me, "SELECT SOMEBODY FIRST.", "Switch User",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            Dim recordId = selected.Value

            Dim target = DataAccess.GetSwitchUserTarget(recordId)
            If target IsNot Nothing AndAlso target.UserId = excludeUserId Then
                MessageBox.Show(Me, "THAT IS YOUR OWN ACCOUNT.", "Switch User",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            If target Is Nothing Then
                ' The snapshot had a row the login no longer backs - deleted between the refresh
                ' and the click. Said plainly rather than closing with nothing chosen, which would
                ' read as the click having missed.
                MessageBox.Show(Me, "THAT ACCOUNT COULD NOT BE READ. REFRESH AND TRY AGAIN.", "Switch User",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return True
            End If

            chosen = target
            DialogResult = DialogResult.OK
            Close()
            Return True
        End Function
    End Class
End Namespace
