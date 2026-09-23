Option Strict On
Option Explicit On

Imports System.Data
Imports System.Linq

Namespace SDC.Framework

    ''' <summary>
    ''' Leaves the browse grid showing the record that was just created, and nothing else.
    '''
    ''' A browse grid is capped - the top N of a table that may hold ten thousand - and the cap is
    ''' applied by SQL Server before anything reaches the page. A record created at the end of that
    ''' order was therefore absent from the refresh that followed its own save, and the grid
    ''' selected nothing and scrolled to the top. From the outside that is indistinguishable from a
    ''' save that silently failed, and it gets reported as one.
    '''
    ''' **The rule is one line: after a create, the grid shows the new record alone.** Selected,
    ''' flashed, and the only thing on screen. Then it stops, and the list comes back when the user
    ''' presses Find.
    '''
    ''' Two other designs were built or considered first and are worth not rediscovering.
    ''' *Merging the new row into the capped result* works, but it puts a row in a list it does not
    ''' belong to and needs a sentence explaining that, and the explanation is the tell that the
    ''' screen is no longer one thing. *Showing it alone and then refreshing the list a second later*
    ''' undoes the visibility it just bought - the row returns to the middle of the top N and may
    ''' scroll out of sight - and an application that changes the screen on a timer reads as a
    ''' glitch, because the user cannot tell whether the grid moved because of them.
    '''
    ''' A useful property of the simple rule: when the new record does fall inside the cap, no
    ''' second query is needed at all. The row is already in hand and the others are dropped.
    '''
    ''' **Roles_B does not use this, and that was checked rather than assumed.** Its grid is not
    ''' capped - ExecuteCustomQuery returns every role in the registration - so a created role was
    ''' always in the result. It had the same contract broken by a different fault, fixed where it
    ''' lives: NewButton_Click refreshed without naming the new role, so the row was there and
    ''' nothing pointed at it.
    ''' </summary>
    Friend Module BrowseRowReveal

        ''' <summary>The key alias every browse result carries. Never shown to a user.</summary>
        Private Const KeyColumnName As String = "PK"

        Friend Enum Outcome
            ''' <summary>No record was created, so there is nothing to show.</summary>
            NotRequested

            ''' <summary>The grid now holds the created record and nothing else.</summary>
            ShowingCreatedRecord

            ''' <summary>
            ''' It was outside the result and could not be fetched - the page's SQL declines to be
            ''' wrapped, or the read failed. The rows stand as they came back and the caller says
            ''' plainly that the record is not among them.
            ''' </summary>
            Unavailable
        End Enum

        ''' <summary>
        ''' Reduces <paramref name="table"/> to the created record, fetching it if the refresh did
        ''' not return it.
        '''
        ''' Call this **before** the role-invisible and binary columns are stripped. A fetched row
        ''' arrives with the page SQL's full column set, and a table that has already had columns
        ''' removed cannot take it.
        ''' </summary>
        Friend Function Reveal(table As DataTable,
                               createdRecordKey As Integer,
                               baseSelectSql As String,
                               registrationId As Integer,
                               showDeletedOnly As Boolean,
                               sourceTableName As String) As Outcome

            If table Is Nothing OrElse createdRecordKey <= 0 Then Return Outcome.NotRequested
            If Not table.Columns.Contains(KeyColumnName) Then Return Outcome.NotRequested

            ' Already here - the common case when the table is small or the key sorts early. The
            ' others are dropped and no second query is made.
            If ContainsKey(table, createdRecordKey) Then
                KeepOnly(table, createdRecordKey)
                Return Outcome.ShowingCreatedRecord
            End If

            Dim fetched = DataAccess.GetBrowseRowByKey(baseSelectSql,
                                                       createdRecordKey,
                                                       registrationId,
                                                       showDeletedOnly,
                                                       sourceTableName)

            ' Nothing means the question could not be asked - a page whose SQL will not wrap, or a
            ' read that failed. It does not mean the record is gone, and the caller must not say so.
            If fetched Is Nothing OrElse fetched.Rows.Count = 0 Then Return Outcome.Unavailable

            table.Rows.Clear()

            ' Copied column by column, by name. The two tables come from the same SELECT through the
            ' same wrapper so their columns agree, but ItemArray would depend on their order agreeing
            ' too - and a change that reordered one and not the other would put values in the wrong
            ' columns rather than failing.
            Dim destination = table.NewRow()
            For Each column As DataColumn In table.Columns
                If fetched.Columns.Contains(column.ColumnName) Then
                    destination(column.ColumnName) = fetched.Rows(0)(column.ColumnName)
                End If
            Next

            table.Rows.Add(destination)

            Return Outcome.ShowingCreatedRecord
        End Function

        ''' <summary>What to tell the user, or empty where nothing needs saying.</summary>
        Friend Function DescribeOutcome(result As Outcome) As String
            Select Case result
                Case Outcome.ShowingCreatedRecord
                    ' Said every time, because a grid holding one row is otherwise indistinguishable
                    ' from a table holding one row.
                    Return "Showing the record you just added. Find to see the rest."

                Case Outcome.Unavailable
                    ' Said plainly. The alternative is a grid that quietly does not contain the
                    ' record somebody just saved, which reads as the save having failed.
                    Return "The record was saved but is not among the rows shown. Use Find to see it."

                Case Else
                    Return String.Empty
            End Select
        End Function

        Private Function ContainsKey(table As DataTable, recordKey As Integer) As Boolean
            For Each row As DataRow In table.Rows
                If KeyMatches(row, recordKey) Then Return True
            Next
            Return False
        End Function

        Private Sub KeepOnly(table As DataTable, recordKey As Integer)
            For i As Integer = table.Rows.Count - 1 To 0 Step -1
                If Not KeyMatches(table.Rows(i), recordKey) Then
                    table.Rows.RemoveAt(i)
                End If
            Next
        End Sub

        ''' <summary>Whether this row's key is the one wanted, tolerating nulls and widths.</summary>
        Private Function KeyMatches(row As DataRow, recordKey As Integer) As Boolean
            Dim raw = row(KeyColumnName)
            If raw Is Nothing OrElse Convert.IsDBNull(raw) Then Return False

            Dim value As Integer
            If Not Integer.TryParse(Convert.ToString(raw, Globalization.CultureInfo.InvariantCulture),
                                    Globalization.NumberStyles.Integer,
                                    Globalization.CultureInfo.InvariantCulture,
                                    value) Then
                Return False
            End If

            Return value = recordKey
        End Function
    End Module
End Namespace
