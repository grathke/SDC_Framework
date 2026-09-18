/*
    Remove the generation request for FW_UserAccessDiagnostic_B.

    The page was generated once on 2026-08-19 and has been built by hand since - 878 lines in the
    single file a browse page was before it was split into a generated half and a companion. There
    is no half for that work to live in, so regenerating it would replace all of it, and the page
    has no stored baseline so nothing ever flagged it as hand-edited.

    It was soft-deleted first and the Update button now refuses it, but a soft-deleted request is a
    thing somebody restores. The page is a hand-written framework page like Roles_U, so the honest
    state is no request at all rather than one that is hidden.

    Physical rather than soft, deliberately: soft delete is what was tried, and the point is to
    remove the claim that this page is generated, not to file it away. CLAUDE.md records the page
    as an exception in the same change, and its dashboard button was renamed off "generated".

    What is lost: the field choices the page was born from, five weeks and 878 lines ago. They
    describe a page that no longer exists.
*/

DELETE FROM dbo.FW_GeneratedPages
WHERE BrowsePageName = 'FW_UserAccessDiagnostic_B';

SELECT GeneratedPageID, RequestName, BrowsePageName, ISNULL(DeletedFlag, 0) AS DeletedFlag
FROM dbo.FW_GeneratedPages
ORDER BY GeneratedPageID;
