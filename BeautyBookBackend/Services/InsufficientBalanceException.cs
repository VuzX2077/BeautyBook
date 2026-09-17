namespace BeautyBookBackend.Services
{
    public sealed class InsufficientBalanceException : Exception
    {
        public decimal RequiredAmount { get; }
        public decimal CurrentBalance { get; }
        public decimal MissingAmount => RequiredAmount - CurrentBalance;

        public InsufficientBalanceException(decimal requiredAmount, decimal currentBalance)
            : base($"Số dư ví không đủ. Cần {requiredAmount:N0} VND, hiện có {currentBalance:N0} VND.")
        {
            RequiredAmount = requiredAmount;
            CurrentBalance = currentBalance;
        }
    }
}
