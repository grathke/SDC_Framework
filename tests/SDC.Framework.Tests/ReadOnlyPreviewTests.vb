Option Strict On
Option Explicit On

Imports Microsoft.VisualStudio.TestTools.UnitTesting
Imports SDC.Framework

Namespace SDC.Framework.Tests

    ''' <summary>
    ''' The latch that stops a page written to be looked at from writing.
    '''
    ''' Worth pinning rather than trusting, because both ways of getting it wrong are silent. A
    ''' latch that fails to set lets a preview save over somebody's record; a latch left set stops
    ''' real work saving and looks like a broken application, with nothing on screen to say why.
    ''' </summary>
    <TestClass>
    Public Class ReadOnlyPreviewTests

        <TestMethod>
        Public Sub NotActiveByDefault()
            Assert.IsFalse(ReadOnlyPreview.IsActive, "A thread that has opened no preview must be free to write.")
        End Sub

        <TestMethod>
        Public Sub ActiveInsideTheScope()
            Using ReadOnlyPreview.Begin()
                Assert.IsTrue(ReadOnlyPreview.IsActive)
                Assert.IsTrue(ReadOnlyPreview.ShouldSkip())
            End Using

            Assert.IsFalse(ReadOnlyPreview.IsActive, "The scope must clear the latch when it is left.")
        End Sub

        ''' <summary>
        ''' The case that matters most. A latch left set by a failure would stop every later save
        ''' in the session, and the page would report a refusal nobody could explain.
        ''' </summary>
        <TestMethod>
        Public Sub ClearedWhenTheScopeIsLeftByAnException()
            Try
                Using ReadOnlyPreview.Begin()
                    Throw New InvalidOperationException("the page threw while being previewed")
                End Using
            Catch ex As InvalidOperationException
                ' Expected - what matters is the state afterwards.
            End Try

            Assert.IsFalse(ReadOnlyPreview.IsActive, "An exception must not leave the application unable to save.")
        End Sub

        <TestMethod>
        Public Sub NestedScopesRestoreRatherThanClear()
            Using ReadOnlyPreview.Begin()
                Using ReadOnlyPreview.Begin()
                    Assert.IsTrue(ReadOnlyPreview.IsActive)
                End Using

                Assert.IsTrue(ReadOnlyPreview.IsActive, "Leaving an inner scope must not unlatch the outer one.")
            End Using

            Assert.IsFalse(ReadOnlyPreview.IsActive)
        End Sub

        <TestMethod>
        Public Sub RefuseThrowsOnlyWhenLatched()
            ReadOnlyPreview.Refuse("The record")

            Using ReadOnlyPreview.Begin()
                Dim threw = False
                Try
                    ReadOnlyPreview.Refuse("The record")
                Catch ex As ReadOnlyPreviewException
                    threw = True
                End Try

                Assert.IsTrue(threw, "Refuse must throw while latched.")
            End Using
        End Sub

        ''' <summary>
        ''' The message is the only thing the person sees when a preview refuses, so it has to say
        ''' that nothing was saved rather than merely that something failed.
        ''' </summary>
        <TestMethod>
        Public Sub RefusalSaysNothingWasSaved()
            Using ReadOnlyPreview.Begin()
                Try
                    ReadOnlyPreview.Refuse("The record")
                    Assert.Fail("Refuse must throw while latched.")
                Catch ex As ReadOnlyPreviewException
                    StringAssert.Contains(ex.Message, "READ-ONLY PREVIEW")
                    StringAssert.Contains(ex.Message, "has been saved")
                End Try
            End Using
        End Sub

    End Class
End Namespace
