Option Strict On
Option Explicit On

Imports System.Collections.Generic

Namespace SDC.Framework

    ''' <summary>
    ''' Who soft-deleted a record and when, for the message shown to a user whose save found the
    ''' record already deleted.
    ''' </summary>
    ''' <summary>
    ''' What still depends on a role. Checked before a delete so the user is told what the delete
    ''' would take with it, and so a role a registration depends on cannot be removed at all.
    ''' </summary>
    Public NotInheritable Class RoleUsage
    Public Property RoleName As String = String.Empty
    Public Property UserCount As Integer
    Public Property RegistrationCount As Integer

    Public ReadOnly Property IsHeldByUsers As Boolean
        Get
            Return UserCount > 0
        End Get
    End Property

    Public ReadOnly Property IsRegistrationAdminRole As Boolean
        Get
            Return RegistrationCount > 0
        End Get
    End Property
    End Class
    Public Class SoftDeleteInfo
        Public Property DeletedByName As String = String.Empty
        Public Property DeletedOn As Date?

        ''' <summary>A sentence naming the person and time, degrading gracefully when either is missing.</summary>
        Public Function Describe() As String
            Dim who = If(String.IsNullOrWhiteSpace(DeletedByName), String.Empty, " by " & DeletedByName.Trim())
            Dim whenDeleted = If(DeletedOn.HasValue, " on " & DeletedOn.Value.ToString("d MMM yyyy HH:mm"), String.Empty)
            Return "This record was already deleted" & who & whenDeleted & "."
        End Function
    End Class

    ''' <summary>
    ''' Why a database connection failed. The three real causes need different fixes, so they must
    ''' not be reported as one vague "unavailable".
    ''' </summary>
    Public Enum DatabaseFailureKind
        None
        ServerUnreachable
        BadCredentials
        DatabaseUnavailable
        Other
    End Enum

    Public Class DatabaseConnectionStatus
        Public Property Kind As DatabaseFailureKind = DatabaseFailureKind.None
        Public Property Message As String = String.Empty

        Public ReadOnly Property Succeeded As Boolean
            Get
                Return Kind = DatabaseFailureKind.None
            End Get
        End Property

        ''' <summary>Whether changing the connection settings could plausibly fix this.</summary>
        Public ReadOnly Property SettingsMightFixIt As Boolean
            Get
                Return Kind = DatabaseFailureKind.BadCredentials OrElse
                       Kind = DatabaseFailureKind.DatabaseUnavailable
            End Get
        End Property

        Public Function Describe() As String
            Select Case Kind
                Case DatabaseFailureKind.ServerUnreachable
                    Return "The database server could not be reached."
                Case DatabaseFailureKind.BadCredentials
                    Return "The database server refused the sign-in it was given."
                Case DatabaseFailureKind.DatabaseUnavailable
                    Return "The server answered, but that database could not be opened."
                Case DatabaseFailureKind.Other
                    Return "The database returned an error."
                Case Else
                    Return String.Empty
            End Select
        End Function
    End Class

    ''' <summary>
    ''' The first administrator of a new registration, asked for just before it is saved.
    '''
    ''' A registration with no roles and nobody in it cannot be signed into, so creating one
    ''' without this leaves something nobody can reach.
    ''' </summary>
    Public Class RegistrationAdminRequest
        Public Property FirstName As String = String.Empty
        Public Property LastName As String = String.Empty
        Public Property UserName As String = String.Empty
        Public Property TemporaryPassword As String = String.Empty

        Public ReadOnly Property IsComplete As Boolean
            Get
                Return FirstName.Trim() <> String.Empty AndAlso
                       LastName.Trim() <> String.Empty AndAlso
                       UserName.Trim() <> String.Empty AndAlso
                       TemporaryPassword.Trim() <> String.Empty
            End Get
        End Property
    End Class

    Public Enum SaveResult
        Succeeded
        RecordChanged
        RecordDeleted
        ConcurrencyUnavailable
        Failed
    End Enum
    Public Class UserContext
        Public Property UserId As Integer
        Public Property Email As String
        Public Property FirstName As String
        Public Property LastName As String

        Public ReadOnly Property DisplayName As String
            Get
                Dim full = (Me.FirstName & " " & Me.LastName).Trim()
                If full = String.Empty Then
                    Return Me.Email
                End If

                Return full
            End Get
        End Property
    End Class

    Public Structure UserSessionVariables
        Public Property UserID As Integer
        Public Property RegistrationID As Integer
        Public Property RegistrationName As String
        Public Property Smarty_AuthID As String
        Public Property Smarty_AuthToken As String
        Public Property Smarty_EmbeddedKey As String
        Public Property Smarty_UseEmbeddedKey As Boolean
        Public Property RoleID As Integer
        Public Property CompanyAdminRoleID As Integer
        Public Property CompanyAdminUserID As Integer
        Public Property HDUserSupport As Integer
        Public Property HDApplicationSupport As Integer
        Public Property RoleName As String
        Public Property RoleType As String
        Public Property IsApplicationAdminRole As Boolean
        Public Property IsCompanyAdminRole As Boolean
        Public Property FirstName As String
        Public Property LastName As String
        Public Property FirstLast As String
        Public Property LastFirst As String
        Public Property CrudCreateCaption As String
        Public Property CrudReadCaption As String
        Public Property CrudUpdateCaption As String
        Public Property CrudDeleteCaption As String
        Public Property MaxRecordsNoQBE As Integer

        ''' <summary>
        ''' The cap once criteria have been entered, from FW_Registration.MaxRecordsWithQBE.
        '''
        ''' A separate setting because the two caps mean different things. Without criteria the cap
        ''' says "this is the top of a longer list"; with them it says "your search was not narrow
        ''' enough", and a handful there is infuriating to somebody who has just filtered.
        ''' </summary>
        Public Property MaxRecordsWithQBE As Integer

        ''' <summary>
        ''' Minutes between new-message checks, as this registration set it. Zero means it has set
        ''' nothing, and the menu uses its own default.
        ''' </summary>
        Public Property MessageRetrievalMinutes As Integer

        ''' <summary>
        ''' How this company writes a date and a time - the .NET patterns, resolved at login from
        ''' FW_Format_Date and FW_Format_Time through the registration's choice.
        '''
        ''' Held on the session rather than looked up where they are needed, for the reason every
        ''' other registration setting here is: a page with five date fields would otherwise ask
        ''' the same question five times. Read them through DisplayFormats, which supplies the
        ''' default when a registration has not chosen and when a stored pattern is unusable.
        ''' </summary>
        Public Property DateFormat As String
        Public Property TimeFormat As String

        ''' <summary>
        ''' Whether this company lets people edit their own record. Off hides the My Profile tile
        ''' outright rather than showing one that refuses - a button that is there and does
        ''' nothing is worse than no button.
        ''' </summary>
        Public Property AllowUpdateMyProfile As Boolean

        ''' <summary>This registration's Home Region picture, a file name in assets\images.</summary>
        Public Property HomeGraphic As String

        ''' <summary>The registration's IANA time zone id, or empty. Applied to dates through TimeZones.</summary>
        Public Property TimeZoneName As String
    End Structure

    Public Module SessionState
        Private currentSession As UserSessionVariables?
        Private seenUiHints As HashSet(Of String) = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        Public ReadOnly Property IsActive As Boolean
            Get
                Return currentSession.HasValue
            End Get
        End Property

        Public ReadOnly Property Current As UserSessionVariables?
            Get
                Return currentSession
            End Get
        End Property

        ''' <summary>
        ''' Whether the session is running under an Application Admin role.
        '''
        ''' One owner, because the test was written out longhand in six places - Base_B, Base_U
        ''' twice, the main menu, Entity_U and the help desk support browse - and a seventh copy is
        ''' how the six became six. Base_B's IsAppAdminSession is Protected, so a form that does not
        ''' inherit it, such as either dashboard, could not reach the answer at all.
        '''
        ''' No session is not an admin. That is the safe reading and the only one available.
        ''' </summary>
        Public ReadOnly Property IsApplicationAdmin As Boolean
            Get
                Return currentSession.HasValue AndAlso currentSession.Value.IsApplicationAdminRole
            End Get
        End Property

        ''' <summary>
        ''' Who is actually doing this: the administrator while viewing as somebody else, and the
        ''' signed-in user otherwise.
        '''
        ''' Every CreatedBy, UpdatedBy, DeletedBy and audit row takes this rather than UserID. An
        ''' administrator viewing as Alan and saving a record made that change, and a trail saying
        ''' Alan did it is a trail that cannot be trusted about anybody. There is no path that
        ''' writes the viewed user's name onto a change the administrator made - it is not a
        ''' setting, and it is not offered.
        '''
        ''' **Not for a UserID that is a row's key.** A saved layout, a saved search, a page zoom,
        ''' a UI hint, a mailbox and a Help Desk reporter all say whose row it is, not who changed
        ''' it, and while viewing as Alan those must stay Alan's or the administrator would be
        ''' handed their own layouts on his screen. Where one method writes both - the key and the
        ''' stamp - they take different answers.
        ''' </summary>
        Public ReadOnly Property ActingUserID As Integer
            Get
                If SwitchedUser.IsActive AndAlso SwitchedUser.Original IsNot Nothing Then
                    Return SwitchedUser.Original.UserId
                End If

                Return If(currentSession.HasValue, currentSession.Value.UserID, 0)
            End Get
        End Property

        ''' <summary>
        ''' Whose row this is: the signed-in user, or the user being viewed while an administrator
        ''' is switched. The key for a layout, a saved search, a mailbox or a Help Desk reporter.
        ''' Never a CreatedBy, UpdatedBy or DeletedBy - those take ActingUserID. Zero with no
        ''' session.
        '''
        ''' One owner, because three Help Desk pages each wrote this as a private CurrentUserId,
        ''' under the same name two other files used for ActingUserID, and the two meanings were
        ''' mixed at DeleteIssue: a ticket deleted while switched was stamped as the viewed user's.
        '''
        ''' Not called UserID: a module's members are visible unqualified, and an inferred
        ''' `For Each userId In ...` then binds to the property instead of declaring a variable.
        ''' </summary>
        Public ReadOnly Property SessionUserID As Integer
            Get
                Return If(currentSession.HasValue, currentSession.Value.UserID, 0)
            End Get
        End Property

        ''' <summary>
        ''' The session's user as a UserContext, for a page opened without one. An empty
        ''' UserContext with no session. Seven copies of this lived on pages until 2026-09-25.
        ''' </summary>
        Public Function CurrentUser() As UserContext
            If Not currentSession.HasValue Then
                Return New UserContext With {.UserId = 0, .Email = String.Empty, .FirstName = String.Empty, .LastName = String.Empty}
            End If

            Dim session = currentSession.Value
            Return New UserContext With {
                .UserId = session.UserID,
                .Email = String.Empty,
                .FirstName = session.FirstName,
                .LastName = session.LastName
            }
        End Function

        ''' <summary>The same, for the Company Admin role.</summary>
        Public ReadOnly Property IsCompanyAdmin As Boolean
            Get
                Return currentSession.HasValue AndAlso currentSession.Value.IsCompanyAdminRole
            End Get
        End Property

        Public Sub StartSession(user As UserContext,
                                Optional registrationId As Integer = 1,
                                Optional registrationName As String = "",
                                Optional smartyAuthId As String = "",
                                Optional smartyAuthToken As String = "",
                                Optional smartyEmbeddedKey As String = "",
                                Optional smartyUseEmbeddedKey As Boolean = False,
                                Optional roleId As Integer = 0,
                                Optional companyAdminRoleId As Integer = 0,
                                Optional companyAdminUserId As Integer = 0,
                                Optional hdUserSupport As Integer = 0,
                                Optional hdApplicationSupport As Integer = 0,
                                Optional roleName As String = "",
                                Optional roleType As String = "",
                                Optional isApplicationAdminRole As Boolean = False,
                                Optional isCompanyAdminRole As Boolean = False,
                                Optional maxRecordsNoQBE As Integer = 10,
                                Optional dateFormat As String = "",
                                Optional timeFormat As String = "",
                                Optional allowUpdateMyProfile As Boolean = False,
                                Optional homeGraphic As String = "",
                                Optional timeZoneName As String = "",
                                Optional messageRetrievalMinutes As Integer = 0,
                                Optional maxRecordsWithQBE As Integer = 200)
            If user Is Nothing Then
                ClearSession()
                Return
            End If

            Dim firstNameValue = If(user.FirstName, String.Empty).Trim()
            Dim lastNameValue = If(user.LastName, String.Empty).Trim()

            Dim firstLastValue = (firstNameValue & " " & lastNameValue).Trim()
            Dim lastFirstValue As String

            If firstNameValue <> String.Empty AndAlso lastNameValue <> String.Empty Then
                lastFirstValue = lastNameValue & ", " & firstNameValue
            Else
                lastFirstValue = (lastNameValue & " " & firstNameValue).Trim()
            End If

            currentSession = New UserSessionVariables With {
                .UserID = user.UserId,
                .RegistrationID = registrationId,
                .RegistrationName = If(registrationName, String.Empty).Trim(),
                .Smarty_AuthID = If(smartyAuthId, String.Empty).Trim(),
                .Smarty_AuthToken = If(smartyAuthToken, String.Empty).Trim(),
                .Smarty_EmbeddedKey = If(smartyEmbeddedKey, String.Empty).Trim(),
                .Smarty_UseEmbeddedKey = smartyUseEmbeddedKey,
                .RoleID = roleId,
                .CompanyAdminRoleID = companyAdminRoleId,
                .CompanyAdminUserID = companyAdminUserId,
                .HDUserSupport = hdUserSupport,
                .HDApplicationSupport = hdApplicationSupport,
                .RoleName = If(roleName, String.Empty).Trim(),
                .RoleType = If(roleType, String.Empty).Trim(),
                .IsApplicationAdminRole = isApplicationAdminRole,
                .IsCompanyAdminRole = isCompanyAdminRole,
                .FirstName = firstNameValue,
                .LastName = lastNameValue,
                .FirstLast = firstLastValue,
                .LastFirst = lastFirstValue,
                .CrudCreateCaption = "New",
                .CrudReadCaption = "Read",
                .CrudUpdateCaption = "Modify",
                .CrudDeleteCaption = "Delete",
                .MaxRecordsNoQBE = If(maxRecordsNoQBE > 0, maxRecordsNoQBE, DataAccess.DefaultRowsNoSearch),
                .MaxRecordsWithQBE = If(maxRecordsWithQBE > 0, maxRecordsWithQBE, DataAccess.DefaultRowsWithSearch),
                .MessageRetrievalMinutes = If(messageRetrievalMinutes > 0, messageRetrievalMinutes, 0),
                .DateFormat = If(dateFormat, String.Empty).Trim(),
                .TimeFormat = If(timeFormat, String.Empty).Trim(),
                .AllowUpdateMyProfile = allowUpdateMyProfile,
                .HomeGraphic = If(homeGraphic, String.Empty).Trim(),
                .TimeZoneName = If(timeZoneName, String.Empty).Trim()
            }

            seenUiHints = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            If registrationId > 0 AndAlso user.UserId > 0 Then
                Dim loadedHintKeys = DataAccess.GetUserUiHintKeys(registrationId, user.UserId)
                For Each hintKey In loadedHintKeys
                    Dim normalized = NormalizeHintKey(hintKey)
                    If normalized <> String.Empty Then
                        seenUiHints.Add(normalized)
                    End If
                Next
            End If
        End Sub

        Public Function HasSeenUiHint(hintKey As String) As Boolean
            Dim normalized = NormalizeHintKey(hintKey)
            If normalized = String.Empty Then
                Return True
            End If

            Return seenUiHints.Contains(normalized)
        End Function

        Public Sub MarkUiHintSeen(hintKey As String)
            If Not currentSession.HasValue Then
                Return
            End If

            Dim normalized = NormalizeHintKey(hintKey)
            If normalized = String.Empty Then
                Return
            End If

            If seenUiHints.Contains(normalized) Then
                Return
            End If

            Dim session = currentSession.Value
            If session.RegistrationID <= 0 OrElse session.UserID <= 0 Then
                seenUiHints.Add(normalized)
                Return
            End If

            ' Whose hint it is, then who marked it seen. The same id answered both until
            ' 2026-09-17: a hint dismissed by an administrator viewing as somebody else is still
            ' that person's hint, and the administrator is still the one who dismissed it.
            DataAccess.MarkUserUiHintSeen(session.RegistrationID, session.UserID, normalized, ActingUserID)
            seenUiHints.Add(normalized)
        End Sub

        Private Function NormalizeHintKey(value As String) As String
            Return If(value, String.Empty).Trim()
        End Function

        Public Sub UpdateCrudCaptions(createCaption As String,
                                      readCaption As String,
                                      updateCaption As String,
                                      deleteCaption As String)
            If Not currentSession.HasValue Then
                Return
            End If

            Dim session = currentSession.Value
            session.CrudCreateCaption = NormalizeCrudCaption(createCaption, "New")
            session.CrudReadCaption = NormalizeCrudCaption(readCaption, "Read")
            session.CrudUpdateCaption = NormalizeCrudCaption(updateCaption, "Modify")
            session.CrudDeleteCaption = NormalizeCrudCaption(deleteCaption, "Delete")
            currentSession = session
        End Sub

        Public Sub UpdateHelpDeskRouting(userSupportId As Integer, applicationSupportId As Integer)
            If Not currentSession.HasValue Then Return
            Dim session = currentSession.Value
            session.HDUserSupport = userSupportId
            session.HDApplicationSupport = applicationSupportId
            currentSession = session
        End Sub

        ''' <summary>
        ''' The row caps, after somebody has changed them on the Registration page.
        '''
        ''' The session reads them once at sign-in, so without this a saved change does nothing
        ''' until the next login - which is how it behaved the first time the fields went on the
        ''' page: the database said 25 and the grid went on showing 11, with nothing to say why.
        '''
        ''' Null keeps the framework default rather than becoming zero, which FW_Base_B.RefreshGrid
        ''' reads as no cap at all. Same rule as the column, and it has to be the same rule here or
        ''' saving an empty box would remove the limit for the rest of the session.
        '''
        ''' Only for the registration the session belongs to. An App Admin editing another
        ''' registration's caps changes that registration, not their own view.
        ''' </summary>
        Public Sub UpdateRowLimits(registrationId As Integer, noQbe As Integer?, withQbe As Integer?)
            If Not currentSession.HasValue Then Return

            Dim session = currentSession.Value
            If registrationId <> session.RegistrationID Then Return

            session.MaxRecordsNoQBE = If(noQbe.HasValue AndAlso noQbe.Value > 0, noQbe.Value, DataAccess.DefaultRowsNoSearch)
            session.MaxRecordsWithQBE = If(withQbe.HasValue AndAlso withQbe.Value > 0, withQbe.Value, DataAccess.DefaultRowsWithSearch)
            currentSession = session
        End Sub

        ''' <summary>
        ''' The registration the user is currently looking at, which is the session's own unless an
        ''' App Admin has chosen another in a browse page's selector.
        '''
        ''' Separate from RegistrationID, which decides permissions and must not follow a combo.
        ''' This only defaults a new record, so one created while looking at Saraland belongs to
        ''' Saraland rather than to whoever created it.
        ''' </summary>
        Private activeWorkingRegistration As Integer

        Public Sub SetWorkingRegistration(registrationId As Integer)
            activeWorkingRegistration = Math.Max(0, registrationId)
        End Sub

        Public Function WorkingRegistrationID() As Integer
            If activeWorkingRegistration > 0 Then Return activeWorkingRegistration
            If currentSession.HasValue Then Return currentSession.Value.RegistrationID
            Return 0
        End Function

        ''' <summary>
        ''' Overrides the time zone for this session only. Nothing is stored - signing in again
        ''' returns to the employee's zone, or the registration's.
        ''' </summary>
        Public Sub OverrideTimeZone(ianaId As String)
            If Not currentSession.HasValue Then Return
            Dim session = currentSession.Value
            session.TimeZoneName = If(ianaId, String.Empty).Trim()
            currentSession = session
        End Sub

        Private Function NormalizeCrudCaption(value As String, fallbackValue As String) As String
            Dim trimmed = If(value, String.Empty).Trim()
            If trimmed = String.Empty Then
                Return fallbackValue
            End If

            Return trimmed
        End Function

        Public Sub ClearSession()
            currentSession = Nothing
            seenUiHints = New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        End Sub
    End Module


    Public Class UserAdminRecord
        Public Property UserID As Integer
        Public Property RegistrationID As Integer
        Public Property FirstName As String
        Public Property LastName As String
        Public Property FirstLast As String
        Public Property LastFirst As String
        Public Property Email As String
        Public Property Phone As String
        Public Property Address1 As String
        Public Property Address2 As String
        Public Property City As String
        Public Property State As String
        Public Property Zip As String
        Public Property IsActive As Boolean
        Public Property SuperAdmin As Boolean

        ''' <summary>
        ''' The user this user reports to - another row in FW_Users. Zero means none recorded, and
        ''' is written to the column as NULL: the column carries a foreign key, so it must point at
        ''' a real user or at nothing.
        '''
        ''' Named against the convention deliberately. See sql/071.
        ''' </summary>
        Public Property AssignedManagerID As Integer

        Public Property RowVersion As Byte()
    End Class

    Public Class RegistrationRecord
        Public Property ID As Integer
        Public Property RegName As String
        Public Property Smarty_AuthID As String
        Public Property Smarty_AuthToken As String
        Public Property Smarty_EmbeddedKey As String
        Public Property Smarty_UseEmbeddedKey As Boolean
        Public Property RegistrationTypeID As Integer
        Public Property Address1 As String
        Public Property Address2 As String
        Public Property City As String
        Public Property State As String
        Public Property Zip As String
        Public Property MainFax As String
        Public Property MainPhone As String
        Public Property MainEMail As String
        Public Property WebLandingPage As String

        ''' <summary>
        ''' How many rows a browse page returns without criteria, and with them.
        '''
        ''' **Nullable because the columns are.** The Registration page requires both since
        ''' 2026-09-26 and a new registration starts at DataAccess.DefaultRowsNoSearch and
        ''' DefaultRowsWithSearch, but a row saved before then can still be null, and the reads fall
        ''' back to those same two constants. A non-nullable property would turn that null into a
        ''' stored zero - and zero is read by FW_Base_B.RefreshGrid as "no cap", which is the
        ''' condition that made the employee page unopenable on 2026-09-20 with ten thousand rows.
        '''
        ''' UserSessionVariables holds the same two as plain Integers on purpose: by the time a
        ''' session exists the default has been applied and there is nothing left to say.
        ''' </summary>
        Public Property MaxRecordsNoQBE As Integer?
        Public Property MaxRecordsWithQBE As Integer?

        Public Property AllowMultipleRoles As Boolean
        Public Property AllowPasswordChangeAtLogin As Boolean
        Public Property AllowUpdateMyProfile As Boolean
        Public Property AllowUpdateMyProfileEmail As Boolean

        ''' <summary>File name in assets\images for the main menu's Home Region. Never a path.</summary>
        Public Property HomeGraphic As String = String.Empty

        ''' <summary>
        ''' When the registration's licence runs out. Null means no expiry has been set; the login
        ''' licence check reads a null as no limit.
        ''' </summary>
        Public Property LicenseExpiration As Date?

        ''' <summary>
        ''' The term the expiry was set from, and the day that term was applied.
        '''
        ''' Intent, not truth. LicenseExpiration is what the licence check reads; these two only
        ''' explain how it got there, and the page shows Custom whenever re-applying the term
        ''' to the start no longer produces the expiry.
        ''' </summary>
        Public Property LicenseTermID As Integer
        Public Property LicenseStart As Date?

        Public Property TwoFactorAuthentication As Boolean

        ''' <summary>
        ''' How this company writes dates and times. Zero means it has not chosen, and the
        ''' framework default applies - which is why these are plain Integers with a zero meaning
        ''' rather than Nullable: the page's combo has a "use the default" row at value 0, and one
        ''' spelling of "not chosen" is easier to keep right than two.
        ''' </summary>
        Public Property FormatDateID As Integer
        Public Property FormatTimeID As Integer
        Public Property TimeZoneID As Integer

        ''' <summary>The IANA id from FW_TimeZones, for TimeZoneInfo. Read only - never saved back.</summary>
        Public Property TimeZoneName As String = String.Empty

        ''' <summary>
        ''' The role a new employee is given when their record is created, or zero for none.
        '''
        ''' Zero is not "no access by mistake" - it is a company that has chosen to assign roles
        ''' by hand, which is what every registration did before this existed.
        ''' </summary>

        Public Property HDUserSupport As Integer
        Public Property HDApplicationSupport As Integer

        ''' <summary>
        ''' How often, in minutes, the main menu asks whether mail has arrived.
        '''
        ''' Nullable because the column is: a registration that has never chosen one gets the
        ''' five minutes the timer used to have written into it. The check costs a query per
        ''' interval per signed-in user, and the right number depends on the site - a busy support
        ''' desk wants a minute, a two-person office does not want the traffic.
        ''' </summary>
        Public Property MessageRetrievalFrequency As Integer?

        Public Property IsActive As Boolean
        Public Property RowVersion As Byte()
    End Class

    Public Class UserRoleOption
        Public Property RoleID As Integer
        Public Property RoleName As String
        Public Property DisplayOrder As Integer
        Public Property RoleType As String
        Public Property IsApplicationAdmin As Boolean
        Public Property IsCompanyAdmin As Boolean

        Public Overrides Function ToString() As String
            If String.IsNullOrWhiteSpace(RoleType) Then
                Return RoleName
            End If

            Return RoleName & " (" & RoleType & ")"
        End Function
    End Class

    Public Enum QbeFieldKind
        TextField
        NumericField
        BooleanField
        DateField
    End Enum

    Public Enum QbeComparisonOperator
        EqualsTo
        NotEquals
        Contains
        StartsWith
        EndsWith
        GreaterThan
        GreaterThanOrEqual
        LessThan
        LessThanOrEqual

        ''' <summary>
        ''' Two dates, and the only operator that needs a second value.
        '''
        ''' It never reaches a filter. The search row expands it into the two comparisons it
        ''' already means - on or after the first day, on or before the last - and nothing below
        ''' the row learns a new word. Added at the end because a saved search stores the name,
        ''' not the number, and a name is only stable while nothing is inserted above it.
        ''' </summary>
        Between
    End Enum

    Public Class QbeFieldDefinition
        Public Property FieldName As String
        Public Property DisplayName As String
        Public Property FieldKind As QbeFieldKind
    End Class

    Public Class QbeFilterCriteria
        Public Property FieldName As String
        Public Property DisplayName As String
        Public Property FieldKind As QbeFieldKind
        Public Property ComparisonOperator As QbeComparisonOperator
        Public Property FilterValue As String
    End Class


    Public Class SavedQbeRecord
        Public Property SavedQbeID As Integer
        Public Property RegistrationID As Integer
        Public Property UserID As Integer
        Public Property QbeName As String
        Public Property IsCompanyWide As Boolean
        Public Property TableContext As String
        Public Property QbeData As String
    End Class
End Namespace
