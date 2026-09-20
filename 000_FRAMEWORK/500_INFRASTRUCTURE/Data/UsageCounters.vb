Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Concurrent
Imports System.Collections.Generic
Imports System.Data
Imports System.Diagnostics
Imports System.Globalization
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>
    ''' How much the application is used, and how long it takes.
    '''
    ''' Three kinds, all of them a person deciding to look at something: a Search when Find is
    ''' pressed on the QBE row, a BrowseOpen when a browse page loads its grid, a RecordOpen when
    ''' Modify or Read opens one record. All three have their owner in FW_Base_B, so this is a few
    ''' call sites in one file rather than instrumentation spread across the application - and
    ''' every generated page gets it by inheriting, without knowing.
    '''
    ''' **Accumulated in memory, one row per hour per registration per kind per page.** Not a row
    ''' per event: a write per Find would be a round trip added to the thing being measured, which
    ''' is both wasteful and dishonest - the measurement would include itself.
    '''
    ''' **It rides Telemetry's flush.** One timer and one connection for everything this
    ''' application records in the background, because a second timer would be a second copy of
    ''' the same policy and twice the round trips. Telemetry.Flush calls Flush here.
    '''
    ''' **It never throws and never blocks.** Counting is a nicety; failing to count must never
    ''' cost somebody their Find.
    '''
    ''' It holds no record values, no record keys and no search terms. The question it answers is
    ''' "how much is this being used, which pages, and how fast", never "what did this person
    ''' look at".
    ''' </summary>
    Public Module UsageCounters

        Public Enum UsageKind
            Search
            BrowseOpen
            RecordOpen
        End Enum

        ''' <summary>
        ''' What makes two events the same bucket.
        '''
        ''' RegistrationID is carried as an Integer with zero meaning "none known", rather than a
        ''' nullable - a dictionary key that can be Nothing is a class of bug this does not need,
        ''' and the write turns zero back into NULL.
        ''' </summary>
        Private Structure BucketKey
            Public HourUtc As Date
            Public Kind As String
            Public PageName As String
            Public RegistrationID As Integer
        End Structure

        Private NotInheritable Class Bucket
            Public Property EventCount As Integer
            Public Property DbTotal As Long
            Public Property DbMin As Integer?
            Public Property DbMax As Integer?
            Public Property PerceivedTotal As Long
            Public Property PerceivedMin As Integer?
            Public Property PerceivedMax As Integer?

            Public Sub Add(dbMillis As Integer?, perceivedMillis As Integer?)
                EventCount += 1

                If dbMillis.HasValue Then
                    DbTotal += dbMillis.Value
                    If Not DbMin.HasValue OrElse dbMillis.Value < DbMin.Value Then DbMin = dbMillis.Value
                    If Not DbMax.HasValue OrElse dbMillis.Value > DbMax.Value Then DbMax = dbMillis.Value
                End If

                If perceivedMillis.HasValue Then
                    PerceivedTotal += perceivedMillis.Value
                    If Not PerceivedMin.HasValue OrElse perceivedMillis.Value < PerceivedMin.Value Then PerceivedMin = perceivedMillis.Value
                    If Not PerceivedMax.HasValue OrElse perceivedMillis.Value > PerceivedMax.Value Then PerceivedMax = perceivedMillis.Value
                End If
            End Sub
        End Class

        Private ReadOnly pending As New ConcurrentDictionary(Of BucketKey, Bucket)()
        Private ReadOnly flushLock As New Object()
        Private flushing As Boolean

        ''' <summary>
        ''' Buckets held before a flush is refused. Far smaller than the telemetry cap can afford
        ''' to be, because a bucket is an hour of one page rather than one event: reaching this
        ''' means something has gone very wrong with the flush, not that somebody has been busy.
        ''' </summary>
        Private Const BucketCap As Integer = 2000

        ''' <summary>
        ''' Records one event. Both timings are optional - a BrowseOpen has no perceived time
        ''' separate from its load, and a RecordOpen has no query of its own to time.
        ''' </summary>
        Public Sub Record(kind As UsageKind,
                          pageName As String,
                          registrationId As Integer,
                          Optional dbMillis As Integer? = Nothing,
                          Optional perceivedMillis As Integer? = Nothing)
            Try
                If pending.Count >= BucketCap Then Return

                Dim key As New BucketKey With {
                    .HourUtc = HourFloor(Date.UtcNow),
                    .Kind = kind.ToString(),
                    .PageName = Clip(pageName, 100),
                    .RegistrationID = Math.Max(0, registrationId)
                }

                Dim bucket = pending.GetOrAdd(key, Function(k) New Bucket())

                ' Locked on the bucket rather than globally. Two pages counting at once is the
                ' normal case and they are almost never the same bucket.
                SyncLock bucket
                    bucket.Add(dbMillis, perceivedMillis)
                End SyncLock

                ' Whoever records first starts the background flush. It used to be keyed to the
                ' first fault, which meant a healthy installation - where nothing ever throws -
                ' never started it, and these would have sat in memory until the process ended.
                Telemetry.EnsureFlushTimer()

            Catch
                ' Counting is a nicety. A failure here must never reach the page that was counting.
            End Try
        End Sub

        ''' <summary>
        ''' Writes what has accumulated and empties the queue.
        '''
        ''' Called by Telemetry.Flush, so this and the fault log share one timer and one
        ''' connection. Drops its batch on failure for the same reason Telemetry does: a database
        ''' that is down must not make the application accumulate counters until it runs out of
        ''' memory, and a lost hour of counting costs nothing that matters.
        ''' </summary>
        Public Sub Flush()
            If pending.IsEmpty Then Return

            SyncLock flushLock
                If flushing Then Return
                flushing = True
            End SyncLock

            Try
                Dim batch As New List(Of KeyValuePair(Of BucketKey, Bucket))()

                For Each key In pending.Keys
                    Dim bucket As Bucket = Nothing
                    If pending.TryRemove(key, bucket) AndAlso bucket IsNot Nothing Then
                        batch.Add(New KeyValuePair(Of BucketKey, Bucket)(key, bucket))
                    End If
                Next

                If batch.Count = 0 Then Return

                WriteBatch(batch)

            Catch
                ' Dropped on purpose - see the summary.
            Finally
                SyncLock flushLock
                    flushing = False
                End SyncLock
            End Try
        End Sub

        ''' <summary>
        ''' One upsert per bucket, over a shared connection.
        '''
        ''' MERGE rather than an insert, because a bucket flushed twice within the same hour must
        ''' add to the row already there rather than collide with the unique index. The minima and
        ''' maxima are combined rather than replaced, or a later quiet flush would erase the worst
        ''' case the hour actually saw - which is the number most worth keeping.
        '''
        ''' ISNULL(RegistrationID, -1) in the match, because NULL never equals NULL: without it,
        ''' every flush of a registration-less event would insert another row.
        ''' </summary>
        Private Sub WriteBatch(batch As List(Of KeyValuePair(Of BucketKey, Bucket)))
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()

                For Each entry In batch
                    Try
                        Using cmd As New SqlCommand(
                            "MERGE dbo.FW_UsageCounter AS target " &
                            "USING (SELECT @HourUtc AS HourUtc, @Kind AS Kind, @PageName AS PageName, " &
                            "              @RegistrationID AS RegistrationID) AS source " &
                            "ON  target.HourUtc = source.HourUtc " &
                            "AND target.Kind = source.Kind " &
                            "AND target.PageName = source.PageName " &
                            "AND ISNULL(target.RegistrationID, -1) = ISNULL(source.RegistrationID, -1) " &
                            "WHEN MATCHED THEN UPDATE SET " &
                            "  EventCount = target.EventCount + @EventCount, " &
                            "  DbMillisTotal = target.DbMillisTotal + @DbTotal, " &
                            "  DbMillisMin = CASE WHEN target.DbMillisMin IS NULL THEN @DbMin " &
                            "                     WHEN @DbMin IS NULL THEN target.DbMillisMin " &
                            "                     WHEN @DbMin < target.DbMillisMin THEN @DbMin " &
                            "                     ELSE target.DbMillisMin END, " &
                            "  DbMillisMax = CASE WHEN target.DbMillisMax IS NULL THEN @DbMax " &
                            "                     WHEN @DbMax IS NULL THEN target.DbMillisMax " &
                            "                     WHEN @DbMax > target.DbMillisMax THEN @DbMax " &
                            "                     ELSE target.DbMillisMax END, " &
                            "  PerceivedMillisTotal = target.PerceivedMillisTotal + @PerTotal, " &
                            "  PerceivedMillisMin = CASE WHEN target.PerceivedMillisMin IS NULL THEN @PerMin " &
                            "                            WHEN @PerMin IS NULL THEN target.PerceivedMillisMin " &
                            "                            WHEN @PerMin < target.PerceivedMillisMin THEN @PerMin " &
                            "                            ELSE target.PerceivedMillisMin END, " &
                            "  PerceivedMillisMax = CASE WHEN target.PerceivedMillisMax IS NULL THEN @PerMax " &
                            "                            WHEN @PerMax IS NULL THEN target.PerceivedMillisMax " &
                            "                            WHEN @PerMax > target.PerceivedMillisMax THEN @PerMax " &
                            "                            ELSE target.PerceivedMillisMax END " &
                            "WHEN NOT MATCHED THEN INSERT " &
                            "  (HourUtc, Kind, PageName, RegistrationID, EventCount, " &
                            "   DbMillisTotal, DbMillisMin, DbMillisMax, " &
                            "   PerceivedMillisTotal, PerceivedMillisMin, PerceivedMillisMax) " &
                            "  VALUES (@HourUtc, @Kind, @PageName, @RegistrationID, @EventCount, " &
                            "          @DbTotal, @DbMin, @DbMax, @PerTotal, @PerMin, @PerMax);", conn)

                            Dim key = entry.Key
                            Dim bucket = entry.Value

                            cmd.Parameters.Add("@HourUtc", SqlDbType.DateTime2).Value = key.HourUtc
                            cmd.Parameters.Add("@Kind", SqlDbType.VarChar, 20).Value = key.Kind
                            cmd.Parameters.Add("@PageName", SqlDbType.VarChar, 100).Value = key.PageName
                            cmd.Parameters.Add("@RegistrationID", SqlDbType.Int).Value =
                                If(key.RegistrationID > 0, CType(key.RegistrationID, Object), DBNull.Value)

                            cmd.Parameters.Add("@EventCount", SqlDbType.Int).Value = bucket.EventCount
                            cmd.Parameters.Add("@DbTotal", SqlDbType.BigInt).Value = bucket.DbTotal
                            cmd.Parameters.Add("@DbMin", SqlDbType.Int).Value = NullableValue(bucket.DbMin)
                            cmd.Parameters.Add("@DbMax", SqlDbType.Int).Value = NullableValue(bucket.DbMax)
                            cmd.Parameters.Add("@PerTotal", SqlDbType.BigInt).Value = bucket.PerceivedTotal
                            cmd.Parameters.Add("@PerMin", SqlDbType.Int).Value = NullableValue(bucket.PerceivedMin)
                            cmd.Parameters.Add("@PerMax", SqlDbType.Int).Value = NullableValue(bucket.PerceivedMax)

                            cmd.ExecuteNonQuery()
                        End Using
                    Catch
                        ' One bad bucket does not cost the rest of the batch.
                    End Try
                Next
            End Using
        End Sub

        ''' <summary>
        ''' A stopwatch that is safe to leave in a hot path.
        '''
        ''' Stopwatch.StartNew allocates, and this is called on every browse load. It is still
        ''' cheap enough to be invisible next to a database round trip, which is the only thing it
        ''' ever wraps - but it is worth saying that it was considered rather than assumed.
        ''' </summary>
        Public Function StartTimer() As Stopwatch
            Return Stopwatch.StartNew()
        End Function

        ''' <summary>
        ''' Milliseconds elapsed, or Nothing if there was no timer.
        '''
        ''' Capped at one hour. A stopwatch left running across a modal dialog would otherwise
        ''' record a number that says nothing about the query and ruins the maximum for the hour.
        ''' </summary>
        Public Function ElapsedMillis(timer As Stopwatch) As Integer?
            If timer Is Nothing Then Return Nothing

            timer.Stop()

            Dim elapsed = timer.ElapsedMilliseconds
            If elapsed < 0 OrElse elapsed > 3600000 Then Return Nothing

            Return CInt(elapsed)
        End Function

        Private Function HourFloor(value As Date) As Date
            Return New Date(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Utc)
        End Function

        Private Function NullableValue(value As Integer?) As Object
            Return If(value.HasValue, CType(value.Value, Object), DBNull.Value)
        End Function

        Private Function Clip(value As String, length As Integer) As String
            Dim text = If(value, String.Empty).Trim()
            If text.Length <= length Then Return text
            Return text.Substring(0, length)
        End Function

    End Module
End Namespace
