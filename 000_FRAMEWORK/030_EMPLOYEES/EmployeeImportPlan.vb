Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Text.Json

Namespace SDC.Framework

    ''' <summary>One target column and what fills it: a heading in the file, a default, or both.</summary>
    Friend NotInheritable Class ImportFieldMapping
        Public Property Target As ImportTargetColumn

        ''' <summary>The file heading, or empty for none.</summary>
        Public Property SourceColumn As String = String.Empty

        ''' <summary>
        ''' Used wherever the file gives nothing - because the field is not mapped at all, or
        ''' because this row's cell is blank. A default with no mapping is how a column the file
        ''' does not carry, Country for one, is filled for everybody.
        ''' </summary>
        Public Property DefaultValue As String = String.Empty

        ''' <summary>
        ''' When the pair was made, relative to the others: the middle grid, the Check tab and a
        ''' Saved Import list pairs in this order, so a new one lands at the end rather than wherever
        ''' its column sits in the table (Glenn, 2026-09-24). Meaningful only while IsUsed.
        ''' </summary>
        Public Property MappedOrder As Integer

        Public ReadOnly Property IsUsed As Boolean
            Get
                Return SourceColumn <> String.Empty OrElse DefaultValue.Trim() <> String.Empty
            End Get
        End Property
    End Class

    ''' <summary>One row of the file on its way in: its values, what is wrong with it, and what became of it.</summary>
    Friend NotInheritable Class EmployeeImportRow
        ''' <summary>1 for the first data row, whatever the header did. What the returned file quotes.</summary>
        Public Property RowNumber As Integer

        ''' <summary>Text per target key, as the check grid shows it and the write takes it.</summary>
        Public ReadOnly Property Values As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Every problem, worded for a person, in the order found.</summary>
        Public ReadOnly Property Problems As New List(Of String)()

        ''' <summary>
        ''' The same problems keyed by the target they belong to, so the report can mark the very
        ''' cell of the file that caused each one. A problem with no field - the database refusing
        ''' the row - is in Problems only.
        ''' </summary>
        Public ReadOnly Property FieldProblems As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>
        ''' Values the import supplied rather than read, keyed by target, with a word on how: a user
        ''' name made or numbered, a PIN, a default. What the report shows as a correction and the
        ''' results file lists under Supplied By Import.
        ''' </summary>
        Public ReadOnly Property Supplied As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Targets whose value came from a default because the file's cell was blank. Set when the rows are built.</summary>
        Public ReadOnly Property DefaultedKeys As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Things done to the row that are not faults - a user name numbered, a PIN made.</summary>
        Public ReadOnly Property Notes As New List(Of String)()

        ''' <summary>
        ''' Somebody already in the registration who looks like this person - by name, by email or
        ''' both. Never a problem: two people can share a name, and a department can share an
        ''' address (Glenn, 2026-09-24). Import asks Yes or No when there are any, and that is all.
        ''' </summary>
        Public ReadOnly Property Warnings As New List(Of String)()

        Public Property EmployeeId As Integer

        Public ReadOnly Property IsReady As Boolean
            Get
                Return Problems.Count = 0
            End Get
        End Property

        Public Sub AddProblem(key As String, message As String)
            Problems.Add(message)
            If String.IsNullOrEmpty(key) Then Return

            Dim existing As String = Nothing
            FieldProblems(key) = If(FieldProblems.TryGetValue(key, existing), existing & "; " & message, message)
        End Sub

        Public Function ValueOf(key As String) As String
            Dim text As String = Nothing
            Return If(Values.TryGetValue(key, text), If(text, String.Empty), String.Empty)
        End Function
    End Class

    ''' <summary>
    ''' The import with the screen taken away: mapping, rows, the pre-check and the shape of the
    ''' write. The page holds one of these and draws it.
    '''
    ''' **The pre-check is the gate.** Import is offered only when every included row passes, and
    ''' the write that follows is all-or-nothing (DataAccess.ImportEmployees), so what the check
    ''' approved is exactly what reaches the database - or nothing does.
    ''' </summary>
    Friend NotInheritable Class EmployeeImportPlan

        Friend Const TargetTableName As String = "FW_Employees"

        Public ReadOnly Property Targets As List(Of ImportTargetColumn)
        Public ReadOnly Property Mappings As New List(Of ImportFieldMapping)()
        Public ReadOnly Property Rows As New List(Of EmployeeImportRow)()

        Public Sub New(targets As List(Of ImportTargetColumn))
            Me.Targets = targets
            For Each target In targets
                Mappings.Add(New ImportFieldMapping With {.Target = target})
            Next
        End Sub

        Public Shared ReadOnly Property UserNameKey As String = ImportTargetSchema.EmployeesTable & ".UserName"
        Public Shared ReadOnly Property PasswordKey As String = ImportTargetSchema.EmployeesTable & ".Password"
        Private Shared ReadOnly FirstNameKey As String = ImportTargetSchema.EmployeesTable & ".FirstName"
        Private Shared ReadOnly LastNameKey As String = ImportTargetSchema.EmployeesTable & ".LastName"
        Private Shared ReadOnly EmailKey As String = ImportTargetSchema.EmployeesTable & ".Email"
        Private Shared ReadOnly LoginEmailKey As String = ImportTargetSchema.UsersTable & ".Email"

        Public Function MappingFor(key As String) As ImportFieldMapping
            Return Mappings.FirstOrDefault(Function(m) String.Equals(m.Target.Key, key, StringComparison.OrdinalIgnoreCase))
        End Function

        ''' <summary>
        ''' The targets the check grid shows: every one that is mapped or defaulted, plus the user
        ''' name and password, which are always filled - from the file or generated.
        ''' </summary>
        Public Function ActiveMappings() As List(Of ImportFieldMapping)
            Return Mappings.Where(Function(m) m.IsUsed OrElse m.Target.GeneratedWhenBlank).
                            OrderBy(Function(m) If(m.IsUsed, m.MappedOrder, Integer.MaxValue)).ToList()
        End Function

        ''' <summary>The pairs made so far, in the order they were made.</summary>
        Public Function UsedMappings() As List(Of ImportFieldMapping)
            Return Mappings.Where(Function(m) m.IsUsed).OrderBy(Function(m) m.MappedOrder).ToList()
        End Function

        ''' <summary>
        ''' Puts a pair that has just been made after every other one. Call it only for a field that
        ''' was not mapped before - mapping a field again replaces what it had and keeps its place.
        ''' </summary>
        Public Sub PlaceLast(mapping As ImportFieldMapping)
            Dim others = Mappings.Where(Function(m) m IsNot mapping AndAlso m.IsUsed).ToList()
            mapping.MappedOrder = If(others.Count = 0, 1, others.Max(Function(m) m.MappedOrder) + 1)
        End Sub

        ''' <summary>
        ''' Unmaps every field whose file column is not in these headings - a different file, or
        ''' the same one read with another delimiter. Defaults are kept; they belong to no column.
        '''
        ''' Nothing is ever mapped automatically. Glenn decided on 2026-09-24 that every pair is
        ''' picked by hand: a guess from a heading's name is right most of the time and silently
        ''' wrong the rest, and a wrong pair on a field nobody looks at imports without a word.
        ''' </summary>
        Public Sub DropMissingColumns(headings As IEnumerable(Of String))
            Dim available As New HashSet(Of String)(headings, StringComparer.OrdinalIgnoreCase)
            For Each mapping In Mappings.Where(Function(m) m.SourceColumn <> String.Empty AndAlso Not available.Contains(m.SourceColumn))
                mapping.SourceColumn = String.Empty
            Next
        End Sub

        ''' <summary>
        ''' Required targets that nothing fills. Checked before any row is, because the answer is
        ''' the same for every row and a hundred copies of one message hide it.
        ''' </summary>
        Public Function UnfilledRequiredTargets() As List(Of ImportTargetColumn)
            Return Mappings.Where(Function(m) m.Target.IsRequired AndAlso Not m.IsUsed).
                            Select(Function(m) m.Target).ToList()
        End Function

        ''' <summary>
        ''' Builds the rows from the file through the mapping. Edits made in the check grid are
        ''' lost, which is why the page says so before rebuilding over them.
        ''' </summary>
        Public Sub BuildRows(source As DataTable)
            Rows.Clear()
            If source Is Nothing Then Return

            Dim active = ActiveMappings()
            For i = 0 To source.Rows.Count - 1
                Dim sourceRow = source.Rows(i)
                Dim row As New EmployeeImportRow With {.RowNumber = i + 1}

                For Each mapping In active
                    Dim text = String.Empty
                    If mapping.SourceColumn <> String.Empty AndAlso source.Columns.Contains(mapping.SourceColumn) Then
                        text = Convert.ToString(sourceRow(mapping.SourceColumn), CultureInfo.InvariantCulture)
                    End If
                    If String.IsNullOrWhiteSpace(text) AndAlso Not String.IsNullOrWhiteSpace(mapping.DefaultValue) Then
                        text = mapping.DefaultValue
                        row.DefaultedKeys.Add(mapping.Target.Key)
                    End If
                    row.Values(mapping.Target.Key) = If(text, String.Empty).Trim()
                Next

                Rows.Add(row)
            Next
        End Sub

        ''' <summary>
        ''' The pre-check: every row, every mapped field, every problem named against the field it
        ''' belongs to - and the user names and PINs the import would supply, recorded as such.
        '''
        ''' Runs once per BuildRows. The rows are not edited on screen: an import is the whole file
        ''' or nothing, and a file with problems comes back as a report to fix at its source.
        ''' </summary>
        ''' <param name="existingUserNames">Every user name already in the database, lower case.</param>
        Public Sub Check(existingUserNames As HashSet(Of String))
            Dim active = ActiveMappings()
            Dim taken As New HashSet(Of String)(existingUserNames, StringComparer.Ordinal)
            Dim usedPins As New HashSet(Of String)(Rows.Select(Function(r) r.ValueOf(PasswordKey)).
                                                        Where(Function(p) p <> String.Empty),
                                                   StringComparer.Ordinal)

            Dim userNameTarget = Targets.FirstOrDefault(Function(t) String.Equals(t.Key, UserNameKey, StringComparison.OrdinalIgnoreCase))
            Dim userNameMax = If(userNameTarget IsNot Nothing AndAlso userNameTarget.MaxLength > 0, userNameTarget.MaxLength, 50)

            For Each row In Rows
                row.Problems.Clear()
                row.FieldProblems.Clear()
                row.Supplied.Clear()
                row.Notes.Clear()

                For Each mapping In active
                    Dim target = mapping.Target
                    If row.DefaultedKeys.Contains(target.Key) Then
                        row.Supplied(target.Key) = "default"
                    End If
                    If target.GeneratedWhenBlank Then Continue For

                    Dim text = row.ValueOf(target.Key)
                    Dim converted = ImportRules.ConvertValue(text, target.SqlType, target.MaxLength)

                    If converted.Problem <> String.Empty Then
                        row.AddProblem(target.Key, Caption(target) & " " & converted.Problem)
                    ElseIf converted.IsBlank AndAlso target.IsRequired Then
                        row.AddProblem(target.Key, Caption(target) & " is required")
                    ElseIf Not converted.IsBlank Then
                        CheckColumnRule(row, target, converted.Value)
                    End If
                Next

                ' The user name: the file's, or one made from the name, numbered if taken. After
                ' the name columns are checked, because a generated one is made from them.
                ' A pattern - the User Name default {First}.{Last}, say - is expanded for this person
                ' first, then treated like any supplied name: numbered if taken.
                Dim given = row.ValueOf(UserNameKey)
                Dim pattern = If(ImportRules.IsUserNamePattern(given), given, String.Empty)
                If pattern <> String.Empty Then
                    Dim email = row.ValueOf(EmailKey)
                    If email = String.Empty Then email = row.ValueOf(LoginEmailKey)
                    given = ImportRules.ExpandUserNamePattern(pattern, row.ValueOf(FirstNameKey), row.ValueOf(LastNameKey), email)
                End If

                Dim resolved = ImportRules.ResolveUserName(given,
                                                           row.ValueOf(FirstNameKey),
                                                           row.ValueOf(LastNameKey),
                                                           taken,
                                                           userNameMax)
                If pattern <> String.Empty AndAlso given <> String.Empty Then
                    Dim how = "made from " & pattern & If(String.Equals(given, resolved, StringComparison.Ordinal), String.Empty, ", numbered")
                    row.Notes.Add("user name " & how)
                    row.Supplied(UserNameKey) = how
                ElseIf given = String.Empty Then
                    row.Notes.Add("user name made from the name")
                    row.Supplied(UserNameKey) = "made from the name"
                ElseIf Not String.Equals(given, resolved, StringComparison.Ordinal) Then
                    row.Notes.Add("'" & given & "' was taken; numbered")
                    row.Supplied(UserNameKey) = "'" & given & "' was taken"
                End If
                row.Values(UserNameKey) = resolved
                If resolved.Length > userNameMax Then
                    row.AddProblem(UserNameKey, "User Name is longer than " & userNameMax.ToString(CultureInfo.InvariantCulture) & " characters")
                End If

                If row.ValueOf(PasswordKey) = String.Empty Then
                    row.Values(PasswordKey) = ImportRules.GeneratePin(usedPins)
                    row.Notes.Add("PIN generated")
                    row.Supplied(PasswordKey) = "PIN generated"
                End If
            Next
        End Sub

        ''' <summary>
        ''' The rules a column's type cannot express. The birth date one is the table's own CHECK
        ''' constraint, repeated here so it is reported against the row rather than surfacing as a
        ''' constraint name when the whole import is rolled back.
        ''' </summary>
        Private Shared Sub CheckColumnRule(row As EmployeeImportRow, target As ImportTargetColumn, value As Object)
            If String.Equals(target.Name, "BirthDate", StringComparison.OrdinalIgnoreCase) AndAlso
               TypeOf value Is Date AndAlso CDate(value) >= Date.Today Then
                row.AddProblem(target.Key, Caption(target) & " is not in the past")
            End If

            If String.Equals(target.Name, "Email", StringComparison.OrdinalIgnoreCase) Then
                Dim text = Convert.ToString(value, CultureInfo.InvariantCulture)
                Dim at = text.IndexOf("@"c)
                If at <= 0 OrElse at = text.Length - 1 OrElse text.IndexOf(" "c) >= 0 Then
                    row.AddProblem(target.Key, Caption(target) & " '" & text & "' is not an email address")
                End If
            End If
        End Sub

        ''' <summary>"Hire Date", or "Login Email" where the login has a field the employee also has.</summary>
        Public Shared Function Caption(target As ImportTargetColumn) As String
            If String.Equals(target.TableName, ImportTargetSchema.UsersTable, StringComparison.OrdinalIgnoreCase) Then
                Return "Login " & target.DisplayName
            End If
            Return target.DisplayName
        End Function

        ''' <summary>
        ''' Marks each row that looks like somebody already in the registration. Name and email
        ''' together is "likely already an employee"; either alone is worth a look and no more.
        ''' Warnings only - nothing here stops an import.
        ''' </summary>
        ''' <param name="existing">The registration's people: first name, last name and email.</param>
        Public Sub FlagLikelyDuplicates(existing As IEnumerable(Of (FirstName As String, LastName As String, Email As String)))
            Dim byName As New Dictionary(Of String, List(Of (FirstName As String, LastName As String, Email As String)))(StringComparer.OrdinalIgnoreCase)
            Dim byEmail As New Dictionary(Of String, List(Of (FirstName As String, LastName As String, Email As String)))(StringComparer.OrdinalIgnoreCase)
            For Each person In existing
                AddTo(byName, NameKey(person.FirstName, person.LastName), person)
                AddTo(byEmail, Trimmed(person.Email), person)
            Next

            For Each row In Rows
                row.Warnings.Clear()

                Dim name = NameKey(row.ValueOf(FirstNameKey), row.ValueOf(LastNameKey))
                Dim email = Trimmed(row.ValueOf(EmailKey))
                If email = String.Empty Then email = Trimmed(row.ValueOf(LoginEmailKey))

                Dim sameName As List(Of (FirstName As String, LastName As String, Email As String)) = Nothing
                Dim sameEmail As List(Of (FirstName As String, LastName As String, Email As String)) = Nothing
                Dim hasName = name <> String.Empty AndAlso byName.TryGetValue(name, sameName)
                Dim hasEmail = email <> String.Empty AndAlso byEmail.TryGetValue(email, sameEmail)

                If hasName AndAlso sameName.Any(Function(p) String.Equals(Trimmed(p.Email), email, StringComparison.OrdinalIgnoreCase) AndAlso email <> String.Empty) Then
                    row.Warnings.Add("likely already an employee - same name and email")
                ElseIf hasName Then
                    row.Warnings.Add("an employee with this name already exists")
                ElseIf hasEmail Then
                    Dim other = sameEmail(0)
                    row.Warnings.Add("email already used by " & (Trimmed(other.FirstName) & " " & Trimmed(other.LastName)).Trim())
                End If
            Next
        End Sub

        Private Shared Sub AddTo(index As Dictionary(Of String, List(Of (FirstName As String, LastName As String, Email As String))),
                                 key As String,
                                 person As (FirstName As String, LastName As String, Email As String))
            If key = String.Empty Then Return
            Dim list As List(Of (FirstName As String, LastName As String, Email As String)) = Nothing
            If Not index.TryGetValue(key, list) Then
                list = New List(Of (FirstName As String, LastName As String, Email As String))()
                index(key) = list
            End If
            list.Add(person)
        End Sub

        Private Shared Function Trimmed(text As String) As String
            Return If(text, String.Empty).Trim()
        End Function

        ''' <summary>First and last together, or nothing when either is missing - a surname alone matches too much.</summary>
        Private Shared Function NameKey(first As String, last As String) As String
            If Trimmed(first) = String.Empty OrElse Trimmed(last) = String.Empty Then Return String.Empty
            Return Trimmed(first) & "|" & Trimmed(last)
        End Function

        Public ReadOnly Property WarningCount As Integer
            Get
                Return Enumerable.Count(Rows, Function(r) r.Warnings.Count > 0)
            End Get
        End Property

        Public ReadOnly Property ReadyCount As Integer
            Get
                Return Enumerable.Count(Rows, Function(r) r.IsReady)
            End Get
        End Property

        Public ReadOnly Property ProblemCount As Integer
            Get
                Return Enumerable.Count(Rows, Function(r) Not r.IsReady)
            End Get
        End Property

        ''' <summary>All or none: rows to write, and not one of them with anything wrong.</summary>
        Public ReadOnly Property CanImport As Boolean
            Get
                Return Rows.Count > 0 AndAlso ProblemCount = 0
            End Get
        End Property

        ''' <summary>
        ''' The value sets the save path takes, one employee and one login per ready row.
        '''
        ''' Blank values are left out rather than sent as nulls, so a column with a database
        ''' default - IsActive, HireDate - gets its default instead of being set to nothing.
        ''' </summary>
        Public Sub BuildWriteValues(ByRef employees As List(Of Dictionary(Of String, Object)),
                                    ByRef logins As List(Of Dictionary(Of String, Object)),
                                    ByRef rowsWritten As List(Of EmployeeImportRow))
            employees = New List(Of Dictionary(Of String, Object))()
            logins = New List(Of Dictionary(Of String, Object))()
            rowsWritten = New List(Of EmployeeImportRow)()

            Dim active = ActiveMappings()
            For Each row In Rows.Where(Function(r) r.IsReady)
                Dim employee As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)
                Dim login As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)

                For Each mapping In active
                    Dim target = mapping.Target
                    Dim text = row.ValueOf(target.Key)
                    Dim converted = If(target.GeneratedWhenBlank,
                                       New ImportConversion With {.Value = If(text = String.Empty, CObj(DBNull.Value), text)},
                                       ImportRules.ConvertValue(text, target.SqlType, target.MaxLength))
                    If converted.IsBlank OrElse converted.Problem <> String.Empty Then Continue For

                    If String.Equals(target.TableName, ImportTargetSchema.UsersTable, StringComparison.OrdinalIgnoreCase) Then
                        login(target.Name) = converted.Value
                    Else
                        employee(target.Name) = converted.Value
                    End If
                Next

                employees.Add(employee)
                logins.Add(login)
                rowsWritten.Add(row)
            Next
        End Sub

        ''' <summary>
        ''' The mapping as a template stores it: target key, file heading and default, for every
        ''' target that has either. Targets are keyed by table and column, so a template survives
        ''' a column being added to either table - it is simply not mapped.
        ''' </summary>
        ''' <param name="columns">
        ''' Every heading of the file the template was made from - the ones mapped and the ones
        ''' not. Headings only, never a row: it is what lets a template be opened and its mapping
        ''' changed with no file loaded (Glenn, 2026-09-24), including mapping a column it did not
        ''' use before, and it holds nothing about anybody.
        ''' </param>
        ''' <param name="roleId">
        ''' Role For Everyone when it was saved, or 0. Brought back when the Saved Import is chosen,
        ''' shown in the Role box and still changeable - a choice somebody made on purpose, which is
        ''' not the silent default the role is otherwise never given (Glenn, 2026-09-24).
        ''' </param>
        Public Function MappingToJson(hasHeaderRow As Boolean, columns As IEnumerable(Of String),
                                      Optional roleId As Integer = 0) As String
            Dim fields = UsedMappings().
                                  Select(Function(m) New Dictionary(Of String, String) From {
                                      {"target", m.Target.Key},
                                      {"source", m.SourceColumn},
                                      {"default", m.DefaultValue}
                                  }).ToList()

            Dim document As New Dictionary(Of String, Object) From {
                {"version", 2},
                {"hasHeaderRow", hasHeaderRow},
                {"roleId", roleId},
                {"columns", If(columns, Enumerable.Empty(Of String)()).ToList()},
                {"fields", fields}
            }
            Return JsonSerializer.Serialize(document)
        End Function

        ''' <summary>
        ''' Applies a template. Returns the file headings it names that this file does not have,
        ''' so the page can say which mappings did not carry over rather than leaving them blank
        ''' without a word.
        ''' </summary>
        Public Function ApplyJson(json As String, headings As IEnumerable(Of String)) As List(Of String)
            Dim missing As New List(Of String)()
            Dim available As New HashSet(Of String)(headings, StringComparer.OrdinalIgnoreCase)

            For Each mapping In Mappings
                mapping.SourceColumn = String.Empty
                mapping.DefaultValue = String.Empty
            Next

            Using document = JsonDocument.Parse(If(json, "{}"))
                Dim fields As JsonElement
                If Not document.RootElement.TryGetProperty("fields", fields) OrElse fields.ValueKind <> JsonValueKind.Array Then
                    Return missing
                End If

                ' The array is saved in the order the pairs were made, and read back the same way.
                Dim position = 0
                For Each field In fields.EnumerateArray()
                    Dim mapping = MappingFor(ReadString(field, "target"))
                    If mapping Is Nothing Then Continue For
                    position += 1
                    mapping.MappedOrder = position

                    Dim sourceHeading = ReadString(field, "source")
                    If sourceHeading <> String.Empty Then
                        Dim actual = available.FirstOrDefault(Function(h) String.Equals(h, sourceHeading, StringComparison.OrdinalIgnoreCase))
                        If actual IsNot Nothing Then
                            mapping.SourceColumn = actual
                        Else
                            missing.Add(sourceHeading)
                        End If
                    End If

                    mapping.DefaultValue = ReadString(field, "default")
                Next
            End Using

            Return missing
        End Function

        ''' <summary>
        ''' Whether a template was saved from a file whose first row held the headings, or Nothing
        ''' when it does not say. Chosen before the file, a template decides this rather than the
        ''' guess made from the file - it was right about the last file of this shape.
        ''' </summary>
        ''' <summary>The role a Saved Import remembers, or 0 when it has none.</summary>
        Public Shared Function TemplateRoleId(json As String) As Integer
            Try
                Using document = JsonDocument.Parse(If(json, "{}"))
                    Dim value As JsonElement
                    Dim roleId As Integer
                    If document.RootElement.TryGetProperty("roleId", value) AndAlso
                       value.ValueKind = JsonValueKind.Number AndAlso value.TryGetInt32(roleId) Then
                        Return roleId
                    End If
                End Using
            Catch ex As JsonException
                ' Says nothing; the role stays on Make a Selection.
            End Try
            Return 0
        End Function

        Public Shared Function TemplateHasHeaderRow(json As String) As Boolean?
            Try
                Using document = JsonDocument.Parse(If(json, "{}"))
                    Dim value As JsonElement
                    If document.RootElement.TryGetProperty("hasHeaderRow", value) Then
                        If value.ValueKind = JsonValueKind.True Then Return True
                        If value.ValueKind = JsonValueKind.False Then Return False
                    End If
                End Using
            Catch ex As JsonException
                ' A template that will not parse says nothing; the guess stands.
            End Try
            Return Nothing
        End Function

        ''' <summary>
        ''' The file's columns as the template knows them: every heading of the file it was saved
        ''' from, where it recorded them, or else - a template saved before 2026-09-24 - just the
        ''' ones it maps. What the Source tab shows, and Map Fields offers, with no file loaded.
        ''' </summary>
        Public Shared Function TemplateSourceColumns(json As String) As List(Of String)
            Dim columns As New List(Of String)()
            Try
                Using document = JsonDocument.Parse(If(json, "{}"))
                    Dim recorded As JsonElement
                    If document.RootElement.TryGetProperty("columns", recorded) AndAlso recorded.ValueKind = JsonValueKind.Array Then
                        For Each heading In recorded.EnumerateArray()
                            Dim text = If(heading.ValueKind = JsonValueKind.String, heading.GetString(), String.Empty)
                            If Not String.IsNullOrEmpty(text) AndAlso Not columns.Contains(text, StringComparer.OrdinalIgnoreCase) Then
                                columns.Add(text)
                            End If
                        Next
                        If columns.Count > 0 Then Return columns
                    End If

                    Dim fields As JsonElement
                    If Not document.RootElement.TryGetProperty("fields", fields) OrElse fields.ValueKind <> JsonValueKind.Array Then
                        Return columns
                    End If

                    For Each field In fields.EnumerateArray()
                        Dim heading = ReadString(field, "source")
                        If heading <> String.Empty AndAlso
                           Not columns.Contains(heading, StringComparer.OrdinalIgnoreCase) Then
                            columns.Add(heading)
                        End If
                    Next
                End Using
            Catch ex As JsonException
                ' A template that will not parse shows nothing; choosing the file still works.
            End Try
            Return columns
        End Function

        Private Shared Function ReadString(element As JsonElement, name As String) As String
            Dim value As JsonElement
            If element.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then
                Return If(value.GetString(), String.Empty)
            End If
            Return String.Empty
        End Function
    End Class
End Namespace
