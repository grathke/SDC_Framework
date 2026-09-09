Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace SDC.Framework

    ''' <summary>
    ''' This file is yours. The page generator writes UserX_U.Generated.vb and never
    ''' reads this one, so anything added here survives the page gaining or losing fields.
    '''
    ''' To reach into the generated half, implement OnFieldsBuilt, OnRecordBound or
    ''' OnBeforeSave - they are declared at the end of that file. To refuse a save, put the
    ''' check in TryBuildRecord below.
    ''' </summary>
    Partial Public Class UserX_U
        Inherits FW_Base_U

        Private ReadOnly recordId As Integer
        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile

        Public Sub New(id As Integer, user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New()
            recordId = id
            currentUser = user
            accessProfile = profile
            BuildGeneratedFields()
            BindToForm()
            ApplyMode()
        End Sub

        Public Overrides ReadOnly Property SavedRecordId As Integer
            Get
                If record Is Nothing OrElse record.Table Is Nothing OrElse Not record.Table.Columns.Contains(primaryKey) OrElse record.IsNull(primaryKey) Then Return 0
                Return Convert.ToInt32(record(primaryKey), Globalization.CultureInfo.InvariantCulture)
            End Get
        End Property

        Protected Overrides Function GetPageName() As String
            Return NameOf(UserX_U)
        End Function

        ''' <summary>
        ''' Where a save is refused. FW_Base_U calls this before SaveRecord and abandons the
        ''' save when it returns False, with the page left open and the edits intact.
        ''' </summary>
        Protected Overrides Function TryBuildRecord() As Boolean
            Return True
        End Function

        ''' <summary>Warns before discarding edits. Without this Cancel would discard silently.</summary>
        Protected Overrides Function ShouldWarnOnCancel() As Boolean
            Return True
        End Function

        ''' <summary>
        ''' Field-level permissions choose Can_Create over Can_Update from this. Without it
        ''' every new record would be evaluated as an update and Can_Create would never apply.
        ''' </summary>
        Protected Overrides Function IsCreatingNewRecord() As Boolean
            Return recordId <= 0
        End Function
    End Class
End Namespace
