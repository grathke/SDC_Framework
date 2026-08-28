-- Add LinkedControl column to FW_Enumerations to track associated label controls
ALTER TABLE dbo.FW_Enumerations 
ADD LinkedControl varchar(100) NULL;
