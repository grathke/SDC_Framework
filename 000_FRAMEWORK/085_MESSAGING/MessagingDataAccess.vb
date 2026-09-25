Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework
    Public NotInheritable Class MessagingDataAccess
        Private Sub New()
        End Sub

        Public Class MessageListRow
            Public Property RecipientID As Integer
            Public Property MessageID As Integer
            Public Property FromName As String
            Public Property ToName As String
            Public Property Subject As String
            Public Property SentOn As DateTime
            Public Property IsRead As Boolean
        End Class

        ''' <summary>
        ''' A folder's rows and the inbox's unread count, as one answer.
        '''
        ''' The two were read separately, which cost the poll three round trips whenever the panel
        ''' was open: the menu counted, the panel read its rows, and the panel counted again. The
        ''' count is included whichever folder was asked for, because a reload is a check for new
        ''' mail and the user may be looking at Sent.
        ''' </summary>
        Public Class MessageFolderSnapshot
            Public Property Rows As List(Of MessageListRow)
            Public Property UnreadCount As Integer
        End Class

        Public Class MessageRecipientOption
            Public Property UserID As Integer
            Public Property DisplayName As String
            Public Overrides Function ToString() As String
                Return DisplayName
            End Function
        End Class

        Public Shared Function GetRecipients(registrationId As Integer, currentUserId As Integer) As List(Of MessageRecipientOption)
            Dim result As New List(Of MessageRecipientOption)()
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand("SELECT UserID, LTRIM(RTRIM(ISNULL(FirstLast, ISNULL(FirstName, '') + ' ' + ISNULL(LastName, '')))) AS DisplayName FROM dbo.FW_Users WHERE RegistrationID = @RegistrationID AND ISNULL(IsActive, 1) = 1 AND ISNULL(DeletedFlag, 0) = 0 ORDER BY DisplayName", conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", currentUserId)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            result.Add(New MessageRecipientOption With {
                                .UserID = Convert.ToInt32(reader("UserID"), CultureInfo.InvariantCulture),
                                .DisplayName = If(reader("DisplayName") Is DBNull.Value, "User", reader("DisplayName").ToString())
                            })
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        ''' <summary>
        ''' One round trip for what a folder shows and what the inbox is owed: the rows, then the
        ''' unread count, as two result sets from a single command.
        '''
        ''' Replaces GetFolderRows plus CountUnread as separate calls. The cost of asking is the
        ''' round trip rather than the SQL, and the poll asked three times a minute with the panel
        ''' open. One command also means the list and the count cannot come from two different
        ''' moments and disagree.
        ''' </summary>
        Public Shared Function GetFolderSnapshot(registrationId As Integer, userId As Integer, folderName As String) As MessageFolderSnapshot
            Dim snapshot As New MessageFolderSnapshot With {.Rows = New List(Of MessageListRow)(), .UnreadCount = 0}

            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand(FolderRowsSql & "; " & UnreadCountSql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@FolderName", folderName)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            snapshot.Rows.Add(ReadListRow(reader))
                        End While

                        If reader.NextResult() AndAlso reader.Read() Then
                            snapshot.UnreadCount = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture)
                        End If
                    End Using
                End Using
            End Using

            Return snapshot
        End Function

        Private Shared Function ReadListRow(reader As SqlDataReader) As MessageListRow
            Return New MessageListRow With {
                .RecipientID = Convert.ToInt32(reader("MessageRecipientID"), CultureInfo.InvariantCulture),
                .MessageID = Convert.ToInt32(reader("MessageID"), CultureInfo.InvariantCulture),
                .FromName = If(reader("FromName") Is DBNull.Value, String.Empty, reader("FromName").ToString()),
                .ToName = If(reader("ToName") Is DBNull.Value, String.Empty, reader("ToName").ToString()),
                .Subject = reader("Subject").ToString(),
                .SentOn = Convert.ToDateTime(reader("SentOn"), CultureInfo.InvariantCulture),
                .IsRead = Convert.ToBoolean(reader("IsRead"), CultureInfo.InvariantCulture)
            }
        End Function

        ''' <summary>
        ''' The two reads the panel and the menu live on, written once. GetFolderSnapshot sends
        ''' both in one command; CountUnread sends the second alone, for the menu with no panel
        ''' open.
        ''' </summary>
        Private Const FolderRowsSql As String =
            "SELECT r.MessageRecipientID, r.MessageID, COALESCE(NULLIF(m.FromUserName, ''), ISNULL(sender.FirstLast, sender.Email)) AS FromName, CASE WHEN r.FolderName = 'Sent' THEN COALESCE(NULLIF(m.ToUserName, ''), ISNULL(recipient.FirstLast, recipient.Email)) ELSE ISNULL(currentUser.FirstLast, currentUser.Email) END AS ToName, thread.Subject, m.SentOn, r.IsRead FROM dbo.FW_MessageRecipients r INNER JOIN dbo.FW_Messages m ON m.MessageID = r.MessageID INNER JOIN dbo.FW_MessageThreads thread ON thread.ThreadID = r.ThreadID LEFT JOIN dbo.FW_Users sender ON sender.UserID = m.FromUserID LEFT JOIN dbo.FW_Users recipient ON recipient.UserID = m.ToUserID LEFT JOIN dbo.FW_Users currentUser ON currentUser.UserID = @UserID AND currentUser.RegistrationID = @RegistrationID WHERE r.RegistrationID = @RegistrationID AND r.FolderName = @FolderName AND ((r.FolderName = 'Sent' AND m.FromUserID = @UserID) OR (r.FolderName = 'Inbox' AND r.UserID = @UserID AND r.RecipientType = 'To') OR (r.FolderName NOT IN ('Sent', 'Inbox') AND r.UserID = @UserID)) ORDER BY m.SentOn DESC"

        Private Const UnreadCountSql As String =
            "SELECT COUNT(1) FROM dbo.FW_MessageRecipients WHERE RegistrationID = @RegistrationID AND UserID = @UserID AND RecipientType = 'To' AND FolderName = 'Inbox' AND IsRead = 0"

        Public Shared Function GetFolderRows(registrationId As Integer, userId As Integer, folderName As String) As List(Of MessageListRow)
            Dim result As New List(Of MessageListRow)()
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand(FolderRowsSql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@FolderName", folderName)
                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            result.Add(New MessageListRow With {
                                .RecipientID = Convert.ToInt32(reader("MessageRecipientID"), CultureInfo.InvariantCulture),
                                .MessageID = Convert.ToInt32(reader("MessageID"), CultureInfo.InvariantCulture),
                                .FromName = If(reader("FromName") Is DBNull.Value, String.Empty, reader("FromName").ToString()),
                                .ToName = If(reader("ToName") Is DBNull.Value, String.Empty, reader("ToName").ToString()),
                                .Subject = reader("Subject").ToString(),
                                .SentOn = Convert.ToDateTime(reader("SentOn"), CultureInfo.InvariantCulture),
                                .IsRead = Convert.ToBoolean(reader("IsRead"), CultureInfo.InvariantCulture)
                            })
                        End While
                    End Using
                End Using
            End Using
            Return result
        End Function

        Public Shared Function CountUnread(registrationId As Integer, userId As Integer) As Integer
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand(UnreadCountSql, conn)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    Return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                End Using
            End Using
        End Function

        Public Shared Sub MarkRecipientRead(recipientId As Integer, registrationId As Integer, userId As Integer)
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand("UPDATE dbo.FW_MessageRecipients SET IsRead = 1, ReadOn = SYSUTCDATETIME(), UpdatedOn = SYSUTCDATETIME() WHERE MessageRecipientID = @RecipientID AND RegistrationID = @RegistrationID AND UserID = @UserID AND RecipientType = 'To' AND FolderName = 'Inbox' AND IsRead = 0", conn)
                    cmd.Parameters.AddWithValue("@RecipientID", recipientId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Function GetMessageBody(messageId As Integer, registrationId As Integer, userId As Integer) As String
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand("SELECT TOP 1 m.Body FROM dbo.FW_Messages m INNER JOIN dbo.FW_MessageRecipients r ON r.MessageID = m.MessageID WHERE m.MessageID = @MessageID AND m.RegistrationID = @RegistrationID AND r.RegistrationID = @RegistrationID AND r.UserID = @UserID", conn)
                    cmd.Parameters.AddWithValue("@MessageID", messageId)
                    cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    Dim result = cmd.ExecuteScalar()
                    Return If(result Is Nothing OrElse IsDBNull(result), String.Empty, result.ToString())
                End Using
            End Using
        End Function

        Public Shared Sub SendMessage(registrationId As Integer, senderUserId As Integer, toUserIds As IEnumerable(Of Integer), ccUserIds As IEnumerable(Of Integer), subject As String, body As String)
            Dim toIds = New HashSet(Of Integer)(If(toUserIds, Array.Empty(Of Integer)))
            Dim ccIds = New HashSet(Of Integer)(If(ccUserIds, Array.Empty(Of Integer)))
            ccIds.ExceptWith(toIds)
            If toIds.Count = 0 Then Throw New InvalidOperationException("At least one To recipient is required.")
            If String.IsNullOrWhiteSpace(subject) Then Throw New InvalidOperationException("Subject is required.")
            If String.IsNullOrWhiteSpace(body) Then Throw New InvalidOperationException("Message body is required.")

            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using trans = conn.BeginTransaction()
                    Try
                        Dim threadId As Integer
                        Using cmd As New SqlCommand("INSERT INTO dbo.FW_MessageThreads (RegistrationID, Subject, CreatedBy) VALUES (@RegistrationID, @Subject, @CreatedBy); SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans)
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            cmd.Parameters.AddWithValue("@Subject", subject.Trim())
                            cmd.Parameters.AddWithValue("@CreatedBy", senderUserId)
                            threadId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                        End Using

                        Dim messageId As Integer
                        Dim primaryToUserId = toIds.First()
                        Using cmd As New SqlCommand("INSERT INTO dbo.FW_Messages (ThreadID, RegistrationID, FromUserID, ToUserID, FromUserName, ToUserName, Body) SELECT @ThreadID, @RegistrationID, @FromUserID, @ToUserID, COALESCE(NULLIF(LTRIM(RTRIM(fromUser.FirstLast)), ''), NULLIF(LTRIM(RTRIM(ISNULL(fromUser.FirstName, '') + ' ' + ISNULL(fromUser.LastName, ''))), ''), fromUser.Email), COALESCE(NULLIF(LTRIM(RTRIM(toUser.FirstLast)), ''), NULLIF(LTRIM(RTRIM(ISNULL(toUser.FirstName, '') + ' ' + ISNULL(toUser.LastName, ''))), ''), toUser.Email), @Body FROM dbo.FW_Users fromUser CROSS JOIN dbo.FW_Users toUser WHERE fromUser.UserID = @FromUserID AND fromUser.RegistrationID = @RegistrationID AND toUser.UserID = @ToUserID AND toUser.RegistrationID = @RegistrationID; SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, trans)
                            cmd.Parameters.AddWithValue("@ThreadID", threadId)
                            cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            cmd.Parameters.AddWithValue("@FromUserID", senderUserId)
                            cmd.Parameters.AddWithValue("@ToUserID", primaryToUserId)
                            cmd.Parameters.AddWithValue("@Body", body.Trim())
                            messageId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture)
                        End Using

                        AddRecipient(conn, trans, messageId, threadId, registrationId, senderUserId, "Se", "Sent")
                        For Each userId In toIds
                            AddRecipient(conn, trans, messageId, threadId, registrationId, userId, "To", "Inbox")
                        Next
                        For Each userId In ccIds
                            AddRecipient(conn, trans, messageId, threadId, registrationId, userId, "Cc", "Inbox")
                        Next
                        trans.Commit()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Sub

        Private Shared Sub AddRecipient(conn As SqlConnection, trans As SqlTransaction, messageId As Integer, threadId As Integer, registrationId As Integer, userId As Integer, recipientType As String, folderName As String)
            Using cmd As New SqlCommand("INSERT INTO dbo.FW_MessageRecipients (MessageID, ThreadID, RegistrationID, UserID, RecipientType, FolderName) VALUES (@MessageID, @ThreadID, @RegistrationID, @UserID, @RecipientType, @FolderName)", conn, trans)
                cmd.Parameters.AddWithValue("@MessageID", messageId)
                cmd.Parameters.AddWithValue("@ThreadID", threadId)
                cmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                cmd.Parameters.AddWithValue("@UserID", userId)
                cmd.Parameters.AddWithValue("@RecipientType", recipientType)
                cmd.Parameters.AddWithValue("@FolderName", folderName)
                cmd.ExecuteNonQuery()
            End Using
        End Sub

        Public Shared Sub MoveRecipient(recipientId As Integer, userId As Integer, fromFolder As String, toFolder As String)
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand("UPDATE dbo.FW_MessageRecipients SET PreviousFolderName = CASE WHEN @ToFolder IN ('Trash', 'Archive') THEN @FromFolder ELSE NULL END, FolderName = @ToFolder, UpdatedOn = SYSUTCDATETIME() WHERE MessageRecipientID = @RecipientID AND UserID = @UserID AND FolderName = @FromFolder", conn)
                    cmd.Parameters.AddWithValue("@RecipientID", recipientId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@FromFolder", fromFolder)
                    cmd.Parameters.AddWithValue("@ToFolder", toFolder)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Sub RestoreRecipient(recipientId As Integer, userId As Integer, fromFolder As String)
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand("UPDATE dbo.FW_MessageRecipients SET FolderName = ISNULL(NULLIF(PreviousFolderName, ''), 'Inbox'), PreviousFolderName = NULL, UpdatedOn = SYSUTCDATETIME() WHERE MessageRecipientID = @RecipientID AND UserID = @UserID AND FolderName = @FromFolder", conn)
                    cmd.Parameters.AddWithValue("@RecipientID", recipientId)
                    cmd.Parameters.AddWithValue("@UserID", userId)
                    cmd.Parameters.AddWithValue("@FromFolder", fromFolder)
                    cmd.ExecuteNonQuery()
                End Using
            End Using
        End Sub

        Public Shared Sub DeleteRecipientPermanently(recipientId As Integer, userId As Integer, registrationId As Integer)
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using trans = conn.BeginTransaction()
                    Try
                        Dim messageId As Integer = 0
                        Dim threadId As Integer = 0
                        Using findCmd As New SqlCommand("SELECT MessageID, ThreadID FROM dbo.FW_MessageRecipients WHERE MessageRecipientID = @RecipientID AND UserID = @UserID AND RegistrationID = @RegistrationID AND FolderName = 'Trash'", conn, trans)
                            findCmd.Parameters.AddWithValue("@RecipientID", recipientId)
                            findCmd.Parameters.AddWithValue("@UserID", userId)
                            findCmd.Parameters.AddWithValue("@RegistrationID", registrationId)
                            Using reader = findCmd.ExecuteReader()
                                If Not reader.Read() Then
                                    Return
                                End If
                                messageId = Convert.ToInt32(reader("MessageID"), CultureInfo.InvariantCulture)
                                threadId = Convert.ToInt32(reader("ThreadID"), CultureInfo.InvariantCulture)
                            End Using
                        End Using

                        Using deleteRecipient As New SqlCommand("DELETE FROM dbo.FW_MessageRecipients WHERE MessageRecipientID = @RecipientID AND UserID = @UserID AND RegistrationID = @RegistrationID AND FolderName = 'Trash'", conn, trans)
                            deleteRecipient.Parameters.AddWithValue("@RecipientID", recipientId)
                            deleteRecipient.Parameters.AddWithValue("@UserID", userId)
                            deleteRecipient.Parameters.AddWithValue("@RegistrationID", registrationId)
                            deleteRecipient.ExecuteNonQuery()
                        End Using

                        Using countCmd As New SqlCommand("SELECT COUNT(1) FROM dbo.FW_MessageRecipients WHERE MessageID = @MessageID", conn, trans)
                            countCmd.Parameters.AddWithValue("@MessageID", messageId)
                            If Convert.ToInt32(countCmd.ExecuteScalar(), CultureInfo.InvariantCulture) = 0 Then
                                Using deleteMessage As New SqlCommand("DELETE FROM dbo.FW_Messages WHERE MessageID = @MessageID", conn, trans)
                                    deleteMessage.Parameters.AddWithValue("@MessageID", messageId)
                                    deleteMessage.ExecuteNonQuery()
                                End Using
                                Using deleteThread As New SqlCommand("DELETE FROM dbo.FW_MessageThreads WHERE ThreadID = @ThreadID", conn, trans)
                                    deleteThread.Parameters.AddWithValue("@ThreadID", threadId)
                                    deleteThread.ExecuteNonQuery()
                                End Using
                            End If
                        End Using
                        trans.Commit()
                    Catch
                        trans.Rollback()
                        Throw
                    End Try
                End Using
            End Using
        End Sub
    End Class
End Namespace
