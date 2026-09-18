using System;

namespace RuinRail.Gameplay.Economy
{
    /// <summary>Coins are the only currency; they live in exactly one of these domains at a time (base/77_ECONOMY).</summary>
    public enum CoinDomain
    {
        /// <summary>Held during an expedition; lost on failure.</summary>
        Carried,

        /// <summary>Permanent profile balance; only extraction and base transactions touch it.</summary>
        Banked
    }

    public enum CoinTransactionKind
    {
        Credit,
        Debit,
        Transfer
    }

    /// <summary>Immutable record of one balance change for persistence/audit.</summary>
    public readonly struct CoinReceipt
    {
        public CoinReceipt(CoinTransactionKind kind, CoinDomain domain, int amount, int balanceAfter, string reason)
        {
            Kind = kind;
            Domain = domain;
            Amount = amount;
            BalanceAfter = balanceAfter;
            Reason = reason;
        }

        public CoinTransactionKind Kind { get; }
        public CoinDomain Domain { get; }
        public int Amount { get; }
        public int BalanceAfter { get; }
        public string Reason { get; }
    }

    public enum CoinError
    {
        None,
        InvalidAmount,
        InsufficientFunds
    }

    public readonly struct CoinTransactionResult
    {
        private CoinTransactionResult(bool success, CoinError error, CoinReceipt receipt)
        {
            Success = success;
            Error = error;
            Receipt = receipt;
        }

        public bool Success { get; }
        public CoinError Error { get; }
        public CoinReceipt Receipt { get; }

        public static CoinTransactionResult Ok(CoinReceipt receipt) => new(true, CoinError.None, receipt);
        public static CoinTransactionResult Fail(CoinError error) => new(false, error, default);
    }

    /// <summary>
    /// A single-domain coin balance. Debits are atomic: an insufficient-funds debit changes nothing. Balances can never
    /// go negative and amounts must be positive. Cross-domain movement happens only through <see cref="CoinTransfer"/>.
    /// </summary>
    public sealed class CoinWallet
    {
        private readonly Func<int> _read;
        private readonly Action<int> _write;

        public CoinWallet(CoinDomain domain, int initialBalance = 0)
        {
            var balance = Math.Max(0, initialBalance);
            Domain = domain;
            _read = () => balance;
            _write = value => balance = value;
        }

        /// <summary>Wraps an externally owned balance (e.g. the serialized profile field) so the domain rules apply to it.</summary>
        public CoinWallet(CoinDomain domain, Func<int> read, Action<int> write)
        {
            Domain = domain;
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
        }

        public CoinDomain Domain { get; }
        public int Balance => Math.Max(0, _read());

        public event Action<CoinReceipt> Changed;

        public bool CanAfford(int amount) => amount >= 0 && Balance >= amount;

        public CoinTransactionResult Credit(int amount, string reason = null)
        {
            if (amount <= 0) return CoinTransactionResult.Fail(CoinError.InvalidAmount);
            _write(Balance + amount);
            return Emit(new CoinReceipt(CoinTransactionKind.Credit, Domain, amount, Balance, reason));
        }

        public CoinTransactionResult Debit(int amount, string reason = null)
        {
            if (amount <= 0) return CoinTransactionResult.Fail(CoinError.InvalidAmount);
            if (Balance < amount) return CoinTransactionResult.Fail(CoinError.InsufficientFunds);
            _write(Balance - amount);
            return Emit(new CoinReceipt(CoinTransactionKind.Debit, Domain, amount, Balance, reason));
        }

        /// <summary>Empties the wallet; returns the amount removed (0 when already empty).</summary>
        public int TakeAll(string reason = null)
        {
            var amount = Balance;
            if (amount == 0) return 0;
            _write(0);
            Emit(new CoinReceipt(CoinTransactionKind.Debit, Domain, amount, 0, reason));
            return amount;
        }

        private CoinTransactionResult Emit(CoinReceipt receipt)
        {
            Changed?.Invoke(receipt);
            return CoinTransactionResult.Ok(receipt);
        }
    }

    /// <summary>The only cross-domain path: moves coins from one wallet to another atomically (all or nothing).</summary>
    public static class CoinTransfer
    {
        public static CoinTransactionResult Move(CoinWallet from, CoinWallet to, int amount, string reason)
        {
            if (from == null || to == null || ReferenceEquals(from, to)) return CoinTransactionResult.Fail(CoinError.InvalidAmount);
            var debit = from.Debit(amount, reason);
            if (!debit.Success) return debit;
            var credit = to.Credit(amount, reason);
            return CoinTransactionResult.Ok(new CoinReceipt(CoinTransactionKind.Transfer, to.Domain, amount, credit.Receipt.BalanceAfter, reason));
        }

        public static CoinTransactionResult MoveAll(CoinWallet from, CoinWallet to, string reason)
        {
            var amount = from?.Balance ?? 0;
            return amount == 0 ? CoinTransactionResult.Ok(new CoinReceipt(CoinTransactionKind.Transfer, to.Domain, 0, to.Balance, reason)) : Move(from, to, amount, reason);
        }
    }
}
