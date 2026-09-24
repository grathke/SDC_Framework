Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Data
Imports System.Globalization
Imports System.Linq
Imports System.Net
Imports System.Text

Namespace SDC.Framework

    ''' <summary>
    ''' What an import hands back, both as pages shown in the application with Open In Browser to
    ''' print or save: the file marked up when nothing was written, and a sign-in sheet - user
    ''' names and passwords - when everything was. The sign-in sheet replaced a CSV on 2026-09-24.
    '''
    ''' The problems page is the file as it came in, column for column, because that is what
    ''' somebody fixes: the import's view of the rows is laid back over the original columns, so a
    ''' problem is shown in the very cell that caused it.
    ''' </summary>
    Friend NotInheritable Class EmployeeImportReport

        Private Sub New()
        End Sub

        ''' <summary>
        ''' The page shown when nothing was imported: every row of the file, each problem in red in
        ''' the cell that caused it, and each value the import would have supplied in green.
        '''
        ''' PINs are never in it. Nothing was written, a new PIN is made on the next attempt, and a
        ''' page that may be saved and passed around is no place for one.
        ''' </summary>
        ''' <param name="failureMessage">
        ''' The database's refusal when the check passed and the write still failed - a user name
        ''' taken in the meantime, a lookup number that does not exist. Empty otherwise.
        ''' </param>
        Friend Shared Function ProblemsHtml(plan As EmployeeImportPlan,
                                            source As DataTable,
                                            fileName As String,
                                            registrationName As String,
                                            failureMessage As String) As String
            Dim html As New StringBuilder()
            Dim problemRows = plan.Rows.Where(Function(r) Not r.IsReady).ToList()
            Dim columns = source.Columns.Cast(Of DataColumn)().Select(Function(c) c.ColumnName).ToList()
            Dim extraKeys = UnmappedKeys(plan)

            AppendPageStart(html, "Employee Import - Nothing Imported")

            html.Append("<h1>Nothing was imported</h1>")
            html.Append("<div class=""sub"">").Append(Enc(fileName)).Append(" &middot; into ").Append(Enc(registrationName)).
                 Append(" &middot; checked ").Append(Enc(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append("</div>")

            html.Append("<div>An import writes every row or none. ")
            If problemRows.Count > 0 Then
                html.Append("<b>").Append(problemRows.Count.ToString(CultureInfo.InvariantCulture)).Append(" of ").
                     Append(plan.Rows.Count.ToString(CultureInfo.InvariantCulture)).
                     Append(" rows</b> have problems. Correct them in the file and import it again.")
            End If
            html.Append("</div>")

            If Not String.IsNullOrWhiteSpace(failureMessage) Then
                html.Append("<div class=""box""><b>The database refused the import:</b> ").Append(Enc(failureMessage)).Append("</div>")
            End If

            ' The short list first: what to fix, row by row, before the whole file.
            If problemRows.Count > 0 Then
                html.Append("<div class=""box""><b>To fix</b><ul>")
                For Each row In problemRows
                    html.Append("<li>Row ").Append(row.RowNumber.ToString(CultureInfo.InvariantCulture)).Append(": ").
                         Append(Enc(String.Join("; ", row.Problems))).Append("</li>")
                Next
                html.Append("</ul></div>")
            End If

            html.Append("<div class=""legend""><span style=""background:#fbd5d5"">problem</span>")
            html.Append("<span style=""background:#d8f0d8"">supplied by the import</span></div>")

            html.Append("<table><tr><th>Row</th>")
            For Each column In columns
                html.Append("<th>").Append(Enc(column)).Append("</th>")
            Next
            If extraKeys.Count > 0 Then html.Append("<th>Not in the file</th>")
            html.Append("</tr>")

            For i = 0 To plan.Rows.Count - 1
                Dim row = plan.Rows(i)
                Dim sourceRow = If(i < source.Rows.Count, source.Rows(i), Nothing)

                html.Append(If(row.IsReady, "<tr>", "<tr class=""badrow"">"))
                html.Append("<td class=""rowno"">").Append(row.RowNumber.ToString(CultureInfo.InvariantCulture)).Append("</td>")

                For Each column In columns
                    Dim original = If(sourceRow Is Nothing, String.Empty, Convert.ToString(sourceRow(column), CultureInfo.InvariantCulture))
                    AppendCell(html, plan, row, column, original)
                Next

                If extraKeys.Count > 0 Then AppendUnmappedCell(html, plan, row, extraKeys)
                html.Append("</tr>")
            Next

            html.Append("</table></body></html>")
            Return html.ToString()
        End Function

        ''' <summary>
        ''' One cell of the file: plain, red with the problem, or green with what the import put
        ''' there - the value it replaced struck through where there was one.
        ''' </summary>
        Private Shared Sub AppendCell(html As StringBuilder,
                                      plan As EmployeeImportPlan,
                                      row As EmployeeImportRow,
                                      column As String,
                                      original As String)
            Dim keys = plan.Mappings.Where(Function(m) String.Equals(m.SourceColumn, column, StringComparison.OrdinalIgnoreCase)).
                                     Select(Function(m) m.Target.Key).ToList()

            Dim problems = keys.Select(Function(k) FieldProblemOf(row, k)).Where(Function(p) p <> String.Empty).ToList()
            If problems.Count > 0 Then
                html.Append("<td class=""bad"">").Append(EncMultiline(original))
                For Each problem In problems
                    html.Append("<div class=""msg"">").Append(Enc(problem)).Append("</div>")
                Next
                html.Append("</td>")
                Return
            End If

            Dim suppliedKey = keys.FirstOrDefault(Function(k) row.Supplied.ContainsKey(k))
            If suppliedKey IsNot Nothing Then
                html.Append("<td class=""fix"">")
                If original.Trim() <> String.Empty Then html.Append("<s>").Append(Enc(original)).Append("</s> ")
                html.Append(Enc(DisplayedSupply(row, suppliedKey))).
                     Append("<div class=""how"">").Append(Enc(row.Supplied(suppliedKey))).Append("</div></td>")
                Return
            End If

            html.Append("<td>").Append(EncMultiline(original)).Append("</td>")
        End Sub

        ''' <summary>Problems and supplied values for fields no column of the file feeds.</summary>
        Private Shared Sub AppendUnmappedCell(html As StringBuilder,
                                              plan As EmployeeImportPlan,
                                              row As EmployeeImportRow,
                                              keys As List(Of String))
            Dim parts As New List(Of String)()
            Dim bad = False

            For Each key In keys
                Dim caption = EmployeeImportPlan.Caption(plan.MappingFor(key).Target)
                Dim problem = FieldProblemOf(row, key)
                If problem <> String.Empty Then
                    parts.Add("<div class=""msg"">" & Enc(problem) & "</div>")
                    bad = True
                ElseIf row.Supplied.ContainsKey(key) Then
                    parts.Add("<div>" & Enc(caption) & ": " & Enc(DisplayedSupply(row, key)) &
                              " <span class=""how"">(" & Enc(row.Supplied(key)) & ")</span></div>")
                End If
            Next

            ' Problems that belong to no field - the database refusing the row - go here too.
            For Each problem In row.Problems.Where(Function(p) Not row.FieldProblems.Values.Any(Function(v) v.Contains(p)))
                parts.Add("<div class=""msg"">" & Enc(problem) & "</div>")
                bad = True
            Next

            html.Append(If(bad, "<td class=""bad"">", If(parts.Count > 0, "<td class=""fix"">", "<td>"))).
                 Append(String.Concat(parts)).Append("</td>")
        End Sub

        ''' <summary>What the import put in a field, except a password, which is never shown here.</summary>
        Private Shared Function DisplayedSupply(row As EmployeeImportRow, key As String) As String
            If String.Equals(key, EmployeeImportPlan.PasswordKey, StringComparison.OrdinalIgnoreCase) Then Return "(a PIN)"
            Return row.ValueOf(key)
        End Function

        Private Shared Function FieldProblemOf(row As EmployeeImportRow, key As String) As String
            Dim problem As String = Nothing
            Return If(row.FieldProblems.TryGetValue(key, problem), problem, String.Empty)
        End Function

        ''' <summary>The active targets no file column feeds - defaults, and the user name and password when unmapped.</summary>
        Private Shared Function UnmappedKeys(plan As EmployeeImportPlan) As List(Of String)
            Return plan.ActiveMappings().Where(Function(m) m.SourceColumn = String.Empty).
                                         Select(Function(m) m.Target.Key).ToList()
        End Function

        ''' <summary>
        ''' The one head and style both pages share, so the results look like the problems page
        ''' and a change to one is a change to both.
        ''' </summary>
        Private Shared Sub AppendPageStart(html As StringBuilder, title As String)
            html.Append("<!DOCTYPE html><html><head><meta charset=""utf-8"">")
            html.Append("<title>").Append(Enc(title)).Append("</title><style>")
            html.Append("body{font-family:'Segoe UI',Arial,sans-serif;font-size:13px;color:#222;margin:16px;}")
            html.Append("h1{font-size:20px;margin:0 0 4px 0;} .sub{color:#555;margin-bottom:12px;}")
            html.Append(".box{border:1px solid #d33;background:#fdecec;padding:8px 10px;margin:10px 0;}")
            html.Append(".ok{border:1px solid #3a7;background:#eaf6ee;padding:8px 10px;margin:10px 0;}")
            html.Append(".legend span{display:inline-block;padding:2px 8px;margin-right:8px;border:1px solid #ccc;}")
            html.Append("table{border-collapse:collapse;margin-top:10px;} th,td{border:1px solid #bbb;padding:3px 6px;vertical-align:top;text-align:left;}")
            html.Append("th{background:#dbe8f5;position:sticky;top:0;} tr.badrow td.rowno{background:#f4c7c7;font-weight:bold;}")
            html.Append("td.bad{background:#fbd5d5;} td.fix{background:#d8f0d8;} .msg{color:#b00;font-size:11px;} .how{color:#2a6a2a;font-size:11px;}")
            html.Append("td.pw{font-family:Consolas,monospace;font-size:14px;letter-spacing:1px;}")
            html.Append("s{color:#888;} ul{margin:4px 0 0 18px;padding:0;}")
            html.Append("@media print{th{position:static;} .noprint{display:none;}}")
            html.Append("</style></head><body>")
        End Sub

        ''' <summary>
        ''' The page shown when an import has been written: a sign-in sheet - who, their Employee
        ''' ID, the user name and password they sign in with, their email - to print or save from
        ''' the browser (Glenn, 2026-09-24: a page, not the CSV it replaced, which put the passwords
        ''' after every column of the file and was hard to read).
        '''
        ''' A note only where something is particular to the row - a user name numbered because it
        ''' was taken, one made from a pattern, a password from the file - highlighted as the Check
        ''' tab highlights it. What is true of everybody is said once, above the table.
        '''
        ''' The file's own columns are not repeated: this is what people sign in with, and the file
        ''' is still the file.
        '''
        ''' It holds every new password. It is shown in the application, and a copy is staged for the
        ''' browser only when Open In Browser is pressed - under BrowserDocument's rules, gone when
        ''' the session ends at the latest.
        ''' </summary>
        Friend Shared Function ResultsHtml(plan As EmployeeImportPlan,
                                           fileName As String,
                                           registrationName As String,
                                           roleName As String,
                                           timeZoneName As String,
                                           savedImportName As String) As String
            Dim html As New StringBuilder()
            Dim employees = ImportTargetSchema.EmployeesTable
            Dim imported = plan.Rows.Where(Function(r) r.EmployeeId > 0).ToList()

            AppendPageStart(html, "Employee Import - Results")

            html.Append("<h1>").Append(imported.Count.ToString(CultureInfo.InvariantCulture)).
                 Append(If(imported.Count = 1, " employee", " employees")).
                 Append(" imported into ").Append(Enc(registrationName)).Append("</h1>")
            html.Append("<div class=""sub"">").Append(Enc(fileName))
            If Not String.IsNullOrWhiteSpace(savedImportName) Then html.Append(" &middot; Saved Import ").Append(Enc(savedImportName))
            html.Append(" &middot; ").Append(Enc(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append("</div>")

            html.Append("<div class=""ok"">Everyone below has the role <b>").Append(Enc(roleName)).Append("</b>")
            If Not String.IsNullOrWhiteSpace(timeZoneName) Then
                html.Append(" and, where the file gave none, the time zone <b>").Append(Enc(timeZoneName)).Append("</b>")
            End If
            html.Append(". Each signs in with the user name and password shown. ")
            html.Append("<b>This page is the only copy of the passwords</b> - print it or save it before closing the import.</div>")

            html.Append("<table><tr><th>Row</th><th>Employee ID</th><th>First Name</th><th>Last Name</th>")
            html.Append("<th>User Name</th><th>Password</th><th>Email</th><th>Note</th></tr>")

            For Each row In imported
                Dim email = row.ValueOf(employees & ".Email")
                If email = String.Empty Then email = row.ValueOf(ImportTargetSchema.UsersTable & ".Email")
                Dim note = RowNote(row)

                html.Append("<tr>")
                html.Append("<td>").Append(row.RowNumber.ToString(CultureInfo.InvariantCulture)).Append("</td>")
                html.Append("<td>").Append(row.EmployeeId.ToString(CultureInfo.InvariantCulture)).Append("</td>")
                html.Append("<td>").Append(Enc(row.ValueOf(employees & ".FirstName"))).Append("</td>")
                html.Append("<td>").Append(Enc(row.ValueOf(employees & ".LastName"))).Append("</td>")
                html.Append(If(note.Contains("user name"), "<td class=""fix"">", "<td>")).
                     Append(Enc(row.ValueOf(EmployeeImportPlan.UserNameKey))).Append("</td>")
                html.Append("<td class=""pw"">").Append(Enc(row.ValueOf(EmployeeImportPlan.PasswordKey))).Append("</td>")
                html.Append("<td>").Append(Enc(email)).Append("</td>")
                html.Append("<td class=""how"">").Append(Enc(note)).Append("</td>")
                html.Append("</tr>")
            Next

            html.Append("</table></body></html>")
            Return html.ToString()
        End Function

        ''' <summary>
        ''' What is particular to this row. The routine - a user name made from the name, a PIN, a
        ''' default, the time zone - is left out, being the same for everybody.
        ''' </summary>
        Private Shared Function RowNote(row As EmployeeImportRow) As String
            Dim parts As New List(Of String)()

            Dim how As String = Nothing
            If row.Supplied.TryGetValue(EmployeeImportPlan.UserNameKey, how) Then
                If how.Contains("was taken") Then
                    parts.Add("user name " & how.Replace("was taken", "was taken - numbered"))
                ElseIf how.StartsWith("made from {", StringComparison.Ordinal) OrElse how.Contains("numbered") Then
                    parts.Add("user name " & how)
                End If
            End If

            If Not row.Supplied.ContainsKey(EmployeeImportPlan.PasswordKey) AndAlso
               row.ValueOf(EmployeeImportPlan.PasswordKey) <> String.Empty Then
                parts.Add("password from the file")
            End If

            Return String.Join("; ", parts)
        End Function

        Private Shared Function Enc(text As String) As String
            Return WebUtility.HtmlEncode(If(text, String.Empty))
        End Function

        Private Shared Function EncMultiline(text As String) As String
            Return Enc(text).Replace(vbCrLf, "<br>").Replace(vbLf, "<br>")
        End Function
    End Class
End Namespace
