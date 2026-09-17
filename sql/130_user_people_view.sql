/*
    130_user_people_view.sql

    dbo.FW_UserPeople - who each login belongs to, and which kind of person that is.

    Asked for on 2026-09-17, for the Switch User search. A UserID on its own does not say where
    the login came from; this does, without storing it anywhere. One branch per person table, and
    each branch stamps its own PersonType, so the answer cannot go stale or disagree with the
    foreign key it was derived from.

    Adding contractors later is one more branch and no change to anything that reads the view: a
    Switch User page selects from it, so Company Name becomes searchable when the contractor
    branch starts filling it in. The commented block below is that branch, written out so the next
    person does not have to work out the shape again.

    Built on vw_FW_CurrentUser rather than FW_Users, because that is the view login itself uses:
    it excludes deleted accounts, and who may be signed in as is not a question to answer twice
    with two sets of rules. FW_Users is joined for UserName, which that view does not carry.

    The last branch is logins with no person row at all - two exist in registration 1. Without it
    they could never be found, which is a search that cannot see accounts rather than a tidy one.

    Columns:
      UserId        the login. The Switch User page aliases it AS PK.
      PersonType    'Employee', 'Login only', later 'Contractor'
      PersonID      EmployeeID, or NULL where there is no person row
      IsActive      the login AND the person. Either one inactive reads as inactive, which is
                    what the existing search does and what the "found, but is inactive" message
                    reports.
      LoginActive   so a page can say which of the two it was
      PersonActive
      CompanyName   NULL until contractors exist

    Re-runnable.
*/

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

CREATE OR ALTER VIEW dbo.FW_UserPeople
AS
SELECT
    v.UserId,
    v.RegistrationID,
    CAST('Employee' AS varchar(20))                         AS PersonType,
    e.EmployeeID                                            AS PersonID,
    e.FirstName,
    e.LastName,
    e.FirstLast,
    u.UserName,
    COALESCE(NULLIF(v.Email, ''), e.Email)                  AS Email,
    CAST(NULL AS varchar(75))                               AS CompanyName,
    CAST(CASE WHEN ISNULL(v.IsActive, 0) = 1
               AND ISNULL(e.IsActive, 1) = 1 THEN 1 ELSE 0 END AS bit) AS IsActive,
    CAST(ISNULL(v.IsActive, 0) AS bit)                      AS LoginActive,
    CAST(ISNULL(e.IsActive, 1) AS bit)                      AS PersonActive
  FROM dbo.vw_FW_CurrentUser v
 INNER JOIN dbo.FW_Users u
    ON u.UserId = v.UserId
 INNER JOIN dbo.FW_Employees e
    ON e.UserId = v.UserId
   AND ISNULL(e.DeletedFlag, 0) = 0

UNION ALL

/*  Contractors, when the table exists. Nothing else changes.

SELECT
    v.UserId,
    v.RegistrationID,
    CAST('Contractor' AS varchar(20)),
    c.ContractorID,
    c.FirstName,
    c.LastName,
    c.FirstLast,
    u.UserName,
    COALESCE(NULLIF(v.Email, ''), c.Email),
    c.CompanyName,
    CAST(CASE WHEN ISNULL(v.IsActive, 0) = 1
               AND ISNULL(c.IsActive, 1) = 1 THEN 1 ELSE 0 END AS bit),
    CAST(ISNULL(v.IsActive, 0) AS bit),
    CAST(ISNULL(c.IsActive, 1) AS bit)
  FROM dbo.vw_FW_CurrentUser v
 INNER JOIN dbo.FW_Users u ON u.UserId = v.UserId
 INNER JOIN dbo.FW_Contractors c ON c.UserId = v.UserId AND ISNULL(c.DeletedFlag, 0) = 0

UNION ALL
*/

SELECT
    v.UserId,
    v.RegistrationID,
    CAST('Login only' AS varchar(20))                       AS PersonType,
    CAST(NULL AS int)                                       AS PersonID,
    v.FirstName,
    v.LastName,
    v.FirstLast,
    u.UserName,
    v.Email,
    CAST(NULL AS varchar(75))                               AS CompanyName,
    CAST(ISNULL(v.IsActive, 0) AS bit)                      AS IsActive,
    CAST(ISNULL(v.IsActive, 0) AS bit)                      AS LoginActive,
    CAST(1 AS bit)                                          AS PersonActive
  FROM dbo.vw_FW_CurrentUser v
 INNER JOIN dbo.FW_Users u
    ON u.UserId = v.UserId
 WHERE NOT EXISTS (SELECT 1
                     FROM dbo.FW_Employees e
                    WHERE e.UserId = v.UserId
                      AND ISNULL(e.DeletedFlag, 0) = 0);
GO

SET NOCOUNT ON;

SELECT PersonType, COUNT(*) AS Rows_, SUM(CASE WHEN IsActive = 1 THEN 1 ELSE 0 END) AS Active
  FROM dbo.FW_UserPeople
 GROUP BY PersonType
 ORDER BY PersonType;

SELECT RegistrationID, UserId, PersonType, PersonID, FirstLast, UserName, Email, IsActive, LoginActive, PersonActive
  FROM dbo.FW_UserPeople
 ORDER BY RegistrationID, UserId;
