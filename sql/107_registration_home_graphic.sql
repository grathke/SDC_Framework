/*
    107_registration_home_graphic.sql

    The picture the main menu's Home Region shows, per registration.

    File name only, never a path: IconScaler resolves assets\images, and a stored path would
    break when the application moves to the server.

    Re-runnable. Seeds only where the column is still null, so a chosen picture is never
    overwritten by a second run.
*/

SET NOCOUNT ON;

IF COL_LENGTH('dbo.FW_Registration', 'HomeGraphic') IS NULL
BEGIN
    ALTER TABLE dbo.FW_Registration ADD HomeGraphic VARCHAR(260) NULL;
    PRINT 'Added dbo.FW_Registration.HomeGraphic';
END
ELSE
    PRINT 'dbo.FW_Registration.HomeGraphic already exists';
GO

UPDATE dbo.FW_Registration
   SET HomeGraphic = 'Wise City.png'
 WHERE RegistrationID = 1 AND HomeGraphic IS NULL;

UPDATE dbo.FW_Registration
   SET HomeGraphic = 'CityNexus Saraland Event Center Banner.png'
 WHERE RegistrationID = 2 AND HomeGraphic IS NULL;

SELECT RegistrationID, RegName, ISNULL(HomeGraphic, '(none)') AS HomeGraphic
  FROM dbo.FW_Registration
 ORDER BY RegistrationID;
