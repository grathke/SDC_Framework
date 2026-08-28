USE WX_Framework;
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF OBJECT_ID(N'dbo.FW_IssueCategories', N'U') IS NOT NULL
   AND OBJECT_ID(N'dbo.FW_HD_IssueCategories', N'U') IS NULL
BEGIN
    EXEC sys.sp_rename N'dbo.FW_IssueCategories', N'FW_HD_IssueCategories';
END;

IF OBJECT_ID(N'dbo.FW_HD_IssueCategories', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FW_HD_IssueCategories
    (
        CategoryID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_FW_IssueCategories PRIMARY KEY,
        RegistrationID INT NULL,
        CategoryName VARCHAR(100) NOT NULL,
        Description VARCHAR(500) NULL,
        DisplayOrder INT NOT NULL CONSTRAINT DF_FW_IssueCategories_DisplayOrder DEFAULT (0),
        IsActive BIT NOT NULL CONSTRAINT DF_FW_IssueCategories_IsActive DEFAULT (1),
        CreatedBy INT NOT NULL CONSTRAINT DF_FW_IssueCategories_CreatedBy DEFAULT (0),
        CreatedOn DATETIME2(0) NOT NULL CONSTRAINT DF_FW_IssueCategories_CreatedOn DEFAULT SYSUTCDATETIME(),
        UpdatedBy INT NULL,
        UpdatedOn DATETIME2(0) NULL,
        RowVersion ROWVERSION NOT NULL,
        DeletedFlag BIT NOT NULL CONSTRAINT DF_FW_IssueCategories_DeletedFlag DEFAULT (0),
        DeletedBy INT NULL,
        DeletedOn DATETIME2(0) NULL
    );

    CREATE UNIQUE INDEX UX_FW_HD_IssueCategories_RegistrationName
        ON dbo.FW_HD_IssueCategories(RegistrationID, CategoryName)
        WHERE DeletedFlag = 0;

    CREATE INDEX IX_FW_HD_IssueCategories_RegistrationActive
        ON dbo.FW_HD_IssueCategories(RegistrationID, IsActive, DisplayOrder);
END;

DECLARE @SeedCategories TABLE
(
    CategoryName VARCHAR(100) NOT NULL,
    Description VARCHAR(500) NULL,
    DisplayOrder INT NOT NULL
);

INSERT INTO @SeedCategories (CategoryName, Description, DisplayOrder)
VALUES
    ('Problem', 'Something is not working as expected.', 10),
    ('Question / How To', 'Request for help or instructions.', 20),
    ('Suggestion', 'Idea for improving the application or workflow.', 30),
    ('Feature Request', 'Request for new functionality.', 40),
    ('Bug Report', 'A reproducible software defect.', 50),
    ('Data Correction', 'A request to correct inaccurate or incomplete data.', 60),
    ('Access / Permissions', 'A request for access, permissions, or role assistance.', 70),
    ('Other', 'A request that does not fit another category.', 80);

INSERT INTO dbo.FW_HD_IssueCategories
(
    RegistrationID,
    CategoryName,
    Description,
    DisplayOrder,
    IsActive,
    CreatedBy
)
SELECT
    NULL,
    seed.CategoryName,
    seed.Description,
    seed.DisplayOrder,
    1,
    0
FROM @SeedCategories AS seed
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.FW_HD_IssueCategories AS existing
    WHERE existing.RegistrationID IS NULL
      AND existing.CategoryName = seed.CategoryName
      AND existing.DeletedFlag = 0
);

SELECT
    CategoryID,
    RegistrationID,
    CategoryName,
    Description,
    DisplayOrder,
    IsActive,
    DeletedFlag
FROM dbo.FW_HD_IssueCategories
WHERE RegistrationID IS NULL
ORDER BY DisplayOrder, CategoryName;
