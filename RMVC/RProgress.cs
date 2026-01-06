namespace RMVC 
{
    public class RProgress 
    {
        public int Percent { get; }
        public string Message { get; }
        public string Heading { get; }
        public string Id { get; }

        internal RProgress(
            int percent
            , string message
            , string heading
            , string id) 
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            if (string.IsNullOrWhiteSpace(message)) message = string.Empty;
            if (string.IsNullOrWhiteSpace(heading)) heading = string.Empty;

            Percent = percent;
            Message = message;
            Heading = heading;
            Id = id;
        }
    }
}
