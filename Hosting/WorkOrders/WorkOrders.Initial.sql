CREATE TABLE dbo.WorkOrders (
  Id nvarchar(64) NOT NULL PRIMARY KEY,
  Number nvarchar(64) NOT NULL,
  Status nvarchar(32) NOT NULL,
  Line nvarchar(16) NOT NULL,
  PartNo nvarchar(64) NOT NULL,
  CreatedUtc datetime2(7) NOT NULL,
  DueUtc datetime2(7) NULL,
  JsonPayload nvarchar(max) NULL
);
