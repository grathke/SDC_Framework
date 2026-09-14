Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' This file is yours. The page generator writes FW_Employees_U.Generated.vb and never
    ''' reads this one, so anything added here survives the page gaining or losing fields.
    '''
    ''' To reach into the generated half, implement OnFieldsBuilt, OnRecordBound,
    ''' OnValidating or OnBeforeSave - all four are declared at the end of that file.
    ''' </summary>
    Partial Public Class FW_Employees_U
        Inherits FW_Base_U

        Private ReadOnly recordId As Integer
        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        ''' <summary>
        ''' The two-grid role picker under the fields. An employee's roles are rows in
        ''' FW_EmployeeRoles rather than columns of FW_Employees, so no field the generator knows
        ''' about could offer them.
        ''' </summary>
        Private roleSelector As EmployeeRolesSelector

        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New()
            recordId = id
            currentUser = user
            accessProfile = profile
            BuildGeneratedFields()
            BindToForm()
            ApplyMode()
        End Sub

        ''' <summary>
        ''' Puts the role picker below the generated fields.
        '''
        ''' GeneratedFieldsBottom is told to us rather than worked out here. A number typed into
        ''' this file would be right until somebody added a field, and being wrong quietly is the
        ''' failure the split was built to avoid.
        ''' </summary>
        Private Sub OnFieldsBuilt()
            Dim registrationId = If(SessionState.IsActive AndAlso SessionState.Current.HasValue,
                                    SessionState.Current.Value.RegistrationID, 0)
            roleSelector = New EmployeeRolesSelector(Me,
                                                     EmployeeRolesSelector.CenteredLeft(ClientSize.Width),
                                                     GeneratedFieldsBottom + 8,
                                                     registrationId,
                                                     recordId)
        End Sub

        ''' <summary>
        ''' Refuses a save that would leave this person with no role.
        '''
        ''' Somebody with none authenticates correctly and is then turned away at login for
        ''' having nothing to do, which reads as a broken account rather than an unfinished one.
        ''' </summary>
        Private Sub OnValidating(ByRef allowSave As Boolean)
            If roleSelector Is Nothing Then Return

            Dim reason As String = Nothing
            If roleSelector.Validate(reason) Then Return

            MessageBox.Show(Me, reason, "Roles Required", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            allowSave = False
        End Sub

        ''' <summary>
        ''' Hands the chosen roles to the save, which writes them in the same transaction as the
        ''' employee and their login - so a failure anywhere leaves no half-made person.
        ''' </summary>
        Private Sub OnBeforeSave(values As Dictionary(Of String, Object))
            If roleSelector Is Nothing Then Return

            values(DataAccess.AssignRoleValueKey) = String.Join(",", roleSelector.SelectedRoleIds)
        End Sub

    End Class
End Namespace
