Option Strict On
Option Explicit On

Imports System
Imports System.IO
Imports System.Security.Cryptography
Imports System.Text
Imports System.Text.Json

Namespace SDC.Framework
    ''' <summary>
    ''' Reads and writes the database credentials the user enters when the application starts with
    ''' nothing configured.
    '''
    ''' Stored under the user's local application data, encrypted with DPAPI at CurrentUser scope:
    ''' the blob is tied to the Windows account and the machine, so copying the file to another
    ''' machine or account yields nothing. Nothing is written in the clear, and no credential is
    ''' compiled into the application.
    '''
    ''' Single owner of this file. Do not read or write it anywhere else.
    ''' </summary>
    Public NotInheritable Class DatabaseConfigStore

        Private Const FolderName As String = "WXFramework"
        Private Const FileName As String = "dbconfig.dat"

        ' Distinguishes this blob from any other DPAPI payload the application might store later.
        Private Shared ReadOnly Entropy As Byte() = Encoding.UTF8.GetBytes("WXFramework.DatabaseConfig.v1")

        Private Sub New()
        End Sub

        Public Class DatabaseSettings
            Public Property Server As String = String.Empty
            Public Property UserId As String = String.Empty
            Public Property Password As String = String.Empty
            Public Property Database As String = String.Empty
            Public Property Encrypt As Boolean
            Public Property TrustServerCertificate As Boolean = True
        End Class

        Public Shared ReadOnly Property ConfigFilePath As String
            Get
                Return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    FolderName,
                    FileName)
            End Get
        End Property

        Public Shared Function Exists() As Boolean
            Return File.Exists(ConfigFilePath)
        End Function

        ''' <summary>
        ''' The saved settings, or Nothing when there are none or the blob cannot be decrypted -
        ''' which is what happens on a different machine or under a different Windows account.
        ''' A failure here is not an error: the caller falls back to the environment.
        ''' </summary>
        Public Shared Function Load() As DatabaseSettings
            Try
                If Not File.Exists(ConfigFilePath) Then Return Nothing

                Dim protectedBytes = File.ReadAllBytes(ConfigFilePath)
                Dim plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser)
                Return JsonSerializer.Deserialize(Of DatabaseSettings)(Encoding.UTF8.GetString(plainBytes))
            Catch
                Return Nothing
            End Try
        End Function

        ''' <summary>Writes the settings, encrypted. Returns the failure reason, or empty on success.</summary>
        Public Shared Function Save(settings As DatabaseSettings) As String
            If settings Is Nothing Then Return "No settings to save."

            Try
                Dim folder = Path.GetDirectoryName(ConfigFilePath)
                If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then
                    Directory.CreateDirectory(folder)
                End If

                Dim plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings))
                Dim protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser)
                File.WriteAllBytes(ConfigFilePath, protectedBytes)
                Return String.Empty
            Catch ex As Exception
                Return ex.Message
            End Try
        End Function

        Public Shared Function Delete() As Boolean
            Try
                If File.Exists(ConfigFilePath) Then File.Delete(ConfigFilePath)
                Return True
            Catch
                Return False
            End Try
        End Function

        ''' <summary>
        ''' Builds a connection string from settings without touching anything saved, so the
        ''' configuration dialog can test credentials before committing to them.
        ''' </summary>
        Public Shared Function BuildConnectionString(settings As DatabaseSettings) As String
            If settings Is Nothing Then Return String.Empty

            Return "Server=" & If(settings.Server, String.Empty).Trim() &
                   ";User Id=" & If(settings.UserId, String.Empty).Trim() &
                   ";Password=" & If(settings.Password, String.Empty) &
                   ";Encrypt=" & settings.Encrypt.ToString() &
                   ";TrustServerCertificate=" & settings.TrustServerCertificate.ToString() &
                   ";Initial Catalog=" & If(settings.Database, String.Empty).Trim() & ";"
        End Function
    End Class
End Namespace
