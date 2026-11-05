namespace Domain.SharedKernel.Common.Enums
{
    public enum AppointmentStatus : byte
    {
        Draft = 0,
        PendingDeposit = 1,
        Confirmed = 2,
        InProgress = 3,
        Completed = 4,
        Cancelled = 5,
        NoShow = 6
    }

    public enum CancellationReason : byte
    {
        CustomerRequest = 0,
        StaffUnavailable = 0,
        Weather = 2,
        Emergency = 3,
        NoShow = 4,
        Other = 5
    }

    public enum DayOfWeek : byte
    {
        Saturday = 0,
        Sunday = 1,
        Monday = 2,
        Tuesday = 3,
        Wednesday = 4,
        Thursday = 5,
        Friday = 6
    }

    public enum ServiceProviderType : byte
    {
        Staff = 0,
        Freelancer = 1
    }

    public enum ServiceCategory : byte
    {
        Hair = 0,
        Skin = 1,
        Nail = 2,
        Makeup = 3,
        Massage = 4,
        Other = 5
    }

    public enum PaymentMethod : byte
    {
        Cash = 0,
        PosCard = 1,
        OnlinePayment = 2,
        Wallet = 3,
        Credit = 4
    }

    public enum InvoiceStatus : byte
    {
        Draft = 0,
        Issued = 1,
        PartiallyPaid = 2,
        Paid = 3,
        Cancelled = 4,
        Refunded = 5
    }

    public enum ProductCategory : byte
    {
        Retail = 0,
        Backbar = 1,
        Equipment = 2,
        Consumable = 3
    }

    public enum StockTransactionType : byte
    {
        GoodsReceipt = 0,
        StockTransfer = 1,
        Wastage = 2,
        Adjustment = 3,
        ServiceConsumption = 4,
        Sale = 5,
        Return = 6
    }

    public enum TransactionType : byte
    {
        Income = 0,
        Expense = 1,
        Transfer = 2,
        Adjustment = 3
    }

    public enum AccountType : byte
    {
        Asset = 0,
        Liability = 1,
        Equity = 2,
        Revenue = 3,
        Expense = 4
    }

    public enum CommissionType : byte
    {
        Fixed = 0,
        Percentage = 1,
        Tiered = 2,
        ProductSale = 3
    }

    public enum SubscriptionStatus : byte
    {
        Active = 0,
        Suspended = 1,
        Cancelled = 2,
        Expired = 3
    }

    public enum TopologyMode : byte
    {
        Solo = 0,
        Enterprise = 1
    }

    public enum MediaType : byte
    {
        Image = 0,
        Video = 1,
        File = 2
    }

    public enum MediaProcessingStatus : byte
    {
        Pending = 0,
        Processing = 1,
        Ready = 2,
        Failed = 3
    }

    public enum MediaVariantType : byte
    {
        Original = 0,
        Thumbnail = 1,
        Medium = 2,
        Large = 3
    }

    public enum DiscountType : byte
    {
        Percentage = 0,
        FixedAmount = 1
    }

    public enum LoyaltyTransactionType : byte
    {
        Earned = 0,
        Redeemed = 1,
        Adjusted = 2,
        Expired = 3
    }

    public enum WorkingHourType : byte
    {
        Regular = 0,
        Override = 1
    }
}
