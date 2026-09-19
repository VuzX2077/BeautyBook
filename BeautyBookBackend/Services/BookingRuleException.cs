namespace BeautyBookBackend.Services
{
    public sealed class BookingRuleException : Exception
    {
        public BookingRuleException(string code, string message, int statusCode = 400) : base(message)
        {
            Code = code;
            StatusCode = statusCode;
        }

        public string Code { get; }
        public int StatusCode { get; }
    }
}
