Option Strict On
Option Explicit On

Imports System.Runtime.CompilerServices

Namespace SDC.Framework

    ''' <summary>
    ''' Raised when something tries to write while a read-only preview is open.
    '''
    ''' A distinct type rather than a plain Exception so a caller can tell "this was refused on
    ''' purpose" from "this failed". FW_Base_U's save handler shows the message and keeps the page
    ''' open, which is exactly the right behaviour: nothing was saved, and the page says so.
    ''' </summary>
    Public Class ReadOnlyPreviewException
        Inherits Exception

        Public Sub New(what As String)
            MyBase.New(ReadOnlyPreview.RefusalMessage(what))
        End Sub
    End Class

    ''' <summary>
    ''' A latch that makes the current thread refuse to write a record.
    '''
    ''' It exists because a maintenance page opened to be looked at can still write without anybody
    ''' pressing OK. Seven paths were found: the record save and its audit rows, the roles that ride
    ''' along with an employee save, the tab order manager, the page zoom keys, the background
    ''' colour picker and a help desk issue. Four of those need no button on the page at all - a
    ''' zoom keypress alone persists a row - which is why disabling OK is not a control and this is
    ''' checked at the write instead.
    '''
    ''' It started as "a preview writes nothing" and that was the wrong rule. What must not happen
    ''' is a preview touching **a record**; the rest of that list is preferences, and blocking those
    ''' produced a Save that appeared to do nothing and a zoom that would not be remembered. So the
    ''' rule is now: a preview never writes a record, and preferences behave exactly as they do on
    ''' any other page. Nothing to remember and no carve-outs to explain.
    '''
    ''' Gated: the record save, which throws, and the audit rows that belong to it, which skip - an
    ''' audit row for a save that never happened asserts a change nobody made. Not gated: the tab
    ''' order, the page zoom and the page background colour.
    '''
    ''' Marked thread-static and held only for as long as a modal preview is on screen. A latch that
    ''' outlived its window, or reached another thread, would silently stop real work from saving -
    ''' worse than the problem it solves. Begin returns a scope that clears it on Dispose, so an
    ''' exception cannot leave it set.
    ''' </summary>
    Public Module ReadOnlyPreview

        <ThreadStatic>
        Private active As Boolean

        ''' <summary>
        ''' What a preview says when it refuses. One wording, because two callers say it: the
        ''' exception thrown at the write, and the OK button, which says it before validating.
        ''' </summary>
        Public Function RefusalMessage(what As String) As String
            Return "THIS PAGE IS OPEN AS A READ-ONLY PREVIEW." & Environment.NewLine & Environment.NewLine &
                   what & " was not written, and nothing on this page has been saved." & Environment.NewLine & Environment.NewLine &
                   "Close the preview and open the page normally to make changes."
        End Function

        ''' <summary>Whether the current thread is inside a read-only preview.</summary>
        Public ReadOnly Property IsActive As Boolean
            Get
                Return active
            End Get
        End Property

        ''' <summary>
        ''' Opens a read-only scope. Use it with Using, never by hand - the point of the scope is
        ''' that the latch is cleared however the block is left.
        ''' </summary>
        Public Function Begin() As IDisposable
            Return New Scope()
        End Function

        ''' <summary>
        ''' Refuses a write that must not be reported as having succeeded, naming what was refused.
        ''' Called at the top of a writer, before it opens a connection.
        ''' </summary>
        Public Sub Refuse(what As String)
            If active Then Throw New ReadOnlyPreviewException(what)
        End Sub

        ''' <summary>
        ''' Whether a convenience write should be skipped. For the writes nobody asked for and
        ''' nobody is waiting on - tab order, zoom, page colour - where refusing loudly would
        ''' interrupt somebody reading a preview to tell them their zoom was not remembered.
        ''' </summary>
        Public Function ShouldSkip() As Boolean
            Return active
        End Function

        Private NotInheritable Class Scope
            Implements IDisposable

            Private ReadOnly wasActive As Boolean

            Public Sub New()
                wasActive = active
                active = True
            End Sub

            Public Sub Dispose() Implements IDisposable.Dispose
                active = wasActive
            End Sub
        End Class

    End Module
End Namespace
