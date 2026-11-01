using System;

namespace RMVC
{
    public sealed class RErrorItem
    {
        public string Message { get; }
        public string CommandName { get; }
        /// <summary>Identifies the command execution that recorded this error.</summary>
        public string CommandId { get; }
        /// <summary>Identifies its root command execution; equals CommandId for a root command.</summary>
        public string CommandRootId { get; }
        public DateTimeOffset RecordedAtUtc { get; }
        public string? ExceptionDetails { get; }

        internal RErrorItem(
            string message,
            string commandName,
            string commandId,
            string commandRootId,
            string? exceptionDetails)
        {
            Message = message;
            CommandName = commandName;
            CommandId = commandId;
            CommandRootId = commandRootId;
            RecordedAtUtc = DateTimeOffset.UtcNow;
            ExceptionDetails = exceptionDetails;
        }
    }
}