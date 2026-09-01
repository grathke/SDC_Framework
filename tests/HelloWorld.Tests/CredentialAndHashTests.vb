Option Strict On
Option Explicit On

Imports System
Imports System.Windows.Forms
Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports HelloWorld.HelloWorld

Namespace HelloWorldTests

    ''' <summary>
    ''' The keyed hash is shared between user passwords and the developer access gate, so a change
    ''' to it would silently invalidate both.
    ''' </summary>
    <TestClass>
    Public Class KeyedHashTests

        Private Const GateKey As String = "WXFramework.DeveloperAccessGate.v1"

        <TestMethod>
        Public Sub SameInputAndKey_ProducesSameHash()
            Assert.AreEqual(DataAccess.ComputeKeyedHashHex("Buttons", GateKey),
                            DataAccess.ComputeKeyedHashHex("Buttons", GateKey))
        End Sub

        <TestMethod>
        Public Sub DifferentKey_ProducesDifferentHash()
            Assert.AreNotEqual(DataAccess.ComputeKeyedHashHex("Buttons", GateKey),
                               DataAccess.ComputeKeyedHashHex("Buttons", "another-key"))
        End Sub

        <TestMethod>
        Public Sub HashIsCaseSensitive()
            ' Rs0lt0ff and rs0lt0ff are both accepted by the gate, but only because both are
            ' listed. The hash itself must not fold case, or listing them separately would be
            ' pointless and every password would become case-insensitive.
            Assert.AreNotEqual(DataAccess.ComputeKeyedHashHex("Rs0lt0ff", GateKey),
                               DataAccess.ComputeKeyedHashHex("rs0lt0ff", GateKey))
        End Sub

        <TestMethod>
        Public Sub SpacesAreStripped_MatchingTheUserPasswordRule()
            Assert.AreEqual(DataAccess.ComputeKeyedHashHex("Buttons", GateKey),
                            DataAccess.ComputeKeyedHashHex(" But tons ", GateKey))
        End Sub

        <TestMethod>
        Public Sub ProducesLowerCaseHexOfExpectedLength()
            Dim hash = DataAccess.ComputeKeyedHashHex("Buttons", GateKey)

            Assert.AreEqual(128, hash.Length, "HMAC-SHA512 is 64 bytes, so 128 hex characters.")
            Assert.AreEqual(hash.ToLowerInvariant(), hash, "Stored gate values are lower case hex.")
        End Sub

        <TestMethod>
        Public Sub NothingKeyOrValue_DoesNotThrow()
            Assert.IsFalse(String.IsNullOrEmpty(DataAccess.ComputeKeyedHashHex(Nothing, Nothing)))
        End Sub

    End Class

    ''' <summary>
    ''' Connection string assembly and the empty-combo test. Neither touches a database.
    ''' </summary>
    <TestClass>
    Public Class DatabaseSettingsTests

        <TestMethod>
        Public Sub BuildConnectionString_IncludesEveryPart()
            Dim settings As New DatabaseConfigStore.DatabaseSettings With {
                .Server = " BEELINK ",
                .UserId = " sa ",
                .Password = "secret",
                .Database = " WX_Framework ",
                .Encrypt = False,
                .TrustServerCertificate = True
            }

            Dim result = DatabaseConfigStore.BuildConnectionString(settings)

            StringAssert.Contains(result, "Server=BEELINK")
            StringAssert.Contains(result, "User Id=sa")
            StringAssert.Contains(result, "Password=secret")
            StringAssert.Contains(result, "Initial Catalog=WX_Framework")
            StringAssert.Contains(result, "Encrypt=False")
            StringAssert.Contains(result, "TrustServerCertificate=True")
        End Sub

        <TestMethod>
        Public Sub BuildConnectionString_WithNothing_ReturnsEmpty()
            Assert.AreEqual(String.Empty, DatabaseConfigStore.BuildConnectionString(Nothing))
        End Sub

        <TestMethod>
        Public Sub UsingEnvironmentCredentials_TracksThePasswordVariable()
            Dim original = Environment.GetEnvironmentVariable("HELLOWORLD_DB_PASSWORD")
            Dim originalFull = Environment.GetEnvironmentVariable("HELLOWORLD_DB_CONNECTION")

            Try
                Environment.SetEnvironmentVariable("HELLOWORLD_DB_CONNECTION", Nothing)

                Environment.SetEnvironmentVariable("HELLOWORLD_DB_PASSWORD", "something")
                Assert.IsTrue(DataAccess.IsUsingEnvironmentCredentials())

                Environment.SetEnvironmentVariable("HELLOWORLD_DB_PASSWORD", Nothing)
                Assert.IsFalse(DataAccess.IsUsingEnvironmentCredentials())

                ' A full connection string counts on its own.
                Environment.SetEnvironmentVariable("HELLOWORLD_DB_CONNECTION", "Server=x;")
                Assert.IsTrue(DataAccess.IsUsingEnvironmentCredentials())
            Finally
                Environment.SetEnvironmentVariable("HELLOWORLD_DB_PASSWORD", original)
                Environment.SetEnvironmentVariable("HELLOWORLD_DB_CONNECTION", originalFull)
            End Try
        End Sub

        <TestMethod>
        Public Sub EnvironmentPassword_CountsAsConfigured()
            ' The regression this pins: the startup check once looked only at the environment and
            ' reported "not configured" whenever the password came from anywhere else.
            Dim original = Environment.GetEnvironmentVariable("HELLOWORLD_DB_PASSWORD")

            Try
                Environment.SetEnvironmentVariable("HELLOWORLD_DB_PASSWORD", "something")
                Assert.AreEqual(String.Empty, DataAccess.GetMissingConfigurationMessage())
            Finally
                Environment.SetEnvironmentVariable("HELLOWORLD_DB_PASSWORD", original)
            End Try
        End Sub

        <TestMethod>
        Public Sub TestConnection_WithNoConnectionString_ReportsRatherThanThrows()
            Assert.AreNotEqual(String.Empty, DataAccess.TestConnection(String.Empty))
        End Sub

    End Class

    ''' <summary>
    ''' Single owner of the empty-combo test, shared by required-field validation and the red
    ''' border rule. A ComboBox needs no form, so this runs headless.
    ''' </summary>
    <TestClass>
    Public Class EmptyComboSelectionTests

        Private Shared Function ComboWith(values As (Value As Object, Display As String)()) As ComboBox
            Dim table As New Data.DataTable()
            table.Columns.Add("Value", If(values.Length > 0 AndAlso TypeOf values(0).Value Is Integer, GetType(Integer), GetType(String)))
            table.Columns.Add("Display", GetType(String))

            For Each item In values
                table.Rows.Add(item.Value, item.Display)
            Next

            ' A ComboBox normally inherits its BindingContext from the form. There is no form
            ' here, so supply one - without it the data source never binds, Items stays empty and
            ' SelectedValue is always Nothing.
            Dim combo As New ComboBox() With {.BindingContext = New BindingContext()}
            combo.DataSource = table
            combo.ValueMember = "Value"
            combo.DisplayMember = "Display"
            Return combo
        End Function

        <TestMethod>
        Public Sub Nothing_IsEmpty()
            Assert.IsTrue(DataAccess.IsEmptyComboSelection(Nothing))
        End Sub

        <TestMethod>
        Public Sub NoSelection_IsEmpty()
            Using combo As New ComboBox()
                Assert.IsTrue(DataAccess.IsEmptyComboSelection(combo))
            End Using
        End Sub

        <TestMethod>
        Public Sub NumericPlaceholderZero_IsEmpty()
            Using combo = ComboWith({(CObj(0), "Make a Selection"), (CObj(7), "Role-Based")})
                combo.SelectedIndex = 0
                Assert.IsTrue(DataAccess.IsEmptyComboSelection(combo))
            End Using
        End Sub

        <TestMethod>
        Public Sub RealNumericSelection_IsNotEmpty()
            Using combo = ComboWith({(CObj(0), "Make a Selection"), (CObj(7), "Role-Based")})
                combo.SelectedIndex = 1
                Assert.IsFalse(DataAccess.IsEmptyComboSelection(combo))
            End Using
        End Sub

        <TestMethod>
        Public Sub EmptyStringPlaceholder_IsEmpty()
            Using combo = ComboWith({(CObj(""), "Make a Selection"), (CObj("BR_RoleBased"), "Role-Based")})
                combo.SelectedIndex = 0
                Assert.IsTrue(DataAccess.IsEmptyComboSelection(combo))
            End Using
        End Sub

        <TestMethod>
        Public Sub TextValuedSelection_IsNotEmpty()
            ' The defect this pins: the old test parsed SelectedValue as an integer and called
            ' anything that did not parse empty, so a real text-keyed selection read as missing.
            Using combo = ComboWith({(CObj(""), "Make a Selection"), (CObj("BR_RoleBased"), "Role-Based")})
                combo.SelectedIndex = 1
                Assert.IsFalse(DataAccess.IsEmptyComboSelection(combo),
                               "A non-numeric value is a genuine selection.")
            End Using
        End Sub

        <TestMethod>
        Public Sub UnboundPlaceholderSelection_IsEmpty()
            Using combo As New ComboBox()
                combo.Items.AddRange(New Object() {DataAccess.EmptyComboPlaceholder, "Dashboard_Application"})
                combo.SelectedIndex = 0
                Assert.IsTrue(DataAccess.IsEmptyComboSelection(combo),
                              "Sitting on the placeholder is selecting nothing.")
            End Using
        End Sub

        <TestMethod>
        Public Sub UnboundRealSelection_IsNotEmpty()
            ' The defect this pins: a combo filled with plain items has no SelectedValue, so the
            ' bound-combo rules called every selection empty and the field stayed red however the
            ' user answered it.
            Using combo As New ComboBox()
                combo.Items.AddRange(New Object() {DataAccess.EmptyComboPlaceholder, "Dashboard_Application"})
                combo.SelectedIndex = 1
                Assert.IsFalse(DataAccess.IsEmptyComboSelection(combo),
                               "An item selected from an unbound list is a genuine selection.")
            End Using
        End Sub

    End Class

End Namespace
