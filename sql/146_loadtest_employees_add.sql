-- 146_loadtest_employees_add.sql
--
-- TEST DATA. Adds 10,000 employees so the QBE Find button has something to search through and the
-- health page's timings have something to show. NOT FOR A CUSTOMER DATABASE.
--
-- Run 147_loadtest_employees_remove.sql to take them out again.
--
-- WHY USER ROWS TOO. FW_Employees.UserId is NOT NULL and carries FK_FW_Employees_User, so an
-- employee cannot exist without a user. Roles are NOT needed - a role is what lets somebody log in
-- and see things, and these never will. FW_EmployeeRoles is left alone entirely, and no password
-- hash is written: TryAuthenticate refuses outright when the stored hash is empty, so these
-- accounts cannot be signed into even by accident.
--
-- USERNAME IS UNIQUE, and has to be: UX_FW_Users_UserName is a unique index. The sequence number
-- guarantees it - lt00001@loadtest.invalid to lt10000@loadtest.invalid - rather than anything
-- derived from the name, which would collide the moment two people shared one.
--
-- THE MARKER. Every row carries '@loadtest.invalid' in its UserName. The .invalid TLD is reserved
-- by RFC 2606 and can never be a real address, so the removal script cannot match a record
-- somebody actually uses. That is why it is this rather than a name prefix or a range of ids - an
-- id range stops being true the moment anything else is inserted.
--
-- HOW THE NAMES ARE PAIRED, because the obvious way is wrong. Taking both the forename and the
-- surname from n directly - (n % 50) and (n % 120) - cycles them in lockstep: the pair repeats
-- every LCM(50,120) = 600 rows, so 10,000 rows would hold 600 distinct people repeated seventeen
-- times each. Dividing before the second modulo enumerates every combination instead: forename
-- (n % 50), surname ((n / 50) % 120), which walks 6,000 distinct pairs before any repeats.
--
-- WHAT THIS DOES TO THE APPLICATION WHILE IT IS IN PLACE:
--
--   * The Employees browse page lists 10,014 rows. That is the intent.
--   * The Assigned Manager combo on FW_Employees_U reads employees, so it holds 10,014 entries
--     and will be slow. Expect it rather than hunting for a new bug.
--   * Switch User searches employees, so it is affected too.
--   * Anything counting employees reads ten thousand too many.
--
-- These are created IsActive = 0 to limit that. Change the flag below if the test needs them
-- active, knowing what it costs.

SET NOCOUNT ON;

-- Required, not optional. FW_Users carries indexed computed columns (FirstLast, LastFirst), and
-- SQL Server refuses any INSERT touching those unless QUOTED_IDENTIFIER is ON. sqlcmd defaults it
-- OFF, so a script that runs perfectly in SSMS fails here without this line.
SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM dbo.FW_Employees WHERE UserName LIKE '%@loadtest.invalid')
BEGIN
    PRINT 'ABORTED: load test employees already exist. Run 147 first.';
    SET NOEXEC ON;
END
GO

DECLARE @Rows int = 10000;
DECLARE @RegistrationID int = 1;
DECLARE @IsActive bit = 0;

DECLARE @Forenames TABLE (n int NOT NULL PRIMARY KEY, v varchar(45));
INSERT INTO @Forenames (n, v) VALUES
 (0,'James'),(1,'Maria'),(2,'Robert'),(3,'Patricia'),(4,'Michael'),(5,'Jennifer'),(6,'David'),
 (7,'Linda'),(8,'William'),(9,'Elizabeth'),(10,'Richard'),(11,'Barbara'),(12,'Joseph'),
 (13,'Susan'),(14,'Thomas'),(15,'Jessica'),(16,'Charles'),(17,'Sarah'),(18,'Christopher'),
 (19,'Karen'),(20,'Daniel'),(21,'Nancy'),(22,'Matthew'),(23,'Lisa'),(24,'Anthony'),
 (25,'Margaret'),(26,'Marcus'),(27,'Angela'),(28,'Steven'),(29,'Deborah'),(30,'Andrew'),
 (31,'Rachel'),(32,'Kenneth'),(33,'Carolyn'),(34,'Joshua'),(35,'Janet'),(36,'Kevin'),
 (37,'Catherine'),(38,'Brian'),(39,'Frances'),(40,'Jerome'),(41,'Yolanda'),(42,'Timothy'),
 (43,'Denise'),(44,'Gregory'),(45,'Tanya'),(46,'Raymond'),(47,'Gloria'),(48,'Curtis'),
 (49,'Priscilla');

DECLARE @Surnames TABLE (n int NOT NULL PRIMARY KEY, v varchar(45));
INSERT INTO @Surnames (n, v) VALUES
 (0,'Smith'),(1,'Johnson'),(2,'Williams'),(3,'Brown'),(4,'Jones'),(5,'Garcia'),(6,'Miller'),
 (7,'Davis'),(8,'Rodriguez'),(9,'Martinez'),(10,'Hernandez'),(11,'Lopez'),(12,'Gonzalez'),
 (13,'Wilson'),(14,'Anderson'),(15,'Thomas'),(16,'Taylor'),(17,'Moore'),(18,'Jackson'),
 (19,'Martin'),(20,'Lee'),(21,'Perez'),(22,'Thompson'),(23,'White'),(24,'Harris'),
 (25,'Sanchez'),(26,'Clark'),(27,'Ramirez'),(28,'Lewis'),(29,'Robinson'),(30,'Walker'),
 (31,'Young'),(32,'Allen'),(33,'King'),(34,'Wright'),(35,'Scott'),(36,'Torres'),(37,'Nguyen'),
 (38,'Hill'),(39,'Flores'),(40,'Green'),(41,'Adams'),(42,'Nelson'),(43,'Baker'),(44,'Hall'),
 (45,'Rivera'),(46,'Campbell'),(47,'Mitchell'),(48,'Carter'),(49,'Roberts'),(50,'Gomez'),
 (51,'Phillips'),(52,'Evans'),(53,'Turner'),(54,'Diaz'),(55,'Parker'),(56,'Cruz'),
 (57,'Edwards'),(58,'Collins'),(59,'Reyes'),(60,'Stewart'),(61,'Morris'),(62,'Morales'),
 (63,'Murphy'),(64,'Cook'),(65,'Rogers'),(66,'Gutierrez'),(67,'Ortiz'),(68,'Morgan'),
 (69,'Cooper'),(70,'Peterson'),(71,'Bailey'),(72,'Reed'),(73,'Kelly'),(74,'Howard'),
 (75,'Ramos'),(76,'Kim'),(77,'Cox'),(78,'Ward'),(79,'Richardson'),(80,'Watson'),(81,'Brooks'),
 (82,'Chavez'),(83,'Wood'),(84,'Bennett'),(85,'Gray'),(86,'Mendoza'),(87,'Ruiz'),(88,'Hughes'),
 (89,'Price'),(90,'Alvarez'),(91,'Castillo'),(92,'Sanders'),(93,'Patel'),(94,'Myers'),
 (95,'Long'),(96,'Ross'),(97,'Foster'),(98,'Jimenez'),(99,'Powell'),(100,'Jenkins'),
 (101,'Perry'),(102,'Russell'),(103,'Sullivan'),(104,'Bell'),(105,'Coleman'),(106,'Butler'),
 (107,'Henderson'),(108,'Barnes'),(109,'Gonzales'),(110,'Fisher'),(111,'Vasquez'),
 (112,'Simmons'),(113,'Romero'),(114,'Jordan'),(115,'Patterson'),(116,'Alexander'),
 (117,'Hamilton'),(118,'Graham'),(119,'Reynolds');

DECLARE @Cities TABLE (n int NOT NULL PRIMARY KEY, city varchar(60), st nchar(50));
INSERT INTO @Cities (n, city, st) VALUES
 (0,'Saraland','AL'),(1,'Mobile','AL'),(2,'Birmingham','AL'),(3,'Montgomery','AL'),
 (4,'Huntsville','AL'),(5,'Port Saint Lucie','FL'),(6,'Pensacola','FL'),(7,'Gulfport','MS'),
 (8,'Hattiesburg','MS'),(9,'Baton Rouge','LA'),(10,'Savannah','GA'),(11,'Columbus','GA'),
 (12,'Chattanooga','TN'),(13,'Knoxville','TN'),(14,'Asheville','NC'),(15,'Charleston','SC'),
 (16,'Augusta','GA');

DECLARE @Titles TABLE (n int NOT NULL PRIMARY KEY, v varchar(50));
INSERT INTO @Titles (n, v) VALUES
 (0,'Field Inspector'),(1,'Permit Clerk'),(2,'Code Enforcement Officer'),(3,'Plans Examiner'),
 (4,'Administrative Assistant'),(5,'Building Inspector'),(6,'Records Technician'),
 (7,'Zoning Analyst'),(8,'Utility Technician'),(9,'Senior Inspector'),(10,'Permit Supervisor'),
 (11,'GIS Analyst'),(12,'Compliance Officer');

-- Built from system columns rather than a loop. Ten thousand single-row inserts would take
-- minutes and fill the transaction log to no purpose.
;WITH Numbers AS (
    SELECT TOP (@Rows) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n
    FROM sys.all_columns a CROSS JOIN sys.all_columns b
)
SELECT n INTO #Seq FROM Numbers;

-- Users first, because the employee's foreign key points at them.
INSERT INTO dbo.FW_Users (FirstName, LastName, UserName, Email, IsActive)
SELECT
    f.v,
    s.v,
    'lt' + RIGHT('00000' + CAST(q.n + 1 AS varchar(10)), 5) + '@loadtest.invalid',
    LOWER(LEFT(f.v, 1) + s.v) + CAST(q.n + 1 AS varchar(10)) + '@loadtest.invalid',
    0
FROM #Seq q
JOIN @Forenames f ON f.n = q.n % 50
JOIN @Surnames  s ON s.n = (q.n / 50) % 120;

PRINT 'ADDED users: ' + CAST(@@ROWCOUNT AS varchar(10));

-- Then the employees, matched back by the marker.
INSERT INTO dbo.FW_Employees
    (UserId, RegistrationId, IsActive, FirstName, LastName, UserName, Email,
     Address1, City, State, Zip, Title, HireDate, BirthDate,
     CellPhone, CreatedBy, CreatedOn, DeletedFlag)
SELECT
    u.UserId,
    @RegistrationID,
    @IsActive,
    u.FirstName,
    u.LastName,
    u.UserName,
    u.Email,
    CAST(((ABS(CHECKSUM(u.UserId, 'a')) % 8900) + 100) AS varchar(10)) + ' ' + t.v + ' Road',
    c.city,
    c.st,
    RIGHT('00000' + CAST(30000 + (ABS(CHECKSUM(u.UserId, 'z')) % 9000) AS varchar(10)), 5),
    t.v,
    DATEADD(day, -(ABS(CHECKSUM(u.UserId, 'h')) % 5400), CAST(SYSUTCDATETIME() AS date)),
    DATEADD(day, -(7300 + ABS(CHECKSUM(u.UserId, 'b')) % 12000), CAST(SYSUTCDATETIME() AS date)),
    '251-' + RIGHT('000' + CAST(200 + (ABS(CHECKSUM(u.UserId, 'p')) % 700) AS varchar(10)), 3) +
        '-' + RIGHT('0000' + CAST(ABS(CHECKSUM(u.UserId, 'q')) % 10000 AS varchar(10)), 4),
    2,
    SYSUTCDATETIME(),
    0
FROM dbo.FW_Users u
JOIN @Cities c ON c.n = ABS(CHECKSUM(u.UserId, 'c')) % 17
JOIN @Titles t ON t.n = ABS(CHECKSUM(u.UserId, 't')) % 13
WHERE u.UserName LIKE '%@loadtest.invalid'
  AND NOT EXISTS (SELECT 1 FROM dbo.FW_Employees e WHERE e.UserId = u.UserId);

PRINT 'ADDED employees: ' + CAST(@@ROWCOUNT AS varchar(10));

DROP TABLE #Seq;
GO

SET NOEXEC OFF;
GO

SELECT COUNT(*) AS LoadTestEmployees FROM dbo.FW_Employees WHERE UserName LIKE '%@loadtest.invalid';
SELECT COUNT(*) AS TotalEmployees FROM dbo.FW_Employees;

-- Proof the names actually vary, rather than taking it on trust.
SELECT COUNT(DISTINCT FirstName + '|' + LastName) AS DistinctNames
FROM dbo.FW_Employees WHERE UserName LIKE '%@loadtest.invalid';

SELECT TOP 10 FirstName, LastName, City, State, Title
FROM dbo.FW_Employees WHERE UserName LIKE '%@loadtest.invalid'
ORDER BY EmployeeID;
