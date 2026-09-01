Option Strict On
Option Explicit On

Imports System.Collections.Generic

Namespace HelloWorld

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
        Public Property BusinessRuleType As String
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

        Public Sub StartSession(user As UserContext,
                                Optional registrationId As Integer = 1,
                                Optional registrationName As String = "",
                                Optional smartyAuthId As String = "",
                                Optional smartyAuthToken As String = "",
                                Optional smartyEmbeddedKey As String = "",
                                Optional smartyUseEmbeddedKey As Boolean = False,
                                Optional businessRuleType As String = "",
                                Optional roleId As Integer = 0,
                                Optional companyAdminRoleId As Integer = 0,
                                Optional companyAdminUserId As Integer = 0,
                                Optional hdUserSupport As Integer = 0,
                                Optional hdApplicationSupport As Integer = 0,
                                Optional roleName As String = "",
                                Optional roleType As String = "",
                                Optional isApplicationAdminRole As Boolean = False,
                                Optional isCompanyAdminRole As Boolean = False,
                                Optional maxRecordsNoQBE As Integer = 10)
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
                .BusinessRuleType = NormalizeBusinessRuleType(businessRuleType),
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
                .MaxRecordsNoQBE = If(maxRecordsNoQBE > 0, maxRecordsNoQBE, 10)
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

            DataAccess.MarkUserUiHintSeen(session.RegistrationID, session.UserID, normalized, session.UserID)
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

    Public Class EntityRecord
        Public Property ID As Integer
        Public Property RegistrationID As Integer
        Public Property AssignedManagerID As Integer
        Public Property GenderID As Integer
        Public Property FirstName As String
        Public Property MiddleName As String
        Public Property LastName As String
        Public Property FirstLast As String
        Public Property LastFirst As String
        Public Property EMail1 As String
        Public Property Phone1 As String
        Public Property IsActive As Boolean
        Public Property RowVersion As Byte()
    End Class

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
        Public Property RowVersion As Byte()
    End Class

    Public Class RegistrationRecord
        Public Property ID As Integer
        Public Property RegName As String
        Public Property Smarty_AuthID As String
        Public Property Smarty_AuthToken As String
        Public Property Smarty_EmbeddedKey As String
        Public Property Smarty_UseEmbeddedKey As Boolean
        Public Property BusinessRuleType As String
        Public Property RegTypeId As Integer
        Public Property Address1 As String
        Public Property Address2 As String
        Public Property City As String
        Public Property State As String
        Public Property Zip As String
        Public Property MainFax As String
        Public Property MainPhone As String
        Public Property MainEMail As String
        Public Property WebLandingPage As String
        Public Property DisplayDashboardOnStartUp As Boolean
        Public Property AllowMessaging As Boolean
        Public Property AllowMultipleRoles As Boolean
        Public Property AllowPasswordChangeAtLogin As Boolean
        Public Property AllowUpdateMyProfile As Boolean
        Public Property AllowUpdateMyProfileEmail As Boolean
        Public Property Ribbonbar_InvisibleIcons As Boolean
        Public Property Use2FA As Boolean
        Public Property HDUserSupport As Integer
        Public Property HDApplicationSupport As Integer
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

    Public Enum EntityEditMode
        CreateMode
        ReadMode
        UpdateMode
        DeleteMode
    End Enum

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
