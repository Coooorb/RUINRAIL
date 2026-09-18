namespace RuinRail.Gameplay.Items
{
    public enum TransferError
    {
        None,
        InvalidRequest,
        SourceMissingItem,
        InvalidQuantity,
        DestinationRejected,
        DuplicateOwnership
    }

    public readonly struct TransferResult
    {
        public bool Success { get; }
        public TransferError Error { get; }
        public string InstanceId { get; }
        public int Quantity { get; }

        private TransferResult(bool success, TransferError error, string instanceId, int quantity)
        {
            Success = success;
            Error = error;
            InstanceId = instanceId;
            Quantity = quantity;
        }

        public static TransferResult Ok(string instanceId, int quantity) => new(true, TransferError.None, instanceId, quantity);
        public static TransferResult Fail(TransferError error, string instanceId = null) => new(false, error, instanceId, 0);
    }
}
