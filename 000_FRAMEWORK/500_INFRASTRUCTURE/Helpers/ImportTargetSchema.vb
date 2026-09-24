Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports Microsoft.Data.SqlClient

Namespace SDC.Framework

    ''' <summary>One column of an import target, as the mapping grid needs to show it.</summary>
    Friend NotInheritable Class ImportTargetColumn
        Public Property TableName As String = String.Empty
        Public Property Name As String = String.Empty
        Public Property SqlType As String = String.Empty
        Public Property MaxLength As Integer
        Public Property IsNullable As Boolean
        Public Property HasDefault As Boolean

        ''' <summary>
        ''' Filled in by the import when the file leaves it blank - the user name and the password.
        ''' Never reported as missing, because a blank is an instruction rather than a fault.
        ''' </summary>
        Public Property GeneratedWhenBlank As Boolean

        ''' <summary>Table and column together, which is unique where the column name alone is not.</summary>
        Public ReadOnly Property Key As String
            Get
                Return TableName & "." & Name
            End Get
        End Property

        ''' <summary>
        ''' Which half of the person this lands in, in words: an employee is the person and a login
        ''' is how they sign in, and several fields - Email, the address - exist on both.
        ''' </summary>
        Public ReadOnly Property TableLabel As String
            Get
                Return ImportTargetSchema.LabelFor(TableName)
            End Get
        End Property

        ''' <summary>
        ''' Nothing may be written without it: NOT NULL and no default to fall back on.
        '''
        ''' Not the same question as FW_RoleFields.IsRequired, which is a role's opinion about a
        ''' page. This is the database refusing the row, and no permission or page setting can
        ''' talk it round.
        ''' </summary>
        Public ReadOnly Property IsRequired As Boolean
            Get
                Return Not IsNullable AndAlso Not HasDefault AndAlso Not GeneratedWhenBlank
            End Get
        End Property

        ''' <summary>
        ''' The caption Roles gives this field for the signed-in role - "Reports To" for
        ''' AssignedManagerID - or empty for none. Set when the page opens, from the same lookup the
        ''' browse pages use (DataAccess.GetPageInitMetadata), so the import names a field exactly
        ''' as the Employees page does (Glenn, 2026-09-24).
        ''' </summary>
        Public Property CaptionOverride As String = String.Empty

        ''' <summary>The column's name made readable - "Assigned Manager ID" - whatever Roles says.</summary>
        Public ReadOnly Property FormattedName As String
            Get
                Return DisplayNameFormatter.ToDisplayName(Name)
            End Get
        End Property

        ''' <summary>
        ''' The field as a person reads it everywhere in the import: the role's caption where Roles
        ''' sets one, the readable name otherwise.
        ''' </summary>
        Public ReadOnly Property DisplayName As String
            Get
                Return If(String.IsNullOrWhiteSpace(CaptionOverride), FormattedName, CaptionOverride.Trim())
            End Get
        End Property

        ''' <summary>"varchar(45)", "date", "int" - what the mapping grid shows under Type.</summary>
        Public ReadOnly Property TypeText As String
            Get
                If MaxLength = Integer.MaxValue Then Return SqlType & "(max)"
                If MaxLength > 0 Then Return SqlType & "(" & MaxLength.ToString(CultureInfo.InvariantCulture) & ")"
                Return SqlType
            End Get
        End Property
    End Class

    ''' <summary>
    ''' Which columns an import may write to, read from the database rather than listed in code.
    '''
    ''' Read at runtime so a column added to a table is offered without anybody remembering to come
    ''' back here - which is the same reason the browse pages read their columns rather than
    ''' compiling them in.
    '''
    ''' **The exclusions are one list per table on purpose.** Every one of them is a column the
    ''' framework owns, and the reason they are grouped rather than scattered through the caller is
    ''' that the next person adding an importable table needs to find the rule, not rediscover it.
    ''' </summary>
    Friend Module ImportTargetSchema

        Friend Const EmployeesTable As String = "FW_Employees"
        Friend Const UsersTable As String = "FW_Users"

        ''' <summary>
        ''' Columns never offered as a mapping target, whatever the table.
        '''
        ''' - The identity key is issued by the insert.
        ''' - A computed column cannot be written at all: SQL Server refuses any INSERT naming one,
        '''   which is how FW_Users.FirstLast once broke a generated page that offered it as a text
        '''   box and then could not save the record it had invited.
        ''' - A rowversion is the database's own.
        ''' - Created/Updated/Deleted are stamped by the save path, and a file that carried them
        '''   would be asserting who changed a record it is only now creating.
        ''' - Photo is an image column; a CSV cell cannot hold one. PhotoPath goes with it (Glenn,
        '''   2026-09-24): a path in a file names a picture on somebody else's machine, and under
        '''   Thinfinity not even one the server can see.
        ''' - PasswordHash is produced by ComputePasswordHashForUser and nothing else.
        ''' - UserId and RegistrationId are set by the import itself: the first by the login the
        '''   save path creates, the second from the registration combo.
        '''
        ''' Matched case-insensitively, and by prefix where the framework uses a family of names -
        ''' FW_Employees spells its registration column RegistrationId with a lower-case d while
        ''' everything else spells it RegistrationID, and an exact match would have missed it.
        ''' </summary>
        Private ReadOnly ExcludedExactNames As String() = {
            "Photo", "PhotoPath", "PasswordHash", "UserId", "RegistrationId", "RowVersion"
        }

        Private ReadOnly ExcludedPrefixes As String() = {
            "Created", "Updated", "Deleted"
        }

        ''' <summary>
        ''' The login's own exclusions, on top of the shared ones.
        '''
        ''' - SuperAdmin, TOTPKey and Use2FA are security settings. An import is run by a company
        '''   administrator as readily as by an App Admin, and a file is not how anybody should be
        '''   handed super-administrator rights or a second-factor secret - nor how 2FA is switched
        '''   on for an account that has no key to satisfy it. The save path refuses them as well:
        '''   see DataAccess.ExcludedImportLoginColumns.
        ''' - Password: the one password target is the employee's, which the save path hashes
        '''   against the login. Offering it twice would invite two different values.
        ''' - UserName, FirstName, LastName and IsActive are copied onto the login from the
        '''   employee by the save path already. Offering them again would let the two halves of one
        '''   person disagree about their own name.
        ''' </summary>
        Private ReadOnly ExcludedLoginNames As String() = {
            "SuperAdmin", "TOTPKey", "Use2FA", "Password",
            "UserName", "FirstName", "LastName", "IsActive"
        }

        ''' <summary>
        ''' The tables an import may write to.
        '''
        ''' A list rather than "any table with a RegistrationID", because writing to a table means
        ''' knowing what else has to happen around the write - an employee needs a login first and
        ''' a role after, and a table added here without that knowledge would produce rows that
        ''' satisfy the database and nothing else.
        ''' </summary>
        Friend ReadOnly ImportableTables As String() = {EmployeesTable, UsersTable}

        Friend Function LabelFor(tableName As String) As String
            If String.Equals(tableName, UsersTable, StringComparison.OrdinalIgnoreCase) Then Return "Login"
            Return "Employee"
        End Function

        Friend Function IsExcluded(tableName As String, columnName As String) As Boolean
            Dim name = If(columnName, String.Empty).Trim()
            If name = String.Empty Then Return True

            If ExcludedExactNames.Any(Function(x) String.Equals(x, name, StringComparison.OrdinalIgnoreCase)) Then
                Return True
            End If

            If ExcludedPrefixes.Any(Function(p) name.StartsWith(p, StringComparison.OrdinalIgnoreCase)) Then
                Return True
            End If

            If String.Equals(tableName, UsersTable, StringComparison.OrdinalIgnoreCase) AndAlso
               ExcludedLoginNames.Any(Function(x) String.Equals(x, name, StringComparison.OrdinalIgnoreCase)) Then
                Return True
            End If

            Return False
        End Function

        ''' <summary>
        ''' Every column a person can be imported into: the employee's, then the login's.
        '''
        ''' One round trip for both tables. Identity and computed columns are filtered by
        ''' sys.columns rather than by name, because those are facts about the column rather than
        ''' conventions about what it is called - and a convention is the thing that eventually
        ''' has an exception.
        ''' </summary>
        Friend Function ReadEmployeeTargets() As List(Of ImportTargetColumn)
            Dim columns As New List(Of ImportTargetColumn)()

            ' The route HealthDataAccess already uses. DataAccess keeps its connection string
            ' private and exposes this instead, so a sibling reads the same settings rather than
            ' building a second answer to where the database is.
            Using conn As New SqlConnection(DataAccess.BuildConnectionStringForDatabase(String.Empty))
                conn.Open()
                Using cmd As New SqlCommand(
                    "SELECT t.name AS TableName, c.name AS ColumnName, ty.name AS SqlType, c.max_length, c.is_nullable, " &
                    "       HasDefault = CASE WHEN d.object_id IS NULL THEN 0 ELSE 1 END " &
                    "FROM sys.columns c " &
                    "JOIN sys.tables t ON t.object_id = c.object_id " &
                    "JOIN sys.schemas s ON s.schema_id = t.schema_id AND s.name = 'dbo' " &
                    "JOIN sys.types ty ON ty.user_type_id = c.user_type_id " &
                    "LEFT JOIN sys.default_constraints d " &
                    "       ON d.parent_object_id = c.object_id AND d.parent_column_id = c.column_id " &
                    "WHERE t.name IN (@Employees, @Users) " &
                    "  AND c.is_identity = 0 AND c.is_computed = 0 " &
                    "  AND ty.name NOT IN ('timestamp', 'image', 'varbinary', 'binary') " &
                    "ORDER BY CASE WHEN t.name = @Employees THEN 0 ELSE 1 END, c.column_id", conn)

                    cmd.Parameters.Add("@Employees", SqlDbType.VarChar, 128).Value = EmployeesTable
                    cmd.Parameters.Add("@Users", SqlDbType.VarChar, 128).Value = UsersTable

                    Using reader = cmd.ExecuteReader()
                        While reader.Read()
                            Dim table = Convert.ToString(reader("TableName"), CultureInfo.InvariantCulture)
                            Dim name = Convert.ToString(reader("ColumnName"), CultureInfo.InvariantCulture)
                            If IsExcluded(table, name) Then Continue While

                            Dim sqlType = Convert.ToString(reader("SqlType"), CultureInfo.InvariantCulture)
                            columns.Add(New ImportTargetColumn With {
                                .TableName = table,
                                .Name = name,
                                .SqlType = sqlType,
                                .MaxLength = ResolveMaxLength(sqlType, Convert.ToInt32(reader("max_length"), CultureInfo.InvariantCulture)),
                                .IsNullable = Convert.ToBoolean(reader("is_nullable"), CultureInfo.InvariantCulture),
                                .HasDefault = Convert.ToBoolean(reader("HasDefault"), CultureInfo.InvariantCulture),
                                .GeneratedWhenBlank = String.Equals(table, EmployeesTable, StringComparison.OrdinalIgnoreCase) AndAlso
                                                      (String.Equals(name, "UserName", StringComparison.OrdinalIgnoreCase) OrElse
                                                       String.Equals(name, "Password", StringComparison.OrdinalIgnoreCase))
                            })
                        End While
                    End Using
                End Using
            End Using

            Return columns
        End Function

        ''' <summary>
        ''' Characters rather than bytes, and Integer.MaxValue for the MAX types.
        '''
        ''' sys.columns reports max_length in bytes, so an nvarchar(50) says 100 - and a validation
        ''' message quoting 100 for a field that holds 50 characters would send somebody counting
        ''' their data twice. ntext reports 16, a pointer size, and is unbounded in practice.
        ''' </summary>
        Private Function ResolveMaxLength(sqlType As String, rawMaxLength As Integer) As Integer
            If rawMaxLength = -1 Then Return Integer.MaxValue

            Select Case If(sqlType, String.Empty).ToLowerInvariant()
                Case "ntext", "text"
                    Return Integer.MaxValue
                Case "nvarchar", "nchar"
                    Return rawMaxLength \ 2
                Case "varchar", "char"
                    Return rawMaxLength
                Case Else
                    Return 0
            End Select
        End Function
    End Module
End Namespace
