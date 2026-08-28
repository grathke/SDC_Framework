Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic

Namespace HelloWorld
    <Flags>
    Public Enum AccessCapability
        None = 0
        Read = 1
        Create = 2
        Update = 4
        Delete = 8
        UseQbe = 16
        Execute = 32
        ImportData = 64
        ViewAllRecords = 128
        ViewOnlyMyRecords = 256
        ExpandQbe = 512
    End Enum

    Public Class AccessProfile
        Private ReadOnly tablePermissions As Dictionary(Of String, AccessCapability)

        Public Property RoleLevel As Integer
        Public Property RoleName As String

        Public Sub New()
            tablePermissions = New Dictionary(Of String, AccessCapability)(StringComparer.OrdinalIgnoreCase)
            RoleLevel = 0
            RoleName = "Unassigned"
        End Sub

        Public Sub SetRole(level As Integer, name As String)
            RoleLevel = level
            RoleName = If(String.IsNullOrWhiteSpace(name), "Unassigned", name.Trim())
        End Sub

        Public Sub SetTablePermissions(tableName As String, capabilities As AccessCapability)
            If String.IsNullOrWhiteSpace(tableName) Then
                Return
            End If

            tablePermissions(NormalizeTableKey(tableName)) = capabilities
        End Sub

        Public Sub SetTablePermissions(entry As RoleTableAccessEntry)
            If entry Is Nothing Then
                Return
            End If

            SetTablePermissions(entry.TableName, entry.ToCapabilities())
        End Sub

        Public Function GetCapabilities(tableName As String) As AccessCapability
            If String.IsNullOrWhiteSpace(tableName) Then
                Return AccessCapability.None
            End If

            Dim capabilities As AccessCapability = AccessCapability.None
            If tablePermissions.TryGetValue(NormalizeTableKey(tableName), capabilities) Then
                Return capabilities
            End If

            Return AccessCapability.None
        End Function

        Public Function Can(tableName As String, required As AccessCapability) As Boolean
            If required = AccessCapability.None Then
                Return True
            End If

            Dim assigned = GetCapabilities(tableName)
            Return (assigned And required) = required
        End Function

        Private Shared Function NormalizeTableKey(tableName As String) As String
            Dim key = tableName.Trim().ToUpperInvariant()

            If key.StartsWith("DBO.", StringComparison.Ordinal) Then
                key = key.Substring(4)
            End If

            If key.StartsWith("FW_", StringComparison.Ordinal) OrElse key.StartsWith("AS_", StringComparison.Ordinal) Then
                key = key.Substring(3)
            End If

            key = key.Replace("_", String.Empty)
            key = key.Replace(" ", String.Empty)
            Return key
        End Function
    End Class

    Public Class RoleTableAccessEntry
        Public Property TableName As String
        Public Property CanCreate As Boolean
        Public Property CanReadOnly As Boolean
        Public Property CanUpdate As Boolean
        Public Property CanDelete As Boolean
        Public Property CanImport As Boolean
        Public Property CanViewAllRecords As Boolean
        Public Property CanViewOnlyMy As Boolean
        Public Property CanUseQbe As Boolean
        Public Property CanExpandQbe As Boolean

        Public Function ToCapabilities() As AccessCapability
            Dim capabilities As AccessCapability = AccessCapability.None

            If CanReadOnly Then
                capabilities = capabilities Or AccessCapability.Read
            End If
            If CanCreate Then
                capabilities = capabilities Or AccessCapability.Create
            End If
            If CanUpdate Then
                capabilities = capabilities Or AccessCapability.Update
            End If
            If CanDelete Then
                capabilities = capabilities Or AccessCapability.Delete
            End If
            If CanUseQbe OrElse CanExpandQbe Then
                capabilities = capabilities Or AccessCapability.UseQbe
            End If
            If CanExpandQbe Then
                capabilities = capabilities Or AccessCapability.ExpandQbe
            End If
            If CanImport Then
                capabilities = capabilities Or AccessCapability.ImportData
            End If
            If CanViewAllRecords Then
                capabilities = capabilities Or AccessCapability.ViewAllRecords
            End If
            If CanViewOnlyMy Then
                capabilities = capabilities Or AccessCapability.ViewOnlyMyRecords
            End If
            If CanImport OrElse CanViewAllRecords OrElse CanViewOnlyMy Then
                capabilities = capabilities Or AccessCapability.Execute
            End If

            Return capabilities
        End Function
    End Class

    Public Interface IAccessProfileProvider
        Function GetProfile(user As UserContext) As AccessProfile
    End Interface

    Public Interface IAccessControlledControl
        Sub ApplyAccess(profile As AccessProfile, tableName As String)
    End Interface

    Public Class RoleMatrixAccessProfileProvider
        Implements IAccessProfileProvider

        Private ReadOnly roleLevel As Integer
        Private ReadOnly roleName As String
        Private ReadOnly rows As IEnumerable(Of RoleTableAccessEntry)

        Public Sub New(roleLevelValue As Integer, roleNameValue As String, roleRows As IEnumerable(Of RoleTableAccessEntry))
            roleLevel = roleLevelValue
            roleName = roleNameValue
            rows = roleRows
        End Sub

        Public Function GetProfile(user As UserContext) As AccessProfile Implements IAccessProfileProvider.GetProfile
            Dim profile As New AccessProfile()
            profile.SetRole(roleLevel, roleName)

            If rows IsNot Nothing Then
                For Each row In rows
                    profile.SetTablePermissions(row)
                Next
            End If

            Return profile
        End Function
    End Class

    Public Class DemoAccessProfileProvider
        Implements IAccessProfileProvider

        Public Function GetProfile(user As UserContext) As AccessProfile Implements IAccessProfileProvider.GetProfile
            Dim profile As New AccessProfile()

            Dim email = If(user?.Email, String.Empty)
            Dim emailLower = email.ToLowerInvariant()

            If emailLower.Contains("admin") Then
                profile.SetRole(1, "Application Admin")
                profile.SetTablePermissions("MESSAGING", AccessCapability.Read Or AccessCapability.Create Or AccessCapability.Update Or AccessCapability.Delete Or AccessCapability.UseQbe)
                profile.SetTablePermissions("ENTITY", AccessCapability.Read Or AccessCapability.Create Or AccessCapability.Update Or AccessCapability.Delete Or AccessCapability.UseQbe)
                profile.SetTablePermissions("ROLES", AccessCapability.Read Or AccessCapability.Create Or AccessCapability.Update Or AccessCapability.Delete)
                profile.SetTablePermissions("REGISTRATION DASHBOARD", AccessCapability.Read Or AccessCapability.Update)
                profile.SetTablePermissions("APPLICATION SETTINGS DASHBOARD", AccessCapability.Read Or AccessCapability.Update)
                profile.SetTablePermissions("FRAMEWORK DASHBOARD", AccessCapability.Read)
                Return profile
            End If

            If emailLower.Contains("owner") Then
                profile.SetRole(4, "User Owner Only")
                profile.SetTablePermissions("MESSAGING", AccessCapability.Read Or AccessCapability.Create)
                profile.SetTablePermissions("ENTITY", AccessCapability.Read Or AccessCapability.Update Or AccessCapability.UseQbe)
                profile.SetTablePermissions("ROLES", AccessCapability.None)
                profile.SetTablePermissions("REGISTRATION DASHBOARD", AccessCapability.Read)
                profile.SetTablePermissions("APPLICATION SETTINGS DASHBOARD", AccessCapability.None)
                profile.SetTablePermissions("FRAMEWORK DASHBOARD", AccessCapability.Read)
                Return profile
            End If

            profile.SetRole(5, "User RO")
            profile.SetTablePermissions("MESSAGING", AccessCapability.Read)
            profile.SetTablePermissions("ENTITY", AccessCapability.Read)
            profile.SetTablePermissions("ROLES", AccessCapability.Read)
            profile.SetTablePermissions("REGISTRATION DASHBOARD", AccessCapability.Read)
            profile.SetTablePermissions("APPLICATION SETTINGS DASHBOARD", AccessCapability.None)
            profile.SetTablePermissions("FRAMEWORK DASHBOARD", AccessCapability.Read)

            Return profile
        End Function
    End Class
End Namespace
