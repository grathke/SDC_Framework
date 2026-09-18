/*
    Remove the CTY_TestEmployee page row.

    The page was generated on 2026-09-18 to prove that a browse page is now written as two files -
    a generated half and a companion - which it did. Its four files and its ribbon tile have been
    deleted; this is the FW_Pages row that opening it created.

    No role, field, layout or dashboard rows exist for it: the page was opened once and never
    granted to anybody, and it ran against FW_Employees, which has its own rows.

    The generation request in FW_GeneratedPages is deliberately left in place - it is being kept
    for comparison.
*/

DELETE FROM dbo.FW_Pages
WHERE WindowOrPage IN ('CTY_TestEmployee_B', 'CTY_TestEmployee_U');

SELECT WindowOrPage, DB_Table
FROM dbo.FW_Pages
WHERE WindowOrPage LIKE '%Test%';
