Option Strict On
Option Infer On
Option Explicit On

Imports System

Public Enum BusinessRuleType
    Unknown = 0
    BRRoleBased = 1
    BRRegIDBased = 2
End Enum

Public Module BusinessRuleTypeConstants
    Public Const BR_RoleBased As String = "BR_RoleBased"
    Public Const BR_RegIDBased As String = "BR_RegIDBased"
    Public Const BR_Legacy As String = BR_RegIDBased

    Public Function NormalizeBusinessRuleType(rawValue As String, Optional defaultValue As String = BR_Legacy) As String
        If String.IsNullOrWhiteSpace(rawValue) Then
            Return defaultValue
        End If

        Select Case rawValue.Trim().ToUpperInvariant()
            Case "BR_ROLEBASED", "ROLEBASED_OVERRIDES", "ROLEBASED_BR", "ROLEBASED"
                Return BR_RoleBased
            Case "BR_REGIDBASED", "REGISTRATIONBASED_OVERRIDES", "REGIDBASED_BR", "REGIDBASED", "LEGACY", "REGISTRATIONBASED"
                Return BR_RegIDBased
            Case Else
                Return defaultValue
        End Select
    End Function

    Public Function ParseBusinessRuleType(rawValue As String, Optional defaultValue As BusinessRuleType = BusinessRuleType.BRRegIDBased) As BusinessRuleType
        Dim normalized = NormalizeBusinessRuleType(rawValue)
        If String.Equals(normalized, BR_RegIDBased, StringComparison.OrdinalIgnoreCase) Then
            Return BusinessRuleType.BRRegIDBased
        End If
        If String.Equals(normalized, BR_RoleBased, StringComparison.OrdinalIgnoreCase) Then
            Return BusinessRuleType.BRRoleBased
        End If
        Return defaultValue
    End Function
End Module
