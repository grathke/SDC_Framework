/*
    121_role_template.sql

    FW_RoleTemplate - the roles a new registration starts with.

    Same structure as FW_Roles, seeded from registration 2, which is the set arrived at for
    Saraland: City Admin, User RW, User, User RO. RegistrationID is kept so the source is
    visible, but a template belongs to no registration in the sense FW_Roles does.

    SELECT INTO rather than a written-out column list, so the two cannot drift apart if FW_Roles
    gains a column. It copies no key, no identity and no constraints, which is what a template
    wants - the rows are read and copied, never joined to.

    Re-runnable: does nothing if the table already exists.
*/

SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;

IF OBJECT_ID('dbo.FW_RoleTemplate', 'U') IS NOT NULL
BEGIN
    PRINT 'dbo.FW_RoleTemplate already exists';
    SELECT * FROM dbo.FW_RoleTemplate ORDER BY ID;
    RETURN;
END

SELECT *
  INTO dbo.FW_RoleTemplate
  FROM dbo.FW_Roles
 WHERE RegistrationID = 2;

PRINT 'Created dbo.FW_RoleTemplate with ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' row(s)';

SELECT ID, RegistrationID, RoleName, DisplayOrder, IsActive,
       Typ_AppAdmin, Typ_CompanyAdmin, Typ_RW, Typ_RO, Typ_User
  FROM dbo.FW_RoleTemplate
 ORDER BY ID;
