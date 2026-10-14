using System;

namespace RMVC
{
    public sealed class RErrorItem
    {
        public string Message { get; }
        public string CommandName { get; }
        public string CommandId { get; }
        public string ChainId { get; }
        public DateTimeOffset RecordedAtUtc { get; }
        public string? ExceptionDetails { get; }

        internal RErrorItem(
            string message,
            string commandName,
            string commandId,
            string chainId,
            string? exceptionDetails)
        {
            Message = message;
            CommandName = commandName;
            CommandId = commandId;
            ChainId = chainId;
            RecordedAtUtc = DateTimeOffset.UtcNow;
            ExceptionDetails = exceptionDetails;
        }
    }
}