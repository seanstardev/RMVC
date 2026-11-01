namespace RMVC
{
    public sealed class RProgress
    {
        public int Percent { get; }
        public string Message { get; }
        /// <summary>The progress title supplied by the represented command execution.</summary>
        public string Title { get; }
        /// <summary>Identifies the command execution represented by this progress item.</summary>
        public string CommandId { get; }

        internal RProgress(
            int percent,
            string message,
            string title,
            string commandId)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            if (string.IsNullOrWhiteSpace(message)) message = string.Empty;
            if (string.IsNullOrWhiteSpace(title)) title = string.Empty;

            Percent = percent;
            Message = message;
            Title = title;
            CommandId = commandId;
        }
    }
}
