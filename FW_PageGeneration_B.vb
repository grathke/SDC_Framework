Option Strict On
Option Explicit On

Imports System
Imports System.Data
Imports System.Drawing
Imports System.Windows.Forms

Namespace HelloWorld
    Public Class FW_PageGeneration_B
        Inherits FW_Base_B

        Private ReadOnly currentUser As UserContext
        Private ReadOnly accessProfile As AccessProfile
        Public Sub New(user As UserContext, Optional profile As AccessProfile = Nothing)
            MyBase.New(user, profile, "FW_PageGeneration_B_U")
            currentUser = user
            accessProfile = profile
            Text = "Page Generation"
        End Sub

        Protected Overrides Function ResolveBrowsePageName() As String
            Return "PageGeneration_B"
        End Function

        Protected Overrides Sub ApplyPageSpecificLayout()
            Dim browseSplit = FindBrowseSplitContainer(Me)
            If browseSplit Is Nothing Then
                Return
            End If

            Dim qbeButton = FindButtonStartingWithText(Me, "QBE")
            Dim closeButton = FindButtonByText(Me, "Close")
            Dim actionTop = If(IsAppAdminSession(), 112, 42)
            Dim splitTop = If(IsAppAdminSession(), 162, 92)

            If qbeButton IsNot Nothing Then
                qbeButton.Top = actionTop
            End If
            If closeButton IsNot Nothing Then
                closeButton.Top = actionTop
            End If

            browseSplit.Top = splitTop
            browseSplit.Height = Math.Max(180, ClientSize.Height - browseSplit.Top - 20)
        End Sub

        Protected Overrides Function HandleCustomCreateAction() As Boolean
            Using page As New PageGeneration_U(0, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction()
                End If
            End Using
            Return True
        End Function

        Protected Overrides Function HandleCustomReadAction() As Boolean
            Return OpenSelectedRequest()
        End Function

        Protected Overrides Function HandleCustomUpdateAction(recordId As Integer) As Boolean
            Using page As New PageGeneration_U(recordId, currentUser, accessProfile)
                If ShouldRefreshAfterMaintenance(page.ShowDialog(Me)) Then
                    RefreshGridForCustomAction(recordId)
                End If
            End Using
            Return True
        End Function

        Private Function OpenSelectedRequest() As Boolean
            Dim recordId = GetSelectedRecordIdForCustomAction()
            If Not recordId.HasValue Then
                MessageBox.Show(Me, "Select a request first.", "Page Generation", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return True
            End If

            Return HandleCustomUpdateAction(recordId.Value)
        End Function

        Private Shared Function FindBrowseSplitContainer(parent As Control) As SplitContainer
            For Each child As Control In parent.Controls
                Dim split = TryCast(child, SplitContainer)
                If split IsNot Nothing Then
                    Return split
                End If

                Dim nested = FindBrowseSplitContainer(child)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindButtonByText(parent As Control, text As String) As Button
            For Each child As Control In parent.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing AndAlso String.Equals(button.Text, text, StringComparison.OrdinalIgnoreCase) Then
                    Return button
                End If

                Dim nested = FindButtonByText(child, text)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function

        Private Shared Function FindButtonStartingWithText(parent As Control, text As String) As Button
            For Each child As Control In parent.Controls
                Dim button = TryCast(child, Button)
                If button IsNot Nothing AndAlso button.Text.StartsWith(text, StringComparison.OrdinalIgnoreCase) Then
                    Return button
                End If

                Dim nested = FindButtonStartingWithText(child, text)
                If nested IsNot Nothing Then
                    Return nested
                End If
            Next

            Return Nothing
        End Function
    End Class
End Namespace