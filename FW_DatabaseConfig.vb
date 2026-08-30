Option Strict On
Option Explicit On

Imports System
Imports System.Collections.Generic
Imports System.Linq
Imports System.Drawing
Imports System.Windows.Forms
Imports Microsoft.Data.SqlClient

Namespace HelloWorld
    ''' <summary>
    ''' Collects database credentials when the application starts with nothing configured, and
    ''' whenever the user asks to change them with --configure-db.
    '''
    ''' Shown before the login screen, because without a database there is nothing to log in to.
    ''' Credentials cannot be saved until a test connection has actually succeeded, so a working
    ''' configuration is the only thing that ever reaches the store.
    ''' </summary>
    Public Class FW_DatabaseConfig
        Inherits Form

        Private ReadOnly serverTextBox As TextBox
        Private ReadOnly userTextBox As TextBox
        Private ReadOnly passwordTextBox As TextBox
        Private ReadOnly databaseComboBox As ComboBox
        Private ReadOnly encryptCheckBox As CheckBox
        Private ReadOnly trustCertificateCheckBox As CheckBox
        Private ReadOnly testButton As Button
        Private ReadOnly saveButton As Button
        Private ReadOnly cancelActionButton As Button
        Private ReadOnly statusLabel As Label

        Private testPassed As Boolean

        Public Sub New(Optional reason As String = "")
            Text = "Database Configuration"
            ClientSize = New Size(560, 400)
            FormBorderStyle = FormBorderStyle.FixedDialog
            StartPosition = FormStartPosition.CenterScreen
            MaximizeBox = False
            MinimizeBox = False

            Dim titleLabel As New Label() With {
                .Text = "Database Configuration",
                .Font = New Font("Segoe UI", 14.0F, FontStyle.Bold),
                .Location = New Point(20, 15),
                .AutoSize = True
            }

            Dim reasonLabel As New Label() With {
                .Text = If(String.IsNullOrWhiteSpace(reason),
                           "Enter the database this application should connect to.",
                           reason),
                .Location = New Point(20, 48),
                .Size = New Size(520, 40),
                .ForeColor = SystemColors.GrayText
            }

            Controls.Add(titleLabel)
            Controls.Add(reasonLabel)

            serverTextBox = AddField("Server", 100)
            userTextBox = AddField("User", 142)
            passwordTextBox = AddField("Password", 184)
            passwordTextBox.UseSystemPasswordChar = True
            ' Editable rather than a fixed list: the name can be typed before any connection has
            ' been made, and a successful test fills the list with what is actually on the server.
            Dim databaseLabel As New Label() With {
                .Name = "Label_Database",
                .Text = "Database",
                .Location = New Point(20, 231),
                .Size = New Size(120, 24)
            }
            databaseComboBox = New ComboBox() With {
                .Name = "ComboBox_Database",
                .Location = New Point(150, 226),
                .Size = New Size(380, 26),
                .DropDownStyle = ComboBoxStyle.DropDown,
                .AutoCompleteMode = AutoCompleteMode.SuggestAppend,
                .AutoCompleteSource = AutoCompleteSource.ListItems
            }
            Controls.Add(databaseLabel)
            Controls.Add(databaseComboBox)

            encryptCheckBox = New CheckBox() With {
                .Name = "CheckBox_Encrypt",
                .Text = "Encrypt connection",
                .Location = New Point(150, 264),
                .AutoSize = True
            }
            trustCertificateCheckBox = New CheckBox() With {
                .Name = "CheckBox_TrustServerCertificate",
                .Text = "Trust server certificate",
                .Location = New Point(320, 264),
                .AutoSize = True,
                .Checked = True
            }
            Controls.Add(encryptCheckBox)
            Controls.Add(trustCertificateCheckBox)

            statusLabel = New Label() With {
                .Location = New Point(20, 296),
                .Size = New Size(520, 44),
                .Text = String.Empty
            }
            Controls.Add(statusLabel)

            testButton = New Button() With {.Text = "Test Connection", .Size = New Size(130, 30), .Location = New Point(20, 350)}
            saveButton = New Button() With {.Text = "Save", .Size = New Size(90, 30), .Location = New Point(354, 350), .Enabled = False}
            cancelActionButton = New Button() With {.Text = "Cancel", .Size = New Size(90, 30), .Location = New Point(450, 350), .DialogResult = DialogResult.Cancel}

            AddHandler testButton.Click, AddressOf TestButton_Click
            AddHandler saveButton.Click, AddressOf SaveButton_Click
            Controls.AddRange({testButton, saveButton, cancelActionButton})
            CancelButton = cancelActionButton

            ' Any edit invalidates the last successful test, so Save can never store untested values.
            For Each editable As Control In New Control() {serverTextBox, userTextBox, passwordTextBox, databaseComboBox}
                AddHandler editable.TextChanged, AddressOf Setting_Changed
            Next
            AddHandler encryptCheckBox.CheckedChanged, AddressOf Setting_Changed
            AddHandler trustCertificateCheckBox.CheckedChanged, AddressOf Setting_Changed

            LoadExistingSettings()
        End Sub

        Private Function AddField(caption As String, y As Integer) As TextBox
            Dim fieldLabel As New Label() With {
                .Name = "Label_" & caption,
                .Text = caption,
                .Location = New Point(20, y + 5),
                .Size = New Size(120, 24)
            }
            Dim fieldTextBox As New TextBox() With {
                .Name = "TextBox_" & caption,
                .Location = New Point(150, y),
                .Size = New Size(380, 26),
                .BorderStyle = BorderStyle.FixedSingle
            }

            Controls.Add(fieldLabel)
            Controls.Add(fieldTextBox)
            Return fieldTextBox
        End Function

        ''' <summary>
        ''' Pre-fills from whatever is already saved, so changing credentials is an edit rather
        ''' than a retype. The password is never pre-filled - it has to be entered deliberately.
        ''' </summary>
        Private Sub LoadExistingSettings()
            Dim saved = DatabaseConfigStore.Load()

            If saved Is Nothing Then
                serverTextBox.Text = "BEELINK"
                userTextBox.Text = "sa"
                databaseComboBox.Text = "WX_Framework"
                Return
            End If

            serverTextBox.Text = saved.Server
            userTextBox.Text = saved.UserId
            databaseComboBox.Text = saved.Database
            encryptCheckBox.Checked = saved.Encrypt
            trustCertificateCheckBox.Checked = saved.TrustServerCertificate
        End Sub

        Private Sub Setting_Changed(sender As Object, e As EventArgs)
            testPassed = False
            saveButton.Enabled = False
            statusLabel.Text = String.Empty
        End Sub

        Private Function BuildSettings() As DatabaseConfigStore.DatabaseSettings
            Return New DatabaseConfigStore.DatabaseSettings With {
                .Server = serverTextBox.Text.Trim(),
                .UserId = userTextBox.Text.Trim(),
                .Password = passwordTextBox.Text,
                .Database = databaseComboBox.Text.Trim(),
                .Encrypt = encryptCheckBox.Checked,
                .TrustServerCertificate = trustCertificateCheckBox.Checked
            }
        End Function

        Private Sub TestButton_Click(sender As Object, e As EventArgs)
            Dim missing As New List(Of String)()
            If serverTextBox.Text.Trim() = String.Empty Then missing.Add("Server")
            If userTextBox.Text.Trim() = String.Empty Then missing.Add("User")
            If passwordTextBox.Text = String.Empty Then missing.Add("Password")
            If databaseComboBox.Text.Trim() = String.Empty Then missing.Add("Database")

            If missing.Count > 0 Then
                ShowStatus("Required: " & String.Join(", ", missing), Color.Firebrick)
                Return
            End If

            Dim candidate = DatabaseConfigStore.BuildConnectionString(BuildSettings())
            Dim previousCursor = Cursor
            Cursor = Cursors.WaitCursor
            testButton.Enabled = False

            Try
                Using conn As New SqlConnection(candidate)
                    conn.Open()
                    Using cmd As New SqlCommand("SELECT 1", conn)
                        cmd.CommandTimeout = 15
                        cmd.ExecuteScalar()
                    End Using
                End Using

                testPassed = True
                saveButton.Enabled = True
                LoadDatabaseNames(candidate)
                ShowStatus("Connection succeeded. Save to use these credentials.", Color.ForestGreen)
            Catch ex As Exception
                testPassed = False
                saveButton.Enabled = False

                ' The credentials may be fine and only the database name wrong. Reaching master
                ' proves that, and fills the list so the right one can simply be picked.
                Dim settings = BuildSettings()
                settings.Database = "master"

                If LoadDatabaseNames(DatabaseConfigStore.BuildConnectionString(settings)) Then
                    ShowStatus("Could not open that database, but the server accepted the credentials. " &
                               "Pick a database from the list and test again.", Color.DarkGoldenrod)
                Else
                    ShowStatus("Connection failed: " & ex.Message, Color.Firebrick)
                End If
            Finally
                testButton.Enabled = True
                Cursor = previousCursor
            End Try
        End Sub

        Private Sub SaveButton_Click(sender As Object, e As EventArgs)
            If Not testPassed Then
                ShowStatus("Test the connection before saving.", Color.Firebrick)
                Return
            End If

            Dim saveError = DatabaseConfigStore.Save(BuildSettings())
            If Not String.IsNullOrWhiteSpace(saveError) Then
                ShowStatus("Could not save: " & saveError, Color.Firebrick)
                Return
            End If

            DataAccess.RefreshConnectionString()
            DialogResult = DialogResult.OK
            Close()
        End Sub

        ''' <summary>
        ''' Fills the database list with what the login can actually see on that server. Returns
        ''' False when the server could not be reached at all, which is how the caller tells a bad
        ''' database name apart from bad credentials. The typed name is preserved either way.
        ''' </summary>
        Private Function LoadDatabaseNames(connectionString As String) As Boolean
            Dim names As New List(Of String)()

            Try
                Using conn As New SqlConnection(connectionString)
                    conn.Open()
                    Using cmd As New SqlCommand("SELECT name FROM sys.databases ORDER BY name", conn)
                        cmd.CommandTimeout = 15
                        Using reader = cmd.ExecuteReader()
                            While reader.Read()
                                names.Add(reader.GetString(0))
                            End While
                        End Using
                    End Using
                End Using
            Catch
                Return False
            End Try

            Dim currentName = databaseComboBox.Text

            ' Repopulating raises TextChanged, which would otherwise clear the test result.
            RemoveHandler databaseComboBox.TextChanged, AddressOf Setting_Changed
            Try
                databaseComboBox.Items.Clear()
                databaseComboBox.Items.AddRange(names.Cast(Of Object)().ToArray())
                databaseComboBox.Text = currentName
            Finally
                AddHandler databaseComboBox.TextChanged, AddressOf Setting_Changed
            End Try

            Return True
        End Function

        Private Sub ShowStatus(message As String, messageColor As Color)
            statusLabel.ForeColor = messageColor
            statusLabel.Text = message
        End Sub
    End Class
End Namespace
