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

        ''' <summary>
        ''' Whether somebody is editing their own record from My Profile.
        '''
        ''' A flag rather than working it out from the signed-in user, because the same person
        ''' editing themselves through Employees is a different act. An administrator fixing
        ''' their own address there is doing administration; the same person in My Profile is
        ''' not, and should not be able to grant themselves a role by opening the other door.
        ''' </summary>
        Private ReadOnly selfServiceProfile As Boolean

        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)
            Me.New(id, user, profile, selfService:=False)
        End Sub

        ''' <summary>
        ''' "My Profile" when somebody is editing their own record, and the ordinary caption
        ''' otherwise.
        '''
        ''' The same class serves both, and the window has to say which of the two it is. An
        ''' administrator editing somebody else sees "Edit Employee", which is what they are
        ''' doing; a person editing themselves should not.
        ''' </summary>
        Protected Overrides Function BuildMaintenanceTitle() As String
            If selfServiceProfile Then Return "My Profile"
            Return MyBase.BuildMaintenanceTitle()
        End Function

        Public Sub New(id As Integer, user As UserContext, profile As AccessProfile, selfService As Boolean)
            MyBase.New()
            recordId = id
            currentUser = user
            accessProfile = profile
            selfServiceProfile = selfService
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
            ' Nobody edits their own access. In My Profile the role grids are not built at all
            ' and the form shrinks to the fields, rather than being shown and refusing - a panel
            ' that is there and does nothing invites somebody to work out why.
            If selfServiceProfile Then
                ClientSize = New Size(ClientSize.Width, GeneratedFieldsBottom + 60)
                okButton.Location = New Point(ClientSize.Width - 270, ClientSize.Height - 46)
                cancelActionButton.Location = New Point(ClientSize.Width - 135, ClientSize.Height - 46)
                LockSelfServiceFields()
                Return
            End If

            ' The registration being worked in, not the one signed in under. An App Admin editing a
            ' Saraland employee from registration 1 was offered registration 1's roles, which the
            ' employee cannot hold.
            Dim registrationId = SessionState.WorkingRegistrationID()
            roleSelector = New EmployeeRolesSelector(Me,
                                                     EmployeeRolesSelector.CenteredLeft(ClientSize.Width),
                                                     GeneratedFieldsBottom + 8,
                                                     registrationId,
                                                     recordId)

            AddHandler roleSelector.SelectionChanged, Sub(s, e) ApplyAdminEmailRule()
            ApplyAdminEmailRule()
        End Sub

        ''' <summary>
        ''' An administrator must have an email address; anybody else need not.
        '''
        ''' The framework's two required paths cannot express this. AddField decides once at build
        ''' time, and FW_RoleFields.IsRequired is keyed to the signed-in user's role - the person
        ''' doing the editing, not the person being edited - so it would make Email required for
        ''' every employee an administrator opened, and for none of the ones a manager opened.
        '''
        ''' Called when the picker changes and once after it is built, so an employee who already
        ''' holds an admin role shows the requirement on opening rather than only after a move.
        '''
        ''' Enforcement is not repeated here. SetFieldRequired sets the Tag that
        ''' ValidateRequiredControls already tests, so the existing save path refuses it.
        ''' </summary>
        Private Sub ApplyAdminEmailRule()
            If roleSelector Is Nothing OrElse emailTextBox Is Nothing Then Return

            SetFieldRequired(emailTextBox, roleSelector.HasAdminRole)
        End Sub

        ''' <summary>
        ''' A new employee starts in their registration's time zone. Null on the employee means
        ''' "use the registration's", so this is a suggestion rather than a rule - it can be
        ''' changed before saving, and left alone it records what the registration already implies.
        ''' </summary>
        Private Sub OnRecordBound()
            If recordId > 0 Then Return

            Dim combo = TryCast(Controls.Find("ComboBox_TimeZoneID", True).FirstOrDefault(), ComboBox)
            If combo Is Nothing OrElse GetComboSelectedIdOrZero(combo) > 0 Then Return

            Dim zoneId = DataAccess.GetRegistrationTimeZoneId(SessionState.WorkingRegistrationID())
            If zoneId > 0 Then combo.SelectedValue = zoneId
        End Sub

        ''' <summary>
        ''' The fields somebody does not get to see about themselves.
        '''
        ''' Who your manager is is a decision the company makes about you, not a fact you report,
        ''' and a disabled combo showing it invites the question of how to change it. Hidden is
        ''' the plainer answer.
        '''
        ''' The column closes up behind each one, so hiding a field from the middle of a column
        ''' leaves no white space. HideFieldAndCloseGap measures the row spacing rather than
        ''' assuming it, which is what makes this survive the page being regenerated with the
        ''' fields in a different order or a different column.
        '''
        ''' Named rather than held as variables, for the same reason: a field no longer on the
        ''' page is simply not found, and nothing here has to be kept in step with the generator.
        ''' </summary>
        Private Sub LockSelfServiceFields()
            ' IsActive is here as well as Assigned Manager. Whether somebody works here is not
            ' theirs to answer, and while the check box is on the page it unticks - which is how
            ' this was found.
            For Each fieldName In {"AssignedManagerID", "IsActive"}
                HideFieldAndCloseGap(fieldName)
            Next
        End Sub

        ''' <summary>
        ''' Refuses a save that would leave this person with no role.
        '''
        ''' Somebody with none authenticates correctly and is then turned away at login for
        ''' having nothing to do, which reads as a broken account rather than an unfinished one.
        ''' </summary>
        Private Sub OnValidating(ByRef allowSave As Boolean)
            ' No picker in self-service, and nothing to check. Roles are left exactly as they
            ' are, because OnBeforeSave sends no list at all.
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
