-- BFF-05.5b SQL Server atomic session store, manual deployment migration v1.
-- Apply to the database configured in ConnectionStrings:BffSessions BEFORE enabling BFF.
-- Reversible only after sessions are drained; do not drop an active session table.
-- This script is idempotent for a clean deployment, NOT a schema-drift repair tool.
IF OBJECT_ID(N'dbo.FoundationBffSessions', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.FoundationBffSessions
    (
        SessionKey VARCHAR(80) COLLATE Latin1_General_100_BIN2 NOT NULL
            CONSTRAINT PK_FoundationBffSessions PRIMARY KEY,
        TicketCiphertext VARBINARY(MAX) NULL,
        SubjectId UNIQUEIDENTIFIER NULL,
        CreatedUtc DATETIME2(7) NOT NULL,
        ExpiresUtc DATETIME2(7) NOT NULL,
        AbsoluteExpiresUtc DATETIME2(7) NOT NULL,
        RevokedUtc DATETIME2(7) NULL,
        Revision BIGINT NOT NULL CONSTRAINT DF_FoundationBffSessions_Revision DEFAULT (1),
        CONSTRAINT CK_FoundationBffSessions_Expiry
            CHECK (ExpiresUtc <= AbsoluteExpiresUtc
                AND AbsoluteExpiresUtc <= DATEADD(DAY, 1, CreatedUtc)),
        CONSTRAINT CK_FoundationBffSessions_Revocation
            CHECK ((RevokedUtc IS NULL AND TicketCiphertext IS NOT NULL)
                OR (RevokedUtc IS NOT NULL AND TicketCiphertext IS NULL)),
        CONSTRAINT CK_FoundationBffSessions_Subject
            CHECK (RevokedUtc IS NOT NULL OR SubjectId IS NOT NULL)
    );
    CREATE INDEX IX_FoundationBffSessions_Cleanup
        ON dbo.FoundationBffSessions(AbsoluteExpiresUtc, RevokedUtc);
END;


-- Upgrade path for deployments using the previous v1 schema. Existing active
-- rows have no trustworthy subject binding and MUST be revoked (fail closed);
-- guessing a subject from opaque Data Protection ciphertext is prohibited.
IF COL_LENGTH(N'dbo.FoundationBffSessions', N'SubjectId') IS NULL
BEGIN
    ALTER TABLE dbo.FoundationBffSessions ADD SubjectId UNIQUEIDENTIFIER NULL;
    UPDATE dbo.FoundationBffSessions
        SET RevokedUtc = SYSUTCDATETIME(),
            TicketCiphertext = NULL,
            Revision = Revision + 1
    WHERE RevokedUtc IS NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'dbo.FoundationBffSessions')
      AND name = N'CK_FoundationBffSessions_Subject')
BEGIN
    ALTER TABLE dbo.FoundationBffSessions WITH CHECK
        ADD CONSTRAINT CK_FoundationBffSessions_Subject
        CHECK (RevokedUtc IS NOT NULL OR SubjectId IS NOT NULL);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID(N'dbo.FoundationBffSessions')
      AND name = N'IX_FoundationBffSessions_Subject_Active')
BEGIN
    CREATE INDEX IX_FoundationBffSessions_Subject_Active
        ON dbo.FoundationBffSessions(SubjectId, RevokedUtc);
END;
