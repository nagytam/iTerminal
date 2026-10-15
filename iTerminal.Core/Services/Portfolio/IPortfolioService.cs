using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using iTerminal.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace iTerminal.Core.Services.Portfolio
{
    public interface IPortfolioService
    {
        Task<List<PortfolioHolding>> GetHoldingsAsync();
        Task<PortfolioHolding> AddHoldingAsync(string symbol, string? exchange, decimal quantity, decimal costBasisPerUnit, string currency, DateTime? acquiredDate, string? notes);
        Task UpdateHoldingAsync(PortfolioHolding holding);
        Task DeleteHoldingAsync(int id);
    }

    public class PortfolioService : IPortfolioService
    {
        private readonly IDbContextFactory<iTerminalDbContext> _dbContextFactory;

        public PortfolioService(IDbContextFactory<iTerminalDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<List<PortfolioHolding>> GetHoldingsAsync()
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            return await dbContext.PortfolioHoldings.AsNoTracking().ToListAsync();
        }

        public async Task<PortfolioHolding> AddHoldingAsync(string symbol, string? exchange, decimal quantity, decimal costBasisPerUnit, string currency, DateTime? acquiredDate, string? notes)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();

            var holding = new PortfolioHolding
            {
                Symbol = symbol,
                Exchange = exchange,
                Quantity = quantity,
                CostBasisPerUnit = costBasisPerUnit,
                Currency = currency,
                AcquiredDate = acquiredDate,
                Notes = notes,
            };

            dbContext.PortfolioHoldings.Add(holding);
            await dbContext.SaveChangesAsync();
            return holding;
        }

        public async Task UpdateHoldingAsync(PortfolioHolding holding)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            dbContext.PortfolioHoldings.Update(holding);
            await dbContext.SaveChangesAsync();
        }

        public async Task DeleteHoldingAsync(int id)
        {
            await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
            await dbContext.PortfolioHoldings.Where(h => h.Id == id).ExecuteDeleteAsync();
        }
    }
}
