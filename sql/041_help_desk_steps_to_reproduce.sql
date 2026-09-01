/*
    041_help_desk_steps_to_reproduce.sql
    ----------------------------------------------------------------------------------------------
    Adds reproduction steps to a help desk report.

    Why
    ---
    A defect that cannot be reproduced cannot be fixed, and "it does not work" is the report that
    costs the most time on both sides. With the problem, the expectation and the steps captured
    together, a ticket carries everything needed to act on it.

    Visibility reuses RequiresPage rather than introducing a third flag: a category that needs the
    page it happened on is a defect, and only a defect has steps to reproduce. Today that means
    Problem and Bug Report.

        Describe the problem   every category
        How it should behave   RequiresExpectedBehavior
        Steps to reproduce     RequiresPage

    Each is App Admin required while it is visible, so a hidden field never blocks a save.

    Rollback
    --------
    At the bottom of this file.
*/

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.FW_HD_Issues')
                 AND name = 'StepsToReproduce')
BEGIN
    ALTER TABLE dbo.FW_HD_Issues ADD StepsToReproduce varchar(max) NULL;
END
GO

/*
    ----------------------------------------------------------------------------------------------
    ROLLBACK - run this block to undo the migration.
    ----------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.FW_HD_Issues')
             AND name = 'StepsToReproduce')
BEGIN
    ALTER TABLE dbo.FW_HD_Issues DROP COLUMN StepsToReproduce;
END
GO
*/
