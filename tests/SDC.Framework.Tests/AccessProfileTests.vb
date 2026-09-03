Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' Permissions run through every page, and a mistake here is silent - a capability quietly
    ''' granted or withheld looks like ordinary behavior. These pin the rules rather than the
    ''' implementation.
    ''' </summary>
    <TestClass>
    Public Class AccessProfileTests

        Private Shared Function ProfileWith(tableName As String, capabilities As AccessCapability) As AccessProfile
            Dim profile As New AccessProfile()
            profile.SetTablePermissions(tableName, capabilities)
            Return profile
        End Function

        <TestMethod>
        Public Sub UnknownTable_IsDenied()
            Dim profile As New AccessProfile()

            Assert.IsFalse(profile.Can("FW_Users", AccessCapability.Read),
                           "An unconfigured table must default to denied, not allowed.")
            Assert.AreEqual(AccessCapability.None, profile.GetCapabilities("FW_Users"))
        End Sub

        <TestMethod>
        Public Sub NoCapabilityRequired_IsAllowed()
            Dim profile As New AccessProfile()
            Assert.IsTrue(profile.Can("FW_Users", AccessCapability.None))
        End Sub

        <TestMethod>
        Public Sub GrantedCapability_IsAllowed_AndOthersAreNot()
            Dim profile = ProfileWith("FW_Users", AccessCapability.Read Or AccessCapability.Update)

            Assert.IsTrue(profile.Can("FW_Users", AccessCapability.Read))
            Assert.IsTrue(profile.Can("FW_Users", AccessCapability.Update))
            Assert.IsFalse(profile.Can("FW_Users", AccessCapability.Delete))
            Assert.IsFalse(profile.Can("FW_Users", AccessCapability.Create))
        End Sub

        <TestMethod>
        Public Sub CombinedRequirement_NeedsEveryFlag()
            Dim profile = ProfileWith("FW_Users", AccessCapability.Read)

            Assert.IsFalse(profile.Can("FW_Users", AccessCapability.Read Or AccessCapability.Update),
                           "Can must require every requested flag, not any of them.")
        End Sub

        <TestMethod>
        Public Sub TableName_MatchesRegardlessOfCaseSchemaOrFrameworkPrefix()
            Dim profile = ProfileWith("FW_Entity", AccessCapability.Read)

            ' Pages hold the table name in whatever form their SQL or metadata gave them.
            For Each spelling In New String() {"FW_Entity", "FW_ENTITY", "fw_entity", "dbo.FW_Entity", "Entity"}
                Assert.IsTrue(profile.Can(spelling, AccessCapability.Read),
                              "Expected '" & spelling & "' to resolve to the FW_Entity permission.")
            Next
        End Sub

        <TestMethod>
        Public Sub TableName_IgnoresUnderscoresAndSpaces()
            Dim profile = ProfileWith("FW_Role_Fields", AccessCapability.Read)
            Assert.IsTrue(profile.Can("RoleFields", AccessCapability.Read))
        End Sub

        <TestMethod>
        Public Sub RoleLevel_DoesNotGrantAnything()
            ' RoleLevel is recorded but deliberately grants nothing on its own. An administrator
            ' with no FW_RoleDetails rows has no access, and that is the intended behavior.
            Dim profile As New AccessProfile()
            profile.SetRole(1, "Application Admin")

            Assert.AreEqual(1, profile.RoleLevel)
            Assert.IsFalse(profile.Can("FW_Users", AccessCapability.Read))
        End Sub

        <TestMethod>
        Public Sub UnnamedRole_FallsBackToUnassigned()
            Dim profile As New AccessProfile()
            profile.SetRole(3, "   ")
            Assert.AreEqual("Unassigned", profile.RoleName)
        End Sub

        <TestMethod>
        Public Sub BlankTableName_IsIgnoredAndDenied()
            Dim profile As New AccessProfile()
            profile.SetTablePermissions("   ", AccessCapability.Delete)

            Assert.AreEqual(AccessCapability.None, profile.GetCapabilities("   "))
            Assert.IsFalse(profile.Can("", AccessCapability.Delete))
        End Sub

    End Class

    ''' <summary>
    ''' The mapping from FW_RoleDetails columns to capabilities has couplings that are easy to
    ''' change by accident. These record what the mapping actually does today.
    ''' </summary>
    <TestClass>
    Public Class RoleTableAccessEntryTests

        <TestMethod>
        Public Sub EachFlag_MapsToItsCapability()
            Dim entry As New RoleTableAccessEntry With {
                .TableName = "FW_Users",
                .CanReadOnly = True,
                .CanCreate = True,
                .CanUpdate = True,
                .CanDelete = True
            }

            Dim capabilities = entry.ToCapabilities()

            Assert.IsTrue(capabilities.HasFlag(AccessCapability.Read))
            Assert.IsTrue(capabilities.HasFlag(AccessCapability.Create))
            Assert.IsTrue(capabilities.HasFlag(AccessCapability.Update))
            Assert.IsTrue(capabilities.HasFlag(AccessCapability.Delete))
        End Sub

        <TestMethod>
        Public Sub ExpandQbe_ImpliesUseQbe()
            Dim entry As New RoleTableAccessEntry With {.TableName = "FW_Users", .CanExpandQbe = True}
            Dim capabilities = entry.ToCapabilities()

            Assert.IsTrue(capabilities.HasFlag(AccessCapability.ExpandQbe))
            Assert.IsTrue(capabilities.HasFlag(AccessCapability.UseQbe),
                          "Expanding QBE without being able to use it would be meaningless.")
        End Sub

        <TestMethod>
        Public Sub UseQbe_DoesNotImplyExpandQbe()
            Dim entry As New RoleTableAccessEntry With {.TableName = "FW_Users", .CanUseQbe = True}
            Dim capabilities = entry.ToCapabilities()

            Assert.IsTrue(capabilities.HasFlag(AccessCapability.UseQbe))
            Assert.IsFalse(capabilities.HasFlag(AccessCapability.ExpandQbe))
        End Sub

        <TestMethod>
        Public Sub UpdateAndDelete_DoNotImplyRead()
            ' Recorded because it is surprising: a role can update a record it cannot read.
            Dim entry As New RoleTableAccessEntry With {
                .TableName = "FW_Users",
                .CanUpdate = True,
                .CanDelete = True
            }

            Assert.IsFalse(entry.ToCapabilities().HasFlag(AccessCapability.Read))
        End Sub

        <TestMethod>
        Public Sub Execute_IsDerivedFromImportOrViewScope()
            ' Execute has no column of its own. It appears when any of three unrelated flags is
            ' set, so it does not mean what its name suggests.
            Assert.IsTrue(New RoleTableAccessEntry With {.CanImport = True}.ToCapabilities().HasFlag(AccessCapability.Execute))
            Assert.IsTrue(New RoleTableAccessEntry With {.CanViewAllRecords = True}.ToCapabilities().HasFlag(AccessCapability.Execute))
            Assert.IsTrue(New RoleTableAccessEntry With {.CanViewOnlyMy = True}.ToCapabilities().HasFlag(AccessCapability.Execute))

            Assert.IsFalse(New RoleTableAccessEntry With {.CanUpdate = True}.ToCapabilities().HasFlag(AccessCapability.Execute))
        End Sub

        <TestMethod>
        Public Sub NoFlags_ProducesNone()
            Assert.AreEqual(AccessCapability.None, New RoleTableAccessEntry().ToCapabilities())
        End Sub

    End Class

End Namespace
