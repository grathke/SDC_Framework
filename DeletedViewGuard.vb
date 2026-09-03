Option Strict On
Option Explicit On

Imports System.Data
Imports System.Windows.Forms

Namespace SDC.Framework
    Friend Module DeletedViewGuard

        Friend Function IsSoftDeleteColumnName(columnName As String) As Boolean
            Dim key = If(columnName, String.Empty).Trim()
            If String.IsNullOrWhiteSpace(key) Then
                Return False
            End If

            Return String.Equals(key, "DeletedFlag", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(key, "DeletedBy", StringComparison.OrdinalIgnoreCase) OrElse
                   String.Equals(key, "DeletedOn", StringComparison.OrdinalIgnoreCase)
        End Function

        Friend Function TableSupportsDeletedView(tableName As String) As Boolean
            Return Not String.IsNullOrWhiteSpace(tableName) AndAlso DataAccess.TableHasColumn(tableName, "DeletedFlag")
        End Function

        Friend Function ResultHasDeletedFlagColumn(grid As DataGridView) As Boolean
            If grid Is Nothing Then
                Return False
            End If

            If grid.Columns IsNot Nothing AndAlso grid.Columns.Contains("DeletedFlag") Then
                Return True
            End If

            Dim sourceTable = TryCast(grid.DataSource, DataTable)
            If sourceTable IsNot Nothing AndAlso sourceTable.Columns IsNot Nothing AndAlso sourceTable.Columns.Contains("DeletedFlag") Then
                Return True
            End If

            Return False
        End Function
    End Module
End Namespace