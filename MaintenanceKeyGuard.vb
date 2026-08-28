Imports System
Imports System.Collections.Generic
Imports System.Windows.Forms

Namespace HelloWorld
    Friend Module MaintenanceKeyGuard
        Friend Function IsPkColumn(col As DataGridViewColumn) As Boolean
            If col Is Nothing Then
                Return False
            End If

            Dim dataProperty = If(col.DataPropertyName, String.Empty)
            Dim name = If(col.Name, String.Empty)
            Dim header = If(col.HeaderText, String.Empty)

            Return String.Equals(dataProperty, "PK", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(name, "PK", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(header, "PK", StringComparison.OrdinalIgnoreCase)
        End Function

        Friend Sub HidePkColumn(grid As DataGridView)
            If grid Is Nothing OrElse grid.Columns Is Nothing Then
                Return
            End If

            For Each col As DataGridViewColumn In grid.Columns
                If IsPkColumn(col) Then
                    col.Visible = False
                End If
            Next
        End Sub

        Friend Function ResolveMaintenanceKeyColumnName(grid As DataGridView, preferredKeys As IEnumerable(Of String)) As String
            If grid Is Nothing OrElse grid.Columns Is Nothing OrElse grid.Columns.Count = 0 Then
                Return String.Empty
            End If

            If preferredKeys IsNot Nothing Then
                For Each preferredKey In preferredKeys
                    If String.IsNullOrWhiteSpace(preferredKey) Then
                        Continue For
                    End If

                    For Each col As DataGridViewColumn In grid.Columns
                        Dim dataProperty = If(col.DataPropertyName, String.Empty)
                        Dim name = If(col.Name, String.Empty)
                        Dim header = If(col.HeaderText, String.Empty)

                        If String.Equals(dataProperty, preferredKey, StringComparison.OrdinalIgnoreCase) OrElse
                           String.Equals(name, preferredKey, StringComparison.OrdinalIgnoreCase) OrElse
                           String.Equals(header, preferredKey, StringComparison.OrdinalIgnoreCase) Then
                            Return col.Name
                        End If
                    Next
                Next
            End If

            Return String.Empty
        End Function

        Friend Function UpdateAvailabilityAndMaybeWarn(isMissing As Boolean,
                                                       ByRef warningShown As Boolean,
                                                       owner As IWin32Window) As Boolean
            If Not isMissing Then
                warningShown = False
                Return False
            End If

            If warningShown Then
                Return True
            End If

            warningShown = True
            MessageBox.Show(owner,
                            "PK COLUMN IS MISSING FROM SQL RESULTS." & vbCrLf & vbCrLf &
                            "READ / MODIFY / DELETE / RESTORE BUTTONS ARE DISABLED." & vbCrLf & vbCrLf &
                            "INCLUDE A PRIMARY KEY ALIAS: AS PK.",
                            "MISSING PK",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning)
            Return True
        End Function

        Friend Function EnsureAvailable(isMissing As Boolean,
                                        actionName As String,
                                        owner As IWin32Window) As Boolean
            If Not isMissing Then
                Return True
            End If

            MessageBox.Show(owner,
                            actionName & " is unavailable because the SQL result is missing PK. Include a primary key alias AS PK.",
                            actionName & " Unavailable",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information)
            Return False
        End Function
    End Module
End Namespace
