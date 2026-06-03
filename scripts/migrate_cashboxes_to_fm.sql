SET QUOTED_IDENTIFIER ON;
GO

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CashBoxTransfers_CashBoxes_FromCashBoxId')
    ALTER TABLE acc.CashBoxTransfers DROP CONSTRAINT FK_CashBoxTransfers_CashBoxes_FromCashBoxId;
IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_CashBoxTransfers_CashBoxes_ToCashBoxId')
    ALTER TABLE acc.CashBoxTransfers DROP CONSTRAINT FK_CashBoxTransfers_CashBoxes_ToCashBoxId;
GO

UPDATE t SET FromCashBoxId = fp.Id
FROM acc.CashBoxTransfers t
INNER JOIN acc.CashBoxes cb ON cb.Id = t.FromCashBoxId
INNER JOIN acc.FinancialParties fp ON fp.AccountId = cb.AccountId AND fp.IsDeleted = 0
INNER JOIN acc.FinancialPartyCategories fpc ON fpc.Id = fp.CategoryId AND fpc.Kind = 4
WHERE fp.Id <> t.FromCashBoxId;

UPDATE t SET ToCashBoxId = fp.Id
FROM acc.CashBoxTransfers t
INNER JOIN acc.CashBoxes cb ON cb.Id = t.ToCashBoxId
INNER JOIN acc.FinancialParties fp ON fp.AccountId = cb.AccountId AND fp.IsDeleted = 0
INNER JOIN acc.FinancialPartyCategories fpc ON fpc.Id = fp.CategoryId AND fpc.Kind = 4
WHERE fp.Id <> t.ToCashBoxId;
GO

UPDATE ucb SET CashBoxId = fp.Id
FROM auth.UserCashBoxes ucb
INNER JOIN acc.CashBoxes cb ON cb.Id = ucb.CashBoxId
INNER JOIN acc.FinancialParties fp ON fp.AccountId = cb.AccountId AND fp.IsDeleted = 0
INNER JOIN acc.FinancialPartyCategories fpc ON fpc.Id = fp.CategoryId AND fpc.Kind = 4
WHERE fp.Id <> ucb.CashBoxId;
GO

UPDATE acc.CashBoxCurrencies SET IsDeleted = 1, UpdatedAt = GETUTCDATE() WHERE IsDeleted = 0;
UPDATE acc.CashBoxes SET IsDeleted = 1, UpdatedAt = GETUTCDATE() WHERE IsDeleted = 0;
GO

SELECT 'FM_CashBoxes' AS Label, COUNT(*) AS Cnt
FROM acc.FinancialParties fp
INNER JOIN acc.FinancialPartyCategories fpc ON fpc.Id = fp.CategoryId AND fpc.Kind = 4
WHERE fp.IsDeleted = 0 AND fp.IsActive = 1;

SELECT 'Legacy_CashBoxes' AS Label, COUNT(*) AS Cnt FROM acc.CashBoxes WHERE IsDeleted = 0;
GO
