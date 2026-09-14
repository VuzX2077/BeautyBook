namespace BeautyBookBackend.Services
{
    public class InsufficientBalanceException : InvalidOperationException
    {
        public InsufficientBalanceException(decimal requiredAmount, decimal currentBalance)
            : base("Số dư ví không đủ để thanh toán booking.")
        {
            RequiredAmount = requiredAmount;
            CurrentBalance = currentBalance;
            MissingAmount = requiredAmount - currentBalance;
        }

        public decimal RequiredAmount { get; }
        public decimal CurrentBalance { get; }
        public decimal MissingAmount { get; }
    }
}
