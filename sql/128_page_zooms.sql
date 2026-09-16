-- 128_page_zooms.sql
--
-- Remembers how large each person reads each page.
--
-- Keyed on the login rather than the employee. Employee was the first choice - the zoom belongs to
-- the person, and a login is one way of being them - but not everybody who signs in is an employee.
-- A contractor has a login and no employee row, and keying on FW_Employees left them with nowhere
-- to store anything and no zoom ever restored.
--
-- Keyed on the page's class name rather than its caption, because a caption is an administrator's
-- to change and this has to keep pointing at the same window afterwards.
--
-- Read once per session, not once per page. Every row for the user is loaded at login and the page
-- opens cost nothing; a row is written only when somebody actually changed the zoom, and only as
-- the page closes.
--
-- No row means 1.0. The table holds what was chosen, not a setting for every page in existence.
-- A row of exactly 1.00 is a choice too: it means somebody zoomed a page and put it back, and
-- without it reopening would restore the larger size they had just rejected.

SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID('dbo.FW_PageZooms', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_PageZooms (
        PageZoomID     int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_PageZooms PRIMARY KEY,
        RegistrationID int           NULL,
        UserID         int           NOT NULL,
        PageName       varchar(128)  NOT NULL,
        ZoomFactor     decimal(4,2)  NOT NULL CONSTRAINT DF_FW_PageZooms_ZoomFactor DEFAULT (1.00),
        CreatedBy      int           NULL,
        CreatedOn      datetime      NULL CONSTRAINT DF_FW_PageZooms_CreatedOn DEFAULT (GETDATE()),
        UpdatedBy      int           NULL,
        UpdatedOn      datetime      NULL,
        DeletedFlag    bit           NOT NULL CONSTRAINT DF_FW_PageZooms_DeletedFlag DEFAULT (0),
        DeletedBy      int           NULL,
        DeletedOn      datetime      NULL,
        RowVersion     rowversion    NOT NULL
    );

    -- One zoom per login per page. The upsert relies on this rather than on reading first.
    CREATE UNIQUE INDEX UX_FW_PageZooms_User_Page
        ON dbo.FW_PageZooms (UserID, PageName);
END
GO
